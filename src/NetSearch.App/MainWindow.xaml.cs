using System.Windows;
using System.Windows.Controls;
using NetSearch.App.ViewModels;

namespace NetSearch.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnRowDoubleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.OpenFileCommand.CanExecute(null))
            vm.OpenFileCommand.Execute(null);
    }

    private void OnTreemapSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Disk.LayoutTiles(e.NewSize.Width, e.NewSize.Height);
    }

    private void OnTileClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border { Tag: NetSearch.Core.DiskUsage.FolderNode node }
            && DataContext is ViewModels.MainViewModel vm)
            vm.Disk.DrillCommand.Execute(node);
    }
}
