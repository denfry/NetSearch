using CommunityToolkit.Mvvm.ComponentModel;
using NetSearch.Core.Settings;

namespace NetSearch.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly string _path;

    [ObservableProperty] private string _rootsText;
    [ObservableProperty] private int _autoRefreshMinutes;
    [ObservableProperty] private int _contentMaxMB;
    [ObservableProperty] private string _textExtensionsText;

    public SettingsViewModel(AppSettings settings, string path)
    {
        _settings = settings;
        _path = path;
        _rootsText = string.Join(Environment.NewLine, settings.Roots);
        _autoRefreshMinutes = settings.AutoRefreshMinutes;
        _contentMaxMB = (int)(settings.ContentMaxFileBytes / (1024 * 1024));
        _textExtensionsText = string.Join(", ", settings.TextExtensions);
    }

    public void Save()
    {
        _settings.Roots = RootsText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _settings.AutoRefreshMinutes = Math.Max(0, AutoRefreshMinutes);
        _settings.ContentMaxFileBytes = Math.Max(1, ContentMaxMB) * 1024L * 1024L;
        _settings.TextExtensions = TextExtensionsText
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('.').ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        SettingsStore.Save(_path, _settings);
    }
}
