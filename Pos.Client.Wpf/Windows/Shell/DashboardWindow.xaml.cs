using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class DashboardWindow
    {
        private readonly DashboardVm _vm;
        private readonly IViewNavigator _views;

        public DashboardWindow(DashboardVm vm, IViewNavigator views)
        {
            InitializeComponent();
            _vm = vm;
            _views = views;
            DataContext = _vm;

            Loaded += (_, __) =>
            {
                _views.Attach(_vm);
                // Optional: land on a default view (e.g., Reports or a Home view)
                // _views.SetRoot<Windows.Reports.ReportsView>();
                _ = _vm.RefreshAsync();
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape && _vm.IsOverlayOpen)
                {
                    _views.HideOverlay();
                    e.Handled = true;
                }
            };
        }
    }
}
