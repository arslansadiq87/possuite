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

        /// <summary>
        /// Create a finalized Sale (IsReturn = true) without linking to an original invoice.
        /// - Validates lines
        /// - Computes totals (no tax logic here)
        /// - Writes stock entries to add stock back to the outlet
        /// - Assigns a new return invoice number (SR series via local helper)
        /// NOTE: No GL posting here (Client layer triggers it).
        /// </summary>
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
                       .Where(l => l.ItemId > 0 && l.Qty > 0m)
                       .ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("Add at least one item.");

            await using var tx = await _db.Database.BeginTransactionAsync();

            // 1) Return header (FINAL)
            var nowUtc = DateTime.UtcNow;
            var ret = new Sale
            {
                Ts = nowUtc,
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

                CustomerKind = customerId.HasValue ? CustomerKind.Registered : CustomerKind.WalkIn,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,

                Note = reason,

                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash
            };

            // 2) Totals (tax = 0 for this path)
            decimal subtotal = 0m;
            foreach (var l in list)
            {
                var net = (l.UnitPrice - l.Discount) * l.Qty;
                subtotal += net;
            }

            ret.Subtotal = subtotal;
            ret.TaxTotal = 0m;
            ret.Total = ret.Subtotal + ret.TaxTotal;

            // immediate refund recorded on Sale (GL will credit Till later from client)
            ret.CashAmount = ret.Total;
            ret.CardAmount = 0m;

            // 3) SR numbering (local helper)
            ret.InvoiceNumber = await NextReturnInvoiceNumber(counterId);

            // 4) Save header
            _db.Sales.Add(ret);
            await _db.SaveChangesAsync();

            // 5) Stock back to outlet
            foreach (var l in list)
            {
                _db.StockEntries.Add(new StockEntry
                {
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = outletId,
                    OutletId = outletId,
                    ItemId = l.ItemId,
                    QtyChange = (int)l.Qty
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return ret.Id;
        }

        // Project-local numbering helper
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
