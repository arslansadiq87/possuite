// Pos.Domain/Formatting/DocNoComposer.cs
using System;
using System.Globalization;
using Pos.Domain.Entities;
using Pos.Domain.Accounting;

namespace Pos.Domain.Formatting
{
    public static class DocNoComposer
    {
        // Core formatters
        public static string Compose(int outletId, int? counterId, int number)
            => $"{outletId}-{(counterId ?? 0)}-{number.ToString("000000", CultureInfo.InvariantCulture)}";

        public static string Compose(int outletId, int? counterId, string number)
            => $"{outletId}-{(counterId ?? 0)}-{NormalizeNumberString(number)}";

        // ---- Sales / Sale Return / Sale Revision (they all share Sale) ----
        public static string FromSale(Sale s)
            => Compose(s.OutletId, s.CounterId, s.InvoiceNumber);

        // ---- Purchases ----
        // Purchase has: int? OutletId, string? DocNo, no CounterId => counter = 0
        public static string FromPurchase(Purchase p)
            => Compose(p.OutletId ?? 0, 0, p.DocNo ?? p.Id.ToString());

        public static string FromStockDoc(StockDoc d)
        {
            var outletId = 0;
            if (d.LocationType == InventoryLocationType.Outlet)
                outletId = d.LocationId;
            else if (d.ToLocationType == InventoryLocationType.Outlet && d.ToLocationId.HasValue)
                outletId = d.ToLocationId.Value;

            var number = string.IsNullOrWhiteSpace(d.TransferNo) ? d.Id.ToString() : d.TransferNo!;
            return Compose(outletId, 0, number);
        }

        // ---- Vouchers ----
        // Voucher has: int? OutletId, string? RefNo, no CounterId => counter = 0
        public static string FromVoucher(Voucher v)
            => Compose(v.OutletId ?? 0, 0, v.RefNo ?? v.Id.ToString());

        // Helper: if the number string is numeric, left-pad to 6; else keep as-is
        private static string NormalizeNumberString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "000000";
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n.ToString("000000", CultureInfo.InvariantCulture);
            return raw.Trim();
        }
    }
}
