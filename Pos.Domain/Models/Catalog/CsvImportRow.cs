// Pos.Domain/Models/Catalog/CsvImportRow.cs
namespace Pos.Domain.Models.Catalog
{
    public sealed class CsvImportRow
    {
        // Common
        public string Type { get; set; } = "";        // "Standalone" | "Variant"
        public string? Brand { get; set; }
        public string? Category { get; set; }

        // Item fields (both standalone and variant items)
        public string ItemName { get; set; } = "";
        public string SKU { get; set; } = "";
        public string? Barcode { get; set; }
        public decimal Price { get; set; }
        public string? TaxCode { get; set; }
        public decimal TaxRatePct { get; set; }
        public bool TaxInclusive { get; set; }

        // Product/variant fields (only if Type == "Variant")
        public string? ProductName { get; set; }
        public string? Variant1Name { get; set; }
        public string? Variant1Value { get; set; }
        public string? Variant2Name { get; set; }
        public string? Variant2Value { get; set; }

        // UI-only
        public string Status { get; set; } = "Pending"; // Pending|Valid|Error|Saved
        public string? Error { get; set; }
        public int RowNo { get; set; }                  // original CSV row (1-based with header)
    }
}
