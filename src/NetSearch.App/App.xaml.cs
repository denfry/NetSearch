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
        var store = new IndexStore(SettingsStore.DefaultDbPath);
        store.Initialize();

        var vm = new MainViewModel(store, settings, SettingsStore.DefaultSettingsPath);
        var window = new MainWindow { DataContext = vm };
        window.Closed += (_, _) => store.Dispose();
        window.Show();
    }
}
