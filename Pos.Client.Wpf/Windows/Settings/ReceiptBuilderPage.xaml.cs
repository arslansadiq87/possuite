using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class ReceiptBuilderPage
    {
        public ReceiptBuilderPage()
        {
            InitializeComponent();
            
            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            // Resolve the VM from DI and set DataContext if not set in XAML
            DataContext ??= App.Services.GetRequiredService<ReceiptBuilderViewModel>();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ReceiptBuilderViewModel vm)
                await vm.InitAsync();
        }
    }
}
