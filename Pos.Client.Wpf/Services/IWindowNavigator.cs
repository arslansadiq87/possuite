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
            w.Owner = Application.Current?.MainWindow;
            w.Show();
        }

        public bool? ShowDialog<TWindow>() where TWindow : Window
        {
            var w = _sp.GetRequiredService<TWindow>();
            w.Owner = Application.Current?.MainWindow;
            return w.ShowDialog();
        }
    }
}
