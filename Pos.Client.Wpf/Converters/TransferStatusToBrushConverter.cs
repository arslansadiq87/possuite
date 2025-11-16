using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Pos.Domain.Entities; // TransferStatus

namespace Pos.Client.Wpf.Converters
{
    public sealed class TransferStatusToBrushConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not TransferStatus s) return Brushes.Gray;

            return s switch
            {
                TransferStatus.Draft => Brushes.SlateGray,
                TransferStatus.Dispatched => Brushes.SteelBlue,
                TransferStatus.Received => Brushes.SeaGreen,
                TransferStatus.Voided => Brushes.IndianRed,
                _ => Brushes.Gray
            };
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
