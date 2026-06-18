using NetSearch.Core.Models;

namespace NetSearch.App.ViewModels;

public sealed class FileRow
{
    public FileRow(FileEntry entry) => Entry = entry;

    public FileEntry Entry { get; }
    public string Name => Entry.Name;
    public string Path => Entry.FullPath;
    public string SizeText => Entry.IsDir ? "" : FormatSize(Entry.Size);
    public string Modified =>
        DateTimeOffset.FromUnixTimeSeconds(Entry.Modified).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Type => Entry.IsDir ? "Папка" : (Entry.Ext.Length > 0 ? Entry.Ext.ToUpperInvariant() : "Файл");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
