using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Shell;

namespace Pos.Client.Wpf.Services
{
    public sealed partial class ViewNavigator : IViewNavigator
    {
        private readonly IServiceProvider _sp;
        private DashboardVm? _shell;

        public ViewNavigator(IServiceProvider sp) => _sp = sp;

        public void Attach(DashboardVm shellVm) => _shell = shellVm;

        public void SetRoot<TView>() where TView : FrameworkElement
        {
            var vm = EnsureShell();
            var view = _sp.GetRequiredService<TView>();
            vm.CurrentView = view;
            // Optional: simple contextual toggle demo
            vm.TransferTabVisible = view.GetType().FullName?.EndsWith(".TransferView") == true;
        }

        public void ShowOverlay<TView>() where TView : FrameworkElement
        {
            var vm = EnsureShell();
            var view = _sp.GetRequiredService<TView>();
            vm.OverlayView = view;
            vm.IsOverlayOpen = true;
        }

        public void HideOverlay()
        {
            var vm = EnsureShell();
            vm.IsOverlayOpen = false;
            vm.OverlayView = null;
        }

        public void ShowOverlay(object viewInstance)
        {
            var vm = EnsureShell();
            vm.OverlayView = viewInstance;
            vm.IsOverlayOpen = true;
        }


        private DashboardVm EnsureShell()
            => _shell ?? throw new InvalidOperationException("ViewNavigator not attached to shell VM. Call IViewNavigator.Attach(...) from DashboardWindow.OnLoaded.");

        public Action ShowOverlayOn(Window owner, object viewInstance)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));

            // look for hosts on this specific window
            var presenter = owner.FindName("OverlayPresenter") as ContentPresenter;
            var mask = owner.FindName("OverlayMask") as UIElement;

            // If not present on this window, fallback to shell overlay for safety
            if (presenter == null || mask == null)
            {
                // fallback to your existing shell overlay path
                ShowOverlay(viewInstance);
                return () => HideOverlay();
            }

            mask.Visibility = Visibility.Visible;
            presenter.Content = viewInstance;
            presenter.Visibility = Visibility.Visible;

            // return per-window hide action
            return () =>
            {
                presenter.Content = null;
                presenter.Visibility = Visibility.Collapsed;
                mask.Visibility = Visibility.Collapsed;
            };
        }
    }
}
