using System.Windows.Media;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public sealed class TreemapTile
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required Brush Fill { get; init; }
    public required string Label { get; init; }
    public required string Tooltip { get; init; }
    public required FolderNode Node { get; init; }
    public bool ShowLabel => Width > 46 && Height > 22;
}
