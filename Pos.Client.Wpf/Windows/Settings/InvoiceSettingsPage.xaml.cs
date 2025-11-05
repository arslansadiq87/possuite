// Pos.Client.Wpf/Windows/Settings/InvoiceSettingsPage.xaml.cs
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;           // <-- ADD


namespace Pos.Client.Wpf.Windows.Settings;

public partial class InvoiceSettingsPage : UserControl
{
    public InvoiceSettingsPage()
    {
        InitializeComponent();
        // Skip DI in XAML designer
        if (DesignerProperties.GetIsInDesignMode(this)) return;
        var sp = App.Services;          // could be null in designer
        if (sp is not null)
            DataContext = sp.GetRequiredService<InvoiceSettingsViewModel>();
        //DataContext = App.Services.GetRequiredService<InvoiceSettingsViewModel>();

    }



}
