// Pos.Client.Wpf/Commands/AppCommands.cs
using System.Windows.Input;

namespace Pos.Client.Wpf.Commands
{
    public static class AppCommands
    {
        public static readonly RoutedUICommand ResetStockData =
            new RoutedUICommand("Reset Stock Data", nameof(ResetStockData), typeof(AppCommands));

        // You can add more app-level commands here later.
    }
}
