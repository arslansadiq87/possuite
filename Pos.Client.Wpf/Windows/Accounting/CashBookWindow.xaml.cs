using System.Threading.Tasks;
using System.Windows;
using Pos.Client.Wpf.Windows.Accounting;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class CashBookWindow : Window
    {
        public CashBookWindow(CashBookVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
