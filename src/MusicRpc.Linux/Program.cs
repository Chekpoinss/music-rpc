using System.Text.Json;
using MusicRpc;

if (args.Contains("--help"))
{
    Console.WriteLine("""
    Music RPC — Linux / MPRIS
      --application-id ID     Save your Discord application ID
      --apple-player NAME     Explicit MPRIS name for your Apple Music client
      --priority auto|telegram|apple
      --no-artwork            Disable catalog searches (use only public MPRIS artwork)
      --diagnose              Print supported MPRIS sessions as JSON without connecting to Discord
    Requires playerctl and a desktop Discord in the same user session. Ctrl+C to exit.
    """);
    return;
}
string? Value(string flag)
{
    var index = Array.IndexOf(args, flag);
    if (index < 0) return null;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--")) throw new ArgumentException("Missing value for " + flag);
    return args[index + 1];
}
try
{
    var allowed = new[] { "--application-id", "--apple-player", "--priority", "--no-artwork", "--diagnose" };
    for (var i = 0; i < args.Length; i++)
    {
        if (!allowed.Contains(args[i])) throw new ArgumentException("Unknown option: " + args[i]);
        if (args[i] is "--application-id" or "--apple-player" or "--priority") { _ = Value(args[i]); i++; }
    }
    var settings = Settings.Load();
    var id = Value("--application-id");
    if (id != null)
    {
        if (!Settings.ValidId(id)) throw new ArgumentException("Application ID must contain 17–20 digits.");
        settings.ApplicationId = id;
        settings.Save();
    }
    settings.Priority = Value("--priority") switch
    {
        null => settings.Priority, "auto" => "Автоматически", "telegram" => "Telegram / AyuGram",
        "apple" => "Apple Music", _ => throw new ArgumentException("Priority: auto, telegram or apple")
    };
    var reader = new MprisMediaReader(Value("--apple-player"));
    if (args.Contains("--diagnose"))
    {
        Console.WriteLine(JsonSerializer.Serialize(await reader.ReadAsync(), new JsonSerializerOptions { WriteIndented = true }));
        return;
    }
    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Use the Windows application on Windows.");
    if (!Settings.ValidId(settings.ApplicationId)) throw new ArgumentException("Run once with --application-id YOUR_ID. Create an application at https://discord.com/developers/applications");
    Directory.CreateDirectory(Settings.DirectoryPath);
    using var instance = new FileStream(Path.Combine(Settings.DirectoryPath, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    using var rpc = new DiscordRpc();
    var publisher = new PlaybackPublisher(rpc.SetActivityAsync);
    var playbackClock = new PlaybackClock();
    var selector = new TrackSelector();
    var catalog = new Catalog();
    Task<CatalogMatch?>? coverTask = null;
    string? coverKey = null;
    DateTimeOffset retryAfter = default, coverAttempt = default;
    string lastStatus = "";
    Console.WriteLine("Music RPC running. Ctrl+C to exit.");
    try
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var track = selector.Select(await reader.ReadAsync(), settings);
                if (track != null && !args.Contains("--no-artwork") && track.ArtworkUrl == null)
                {
                    var key = track.Artist + "\n" + track.Title + "\n" + track.Album;
                    if (coverKey != key || (coverTask?.IsCompleted == true && DateTimeOffset.UtcNow - coverAttempt > TimeSpan.FromMinutes(2)))
                    {
                        coverKey = key; coverAttempt = DateTimeOffset.UtcNow;
                        coverTask = catalog.FindAsync(track);
                    }
                    if (coverTask?.IsCompleted == true && await coverTask is { } match)
                        track = track with { ArtworkUrl = match.ArtworkUrl, TrackUrl = match.TrackUrl,
                            Album = string.IsNullOrWhiteSpace(track.Album) ? match.Album : track.Album };
                }
                var now = DateTimeOffset.UtcNow;
                track = playbackClock.Update(track, now);
                if (!rpc.Connected && now >= retryAfter)
                {
                    await rpc.ConnectAsync(settings.ApplicationId);
                    publisher.Reset();
                }
                if (rpc.Connected) await publisher.UpdateAsync(track, now);
                var status = track == null ? "Waiting for music (paused activity is cleared)." : $"{Presence.DisplaySource(track)}: {track.Artist} — {track.Title}";
                if (status != lastStatus) { Console.WriteLine(status); lastStatus = status; }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                rpc.Dispose(); publisher.Reset(); retryAfter = DateTimeOffset.UtcNow.AddSeconds(10);
                if (lastStatus != ex.Message) { Console.Error.WriteLine(ex.Message); AppLog.Write(ex.Message); lastStatus = ex.Message; }
            }
            await Task.Delay(2000, stop.Token);
        }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    finally
    {
        if (rpc.Connected) try { await rpc.SetActivityAsync(null); } catch (Exception) { }
    }
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
