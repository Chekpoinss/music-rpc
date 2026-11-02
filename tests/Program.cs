using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MusicRpc;

if (args.Contains("--live"))
{
    var liveTrack = (await new MediaReader().ReadAsync()).First(t => t.Playing && t.Source != "Другое");
    var match = await new Catalog().FindAsync(liveTrack);
    liveTrack = await new TelegramTimeline().EnrichAsync(liveTrack, match?.Duration ?? 0);
    if (match != null) liveTrack = liveTrack with { ArtworkUrl = match.ArtworkUrl, TrackUrl = match.TrackUrl, Album = match.Album };
    using var liveRpc = new DiscordRpc();
    await liveRpc.ConnectAsync(Settings.Load().ApplicationId);
    await liveRpc.SetActivityAsync(Presence.Build(liveTrack, DateTimeOffset.UtcNow));
    Console.WriteLine(JsonSerializer.Serialize(liveRpc.LastResponse, new JsonSerializerOptions { WriteIndented = true }));
    await Task.Delay(5000);
    await liveRpc.SetActivityAsync(null);
    return;
}

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    passed++;
    Console.WriteLine("PASS: " + message);
}
var now = DateTimeOffset.UtcNow;
Track TrackFor(string id, string source, bool playing = true, bool current = false) =>
    new(id, source, "Трек", "Исполнитель", "Альбом", playing, 30, 180, now.AddSeconds(-2), current);
var ayu = TrackFor("ayu", "AyuGram");
var apple = TrackFor("apple", "Apple Music", current: true);
var browser = TrackFor("browser", "Другое", current: true);
var settings = new Settings();
var selector = new TrackSelector();
Check(selector.Select([browser, ayu, apple], settings) == apple, "automatic selection uses current supported player, ignores browser");
settings.Priority = "Telegram / AyuGram";
Check(selector.Select([ayu, apple], settings) == ayu, "explicit Telegram priority");
settings.Telegram = false;
Check(selector.Select([ayu, apple], settings) == apple, "disabled source is ignored");
Check(selector.Select([ayu with { Playing = false }, apple with { Playing = false }], new()) == null, "pause clears presence");
Check(selector.Select([], new()) == null, "closing players clears presence");
Check(MediaReader.SourceName("AyuGram.AyuGramDesktop.123") == "AyuGram", "installed AyuGram app id");
Check(MediaReader.SourceName("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App") == "Apple Music", "Apple Music package id");
Check(MediaReader.SourceName("chrome.exe") == "Другое", "browser is not treated as Apple Music");
Check(Settings.ValidId("123456789012345678") && !Settings.ValidId("token") && !Settings.ValidId(new string('1', 21)), "application id validation");
Check(Math.Abs(Presence.PositionAt(ayu, now) - 32) < .1, "timeline advances since Windows update");
Check(Presence.PositionAt(ayu with { Position = 179 }, now) == 180, "timeline clamps to duration");
Check(Presence.PositionAt(ayu with { Updated = DateTimeOffset.MinValue }, now) == 30, "missing timeline timestamp does not overflow");
var presence = JsonSerializer.SerializeToElement(Presence.Build(ayu, now));
Check(presence.GetProperty("type").GetInt32() == 2, "activity is Listening");
Check(presence.GetProperty("timestamps").GetProperty("end").GetInt64() - now.ToUnixTimeSeconds() == 148, "remaining track time");
Check(!Presence.Build(ayu with { Duration = 0, Updated = DateTimeOffset.MinValue }, now).ContainsKey("timestamps"), "no invented timer without position");
Check(Presence.Build(ayu with { Duration = 0 }, now).ContainsKey("timestamps"), "elapsed timer works with real position but unknown duration");
Check(presence.GetProperty("name").GetString() == "Telegram", "Telegram name overrides developer application name");
Check(Presence.Build(apple, now)["name"].ToString() == "Apple Music", "Apple Music name overrides developer application name");
var artworkPresence = JsonSerializer.SerializeToElement(Presence.Build(ayu with { ArtworkUrl = "https://example.com/cover.jpg", TrackUrl = "https://music.apple.com/us/song/1" }, now));
Check(artworkPresence.GetProperty("assets").GetProperty("large_image").GetString() == "https://example.com/cover.jpg", "external artwork URL sent to Discord");
Check(artworkPresence.GetProperty("buttons")[0].GetProperty("url").GetString() == "https://music.apple.com/us/song/1", "matched track link sent to Discord");
Check(TelegramTimeline.ParseTime("01:05") == 65 && TelegramTimeline.ParseTime("1:02:03") == 3723, "Telegram elapsed time label parsed");
Check(TelegramTimeline.ParseTime("65%") == null && TelegramTimeline.ParseTime("12:99") == null, "invalid labels rejected");
using var catalogFixture = JsonDocument.Parse("""
{"results":[
{"trackName":"Трек", "artistName":"Другой", "artworkUrl100":"https://a.mzstatic.com/100x100bb.jpg", "trackViewUrl":"https://music.apple.com/us/song/1"},
{"trackName":"Трек", "artistName":"Исполнитель", "collectionName":"Альбом", "trackTimeMillis":180000,"artworkUrl100":"https://a.mzstatic.com/100x100bb.jpg", "trackViewUrl":"https://music.apple.com/us/song/2"}]}
""");
var catalogMatch = Catalog.Parse(catalogFixture.RootElement, ayu);
Check(catalogMatch?.TrackUrl.EndsWith("/2") == true && catalogMatch.ArtworkUrl.EndsWith("/512x512bb.jpg"), "catalog selects exact artist and song, expands artwork");
Check(Catalog.Parse(catalogFixture.RootElement, ayu with { Duration = 230 }) == null, "catalog rejects different duration version");
Check(Catalog.Parse(catalogFixture.RootElement, ayu with { Title = "Трек (remix)" }) == null, "catalog does not silently substitute original for remix");
using var deezerFixture = JsonDocument.Parse("""
{"data":[{"title":"Трек","duration":180,"link":"https://www.deezer.com/track/1","artist":{"name":"Исполнитель"},"album":{"title":"Альбом","cover_big":"https://cdn-images.dzcdn.net/images/cover/1/500.jpg"}}]}
""");
Check(Catalog.ParseDeezer(deezerFixture.RootElement, ayu)?.Duration == 180, "Deezer fallback yields matched artwork and duration");
Check(Catalog.ParseDeezer(deezerFixture.RootElement, ayu with { Artist = "Другой" }) == null, "Deezer rejects unrelated artist");
var limited = Presence.Limit(string.Concat(Enumerable.Repeat("🎵Ж", 80)));
Check(Encoding.UTF8.GetByteCount(limited) <= 128 && !limited.Contains('\ufffd'), "UTF-8 truncation preserves Unicode");
Check(Presence.Limit("Я").Length >= 2, "Discord minimum field length");

