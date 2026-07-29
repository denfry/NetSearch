using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSearch.App.Services;
using NetSearch.Core.Cleaning;
using NetSearch.Core.Cleaning.Providers;
using NetSearch.Core.DiskUsage;

namespace NetSearch.App.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly List<ICleanProvider> _providers;

    public ObservableCollection<CleanCategoryRow> Categories { get; } = new();

    [ObservableProperty] private bool _isElevated = ElevationService.IsElevated;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Нажмите «Сканировать», чтобы оценить, что можно удалить.";

    private CancellationTokenSource? _cts;

    public CleanerViewModel()
    {
        bool elevated = IsElevated;
        _providers = new List<ICleanProvider>
        {
            TempFilesProvider.ForSystem(elevated),
            new RecycleBinProvider(),
            BrowserCacheProvider.ForSystem(),
            SystemDevCacheProvider.ForSystem(elevated),
        };
        foreach (var p in _providers)
            Categories.Add(new CleanCategoryRow(p.Category, enabled: !p.Category.NeedsAdmin || elevated));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "Сканирование…";
        try
        {
            var ids = Categories.Where(c => c.IsEnabled).Select(c => c.Category.Id).ToList();
            var scanner = new CleanScanner(_providers);
            var token = _cts.Token;
            var results = await Task.Run(() => scanner.Scan(ids, token), token);
            foreach (var row in Categories)
            {
                var scan = results.FirstOrDefault(r => r.Category.Id == row.Category.Id);
                row.Items = scan?.Items ?? System.Array.Empty<CleanItem>();
                row.SizeBytes = scan?.TotalBytes ?? 0;
                row.ItemCount = scan?.Items.Count ?? 0;
                row.Scanned = true;
            }
            Status = $"Можно освободить: {SizeFormatter.Format(SelectedFreeable)} (выбрано категорий: {Categories.Count(c => c.IsSelected)})";
        }
        catch (OperationCanceledException) { Status = "Сканирование отменено."; }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    public long SelectedFreeable => Categories.Where(c => c.IsSelected).Sum(c => c.SizeBytes);

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (IsBusy) return;
        var chosen = Categories.Where(c => c.IsSelected && c.Scanned && c.ItemCount > 0).ToList();
        if (chosen.Count == 0) { Status = "Нечего удалять — сначала отсканируйте и выберите категории."; return; }

        long total = chosen.Sum(c => c.SizeBytes);
        var confirm = MessageBox.Show(
            $"Удалить {SizeFormatter.Format(total)} из {chosen.Count} категорий?\n\n" +
            "Файлы пользователя (документы, загрузки) не затрагиваются. Где возможно — в Корзину.",
            "Подтверждение очистки", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        Status = "Очистка…";
        try
        {
            var token = _cts.Token;
            var executor = new CleanExecutor();
            long freed = 0; int deleted = 0; var skipped = new List<string>();
            await Task.Run(() =>
            {
                foreach (var row in chosen)
                {
                    token.ThrowIfCancellationRequested();
                    var outcome = executor.Delete(row.Items, row.Category.UsesRecycleBin, token);
                    freed += outcome.FreedBytes; deleted += outcome.DeletedCount;
                    skipped.AddRange(outcome.Skipped);
                }
            }, token);

            foreach (var row in chosen) { row.Items = System.Array.Empty<CleanItem>(); row.SizeBytes = 0; row.ItemCount = 0; }
            Status = skipped.Count == 0
                ? $"Освобождено {SizeFormatter.Format(freed)} ({deleted} объектов)."
                : $"Освобождено {SizeFormatter.Format(freed)} ({deleted} объектов). Пропущено: {skipped.Count}.";
        }
        catch (OperationCanceledException) { Status = "Очистка отменена."; }
        catch (Exception ex) { Status = "Ошибка очистки: " + ex.Message; }
        finally { IsBusy = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void RelaunchAdmin() => ElevationService.RelaunchAsAdmin();
}
