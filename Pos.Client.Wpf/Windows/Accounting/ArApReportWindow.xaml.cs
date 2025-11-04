// Pos.Client.Wpf/Windows/Accounting/ArApReportWindow.xaml.cs
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class ArApReportWindow : Window
    {
        public ArApReportWindow()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                var vm = App.Services.GetRequiredService<ArApReportVm>();
                DataContext = vm;
                Loaded += async (_, __) => await vm.InitAsync();
            }
        }
    }
}
