using System.IO;
using System.Windows;
using NetSearch.App.ViewModels;
using NetSearch.Core.Settings;
using NetSearch.Core.Storage;

namespace NetSearch.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(SettingsStore.DefaultDir);

        var settings = SettingsStore.Load(SettingsStore.DefaultSettingsPath);
        var store = IndexStore.OpenWithRecovery(SettingsStore.DefaultDbPath, out var recovered);

        var vm = new MainViewModel(store, settings, SettingsStore.DefaultSettingsPath);
        if (recovered)
            vm.StatusText = "Индекс был повреждён и пересоздан. Нажмите «Обновить».";
        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => { vm.Shutdown(); store.Dispose(); };
        window.Show();
    }
}
