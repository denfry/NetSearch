using System.Text.Json;

namespace NetSearch.Core.Settings;

public sealed class AppSettings
{
    public List<string> Roots { get; set; } = new();
    public int AutoRefreshMinutes { get; set; } = 0; // 0 = disabled
    public int CrawlParallelism { get; set; } = 2;   // reserved for future parallel crawl
    public long ContentMaxFileBytes { get; set; } = 5_000_000;
    public List<string> TextExtensions { get; set; } = new()
    {
        "txt", "log", "csv", "md", "json", "xml", "html", "htm",
        "cs", "js", "ts", "py", "java", "sql", "ini", "cfg", "yml", "yaml",
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSearch");

    public static string DefaultSettingsPath => Path.Combine(DefaultDir, "settings.json");
    public static string DefaultDbPath => Path.Combine(DefaultDir, "index.db");

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
