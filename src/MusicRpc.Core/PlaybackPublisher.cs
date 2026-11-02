namespace MusicRpc;

// Keep playback transitions in one place for both frontends. Discord's rendered timer
// must be torn down on a new song/seek rather than inheriting the previous activity.
public sealed class PlaybackPublisher(Func<Dictionary<string, object>?, Task> send)
{
    private Track? previous;
    private DateTimeOffset lastSent;
    private bool initialized;

    public void Reset() { previous = null; lastSent = default; initialized = false; }
    public static bool SameTrack(Track? a, Track? b) => a != null && b != null
        && a.AppId == b.AppId && a.Title == b.Title && a.Artist == b.Artist;

    public async Task UpdateAsync(Track? track, DateTimeOffset now)
    {
        if (track?.Playing == false) track = null;
        var identityChanged = previous != null && track != null && !SameTrack(previous, track);
        var timingChanged = previous != null && track != null
            && (previous.Updated > DateTimeOffset.UnixEpoch) != (track.Updated > DateTimeOffset.UnixEpoch);
        var seeked = SameTrack(previous, track) && previous!.Updated > DateTimeOffset.UnixEpoch
            && track!.Updated > DateTimeOffset.UnixEpoch
            && Math.Abs(Presence.PositionAt(track, now) - Presence.PositionAt(previous, now)) >= 2;
        if (identityChanged || seeked || timingChanged)
        {
            await send(null);
            previous = null;
            initialized = false;
        }
        var changed = previous?.AppId != track?.AppId || previous?.Title != track?.Title
            || previous?.Artist != track?.Artist || previous?.Album != track?.Album
            || previous?.ArtworkUrl != track?.ArtworkUrl || previous?.Duration != track?.Duration
            || previous?.PlaybackRate != track?.PlaybackRate;
        if (!initialized || changed || (now - lastSent).TotalSeconds >= 10)
        {
            await send(track == null ? null : Presence.Build(track, now));
            previous = track; lastSent = now; initialized = true;
        }
    }
}
