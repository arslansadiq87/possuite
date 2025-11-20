using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class InvoiceSettingsPage : UserControl
    {
        // XAML needs this:
        public InvoiceSettingsPage()
        {
            InitializeComponent();

            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            // Access the static Services (avoid CS0176)
            var sp = App.Services;
            if (sp is not null)
                DataContext = sp.GetRequiredService<InvoiceSettingsViewModel>();
            else
                DataContext = new InvoiceSettingsViewModelStub(); // safe fallback
        }

        // Optional DI ctor for code-created instances
        public InvoiceSettingsPage(InvoiceSettingsViewModel vm)
        {
            InitializeComponent();
            if (!DesignerProperties.GetIsInDesignMode(this))
                DataContext = vm;
        }

        // Minimal stub to avoid design-time/runtime crashes if DI not ready
        internal sealed class InvoiceSettingsViewModelStub
        {
            public string? PrinterName { get; set; }
            public bool CashDrawerKickEnabled { get; set; }
            public bool AutoPrintOnSave { get; set; } = false;
            public bool AskBeforePrint { get; set; } = true;
            public string? FooterSale { get; set; }
            public string? FooterSaleReturn { get; set; }
            public string? FooterVoucher { get; set; }
            public string? FooterZReport { get; set; }
        }
    }
}
