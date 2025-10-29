using System.Windows;
using Pos.Client.Wpf.Windows.Shell;

namespace Pos.Client.Wpf.Services
{
    public interface IViewNavigator
    {
        void Attach(DashboardVm shellVm);

        // Single-view (legacy)
        void SetRoot<TView>() where TView : FrameworkElement;

        // Overlay (unchanged)
        void ShowOverlay<TView>() where TView : FrameworkElement;
        void ShowOverlay(object viewInstance);
        void HideOverlay();

        // NEW: Tabbed API
        ViewTab OpenTab<TView>(string? title = null, string? contextKey = null, bool activate = true)
            where TView : FrameworkElement;
        void CloseTab(ViewTab tab);
        void CloseActiveTab();
        void ActivateTab(ViewTab tab);
        ViewTab OpenTab(object viewInstance, string? title = null, string? contextKey = null, bool activate = true);
        bool TryActivateByContext(string contextKey);

    }
}
