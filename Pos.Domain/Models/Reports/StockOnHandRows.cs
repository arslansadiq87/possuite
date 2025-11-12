namespace Pos.Domain.Models.Reports
{
    public sealed class StockOnHandItemRow
    {
        public string Sku { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Variant { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal OnHand { get; set; }
    }

    public sealed class StockOnHandProductRow
    {
        public string Product { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal OnHand { get; set; }
    }
}
