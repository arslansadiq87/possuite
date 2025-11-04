using System.Threading.Tasks;
using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AccountLedgerWindow : Window
    {
        public AccountLedgerWindow(AccountLedgerVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
