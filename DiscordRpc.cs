using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;

namespace MusicRpc;

// One request at a time; all reads and writes have a deadline. No Discord user token is used.
public sealed class DiscordRpc : IDisposable
{
    private Stream? pipe;
    public bool Connected => pipe != null;
    public JsonElement? LastResponse { get; private set; }
    public void Dispose() { pipe?.Dispose(); pipe = null; }

    public async Task ConnectAsync(string applicationId, string? testPipe = null)
    {
        Dispose();
        if (!OperatingSystem.IsWindows() && testPipe == null)
        {
            await ConnectUnixAsync();
            await HandshakeAsync(applicationId);
            return;
        }
        for (int i = 0; i < (testPipe == null ? 10 : 1); i++)
        {
            var candidate = new NamedPipeClientStream(".", testPipe ?? $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await candidate.ConnectAsync(120); }
            catch (TimeoutException) { candidate.Dispose(); continue; }
            catch (IOException) { candidate.Dispose(); continue; }
            pipe = candidate;
            await HandshakeAsync(applicationId);
            return;
        }
        throw new IOException("Discord не найден. Запусти настольный Discord; подключение повторится автоматически.");
    }

    public static IEnumerable<string> UnixSocketPaths()
    {
        var roots = new[] { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            Environment.GetEnvironmentVariable("TMPDIR"), Environment.GetEnvironmentVariable("TMP"),
            Environment.GetEnvironmentVariable("TEMP"), "/tmp" }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).Distinct();
        foreach (var root in roots)
        {
            // Native Discord and the common Flatpak/Snap socket locations.
            foreach (var directory in new[] { root, Path.Combine(root, "app/com.discordapp.Discord"), Path.Combine(root, "snap.discord") })
                for (int i = 0; i < 10; i++) yield return Path.Combine(directory, $"discord-ipc-{i}");
        }
    }

    private async Task ConnectUnixAsync()
    {
        foreach (var path in UnixSocketPaths())
        {
            if (!File.Exists(path)) continue;
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), timeout.Token);
                pipe = new NetworkStream(socket, ownsSocket: true);
                return;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
            { socket.Dispose(); }
        }
        throw new IOException("Discord IPC не найден. Запусти настольный Discord в этой пользовательской сессии.");
    }

    private async Task HandshakeAsync(string applicationId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await SendAsync(0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = applicationId }), timeout.Token);
            while (true)
            {
                var reply = await ReadAsync(timeout.Token);
                ThrowIfError(reply);
                if (reply.TryGetProperty("evt", out var evt) && evt.GetString() == "READY") return;
            }
        }
        catch { Dispose(); throw; }
    }

    public async Task SetActivityAsync(Dictionary<string, object>? activity)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var nonce = Guid.NewGuid().ToString();
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId, activity }, nonce });
            await SendAsync(1, payload, timeout.Token);
            while (true)
            {
                var reply = await ReadAsync(timeout.Token);
                ThrowIfError(reply);
                if (reply.TryGetProperty("nonce", out var received) && received.GetString() == nonce)
                {
                    LastResponse = reply;
                    return;
                }
            }
        }
        catch { Dispose(); throw; }
    }

    private static void ThrowIfError(JsonElement reply)
    {
        if (reply.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR")
            throw new IOException("Discord отклонил запрос: " + reply.GetProperty("data").ToString());
    }

    private async Task SendAsync(int opcode, byte[] payload, CancellationToken token)
    {
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await pipe!.WriteAsync(frame, token);
        await pipe.FlushAsync(token);
    }

    private async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            var header = new byte[8];
            await pipe!.ReadExactlyAsync(header, token);
            var opcode = BinaryPrimitives.ReadInt32LittleEndian(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length is < 0 or > 1_048_576) throw new IOException("Некорректный размер ответа Discord.");
            var body = new byte[length];
            await pipe.ReadExactlyAsync(body, token);
            if (opcode == 3) { await SendAsync(4, body, token); continue; }
            if (opcode == 2) throw new IOException("Discord закрыл соединение: " + System.Text.Encoding.UTF8.GetString(body));
            if (opcode == 4) continue;
            if (opcode != 1) throw new IOException("Неизвестный ответ Discord.");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }
}
