// Pos.Persistence/Services/ReturnsService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public class ReturnsService : IReturnsService
    {
        private readonly PosClientDbContext _db;
        public ReturnsService(PosClientDbContext db) => _db = db;

        // Pos.Persistence/Services/ReturnsService.cs  (REPLACE the method body)
        public async Task<int> CreateReturnWithoutInvoiceAsync(
            int outletId,
            int counterId,
            int? tillSessionId,
            int userId,
            IEnumerable<ReturnNoInvLine> lines,
            int? customerId = null,
            string? customerName = null,
            string? customerPhone = null,
            string? reason = null)
        {
            var list = (lines ?? Enumerable.Empty<ReturnNoInvLine>())
                       .Where(l => l.ItemId > 0 && l.Qty > 0).ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("Add at least one item.");

            // 1) Create a FINAL return sale header (IsReturn = true)
            var ret = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = outletId,
                CounterId = counterId,
                TillSessionId = tillSessionId,
                IsReturn = true,
                OriginalSaleId = null,
                Revision = 0,
                RevisedFromSaleId = null,
                RevisedToSaleId = null,
                Status = SaleStatus.Final,
                HoldTag = null,
                CustomerName = customerName,
                Note = reason,
                InvoiceNumber = await NextReturnInvoiceNumber(counterId),
                CustomerKind = customerId.HasValue ? CustomerKind.Registered : CustomerKind.WalkIn,
                CustomerId = customerId,
                CustomerPhone = customerPhone,
                PaymentMethod = PaymentMethod.Cash
            };

            // 2) Compute totals (basic: no tax logic here — keep your existing tax path)
            decimal subtotal = 0m;
            foreach (var l in list)
            {
                var lineNet = (l.UnitPrice - l.Discount) * l.Qty;
                subtotal += lineNet;

                // STOCK IN (return): only fields we are sure about
                _db.StockEntries.Add(new StockEntry
                {
                    OutletId = outletId,
                    ItemId = l.ItemId,
                    QtyChange = (int)l.Qty   // your StockEntries query showed int, cast from decimal
                });
            }

            ret.Subtotal = subtotal;
            ret.TaxTotal = 0m;          // plug your tax logic if applicable
            ret.Total = ret.Subtotal + ret.TaxTotal;

            // If you DON'T have a CashMovement entity, do not write one.
            // Record cash in the Sale header fields only:
            ret.CashAmount = ret.Total;
            ret.CardAmount = 0m;

            _db.Sales.Add(ret);
            await _db.SaveChangesAsync();
            return ret.Id;
        }


        // Replace with your existing numbering logic; placeholder shows one way.
        private async Task<int> NextReturnInvoiceNumber(int counterId)
        {
            var last = await _db.Sales
                .Where(s => s.CounterId == counterId && s.IsReturn)
                .OrderByDescending(s => s.InvoiceNumber)
                .Select(s => s.InvoiceNumber)
                .FirstOrDefaultAsync();

            return last + 1;
        }
    }
}
