// Pos.Client.Wpf/Windows/Settings/ReceiptBuilderPage.xaml.cs
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class ReceiptBuilderPage : UserControl
    {
        public ReceiptBuilderPage()
        {
            InitializeComponent();

            if (DesignerProperties.GetIsInDesignMode(this)) return;

            var sp = App.Services;
            if (sp is not null)
            {
                var vm = sp.GetRequiredService<ReceiptBuilderViewModel>();
                DataContext = vm;

                // Call Init once the view is actually loaded (ensures UI thread/dispatcher ready)
                Loaded += async (_, __) =>
                {
                    // only run once
                    Loaded -= async (_, __) => { };
                    //await vm.InitAsync();
                };
            }
        }
    }
}
