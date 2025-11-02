using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class PayrollRunWindow : Window
    {
        public PayrollRunWindow(PayrollRunVm vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}
