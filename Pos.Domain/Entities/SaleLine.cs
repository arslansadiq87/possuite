// Pos.Domain/Entities/SaleLine.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pos.Domain.Entities;
using Pos.Domain.Abstractions;
public class SaleLine : BaseEntity
{
    public int SaleId { get; set; }
    public int ItemId { get; set; }

    public int Qty { get; set; }                 // negative for returns
    public decimal UnitPrice { get; set; }       // list/base price

    // --- Discounts (choose either pct or amt; we'll prefer Amt if both are set) ---
    public decimal? DiscountPct { get; set; }    // e.g., 10 = 10% off (per-unit)
    public decimal? DiscountAmt { get; set; }    // currency off per unit

    // --- Taxes ---
    public string? TaxCode { get; set; }         // e.g., "STD", "ZERO"
    public decimal TaxRatePct { get; set; }      // e.g., 18 = 18%
    public bool TaxInclusive { get; set; }       // true if UnitPrice is tax-inclusive

    // --- Derived amounts (persisted for stable reporting) ---
    public decimal UnitNet { get; set; }         // net (excl. tax) per unit after discount
    public decimal LineNet { get; set; }         // UnitNet * Qty
    public decimal LineTax { get; set; }         // tax amount for the line
    public decimal LineTotal { get; set; }       // LineNet + LineTax
}
