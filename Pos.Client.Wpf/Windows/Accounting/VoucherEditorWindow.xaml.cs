using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherEditorWindow : Window
    {
        public VoucherEditorWindow(VoucherEditorVm vm)
        {
            InitializeComponent();
            DataContext = vm;
            Loaded += async (_, __) => await vm.LoadAsync();
        }
    }
}
