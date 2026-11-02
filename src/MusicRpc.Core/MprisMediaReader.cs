using System.Diagnostics;
using System.Globalization;

namespace MusicRpc;

// playerctl talks to the user's MPRIS session bus. Arguments never go through a shell.
public sealed class MprisMediaReader(string? applePlayer = null) : IMediaReader
{
    public const char Separator = '\u001f';
    public const string Format = "{{status}}\u001f{{position}}\u001f{{mpris:length}}\u001f{{title}}\u001f{{artist}}\u001f{{album}}\u001f{{mpris:artUrl}}\u001f{{xesam:url}}";

    public static string SourceName(string player, string? applePlayer = null)
    {
        if (player.Contains("ayu", StringComparison.OrdinalIgnoreCase)) return "AyuGram";
        if (player.Contains("telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram / AyuGram";
        if (player.Equals(applePlayer, StringComparison.Ordinal) || player.Contains("cider", StringComparison.OrdinalIgnoreCase)
            || player.Contains("applemusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        return "Другое";
    }

    public static Track? Parse(string player, string output, DateTimeOffset captured, string? applePlayer = null)
    {
        var parts = output.TrimEnd('\r', '\n').Split(Separator);
        if (parts.Length != 8 || parts[0] is not ("Playing" or "Paused" or "Stopped")) return null;
        var positionKnown = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var position)
            && double.IsFinite(position) && position >= 0;
        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var duration);
        if (!double.IsFinite(duration) || duration < 0) duration = 0;
        string? Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? value : null;
        return new Track(player, SourceName(player, applePlayer), parts[3], parts[4], parts[5], parts[0] == "Playing",
            positionKnown ? position / 1_000_000 : 0, duration / 1_000_000,
            positionKnown ? captured : DateTimeOffset.MinValue, false, Https(parts[6]), Https(parts[7]), "MPRIS");
    }

    public async Task<List<Track>> ReadAsync()
    {
        var list = await RunAsync(["--list-all"]);
        var tracks = new List<Track>();
        foreach (var player in list.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            if (SourceName(player, applePlayer) == "Другое") continue;
            try
            {
                var output = await RunAsync(["--player=" + player, "metadata", "--format", Format]);
                var track = Parse(player, output, DateTimeOffset.UtcNow, applePlayer);
                if (track != null) tracks.Add(track);
            }
            catch (IOException ex) { AppLog.Write("Медиаплеер исчез или не ответил: " + ex.Message); }
        }
        return tracks;
    }

    private static async Task<string> RunAsync(string[] args)
    {
        var start = new ProcessStartInfo("playerctl") { RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["LC_ALL"] = "C";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить playerctl.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var text = await output;
                var stderr = await error;
                if (process.ExitCode != 0)
                {
                    if (stderr.Contains("No players found", StringComparison.OrdinalIgnoreCase)) return "";
                    throw new IOException("playerctl: " + stderr.Trim());
                }
                return text;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new IOException("playerctl не ответил за 3 секунды.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        { throw new IOException("Нужен playerctl: установи пакет playerctl через пакетный менеджер дистрибутива.", ex); }
    }
}
