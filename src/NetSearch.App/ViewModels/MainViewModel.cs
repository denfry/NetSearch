using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchMode _selectedMode = SearchMode.Substring;
    [ObservableProperty] private string _minSize = "";
    [ObservableProperty] private string _maxSize = "";
    [ObservableProperty] private string _extensions = "";
    [ObservableProperty] private EntryKind _selectedKind = EntryKind.All;
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private DateTime? _modifiedAfter;
    [ObservableProperty] private DateTime? _modifiedBefore;
    [ObservableProperty] private bool _filtersExpanded;
    [ObservableProperty] private bool _filesOnly;
    [ObservableProperty] private bool _foldersOnly;
    [ObservableProperty] private bool _showOnboarding = true;

    public ObservableCollection<FileRow> Results { get; } = new();
    public FileRow? SelectedRow { get; set; }

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
        CheckRootsReachability();
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

    private void CheckRootsReachability()
    {
        var roots = _settings.Roots.ToList();
        if (roots.Count == 0) return;
        Task.Run(() => roots.Where(r => !string.IsNullOrWhiteSpace(r) && !Directory.Exists(r)).Count())
            .ContinueWith(t =>
            {
                if (!t.IsFaulted && t.Result > 0)
                    StatusText += $"  ⚠ Недоступно путей: {t.Result} — индекс может быть устаревшим.";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void LoadIndexIntoMemory()
    {
        _all = _store.LoadAll().ToList();
        ShowOnboarding = _all.Count == 0;
        StatusText = $"Индекс: {_all.Count} записей";
        RunSearch();
    }

    partial void OnSearchTextChanged(string value) => Restart();
    partial void OnSelectedModeChanged(SearchMode value) => Restart();
    partial void OnMinSizeChanged(string value) => Restart();
    partial void OnMaxSizeChanged(string value) => Restart();
    partial void OnExtensionsChanged(string value) => Restart();
    partial void OnSelectedKindChanged(EntryKind value) => Restart();
    partial void OnModifiedAfterChanged(DateTime? value) => Restart();
    partial void OnModifiedBeforeChanged(DateTime? value) => Restart();
    partial void OnFilesOnlyChanged(bool value) => RecomputeKind();
    partial void OnFoldersOnlyChanged(bool value) => RecomputeKind();

    private void RecomputeKind()
    {
        SelectedKind = (FilesOnly, FoldersOnly) switch
        {
            (true, false) => EntryKind.FilesOnly,
            (false, true) => EntryKind.FoldersOnly,
            _ => EntryKind.All,
        };
        Restart();
    }

    private void Restart() { _debounce.Stop(); _debounce.Start(); }

    private const int MaxDisplayedResults = 50_000;

    private void RunSearch()
    {
        DateTimeOffset? after = ModifiedAfter is { } a ? new DateTimeOffset(a.Date) : null;
        DateTimeOffset? before = ModifiedBefore is { } b
            ? new DateTimeOffset(b.Date.AddDays(1).AddTicks(-1)) : null;
        var query = QueryBuilder.Build(SearchText, SelectedMode, MinSize, MaxSize,
            after, before, Extensions, SelectedKind);
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
                foreach (var e in t.Result.Take(MaxDisplayedResults))
                    Results.Add(new FileRow(e));
                var total = t.Result.Count;
                StatusText = total > MaxDisplayedResults
                    ? $"Найдено {total} (показаны первые {MaxDisplayedResults}) из {snapshot.Count}"
                    : $"Найдено {total} из {snapshot.Count}";
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        StatusText = "Индексирование…";
        // Progress<T> is created on the UI thread, so its callbacks marshal back to the UI thread.
        var progress = new Progress<CrawlProgress>(p =>
            StatusText = $"Индексирование… {p.Count} объектов  |  {Shorten(p.CurrentDirectory)}");
        try
        {
            await Task.Run(() =>
            {
                var mgr = new IndexManager(_store, () => new Crawler());
                foreach (var path in _settings.Roots)
                {
                    token.ThrowIfCancellationRequested();
                    var id = _store.UpsertRoot(path);
                    mgr.UpdateRoot(id, path, token, progress);
                }
            }, token);
            LoadIndexIntoMemory();
            StatusText = $"Обновлено в {DateTime.Now:HH:mm}. Записей: {_all.Count}";
        }
        catch (OperationCanceledException)
        {
            LoadIndexIntoMemory();
            StatusText = $"Индексирование отменено. В индексе: {_all.Count} записей.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка индексирования: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
    }

    [RelayCommand]
    private void CancelRefresh() => _refreshCts?.Cancel();

    private static string Shorten(string path)
    {
        const int max = 70;
        if (string.IsNullOrEmpty(path) || path.Length <= max) return path;
        return "…" + path[^max..];
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
    private void QuickType(string group) => Extensions = FileTypeGroups.Extensions(group);

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
        try
        {
            Clipboard.SetText(SelectedRow.Path);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            StatusText = "Не удалось скопировать: " + ex.Message;
        }
    }

    public void Shutdown()
    {
        _refreshCts?.Cancel();
        _debounce.Stop();
        _autoRefresh.Stop();
    }

    private void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex) { StatusText = "Не удалось открыть: " + ex.Message; }
    }
}
