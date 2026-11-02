using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using MusicRpc;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
var now = DateTimeOffset.UtcNow;
var row = string.Join(MprisMediaReader.Separator, "Playing", "65000000", "167000000", "Not Allowed", "TV Girl", "Who Really Cares", "file:///home/user/private.jpg", "https://music.apple.com/us/song/1");
var track = MprisMediaReader.Parse("AyuGram.instance1", row + "\n", now)!;
Check(track.Source == "AyuGram" && track.Playing && track.Position == 65 && track.Duration == 167, "MPRIS microseconds and source");
Check(track.ArtworkUrl == null && track.TrackUrl != null, "local artwork stays local");
Check(MprisMediaReader.Parse("cider", row, now)!.Source == "Apple Music", "Cider is recognized as Apple Music");
Check(MprisMediaReader.SourceName("firefox") == "Другое", "browsers ignored by default");
Check(MprisMediaReader.SourceName("my-client", "my-client") == "Apple Music", "explicit Apple client");
Check(MprisMediaReader.Parse("AyuGram", "bad output", now) == null, "malformed output rejected");
Check(MprisMediaReader.Parse("AyuGram", row + MprisMediaReader.Separator + "injected field", now) == null, "ambiguous delimiter rejected");
var missing = MprisMediaReader.Parse("AyuGram", row.Replace("65000000", ""), now)!;
Check(missing.Updated == DateTimeOffset.MinValue && !Presence.Build(missing, now).ContainsKey("timestamps"), "missing position does not invent a timer");
Check(!new TrackSelector().Select([track with { Playing = false }], new Settings())?.Playing ?? true, "paused player excluded");
var built = JsonSerializer.SerializeToElement(Presence.Build(track, now));
Check(built.GetProperty("name").GetString() == "Telegram", "source name sent instead of developer app name");
Check(built.GetProperty("timestamps").GetProperty("end").GetInt64() - now.ToUnixTimeSeconds() == 102, "MPRIS position reflected in Discord timer");
Check(DiscordRpc.UnixSocketPaths().Any(p => p.Contains("app/com.discordapp.Discord")), "Flatpak socket candidate");

var sent = new List<Dictionary<string, object>?>();
var publisher = new PlaybackPublisher(activity => { sent.Add(activity); return Task.CompletedTask; });
await publisher.UpdateAsync(track, now);
await publisher.UpdateAsync(track with { Position = 67, Updated = now.AddSeconds(2) }, now.AddSeconds(2));
Check(sent.Count == 1, "normal progress keeps a stable timer");
var nextSong = track with { Title = "Другой трек", Position = 19, Duration = 217, Updated = now.AddSeconds(6) };
await publisher.UpdateAsync(nextSong, now.AddSeconds(6));
var nextPayload = JsonSerializer.SerializeToElement(sent[^1]);
Check(sent.Count == 2 && sent.All(x => x != null), "track change is one atomic replacement, no clear/set burst");
Check(nextPayload.GetProperty("timestamps").GetProperty("start").GetInt64() == now.AddSeconds(6 - 19).ToUnixTimeSeconds(), "new song starts at actual 19 seconds");
await publisher.UpdateAsync(nextSong with { Position = 3, Updated = now.AddSeconds(12) }, now.AddSeconds(12));
Check(sent.Count == 3 && sent[^1] != null, "backward seek updates timer without clearing activity");
await publisher.UpdateAsync(nextSong with { Playing = false }, now.AddSeconds(13));
Check(sent[^1] == null, "pause clears activity immediately");
await publisher.UpdateAsync(nextSong with { Position = 3, Updated = now.AddSeconds(25) }, now.AddSeconds(25));
var resumed = JsonSerializer.SerializeToElement(sent[^1]);
Check(resumed.GetProperty("timestamps").GetProperty("start").GetInt64() == now.AddSeconds(22).ToUnixTimeSeconds(), "resume does not include paused wall time");
await publisher.UpdateAsync(nextSong with { Updated = DateTimeOffset.MinValue }, now.AddSeconds(31));
Check(sent[^1] != null && !sent[^1]!.ContainsKey("timestamps"), "lost timeline replaces stale timestamps atomically");

