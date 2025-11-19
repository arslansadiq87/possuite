using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.ComponentModel;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class BarcodeLabelSettingsPage : UserControl
{
    public BarcodeLabelSettingsPage()
    {
        InitializeComponent();
        if (DesignerProperties.GetIsInDesignMode(this)) return;

        var sp = App.Services;
        if (sp is not null)
        { 
            DataContext = sp.GetRequiredService<BarcodeLabelSettingsViewModel>();
            this.Loaded += (_, __) =>
                (DataContext as BarcodeLabelSettingsViewModel)?.ForceRefreshPreview();
        }
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is not BarcodeLabelSettingsViewModel vm) return;
        if (sender is not Thumb th) return;

        // Which handle?
        string which = th.Tag as string ?? "";

        // Adjust deltas for zoom (LayoutTransform)
        double scale = vm.PreviewZoom <= 0 ? 1.0 : vm.PreviewZoom;
        double dx = e.HorizontalChange / scale;
        double dy = e.VerticalChange / scale;

        // Current unscaled coords
        double left = Canvas.GetLeft(th);
        double top = Canvas.GetTop(th);

        double newLeft = left + dx;
        double newTop = top + dy;

        // Clamp within the (unscaled) preview surface
        double maxLeft = Math.Max(0, vm.PreviewWidthDip - th.Width);
        double maxTop = Math.Max(0, vm.PreviewHeightDip - th.Height);
        newLeft = Math.Min(Math.Max(0, newLeft), maxLeft);
        newTop = Math.Min(Math.Max(0, newTop), maxTop);

        // Write back to VM (TwoWay bindings will move the thumbs)
        switch (which)
        {
            case "Name":
                vm.NameXDip = newLeft;
                vm.NameYDip = newTop;
                break;
            case "Price":
                vm.PriceXDip = newLeft;
                vm.PriceYDip = newTop;
                break;
            case "Sku":
                vm.SkuXDip = newLeft;
                vm.SkuYDip = newTop;
                break;
            case "Business":
                vm.BusinessXDip = newLeft;
                vm.BusinessYDip = newTop;
                break;
        }
    }

}
