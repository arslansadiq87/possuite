using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class IdentitySettingsPage : UserControl
{
    public IdentitySettingsPage()
    {
        InitializeComponent();
        if (DesignerProperties.GetIsInDesignMode(this)) return;
        var sp = App.Services;
        if (sp is not null)
            DataContext = sp.GetRequiredService<IdentitySettingsViewModel>();
    }
}
