using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class ReceiptTemplateService : IReceiptTemplateService
    {
        private readonly PosClientDbContext _db;
        public ReceiptTemplateService(PosClientDbContext db) => _db = db;

        public async Task<ReceiptTemplate> GetAsync(int? outletId, ReceiptDocType docType, CancellationToken ct = default)
        {
            // Prefer outlet template, fallback to global
            var q = _db.ReceiptTemplates.AsNoTracking().Where(t => t.DocType == docType);
            var row = await q.FirstOrDefaultAsync(t => t.OutletId == outletId, ct)
                      ?? await q.FirstOrDefaultAsync(t => t.OutletId == null, ct);
            if (row == null) row = Default(outletId, docType);
            return row;
        }

        public async Task<ReceiptTemplate> GetOrCreateDefaultAsync(int? outletId, ReceiptDocType docType, CancellationToken ct = default)
        {
            var t = await GetAsync(outletId, docType, ct);
            if (t.Id == 0)
            {
                _db.ReceiptTemplates.Add(t);
                await _db.SaveChangesAsync(ct);
            }
            return t;
        }

        public async Task SaveAsync(ReceiptTemplate template, CancellationToken ct = default)
        {
            if (template.Id == 0) _db.ReceiptTemplates.Add(template);
            else _db.ReceiptTemplates.Update(template);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<ReceiptTemplate>> GetAllForOutletAsync(int? outletId, CancellationToken ct = default)
        {
            return await _db.ReceiptTemplates.AsNoTracking()
                .Where(t => t.OutletId == outletId || t.OutletId == null)
                .OrderBy(t => t.DocType).ToListAsync(ct);
        }

        // Pos.Persistence/Services/ReceiptTemplateService.cs  (inside ReceiptTemplateService)
        private static ReceiptTemplate Default(int? outletId, ReceiptDocType type)
        {
            return new ReceiptTemplate
            {
                OutletId = outletId,
                DocType = type,

                // sane defaults for builder
                PaperWidthMm = 80,
                EnableDrawerKick = true,

                ShowLogoOnReceipt = true,
                LogoMaxWidthPx = 384,
                LogoAlignment = "Center",

                RowShowProductName = true,
                RowShowProductSku = false,
                RowShowQty = true,
                RowShowUnitPrice = true,
                RowShowLineDiscount = true,
                RowShowLineTotal = true,

                TotalsShowTaxes = true,
                TotalsShowDiscounts = true,
                TotalsShowOtherExpenses = true,
                TotalsShowGrandTotal = true,
                TotalsShowPaymentRecv = true,
                TotalsShowBalance = true,

                ShowQr = false,
                ShowCustomerOnReceipt = true,
                ShowCashierOnReceipt = true,
                PrintBarcodeOnReceipt = false,

                ShowNtnOnReceipt = true,
                ShowFbrOnReceipt = true
            };
        }

    }
}
