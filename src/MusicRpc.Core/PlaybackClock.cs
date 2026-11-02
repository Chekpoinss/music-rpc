namespace MusicRpc;

// Anchor the clock to measured playback. Repeated rounded labels and a briefly
// unavailable accessibility provider must not restart or slow the rendered timer.
public sealed class PlaybackClock
{
    private Track? anchor;
    private double? lastObserved;
    private DateTimeOffset lastMeasurement;

    public Track? Update(Track? sample, DateTimeOffset now)
    {
        if (sample == null || !sample.Playing)
        {
            anchor = null; lastObserved = null;
            return sample;
        }
        var measured = sample.Updated > DateTimeOffset.UnixEpoch;
        if (!PlaybackPublisher.SameTrack(anchor, sample))
        {
            anchor = sample;
            lastObserved = measured ? sample.Position : null;
            lastMeasurement = measured ? now : default;
            return sample;
        }
        if (measured)
        {
            // A whole-second label may repeat between polls. It is not a backward seek.
            var changed = lastObserved == null || Math.Abs(sample.Position - lastObserved.Value) > .01;
            if (changed || anchor!.Updated <= DateTimeOffset.UnixEpoch || anchor.PlaybackRate != sample.PlaybackRate)
            {
                if (anchor!.Updated <= DateTimeOffset.UnixEpoch || anchor.PlaybackRate != sample.PlaybackRate
                    || Math.Abs(Presence.PositionAt(sample, now) - Presence.PositionAt(anchor, now)) >= 2)
                    anchor = sample;
                lastObserved = sample.Position;
                lastMeasurement = now;
            }
        }
        if (anchor!.Updated <= DateTimeOffset.UnixEpoch || now - lastMeasurement > TimeSpan.FromSeconds(20))
            return sample with { Updated = DateTimeOffset.MinValue };
        if (sample.Duration > 0) anchor = anchor with { Duration = sample.Duration };
        // Duration is either authoritative player metadata or a catalog match, never a rounded slider estimate.
        return sample with { Position = anchor.Position, Updated = anchor.Updated,
            Duration = sample.Duration > 0 ? sample.Duration : anchor.Duration,
            PlaybackRate = anchor.PlaybackRate };
    }
}
