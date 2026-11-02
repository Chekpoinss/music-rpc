using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MusicRpc;

public record CatalogMatch(string ArtworkUrl, string TrackUrl, string Album, double Duration);

public sealed class Catalog
{
    private readonly HttpClient http;
    private readonly Dictionary<string, (CatalogMatch? Match, DateTimeOffset Expires)> cache = new();
    private DateTimeOffset nextRequest;
    private DateTimeOffset appleRetry;

    public Catalog(HttpClient? http = null)
    {
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public static string Normalize(string text) => string.Concat(text.ToLowerInvariant().EnumerateRunes()
        .Where(System.Text.Rune.IsLetterOrDigit).Select(r => r.ToString()));

    public static CatalogMatch? Parse(JsonElement data, Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Artist)) return null;
        var title = Normalize(track.Title);
        var artist = Normalize(track.Artist);
        var matches = new List<(CatalogMatch Match, bool Album)>();
        foreach (var item in data.GetProperty("results").EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
            // Do not use a popular but unrelated song or a remix with a different title.
            if (Normalize(Get("trackName")) != title || Normalize(Get("artistName")) != artist) continue;
            var art = Get("artworkUrl100");
            var link = Get("trackViewUrl");
            if (!Uri.TryCreate(art, UriKind.Absolute, out var artUri) || artUri.Scheme != "https"
                || !artUri.Host.EndsWith(".mzstatic.com", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) || linkUri.Scheme != "https"
                || !(linkUri.Host == "music.apple.com" || linkUri.Host == "itunes.apple.com")) continue;
            var duration = item.TryGetProperty("trackTimeMillis", out var time) ? time.GetDouble() / 1000 : 0;
            if (track.Duration > 0 && Math.Abs(track.Duration - duration) > 5) continue;
            art = Regex.Replace(art, @"/100x100[^/]*\.(jpg|png)$", "/512x512bb.$1", RegexOptions.IgnoreCase);
            var album = Get("collectionName");
            matches.Add((new CatalogMatch(art, link, album, duration), Normalize(album) == Normalize(track.Album)));
        }
        return matches.OrderByDescending(x => x.Album).Select(x => x.Match).FirstOrDefault();
    }

    public async Task<CatalogMatch?> FindAsync(Track track)
    {
        if (string.IsNullOrWhiteSpace(track.Artist)) return null;
        var key = track.Artist + "\n" + track.Title + "\n" + track.Album;
        var now = DateTimeOffset.UtcNow;
        if (cache.TryGetValue(key, out var entry) && entry.Expires > now) return entry.Match;
        if (now < nextRequest) return null;
        nextRequest = now.AddSeconds(4);
        CatalogMatch? match = null;
        if (now >= appleRetry) try
        {
            var term = Uri.EscapeDataString(track.Artist + " " + track.Title);
            using var response = await http.GetAsync($"https://itunes.apple.com/search?term={term}&entity=song&media=music&limit=15&country=US");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            match = Parse(doc.RootElement, track);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            appleRetry = now.AddMinutes(10);
            AppLog.Write("Каталог Apple недоступен, используется Deezer: " + ex.Message);
        }
        if (match == null) try
        {
            var term = Uri.EscapeDataString(track.Artist + " " + track.Title);
            using var response = await http.GetAsync($"https://api.deezer.com/search?q={term}&limit=15");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            match = ParseDeezer(doc.RootElement, track);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Write("Каталог Deezer недоступен: " + ex.Message);
        }
        if (cache.Count >= 250) cache.Clear();
        cache[key] = (match, now.AddMinutes(match == null ? 2 : 120));
        return match;
    }

    public static CatalogMatch? ParseDeezer(JsonElement data, Track track)
    {
        if (!data.TryGetProperty("data", out var items) || string.IsNullOrWhiteSpace(track.Artist)) return null;
        var matches = new List<CatalogMatch>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("artist", out var artist) || !item.TryGetProperty("album", out var album)) continue;
            if (Normalize(item.GetProperty("title").GetString() ?? "") != Normalize(track.Title)
                || Normalize(artist.GetProperty("name").GetString() ?? "") != Normalize(track.Artist)) continue;
            var art = album.TryGetProperty("cover_big", out var cover) ? cover.GetString() ?? "" : "";
            var link = item.GetProperty("link").GetString() ?? "";
            if (!Uri.TryCreate(art, UriKind.Absolute, out var artUri) || artUri.Scheme != "https"
                || !artUri.Host.EndsWith(".dzcdn.net", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) || linkUri.Scheme != "https"
                || linkUri.Host is not ("www.deezer.com" or "deezer.com")) continue;
            var duration = item.GetProperty("duration").GetDouble();
            if (track.Duration > 0 && Math.Abs(track.Duration - duration) > 5) continue;
            matches.Add(new CatalogMatch(art, link, album.GetProperty("title").GetString() ?? "", duration));
        }
        return matches.OrderByDescending(x => Normalize(x.Album) == Normalize(track.Album)).FirstOrDefault();
    }
}
