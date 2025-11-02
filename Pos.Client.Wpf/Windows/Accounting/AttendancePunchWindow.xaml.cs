using System.Windows;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AttendancePunchWindow : Window
    {
        public AttendancePunchWindow(AttendancePunchVm vm)
        {
            InitializeComponent();
            if (Content is FrameworkElement fe)
            {
                fe.DataContext = vm;
                Loaded += async (_, __) => await vm.LoadAsync();
            }
        }
    }
}
