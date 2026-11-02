using System.Text.Json;

namespace MusicRpc;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--diagnose"))
        {
            try
            {
                var tracks = new MediaReader().ReadAsync().GetAwaiter().GetResult();
                var catalog = new Catalog();
                var timeline = new TelegramTimeline();
                for (var i = 0; i < tracks.Count; i++)
                {
                    if (tracks[i].Source == "Другое") continue;
                    var match = catalog.FindAsync(tracks[i]).GetAwaiter().GetResult();
                    tracks[i] = timeline.EnrichAsync(tracks[i], match?.Duration ?? 0).GetAwaiter().GetResult();
                    if (match != null) tracks[i] = tracks[i] with { ArtworkUrl = match.ArtworkUrl, TrackUrl = match.TrackUrl, Album = match.Album };
                }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.json"),
                    JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.json"), ex.ToString());
                Environment.ExitCode = 1;
            }
            return;
        }
        using var mutex = new Mutex(true, @"Local\MusicRpc.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("Music RPC уже запущен. Открой значок в трее рядом с часами.", "Music RPC");
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Contains("--background")));
    }
}
