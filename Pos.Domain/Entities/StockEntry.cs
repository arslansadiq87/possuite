// Pos.Domain/Entities/StockEntry.cs
namespace Pos.Domain.Entities;
using Pos.Domain.Abstractions;

public class StockEntry : BaseEntity
{
    // LEGACY (to be removed after migration of callers)
    [Obsolete("Use LocationType + LocationId instead.")]
    public int OutletId { get; set; }
    public int ItemId { get; set; }

    // CHANGED: int -> decimal (18,4)
    public decimal QtyChange { get; set; }     // + for purchase/adjust-in, - for sale/adjust-out

    // NEW: required for Opening
    public decimal UnitCost { get; set; }      // (18,4)

    // Normalized location (self-contained)
    public InventoryLocationType LocationType { get; set; }
    public int LocationId { get; set; }

    // Header link (canonical)
    public int StockDocId { get; set; }
    public StockDoc? StockDoc { get; set; }

    // Keep your existing refs for compatibility
    public string RefType { get; set; } = "";  // "Sale","SaleReturn","Adjust","TransferOut","TransferIn","Opening"
    public int? RefId { get; set; }            // e.g. StockDoc.Id when RefType="Opening"

    public DateTime Ts { get; set; }

    public string? Note { get; set; }
}
