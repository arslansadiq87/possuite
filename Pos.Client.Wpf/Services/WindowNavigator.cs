using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using System.Windows;

public sealed class WindowNavigator : IWindowNavigator
{
    private readonly IServiceProvider _sp;
    public WindowNavigator(IServiceProvider sp) => _sp = sp;

    private static void Prepare(Window w, object? vm)
    {
        if (vm != null) w.DataContext = vm;
        w.Owner ??= Application.Current?.MainWindow;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.ShowInTaskbar = false;
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

    // NEW:
    public bool? ShowDialog<TWindow>(object? viewModel) where TWindow : Window
    {
        var w = _sp.GetRequiredService<TWindow>();
        Prepare(w, viewModel);
        return w.ShowDialog();
    }
}
