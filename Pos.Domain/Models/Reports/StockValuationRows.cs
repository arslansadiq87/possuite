namespace Pos.Domain.Models.Reports
{
    public sealed class StockValuationRow
    {
        public int ItemId { get; set; }
        public string Sku { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal OnHand { get; set; }
        public decimal UnitCost { get; set; }      // avg cost (for Cost view)
        public decimal UnitPrice { get; set; }     // sale price (for Sale view)
        public decimal TotalCost => OnHand * UnitCost;
        public decimal TotalPrice => OnHand * UnitPrice;
    }
}
