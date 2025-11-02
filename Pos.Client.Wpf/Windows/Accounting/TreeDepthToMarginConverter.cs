// Pos.Client.Wpf/Windows/Accounting/TreeDepthToMarginConverter.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public sealed class TreeDepthToMarginConverter : IValueConverter
    {
        public double Indent { get; set; } = 14.0; // one tab
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is DependencyObject d)
            {
                int level = 0;
                for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
                    if (p is TreeViewItem) level++;
                return new Thickness(Indent * Math.Max(level - 1, 0), 0, 0, 0);
            }
            return new Thickness(0);
        }
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => Binding.DoNothing;
    }
}
