using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AccountLedgerView : UserControl
    {
        public AccountLedgerView(AccountLedgerVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
