using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Sales;

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

            // show overlay FIRST so it's visible while initializing
            _views.ShowOverlay(dlg);

            var result = await dlg.InitializeAndShowAsync(
                subtotal, discountValue, tax, grandTotal,
                items, qty, differenceMode, amountDelta, title,
                closeOverlay: _views.HideOverlay);

            // dialog signals completion via TCS; we already hide overlay in the dialog when it completes
            return result;
        }
    }
}
