using Windows.Media.Control;

namespace MusicRpc;

public sealed class MediaReader : IMediaReader
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    public async Task<List<Track>> ReadAsync()
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var currentId = manager.GetCurrentSession()?.SourceAppUserModelId;
        var result = new List<Track>();
        foreach (var session in manager.GetSessions())
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                var timeline = session.GetTimelineProperties();
                var appId = session.SourceAppUserModelId;
                result.Add(new Track(appId, SourceName(appId), properties.Title ?? "",
                    properties.Artist ?? "", properties.AlbumTitle ?? "",
                    session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds),
                    Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds),
                    timeline.LastUpdatedTime, appId == currentId));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A player can close while its session is being read. Other sessions remain usable.
                AppLog.Write($"Не удалось прочитать медиасессию: {ex.Message}");
            }
        }
        return result;
    }

    public static string SourceName(string appId)
    {
        if (appId.Contains("ayu", StringComparison.OrdinalIgnoreCase)) return "AyuGram";
        if (appId.Contains("telegram", StringComparison.OrdinalIgnoreCase)) return "Telegram / AyuGram";
        if (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("AppleInc.AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        return "Другое";
    }
}
