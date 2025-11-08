// Pos.Persistence/Services/SalesService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Persistence.Sync;   // ← add this

namespace Pos.Persistence.Services
{
    public class SalesService : ISalesService
    {
        private readonly PosClientDbContext _db;
        private readonly IOutboxWriter _outbox;   // ← add

        public SalesService(PosClientDbContext db, IOutboxWriter outbox) // ← inject
        {
            _db = db;
            _outbox = outbox;
        }
        /// <summary>
        /// Mark the original Final sale as Revised and create a new Draft sale with the same
        /// InvoiceNumber and Revision+1, linked via RevisedFromSaleId/RevisedToSaleId.
        /// NOTE: No stock or payment reversal is performed here because your model doesn't expose lines.
        ///       Once you confirm the line entity used for stock, we can add reversal safely.
        /// </summary>
        public async Task<int> AmendByReversalAndReissueAsync(
            int originalSaleId, int? tillSessionId, int userId, string? reason = null)
        {
            var original = await _db.Sales.FirstOrDefaultAsync(s => s.Id == originalSaleId);
            if (original == null)
                throw new InvalidOperationException($"Sale #{originalSaleId} not found.");

            if (original.IsReturn)
                throw new InvalidOperationException("Return invoices cannot be amended.");

            if (original.Status != SaleStatus.Final)
                throw new InvalidOperationException("Only Final sales can be amended.");

            // Mark original as Revised
            original.Status = SaleStatus.Revised;
            original.Note = AppendNote(original.Note, $"Revised on {DateTime.UtcNow:u} by {userId}. {reason ?? ""}".Trim());

            // Create new Draft with same invoice number and Revision+1
            var newSale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = original.OutletId,
                CounterId = original.CounterId,
                TillSessionId = null, // set when posted
                IsReturn = false,
                OriginalSaleId = null,
                Revision = original.Revision + 1,
                RevisedFromSaleId = original.Id,
                RevisedToSaleId = null,
                Status = SaleStatus.Draft,
                HoldTag = null,
                CustomerName = original.CustomerName,
                Note = $"Amendment draft of #{original.InvoiceNumber} r{original.Revision}",
                InvoiceNumber = original.InvoiceNumber,
                InvoiceDiscountPct = original.InvoiceDiscountPct,
                InvoiceDiscountAmt = original.InvoiceDiscountAmt,
                InvoiceDiscountValue = original.InvoiceDiscountValue,
                DiscountBeforeTax = original.DiscountBeforeTax,
                Subtotal = 0m,
                TaxTotal = 0m,
                Total = 0m,
                CashierId = original.CashierId,
                SalesmanId = original.SalesmanId,
                CustomerKind = original.CustomerKind,
                CustomerId = original.CustomerId,
                CustomerPhone = original.CustomerPhone,
                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                EReceiptToken = null,
                EReceiptUrl = null,
                InvoiceFooter = original.InvoiceFooter
            };

            _db.Sales.Add(newSale);
            await _db.SaveChangesAsync();

            original.RevisedToSaleId = newSale.Id;
            await _db.SaveChangesAsync();
            // === SYNC: enqueue finalized Sale Return (IsReturn = true) inside the same TX ===
            await _outbox.EnqueueUpsertAsync(_db, newSale, default);  // record the return document
            await _db.SaveChangesAsync();                          // persist the outbox row
            return newSale.Id;
        }

        private static string AppendNote(string? existing, string add)
        {
            if (string.IsNullOrWhiteSpace(add)) return existing ?? "";
            return string.IsNullOrWhiteSpace(existing) ? add : existing + Environment.NewLine + add;
        }
    }
}
