namespace MusicRpc;

// Atomic replacements: never queue CLEAR + SET on a seek. Coalesce rapid changes
// and allow at most five activity commands in any rolling twenty-second window.
public sealed class PlaybackPublisher(Func<Dictionary<string, object>?, Task> send)
{
    private Track? previous;
    private DateTimeOffset lastSent;
    private bool initialized;
    private readonly Queue<DateTimeOffset> writes = new();

    public void Reset() { previous = null; lastSent = default; initialized = false; }
    public static bool SameTrack(Track? a, Track? b) => a != null && b != null
        && a.AppId == b.AppId && a.Title == b.Title && a.Artist == b.Artist;

    public async Task UpdateAsync(Track? track, DateTimeOffset now)
    {
        if (track?.Playing == false) track = null;
        while (writes.TryPeek(out var oldest) && now - oldest >= TimeSpan.FromSeconds(20)) writes.Dequeue();
        if (writes.Count >= 5) return;
        // Reserve immediate delivery for pause/stop. Coalesce all other rapid updates.
        if (initialized && track != null && now - lastSent < TimeSpan.FromSeconds(5)) return;
        var identityChanged = previous != null && track != null && !SameTrack(previous, track);
        var timingChanged = previous != null && track != null
            && (previous.Updated > DateTimeOffset.UnixEpoch) != (track.Updated > DateTimeOffset.UnixEpoch);
        var seeked = SameTrack(previous, track) && previous!.Updated > DateTimeOffset.UnixEpoch
            && track!.Updated > DateTimeOffset.UnixEpoch
            && Math.Abs(Presence.PositionAt(track, now) - Presence.PositionAt(previous, now)) >= 2;
        var changed = previous?.AppId != track?.AppId || previous?.Title != track?.Title
            || previous?.Artist != track?.Artist || previous?.Album != track?.Album
            || previous?.ArtworkUrl != track?.ArtworkUrl || previous?.Duration != track?.Duration
            || previous?.PlaybackRate != track?.PlaybackRate;
        if (!initialized || changed || identityChanged || seeked || timingChanged || (now - lastSent).TotalSeconds >= 30)
        {
            await send(track == null ? null : Presence.Build(track, now));
            writes.Enqueue(now);
            previous = track; lastSent = now; initialized = true;
        }
    }
}
