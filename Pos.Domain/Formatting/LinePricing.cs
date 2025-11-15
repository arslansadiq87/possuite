using System;

namespace Pos.Domain.Formatting
{
    public static class LinePricing
    {
        // Result of a line re-calculation
        public readonly struct LineTotals
        {
            public LineTotals(decimal qty, decimal unitPrice, decimal gross, decimal discount, decimal net, decimal tax, decimal total)
            {
                Qty = qty;
                UnitPrice = unitPrice;
                Gross = gross;
                Discount = discount;
                Net = net;
                Tax = tax;
                Total = total;
            }

            public decimal Qty { get; }
            public decimal UnitPrice { get; }
            public decimal Gross { get; }     // qty * unitPrice
            public decimal Discount { get; }  // pct + absolute (clamped)
            public decimal Net { get; }       // Gross - Discount (pre/post tax depends on taxInclusive)
            public decimal Tax { get; }       // computed tax
            public decimal Total { get; }     // Net (+ tax if exclusive)
        }

        /// <summary>
        /// Single source of truth for line math (sales/sales-return/invoice).
        /// All rounding is AwayFromZero at 2 decimals (tweak if your currency uses different scale).
        /// </summary>
        /// <param name="qty">Quantity (>= 0)</param>
        /// <param name="unitPrice">Unit price (>= 0)</param>
        /// <param name="discountPct">Percentage discount on gross (0..100)</param>
        /// <param name="discountAmt">Absolute discount on gross (>= 0)</param>
        /// <param name="taxInclusive">If true, unit price includes tax</param>
        /// <param name="taxRatePct">Tax rate percent (>= 0)</param>
        public static LineTotals Recalc(
            decimal qty,
            decimal unitPrice,
            decimal discountPct,
            decimal discountAmt,
            bool taxInclusive,
            decimal taxRatePct,
            int currencyDecimals = 2,
            MidpointRounding rounding = MidpointRounding.AwayFromZero)
        {
            qty = Math.Max(0m, qty);
            unitPrice = Math.Max(0m, unitPrice);
            taxRatePct = Math.Max(0m, taxRatePct);

            var gross = Round(qty * unitPrice, currencyDecimals, rounding);

            // percentage discount on gross
            var pctDisc = discountPct <= 0m ? 0m
                         : Round(gross * (discountPct / 100m), currencyDecimals, rounding);

            // absolute discount on gross
            var absDisc = discountAmt <= 0m ? 0m
                         : Round(discountAmt, currencyDecimals, rounding);

            // clamp total discount to [0, gross]
            var discount = Math.Min(gross, Math.Max(0m, pctDisc + absDisc));

            if (taxInclusive)
            {
                // price already includes tax
                var netIncl = Round(gross - discount, currencyDecimals, rounding);
                if (taxRatePct == 0m)
                {
                    var tax0 = 0m;
                    return new LineTotals(qty, unitPrice, gross, discount, netIncl, tax0, netIncl);
                }

                var divisor = 1m + (taxRatePct / 100m);
                var netEx = Round(netIncl / divisor, currencyDecimals, rounding);
                var tax = Round(netIncl - netEx, currencyDecimals, rounding);
                return new LineTotals(qty, unitPrice, gross, discount, netEx, tax, netIncl);
            }
            else
            {
                // price excludes tax
                var netEx = Round(gross - discount, currencyDecimals, rounding);
                var tax = taxRatePct == 0m ? 0m
                           : Round(netEx * (taxRatePct / 100m), currencyDecimals, rounding);
                var total = Round(netEx + tax, currencyDecimals, rounding);
                return new LineTotals(qty, unitPrice, gross, discount, netEx, tax, total);
            }
        }

        private static decimal Round(decimal value, int decimals, MidpointRounding rounding)
            => Math.Round(value, decimals, rounding);
    }
}
