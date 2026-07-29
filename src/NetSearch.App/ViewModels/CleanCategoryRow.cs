using CommunityToolkit.Mvvm.ComponentModel;
using NetSearch.Core.Cleaning;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public partial class CleanCategoryRow : ObservableObject
{
    public CleanCategoryRow(CleanCategory category, bool enabled)
    {
        Category = category;
        IsEnabled = enabled;
        IsSelected = enabled && category.DefaultSelected;
    }

    public CleanCategory Category { get; }
    public string DisplayName => Category.DisplayName;
    public string Description => Category.Description;
    public bool NeedsAdmin => Category.NeedsAdmin;
    public bool UsesRecycleBin => Category.UsesRecycleBin;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private bool _scanned;

    public IReadOnlyList<CleanItem> Items { get; set; } = System.Array.Empty<CleanItem>();
    public string SizeText => Scanned ? SizeFormatter.Format(SizeBytes) : "—";

    partial void OnSizeBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
    partial void OnScannedChanged(bool value) => OnPropertyChanged(nameof(SizeText));
}
