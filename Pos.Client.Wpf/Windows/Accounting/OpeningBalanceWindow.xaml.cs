using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class OpeningBalanceWindow : Window
    {
        public OpeningBalanceWindow(OpeningBalanceVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
