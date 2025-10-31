using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Sales;
using Pos.Client.Wpf.Windows.Shell; // for DashboardWindow type if you want type checks

namespace Pos.Client.Wpf.Services
{
    public sealed class PaymentDialogService : IPaymentDialogService
    {
        private readonly IViewNavigator _views;
        private readonly IServiceProvider _sp;

        public PaymentDialogService(IViewNavigator views, IServiceProvider sp)
        {
            _views = views;
            _sp = sp;
        }

        public async Task<PaymentResult> ShowAsync(
            decimal subtotal,
            decimal discountValue,
            decimal tax,
            decimal grandTotal,
            int items,
            int qty,
            bool differenceMode = false,
            decimal amountDelta = 0m,
            string? title = null)
        {
            var dlg = _sp.GetRequiredService<PayDialog>();

            // Decide target window: the currently active window, else MainWindow
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                     ?? Application.Current?.MainWindow;

            // If active window is the Dashboard shell, use existing shell overlay (keeps current behavior)
            // Otherwise (e.g., EditSaleWindow), show overlay on that window.
            Action hide;
            if (owner != null && owner == Application.Current?.MainWindow)
            {
                _views.ShowOverlay(dlg);
                hide = _views.HideOverlay;
            }
            else
            {
                // show overlay on the editing window
                hide = _views.ShowOverlayOn(owner!, dlg);
            }

            var result = await dlg.InitializeAndShowAsync(
                subtotal, discountValue, tax, grandTotal,
                items, qty, differenceMode, amountDelta, title,
                closeOverlay: hide);

            return result;
        }
    }
}
