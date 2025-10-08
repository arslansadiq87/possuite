using System.Threading.Tasks;

namespace Pos.Client.Wpf.Services
{
    public interface IPaymentDialogService
    {
        Task<PaymentResult> ShowAsync(
            decimal subtotal,
            decimal discountValue,
            decimal tax,
            decimal grandTotal,
            int items,
            int qty,
            bool differenceMode = false,
            decimal amountDelta = 0m,
            string? title = null);
    }
}
