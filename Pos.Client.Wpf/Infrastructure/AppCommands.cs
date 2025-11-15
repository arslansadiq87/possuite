using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pos.Client.Wpf.Infrastructure
{
    public static class AppCommands
    {
        // Usage requires passing the current Window as CommandParameter.
        // If not passed, it will try to close the active window.
        public static ICommand CloseWindow { get; } = new RelayCommand<Window>(w =>
        {
            var win = w ?? GetActiveWindow();
            win?.Close();
        });

        private static Window? GetActiveWindow()
        {
            // Best-effort fallback if CommandParameter wasn't provided
            return Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(x => x.IsActive)
                ?? Application.Current?.MainWindow;
        }
    }

    // Simple generic RelayCommand so we don't depend on any MVVM lib
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Predicate<T?>? _canExecute;

        public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) =>
            _canExecute?.Invoke(parameter is T t ? t : default) ?? true;

        public void Execute(object? parameter) =>
            _execute(parameter is T t ? t : default);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
