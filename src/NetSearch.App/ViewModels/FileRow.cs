using NetSearch.Core.DiskUsage;
using NetSearch.Core.Models;

namespace NetSearch.App.ViewModels;

public sealed class FileRow
{
    public FileRow(FileEntry entry) => Entry = entry;

    public FileEntry Entry { get; }
    public string Name => Entry.Name;
    public string Path => Entry.FullPath;
    public string SizeText => Entry.IsDir ? "" : SizeFormatter.Format(Entry.Size);
    public string Modified =>
        DateTimeOffset.FromUnixTimeSeconds(Entry.Modified).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string Type => Entry.IsDir ? "Папка" : (Entry.Ext.Length > 0 ? Entry.Ext.ToUpperInvariant() : "Файл");
}
