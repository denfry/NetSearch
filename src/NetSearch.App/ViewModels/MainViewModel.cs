using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Search;
using NetSearch.Core.Settings;
using NetSearch.Core.Storage;

namespace NetSearch.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IndexStore _store;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private List<FileEntry> _all = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _autoRefresh;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchMode _selectedMode = SearchMode.Substring;
    [ObservableProperty] private string _minSize = "";
    [ObservableProperty] private string _maxSize = "";
    [ObservableProperty] private string _extensions = "";
    [ObservableProperty] private EntryKind _selectedKind = EntryKind.All;
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<FileRow> Results { get; } = new();
    public FileRow? SelectedRow { get; set; }

    public Array Modes => Enum.GetValues(typeof(SearchMode));
    public Array Kinds => Enum.GetValues(typeof(EntryKind));

    public MainViewModel(IndexStore store, AppSettings settings, string settingsPath)
    {
        _store = store;
        _settings = settings;
        _settingsPath = settingsPath;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };

        _autoRefresh = new DispatcherTimer();
        _autoRefresh.Tick += async (_, _) => await RefreshAsync();
        ConfigureAutoRefresh();

        LoadIndexIntoMemory();
    }

    public void ConfigureAutoRefresh()
    {
        _autoRefresh.Stop();
        if (_settings.AutoRefreshMinutes > 0)
        {
            _autoRefresh.Interval = TimeSpan.FromMinutes(_settings.AutoRefreshMinutes);
            _autoRefresh.Start();
        }
    }

    private void LoadIndexIntoMemory()
    {
        _all = _store.LoadAll().ToList();
        StatusText = $"Индекс: {_all.Count} записей";
        RunSearch();
    }

    partial void OnSearchTextChanged(string value) => Restart();
    partial void OnSelectedModeChanged(SearchMode value) => Restart();
    partial void OnMinSizeChanged(string value) => Restart();
    partial void OnMaxSizeChanged(string value) => Restart();
    partial void OnExtensionsChanged(string value) => Restart();
    partial void OnSelectedKindChanged(EntryKind value) => Restart();

    private void Restart() { _debounce.Stop(); _debounce.Start(); }

    private void RunSearch()
    {
        var query = QueryBuilder.Build(SearchText, SelectedMode, MinSize, MaxSize,
            after: null, before: null, Extensions, SelectedKind);
        var snapshot = _all;
        Task.Run(() => SearchEngine.Search(snapshot, query))
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    StatusText = "Ошибка поиска: " + (t.Exception?.GetBaseException().Message ?? "неизвестно");
                    return;
                }
                Results.Clear();
                foreach (var e in t.Result.Take(50_000))
                    Results.Add(new FileRow(e));
                StatusText = $"Найдено {t.Result.Count} из {snapshot.Count}";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Индексирование…";
        try
        {
            await Task.Run(() =>
            {
                var mgr = new IndexManager(_store, () => new Crawler());
                foreach (var path in _settings.Roots)
                {
                    var id = _store.UpsertRoot(path);
                    mgr.UpdateRoot(id, path, CancellationToken.None);
                }
            });
            LoadIndexIntoMemory();
            StatusText = $"Обновлено в {DateTime.Now:HH:mm}. Записей: {_all.Count}";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка индексирования: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ContentSearchAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(SearchText)) return;
        IsBusy = true;
        try
        {
            var current = Results.Select(r => r.Entry).ToList();
            var searcher = new ContentSearcher(new ContentSearchOptions(
                _settings.ContentMaxFileBytes, _settings.TextExtensions, _settings.CrawlParallelism));
            var progress = new Progress<int>(n => StatusText = $"Просмотрено файлов: {n}");
            var matches = await searcher.SearchAsync(current, SearchText,
                useRegex: SelectedMode == SearchMode.Regex, progress, CancellationToken.None);

            Results.Clear();
            foreach (var m in matches) Results.Add(new FileRow(m.Entry));
            StatusText = $"Совпадений по содержимому: {matches.Count}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_settings, _settingsPath);
        var win = new Views.SettingsWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            ConfigureAutoRefresh();
            StatusText = "Настройки сохранены. Нажмите «Обновить» для переиндексации.";
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedRow is null) return;
        TryStart(new ProcessStartInfo(SelectedRow.Path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRow is null) return;
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedRow.Path}\""));
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedRow is null) return;
        Clipboard.SetText(SelectedRow.Path);
    }

    public void Shutdown()
    {
        _debounce.Stop();
        _autoRefresh.Stop();
    }

    private void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex) { StatusText = "Не удалось открыть: " + ex.Message; }
    }
}
