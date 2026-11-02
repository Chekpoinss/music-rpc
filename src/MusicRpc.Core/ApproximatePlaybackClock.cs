namespace MusicRpc;

// Windows Telegram exposes track/status reliably, but not a usable position.
// Count observed playing time instead; never read the window or infer seeks.
public sealed class ApproximatePlaybackClock
{
    private sealed class State(Track track, DateTimeOffset now)
    {
        public Track Track = track;
        public DateTimeOffset Sampled = now;
        public double Elapsed;
        public double Duration = ValidDuration(track.Duration);
    }

    private readonly Dictionary<string, State> states = new();
    public static bool Supports(Track track) => track.Source is "AyuGram" or "Telegram / AyuGram";
    private static double ValidDuration(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    public void Observe(IEnumerable<Track> snapshots, DateTimeOffset now)
    {
        var observed = new HashSet<string>();
        foreach (var track in snapshots.Where(t => Supports(t) && !string.IsNullOrWhiteSpace(t.Title)))
        {
            observed.Add(track.AppId);
            if (!states.TryGetValue(track.AppId, out var state) || !PlaybackPublisher.SameTrack(state.Track, track))
            {
                states[track.AppId] = new State(track, now);
                continue;
            }
            // Capture the last playing interval before freezing on pause. Resume does not
            // include the paused wall time. Missing position fields never affect the clock.
            if (state.Track.Playing) state.Elapsed += Math.Max(0, (now - state.Sampled).TotalSeconds);
            state.Track = track;
            state.Sampled = now;
            if (ValidDuration(track.Duration) > 0) state.Duration = track.Duration;
        }
        foreach (var removed in states.Keys.Where(key => !observed.Contains(key)).ToArray()) states.Remove(removed);
    }

    public Track Apply(Track track, DateTimeOffset now, double catalogDuration = 0)
    {
        if (!Supports(track)) return track;
        if (!states.TryGetValue(track.AppId, out var state) || !PlaybackPublisher.SameTrack(state.Track, track))
            states[track.AppId] = state = new State(track, now);
        if (state.Duration <= 0 && ValidDuration(catalogDuration) > 0) state.Duration = catalogDuration;
        var elapsed = state.Elapsed + (state.Track.Playing ? Math.Max(0, (now - state.Sampled).TotalSeconds) : 0);
        var known = state.Duration > 0;
        return track with
        {
            Position = known ? elapsed % state.Duration : 0,
            Duration = state.Duration,
            Updated = known ? now : DateTimeOffset.MinValue,
            PlaybackRate = 1,
            TimingSource = "Приблизительно"
        };
    }
}
