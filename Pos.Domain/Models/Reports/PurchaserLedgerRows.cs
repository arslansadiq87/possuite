namespace Pos.Domain.Models.Reports
{
    public sealed class PurchaserLedgerRow
    {
        public int PurchaseId { get; set; }
        public string DocNo { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateTime TsUtc { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal Paid { get; set; }
        public decimal Due => GrandTotal - Paid;
    }
}
