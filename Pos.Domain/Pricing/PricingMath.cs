// Pos.Domain/Pricing/PricingMath.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pos.Domain.Pricing
{
    // Lightweight “inputs → outputs” types so UI can map to/from its models
    public readonly record struct LineInput(
        int Qty,
        decimal UnitPrice,
        decimal? DiscountPct,
        decimal? DiscountAmt,
        decimal TaxRatePct,
        bool TaxInclusive
    );

    public readonly record struct LineAmounts(
        decimal UnitNet,   // per-unit net after discount (excl. tax)
        decimal LineNet,   // UnitNet * Qty
        decimal LineTax,   // tax amount for the whole line
        decimal LineTotal  // LineNet + LineTax
    );

    public static class PricingMath
    {
        public static decimal RoundMoney(decimal v)
            => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        /// <summary>Compute derived amounts for one line.</summary>
        public static LineAmounts CalcLine(LineInput x)
        {
            if (x.Qty == 0) return new(0, 0, 0, 0);

            // prefer DiscountAmt if both present
            var price = x.UnitPrice;
            decimal unitAfterDiscount =
                (x.DiscountAmt ?? 0) > 0 ? price - x.DiscountAmt!.Value :
                (x.DiscountPct ?? 0) > 0 ? price * (1 - (x.DiscountPct!.Value / 100m)) :
                price;

            decimal unitNet, unitTax;
            if (x.TaxInclusive)
            {
                var divisor = 1 + (x.TaxRatePct / 100m);
                unitNet = unitAfterDiscount / divisor;
                unitTax = unitAfterDiscount - unitNet;
            }
            else
            {
                unitNet = unitAfterDiscount;
                unitTax = unitNet * (x.TaxRatePct / 100m);
            }

            var unitNetR = RoundMoney(unitNet);
            var lineNet = RoundMoney(unitNetR * x.Qty);
            var lineTax = RoundMoney(unitTax * x.Qty);
            var lineTot = lineNet + lineTax;
            return new(unitNetR, lineNet, lineTax, lineTot);
        }

        /// <summary>
        /// Apply invoice-level discount BEFORE tax (retail typical).
        /// Returns: (invoiceDiscountValue, subtotalAfterDisc, taxAfterDisc, grand).
        /// Lines are taken as already-calculated (LineNet/LineTax).
        /// </summary>
        public static (decimal invoiceDiscountValue, decimal subtotal, decimal tax, decimal grand)
            CalcTotalsWithInvoiceDiscount(IEnumerable<LineAmounts> lines, decimal? invPct, decimal? invAmt, bool discountBeforeTax = true)
        {
            var list = lines.ToList();
            var baseNet = list.Sum(l => l.LineNet);
            var baseTax = list.Sum(l => l.LineTax);

            if (!discountBeforeTax || baseNet <= 0m)
            {
                var subtotalNoDisc = baseNet;
                var taxNoDisc = baseTax;
                var grandNoDisc = subtotalNoDisc + taxNoDisc;
                return (0m, subtotalNoDisc, taxNoDisc, grandNoDisc);
            }

            var invValue = (invAmt ?? 0m) > 0m
                ? Math.Min(invAmt!.Value, baseNet)
                : RoundMoney(baseNet * ((invPct ?? 0m) / 100m));

            var factor = (baseNet - invValue) / baseNet;

            decimal adjNetSum = 0m, adjTaxSum = 0m;
            foreach (var l in list)
            {
                var adjNet = RoundMoney(l.LineNet * factor);

                // keep each line’s tax ratio stable, scale proportionally
                var taxPerNet = l.LineNet > 0m ? (l.LineTax / l.LineNet) : 0m;
                var adjTax = RoundMoney(adjNet * taxPerNet);

                adjNetSum += adjNet;
                adjTaxSum += adjTax;
            }

            var subtotal = adjNetSum;            // AFTER invoice discount
            var tax = adjTaxSum;
            var grand = subtotal + tax;
            return (invValue, subtotal, tax, grand);
        }
    }
}
