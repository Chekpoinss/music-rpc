using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace MusicRpc;

public record PlayerTiming(double Position, double Duration, double Rate, DateTimeOffset Captured);

// Read only the compact player subtree, never message lists or edit fields.
// Qt exposes the elapsed label even when AyuGram's Windows timeline is empty.
public sealed class TelegramTimeline
{
    private Task<PlayerTiming?>? pending;
    private string? pendingKey;

    public async Task<Track> EnrichAsync(Track track, double catalogDuration)
    {
        if (track.Source is not ("AyuGram" or "Telegram / AyuGram") || track.Duration > 0) return track;
        var key = track.AppId + "\n" + track.Artist + "\n" + track.Title;
        if (pending == null)
        {
            pendingKey = key;
            pending = Task.Run(() => Read(track, catalogDuration));
        }
        var work = pending;
        if (await Task.WhenAny(work, Task.Delay(700)) != work) return track;
        pending = null;
        try
        {
            var timing = await work;
            if (pendingKey != key || timing == null || DateTimeOffset.UtcNow - timing.Captured > TimeSpan.FromSeconds(2)) return track;
            return track with { Position = timing.Position, Duration = timing.Duration,
                Updated = timing.Captured, TimingSource = "Плеер Telegram", PlaybackRate = timing.Rate };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Write("Позиция плеера Telegram недоступна: " + ex.Message);
            return track;
        }
    }

    public static double? ParseTime(string text)
    {
        if (!Regex.IsMatch(text, @"^\d{1,3}:\d{2}(:\d{2})?$")) return null;
        var parts = text.Split(':').Select(int.Parse).ToArray();
        if (parts.Skip(1).Any(x => x >= 60)) return null;
        return parts.Aggregate(0d, (value, part) => value * 60 + part);
    }

    private static PlayerTiming? Read(Track track, double catalogDuration)
    {
        foreach (var process in Process.GetProcessesByName(track.Source == "AyuGram" ? "AyuGram" : "Telegram"))
        {
            using (process)
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;
                var player = FindPlayer(AutomationElement.FromHandle(handle));
                if (player == null) continue;
                var nodes = player.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().ToArray();
                var names = nodes.Where(n => n.Current.ControlType == ControlType.Text).Select(n => n.Current.Name).ToArray();
                var title = Catalog.Normalize(track.Title);
                if (!names.Any(n => Catalog.Normalize(n).Contains(title))) continue;
                var position = names.Select(ParseTime).FirstOrDefault(x => x.HasValue);
                if (position == null) continue;
                double duration = catalogDuration;
                if (duration <= 0)
                {
                    var slider = nodes.FirstOrDefault(n => n.Current.ControlType == ControlType.Slider);
                    if (slider != null && slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var raw))
                    {
                        var range = ((RangeValuePattern)raw).Current;
                        var fraction = (range.Value - range.Minimum) / (range.Maximum - range.Minimum);
                        if (fraction > .02 && fraction <= 1) duration = Math.Round(position.Value / fraction);
                    }
                    if (duration <= 0 && slider != null && slider.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
                    {
                        var percent = ((ValuePattern)value).Current.Value.Trim().TrimEnd('%');
                        if (double.TryParse(percent, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 2 && p <= 100)
                            duration = Math.Round(position.Value * 100 / p);
                    }
                }
                var speed = nodes.Select(n => n.Current.Name).Select(n => Regex.Match(n, @"(?:speed|скорость).*?(\d+(?:[.,]\d+)?)\s*[xх×]", RegexOptions.IgnoreCase))
                    .FirstOrDefault(m => m.Success);
                double rate = speed != null && double.TryParse(speed.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
                return new PlayerTiming(position.Value, duration, rate, DateTimeOffset.UtcNow);
            }
        }
        return null;
    }

    private static AutomationElement? FindPlayer(AutomationElement root)
    {
        var queue = new Queue<(AutomationElement Node, int Depth)>();
        queue.Enqueue((root, 0));
        var walker = TreeWalker.ControlViewWalker;
        int visited = 0;
        while (queue.Count > 0 && visited++ < 100)
        {
            var (node, depth) = queue.Dequeue();
            var id = node.Current.AutomationId;
            if (id.EndsWith(".class Media::Player::Widget", StringComparison.Ordinal)) return node;
            if (depth >= 9 || id.Contains("HistoryWidget") || id.Contains("Dialogs::Widget")) continue;
            for (var child = walker.GetFirstChild(node); child != null; child = walker.GetNextSibling(child))
            {
                var type = child.Current.ControlType;
                if (type == ControlType.Group || type == ControlType.Pane || type == ControlType.Window)
                    queue.Enqueue((child, depth + 1));
            }
        }
        return null;
    }
}
