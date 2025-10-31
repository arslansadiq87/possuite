// Pos.Client.Wpf/Windows/Settings/InvoiceSettingsPage.xaml.cs
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class InvoiceSettingsPage : UserControl
{
    public InvoiceSettingsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<InvoiceSettingsViewModel>();
    }
}