var clock = new PlaybackClock();
var anchor = clock.Update(track with { Position = 9 }, now)!;
var repeated = clock.Update(track with { Position = 9, Updated = now.AddSeconds(1) }, now.AddSeconds(1))!;
Check(Presence.PositionAt(repeated, now.AddSeconds(1)) == 10, "repeated rounded UI label does not slow timer");
var unavailable = clock.Update(track with { Updated = DateTimeOffset.MinValue, Duration = 0 }, now.AddSeconds(4))!;
Check(Presence.PositionAt(unavailable, now.AddSeconds(4)) == 13 && unavailable.Duration == 167, "brief unavailable UI keeps advancing the same clock");
var forward = clock.Update(track with { Position = 95, Updated = now.AddSeconds(5) }, now.AddSeconds(5))!;
Check(Presence.PositionAt(forward, now.AddSeconds(5)) == 95, "forward seek reanchors measured position");
var backward = clock.Update(track with { Position = 2, Updated = now.AddSeconds(7) }, now.AddSeconds(7))!;
Check(Presence.PositionAt(backward, now.AddSeconds(7)) == 2, "backward seek reanchors measured position");
clock.Update(null, now.AddSeconds(8));
var afterPause = clock.Update(track with { Position = 2, Updated = now.AddSeconds(18) }, now.AddSeconds(18))!;
Check(Presence.PositionAt(afterPause, now.AddSeconds(18)) == 2, "clock pauses instead of accumulating wall time");
var other = clock.Update(nextSong with { Position = 0, Updated = now.AddSeconds(19) }, now.AddSeconds(19))!;
Check(Presence.PositionAt(other, now.AddSeconds(19)) == 0, "track change cannot inherit old clock");

var burst = new List<DateTimeOffset>();
DateTimeOffset tickTime = now;
var throttled = new PlaybackPublisher(activity => { burst.Add(tickTime); return Task.CompletedTask; });
for (var i = 0; i < 40; i++)
{
    tickTime = now.AddSeconds(i);
    await throttled.UpdateAsync(track with { Position = i * 5, Updated = tickTime }, tickTime);
}
Check(burst.Count <= 8 && burst.All(t => burst.Count(x => x >= t && x < t.AddSeconds(20)) <= 5), "seek spam stays within five commands per twenty seconds");
var ordinary = new List<Dictionary<string, object>?>();
var stablePublisher = new PlaybackPublisher(x => { ordinary.Add(x); return Task.CompletedTask; });
for (var i = 0; i < 25; i++)
    await stablePublisher.UpdateAsync(track with { Position = 65 + i, Updated = now.AddSeconds(i) }, now.AddSeconds(i));
Check(ordinary.Count == 1, "25 seconds of normal playback need only one activity command");

if (OperatingSystem.IsLinux())
{
    var originalRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
    var runtime = Path.Combine(Path.GetTempPath(), "mrpc-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(runtime);
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtime);
    try
    {
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(Path.Combine(runtime, "discord-ipc-0")));
        listener.Listen(1);
        var serverTask = Task.Run(async () =>
        {
            using var socket = await listener.AcceptAsync();
            using var stream = new NetworkStream(socket);
            var handshake = await Read(stream);
            Check(handshake.Op == 0 && handshake.Data.GetProperty("client_id").GetString() == "123456789012345678", "Unix IPC handshake");
            await Write(stream, 1, new { evt = "READY" });
            var activity = await Read(stream);
            Check(activity.Data.GetProperty("args").GetProperty("activity").GetProperty("name").GetString() == "Telegram", "Unix IPC activity payload");
            await Write(stream, 3, new { ping = true });
            Check((await Read(stream)).Op == 4, "Unix IPC ping/pong");
            await Write(stream, 1, new { nonce = activity.Data.GetProperty("nonce").GetString() });
            var clear = await Read(stream);
            Check(clear.Data.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null, "Unix IPC pause clear");
            await Write(stream, 1, new { nonce = clear.Data.GetProperty("nonce").GetString() });
        });
        using var rpc = new DiscordRpc();
        await rpc.ConnectAsync("123456789012345678");
        await rpc.SetActivityAsync(Presence.Build(track, now));
        await rpc.SetActivityAsync(null);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", originalRuntime);
        Directory.Delete(runtime, recursive: true);
    }
}
Console.WriteLine($"All {checks} portable checks passed on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");

static async Task<(int Op, JsonElement Data)> Read(Stream stream)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var header = new byte[8];
    await stream.ReadExactlyAsync(header, timeout.Token);
    var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
    await stream.ReadExactlyAsync(body, timeout.Token);
    using var doc = JsonDocument.Parse(body);
    return (BinaryPrimitives.ReadInt32LittleEndian(header), doc.RootElement.Clone());
}
static async Task Write(Stream stream, int opcode, object value)
{
    var body = JsonSerializer.SerializeToUtf8Bytes(value);
    var header = new byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(header, opcode);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), body.Length);
    await stream.WriteAsync(header.AsMemory(0, 3));
    await stream.WriteAsync(header.AsMemory(3));
    await stream.WriteAsync(body);
}
