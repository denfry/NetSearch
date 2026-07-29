using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.App.Services;
using NetSearch.Core.DiskUsage;
using NetSearch.Core.Indexing;

namespace NetSearch.App.ViewModels;

public partial class DiskUsageViewModel : ObservableObject
{
    private double _lastWidth = 600, _lastHeight = 360;

    public ObservableCollection<DriveUsage> Drives { get; } = new();
    public ObservableCollection<FolderNode> TreeRoots { get; } = new();
    public ObservableCollection<TreemapTile> Tiles { get; } = new();

    [ObservableProperty] private DriveUsage? _selectedDrive;
    [ObservableProperty] private bool _isElevated = ElevationService.IsElevated;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _status = "Выберите диск и нажмите «Сканировать».";
    [ObservableProperty] private FolderNode? _currentNode;

    private CancellationTokenSource? _cts;

    public DiskUsageViewModel()
    {
        foreach (var d in DriveProbe.FixedDrives()) Drives.Add(d);
        SelectedDrive = Drives.Count > 0 ? Drives[0] : null;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || SelectedDrive is not { } drive) return;
        IsScanning = true;
        _cts = new CancellationTokenSource();
        Status = $"Сканирование {drive.RootPath}…";
        var progress = new Progress<CrawlProgress>(p => Status = $"Сканирование… {p.Count} объектов");
        try
        {
            var token = _cts.Token;
            var root = await Task.Run(() => new DiskUsageScanner().Scan(drive.RootPath, token, progress), token);
            TreeRoots.Clear();
            TreeRoots.Add(root);
            CurrentNode = root;
            LayoutTiles(_lastWidth, _lastHeight);
            Status = $"{drive.RootPath} — {root.SizeText} в {root.FileCount} файлах";
        }
        catch (OperationCanceledException) { Status = "Сканирование отменено."; }
        catch (Exception ex) { Status = "Ошибка сканирования: " + ex.Message; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void CancelScan() => _cts?.Cancel();

    [RelayCommand] private void RelaunchAdmin() => ElevationService.RelaunchAsAdmin();

    [RelayCommand]
    private void Drill(FolderNode? node)
    {
        if (node is null || node.Children.Count == 0) return;
        CurrentNode = node;
        LayoutTiles(_lastWidth, _lastHeight);
    }

    [RelayCommand]
    private void DrillUp()
    {
        if (CurrentNode?.Parent is { } p) { CurrentNode = p; LayoutTiles(_lastWidth, _lastHeight); }
    }

    [RelayCommand]
    private void OpenFolder(FolderNode? node)
    {
        var target = node ?? CurrentNode;
        if (target is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target.FullPath}\"")); }
        catch (Exception ex) { Status = "Не удалось открыть: " + ex.Message; }
    }

    public void LayoutTiles(double width, double height)
    {
        _lastWidth = width > 0 ? width : _lastWidth;
        _lastHeight = height > 0 ? height : _lastHeight;
        Tiles.Clear();
        if (CurrentNode is null) return;

        var children = CurrentNode.Children.Where(c => c.SizeBytes > 0).ToList();
        var weights = children.Select(c => (double)c.SizeBytes).ToList();
        var rects = Treemap.Squarify(_lastWidth, _lastHeight, weights);
        for (int i = 0; i < children.Count; i++)
        {
            var r = rects[i];
            if (r.Width <= 0 || r.Height <= 0) continue;
            var c = children[i];
            Tiles.Add(new TreemapTile
            {
                X = r.X, Y = r.Y, Width = r.Width, Height = r.Height,
                Fill = new SolidColorBrush(ColorFor(i)),
                Label = $"{c.Name}  {c.SizeText}",
                Tooltip = $"{c.FullPath}\n{c.SizeText} • {c.FileCount} файлов",
                Node = c,
            });
        }
    }

    // Stable, distinct fill per child index, spread around the accent blue.
    private static Color ColorFor(int i)
    {
        double hue = (210 + i * 47) % 360;
        return FromHsl(hue, 0.55, 0.55);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60  => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _     => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
