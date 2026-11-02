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
var nextSong = track with { Title = "Другой трек", Position = 19, Duration = 217, Updated = now.AddSeconds(4) };
await publisher.UpdateAsync(nextSong, now.AddSeconds(4));
var nextPayload = JsonSerializer.SerializeToElement(sent[^1]);
Check(sent.Count == 3 && sent[^2] == null, "track change clears previous activity before setting new timer");
Check(nextPayload.GetProperty("timestamps").GetProperty("start").GetInt64() == now.AddSeconds(4 - 19).ToUnixTimeSeconds(), "new song starts at actual 19 seconds, not previous song time");
await publisher.UpdateAsync(nextSong with { Position = 3, Updated = now.AddSeconds(6) }, now.AddSeconds(6));
Check(sent.Count == 5 && sent[^2] == null, "backward seek resets rendered Discord timer");
await publisher.UpdateAsync(nextSong with { Playing = false }, now.AddSeconds(8));
Check(sent[^1] == null, "pause clears activity immediately");
await publisher.UpdateAsync(nextSong with { Position = 3, Updated = now.AddSeconds(20) }, now.AddSeconds(20));
var resumed = JsonSerializer.SerializeToElement(sent[^1]);
Check(resumed.GetProperty("timestamps").GetProperty("start").GetInt64() == now.AddSeconds(17).ToUnixTimeSeconds(), "resume does not include paused wall time");
await publisher.UpdateAsync(nextSong with { Updated = DateTimeOffset.MinValue }, now.AddSeconds(22));
Check(sent[^2] == null && !sent[^1]!.ContainsKey("timestamps"), "lost timeline clears stale timestamps instead of leaving old timer");

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
