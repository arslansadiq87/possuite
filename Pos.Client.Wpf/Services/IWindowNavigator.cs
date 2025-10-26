using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Services
{
    /// <summary>
    /// Simple helper for opening other WPF windows through DI.
    /// </summary>
    public interface IWindowNavigator
    {
        void Show<TWindow>() where TWindow : Window;
        bool? ShowDialog<TWindow>() where TWindow : Window;

        // ✅ NEW overload — allows passing a ViewModel
        bool? ShowDialog<TWindow>(object? viewModel) where TWindow : Window;
    }

    public sealed class WindowNavigator : IWindowNavigator
    {
        private readonly IServiceProvider _sp;

        public WindowNavigator(IServiceProvider sp)
        {
            _sp = sp;
        }

        public void Show<TWindow>() where TWindow : Window
        {
            var w = _sp.GetRequiredService<TWindow>();
            Prepare(w, null);
            w.Show();
        }

        public bool? ShowDialog<TWindow>() where TWindow : Window
        {
            var w = _sp.GetRequiredService<TWindow>();
            Prepare(w, null);
            return w.ShowDialog();
        }

        // ✅ New overload implementation
        public bool? ShowDialog<TWindow>(object? viewModel) where TWindow : Window
        {
            var w = _sp.GetRequiredService<TWindow>();
            if (viewModel != null) w.DataContext = viewModel;
            w.Owner = Application.Current?.MainWindow;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowInTaskbar = false;
            return w.ShowDialog();
        }

        // shared helper
        private static void Prepare(Window w, object? vm)
        {
            if (vm != null)
                w.DataContext = vm;

            w.Owner = Application.Current?.MainWindow;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowInTaskbar = false;
        }
    }
}
