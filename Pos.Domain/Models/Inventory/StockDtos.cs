// Pos.Domain/Models/Inventory/StockDtos.cs
using Pos.Domain;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.Inventory
{
    /// <summary>
    /// Proposed stock change at a specific location.
    /// Matching uses (LocType, LocId) ONLY. OutletId is for message context.
    /// Delta &lt; 0 reduces stock; &gt; 0 increases stock.
    /// </summary>
    public sealed record StockDeltaDto(
        int ItemId,
        int OutletId,
        InventoryLocationType LocType,
        int LocId,
        decimal Delta
    );
}
