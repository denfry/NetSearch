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
}
