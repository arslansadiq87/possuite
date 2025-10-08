// Pos.Client.Wpf/Attached/IconProps.cs
using System.Windows;

namespace Pos.Client.Wpf.Attached
{
    public static class IconProps
    {
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.RegisterAttached(
                "IconSize",
                typeof(double),
                typeof(IconProps),
                new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.Inherits));

        public static void SetIconSize(DependencyObject element, double value) =>
            element.SetValue(IconSizeProperty, value);

        public static double GetIconSize(DependencyObject element) =>
            (double)element.GetValue(IconSizeProperty);
    }
}
