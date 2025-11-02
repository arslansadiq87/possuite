using System.Windows;
using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class ChartOfAccountsView : UserControl
    {
        public ChartOfAccountsView(ChartOfAccountsVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            _ = vm.LoadAsync();

            // ListView uses SelectionChanged, not SelectedItemChanged
            CoaTree.SelectionChanged += (_, __) =>
            {
                if (DataContext is ChartOfAccountsVm m)
                    m.SelectedNode = (CoaTree.SelectedItem as AccountFlatRow)?.Node;
            };
        }
    }
}