async Task<(int Op, JsonElement Data)> ReadFrame(Stream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    byte[] header = new byte[8];
    await stream.ReadExactlyAsync(header, timeout.Token);
    byte[] data = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
    await stream.ReadExactlyAsync(data, timeout.Token);
    using var doc = JsonDocument.Parse(data);
    return (BinaryPrimitives.ReadInt32LittleEndian(header), doc.RootElement.Clone());
}
async Task WriteFrame(Stream stream, int opcode, object payload, bool split = false)
{
    var body = JsonSerializer.SerializeToUtf8Bytes(payload);
    var frame = new byte[8 + body.Length];
    BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
    body.CopyTo(frame, 8);
    if (split)
    {
        await stream.WriteAsync(frame.AsMemory(0, 3));
        await Task.Delay(20);
        await stream.WriteAsync(frame.AsMemory(3));
    }
    else await stream.WriteAsync(frame);
    await stream.FlushAsync();
}

var pipeName = "music-rpc-test-" + Guid.NewGuid();
using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
var serverTask = Task.Run(async () =>
{
    await server.WaitForConnectionAsync();
    var handshake = await ReadFrame(server);
    Check(handshake.Op == 0 && handshake.Data.GetProperty("client_id").GetString() == "123456789012345678", "IPC handshake");
    await WriteFrame(server, 1, new { evt = "READY" }, split: true);
    var update = await ReadFrame(server);
    Check(update.Data.GetProperty("args").GetProperty("activity").GetProperty("details").GetString() == "Трек", "IPC Unicode track payload");
    await WriteFrame(server, 3, new { ping = true });
    var pong = await ReadFrame(server);
    Check(pong.Op == 4, "IPC ping receives pong");
    await WriteFrame(server, 1, new { evt = (string?)null, nonce = update.Data.GetProperty("nonce").GetString() });
    var clear = await ReadFrame(server);
    Check(clear.Data.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "IPC clears paused activity");
    await WriteFrame(server, 1, new { nonce = clear.Data.GetProperty("nonce").GetString() });
    var bad = await ReadFrame(server);
    await WriteFrame(server, 1, new { evt = "ERROR", data = new { message = "test error" }, nonce = bad.Data.GetProperty("nonce").GetString() });
});
using var rpc = new DiscordRpc();
await rpc.ConnectAsync("123456789012345678", pipeName);
await rpc.SetActivityAsync(Presence.Build(ayu, now));
await rpc.SetActivityAsync(null);
try { await rpc.SetActivityAsync(null); throw new Exception("Expected RPC error"); }
catch (IOException ex) { Check(ex.Message.Contains("test error") && !rpc.Connected, "RPC error is surfaced and connection disposed"); }
await serverTask;

var reconnectName = "music-rpc-test-" + Guid.NewGuid();
using var reconnectServer = new NamedPipeServerStream(reconnectName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
var reconnectTask = Task.Run(async () =>
{
    await reconnectServer.WaitForConnectionAsync();
    await ReadFrame(reconnectServer);
    await WriteFrame(reconnectServer, 1, new { evt = "READY" });
});
await rpc.ConnectAsync("123456789012345678", reconnectName);
Check(rpc.Connected, "reconnect after failed RPC");
await reconnectTask;
Console.WriteLine($"All {passed} checks passed.");
