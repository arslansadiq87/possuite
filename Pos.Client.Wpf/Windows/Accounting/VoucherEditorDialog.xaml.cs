using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherEditorDialog : Window
    {
        public VoucherEditorDialog(VoucherEditorVm vm)
        {
            InitializeComponent();
            Editor.AttachVm(vm); // <- simple, explicit, reliable
            vm.CloseRequested += saved =>
            {
                DialogResult = saved;
                Close();
            };
        }
    }
}
