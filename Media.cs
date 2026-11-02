namespace MusicRpc;

public record Track(string AppId, string Source, string Title, string Artist, string Album,
    bool Playing, double Position, double Duration, DateTimeOffset Updated, bool Current,
    string? ArtworkUrl = null, string? TrackUrl = null, string TimingSource = "Windows", double PlaybackRate = 1);

public sealed class TrackSelector
{
    private string? previousApp;
    public Track? Select(IEnumerable<Track> tracks, Settings settings)
    {
        var candidates = tracks.Where(t => t.Playing && !string.IsNullOrWhiteSpace(t.Title)
            && ((settings.Telegram && t.Source is "AyuGram" or "Telegram / AyuGram")
                || (settings.AppleMusic && t.Source == "Apple Music"))).ToList();
        var selected = candidates.OrderByDescending(t => settings.Priority == t.Source
                || (settings.Priority == "Telegram / AyuGram" && t.Source == "AyuGram"))
            .ThenByDescending(t => t.Current).ThenByDescending(t => t.AppId == previousApp)
            .ThenByDescending(t => t.Updated).FirstOrDefault();
        previousApp = selected?.AppId;
        return selected;
    }
}

public static class Presence
{
    // Discord limits these fields to 128 UTF-8 bytes. Preserve complete Unicode characters.
    public static string Limit(string value)
    {
        var result = new System.Text.StringBuilder();
        int bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > 128) break;
            result.Append(rune.ToString());
        }
        var text = result.ToString();
        return text.Length == 1 ? text + "\u200b" : text;
    }

    public static double PositionAt(Track track, DateTimeOffset now)
    {
        var elapsed = track.Playing && track.Updated > DateTimeOffset.UnixEpoch
            ? Math.Max(0, (now - track.Updated).TotalSeconds) * track.PlaybackRate : 0;
        var position = Math.Max(0, track.Position + elapsed);
        return track.Duration > 0 ? Math.Min(position, track.Duration) : position;
    }

    public static Dictionary<string, object> Build(Track track, DateTimeOffset now)
    {
        var activity = new Dictionary<string, object>
        {
            ["name"] = DisplaySource(track),
            ["type"] = 2,
            ["details"] = Limit(track.Title),
            ["state"] = Limit(string.IsNullOrWhiteSpace(track.Artist) ? track.Source : track.Artist),
        };
        if (!string.IsNullOrWhiteSpace(track.ArtworkUrl))
            activity["assets"] = new { large_image = track.ArtworkUrl,
                large_text = Limit(string.IsNullOrWhiteSpace(track.Album) ? $"{track.Artist} — {track.Title}" : track.Album) };
        if (!string.IsNullOrWhiteSpace(track.TrackUrl))
            activity["buttons"] = new[] { new { label = "Открыть трек", url = track.TrackUrl } };
        if (track.Duration > 0 && track.Updated > DateTimeOffset.UnixEpoch && Math.Abs(track.PlaybackRate - 1) < .01)
        {
            var position = PositionAt(track, now);
            activity["timestamps"] = new { start = now.AddSeconds(-position).ToUnixTimeSeconds(),
                end = now.AddSeconds(track.Duration - position).ToUnixTimeSeconds() };
        }
        else if (track.Updated > DateTimeOffset.UnixEpoch && Math.Abs(track.PlaybackRate - 1) < .01)
            activity["timestamps"] = new { start = now.AddSeconds(-PositionAt(track, now)).ToUnixTimeSeconds() };
        return activity;
    }

    public static string DisplaySource(Track track) => track.Source == "Apple Music" ? "Apple Music" : "Telegram";
}
