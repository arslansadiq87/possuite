using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Pos.Client.Wpf.Windows.Accounting;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class CashBookView : UserControl
    {
        public CashBookView(CashBookVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
