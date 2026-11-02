using System.Text.Json;

namespace MusicRpc;

public sealed class Settings
{
    public string ApplicationId { get; set; } = "";
    public bool Telegram { get; set; } = true;
    public bool AppleMusic { get; set; } = true;
    public string Priority { get; set; } = "Автоматически";
    public static string DirectoryPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicRpc")
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "music-rpc");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public static bool ValidId(string id) => id.Length is >= 17 and <= 20 && id.All(char.IsAsciiDigit);

    public static Settings Load()
    {
        if (!File.Exists(FilePath)) return new();
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"Не удалось загрузить настройки: {ex.Message}");
            return new();
        }
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}

public static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            var file = Path.Combine(Settings.DirectoryPath, "music-rpc.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
