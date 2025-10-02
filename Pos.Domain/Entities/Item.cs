// Pos.Domain/Entities/Item.cs
using Pos.Domain.Abstractions;
using Pos.Domain.Entities;

namespace Pos.Domain.Entities
{
    public class Item : BaseEntity
    {
        public string Sku { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Tax defaults
        public string? TaxCode { get; set; }
        public decimal DefaultTaxRatePct { get; set; }
        public bool TaxInclusive { get; set; }

        // Discount defaults
        public decimal? DefaultDiscountPct { get; set; }
        public decimal? DefaultDiscountAmt { get; set; }

        // Parent (null => standalone)
        public int? ProductId { get; set; }
        public Product? Product { get; set; }

        // Variant axes (only meaningful when ProductId != null)
        public string? Variant1Name { get; set; }
        public string? Variant1Value { get; set; }
        public string? Variant2Name { get; set; }
        public string? Variant2Value { get; set; }

        // Optional per-item brand/category (kept nullable; you can rely on Product-level instead)
        public int? BrandId { get; set; }
        public Brand? Brand { get; set; }
        public bool IsActive { get; set; } = true;       // if not already present
        public bool IsVoided { get; set; }               // NEW
        public DateTime? VoidedAtUtc { get; set; }       // NEW
        public string? VoidedBy { get; set; }            // NEW

        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
        public ICollection<ItemBarcode> Barcodes { get; set; } = new List<ItemBarcode>();

    }
}
