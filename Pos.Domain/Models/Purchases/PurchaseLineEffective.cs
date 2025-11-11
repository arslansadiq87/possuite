namespace Pos.Domain.Models.Purchases
{
    public sealed class PurchaseLineEffective
    {
        public int ItemId { get; set; }
        public string? Sku { get; set; }
        public string? Name { get; set; }
        public decimal Qty { get; set; }
        public decimal UnitCost { get; set; }  // display-only
        public decimal Discount { get; set; }  // display-only
        public decimal TaxRate { get; set; }   // display-only
    }
}
