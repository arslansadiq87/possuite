using System.Windows;
using Fluent;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class DashboardWindow : RibbonWindow
    {
        public DashboardWindow(DashboardVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is DashboardVm vm)
                await vm.RefreshAsync();
        }
    }
}