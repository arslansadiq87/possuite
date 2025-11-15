using System.Collections.Generic;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.Purchases
{
    public sealed class PurchaseReturnDraft
    {
        public int PartyId { get; set; }
        public InventoryLocationType LocationType { get; set; }
        public int? OutletId { get; set; }
        public int? WarehouseId { get; set; }
        public int RefPurchaseId { get; set; }
        public List<PurchaseReturnDraftLine> Lines { get; set; } = new();
    }

    public sealed class PurchaseReturnDraftLine
    {
        public int? OriginalLineId { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public decimal UnitCost { get; set; }
        public decimal MaxReturnQty { get; set; }
        public decimal ReturnQty { get; set; }
    }
}
