using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls.Primitives;
using System.Windows;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class BarcodeLabelSettingsPage : UserControl
{
    public BarcodeLabelSettingsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<BarcodeLabelSettingsViewModel>();
        this.Loaded += (_, __) =>
            (DataContext as BarcodeLabelSettingsViewModel)?.ForceRefreshPreview();
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not BarcodeLabelSettingsViewModel vm) return;
        if (sender is not Thumb th) return;
        string which = th.Tag as string ?? "";

        // Clamp movement within preview bounds
        double newLeft = Canvas.GetLeft(th) + e.HorizontalChange;
        double newTop = Canvas.GetTop(th) + e.VerticalChange;
        newLeft = Math.Max(0, Math.Min(newLeft, vm.PreviewWidthDip - th.Width));
        newTop = Math.Max(0, Math.Min(newTop, vm.PreviewHeightDip - th.Height));

        switch (which)
        {
            case "Name": vm.NameXDip = newLeft; vm.NameYDip = newTop; break;
            case "Price": vm.PriceXDip = newLeft; vm.PriceYDip = newTop; break;
            case "Sku": vm.SkuXDip = newLeft; vm.SkuYDip = newTop; break;
            case "Business": vm.BusinessXDip = newLeft; vm.BusinessYDip = newTop; break;

        }
        // the TwoWay bindings propagate to mm via the proxies → preview auto-refreshes
    }
}
