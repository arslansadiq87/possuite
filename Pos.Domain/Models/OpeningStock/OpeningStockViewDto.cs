// Pos.Domain/Models/OpeningStock/OpeningStockViewDto.cs
using System;
using Pos.Domain;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.OpeningStock
{
    /// <summary>
    /// Read model for grids/reports. Includes display fields.
    /// </summary>
    public sealed class OpeningStockViewDto
    {
        public int Id { get; set; }
        public string DocNoOrId => $"OS-{Id:D5}";

        public StockDocStatus Status { get; set; }

        public InventoryLocationType LocationType { get; set; }
        public int LocationId { get; set; }

        public DateTime EffectiveDateUtc { get; set; }
        public string? Note { get; set; }

        public DateTime? PostedAtUtc { get; set; }
        public DateTime? LockedAtUtc { get; set; }
        public DateTime? VoidedAtUtc { get; set; }

        public string? VoidReason { get; set; }

        // display helpers
        public string? LocationName { get; set; }
        public string? CreatedByName { get; set; }
    }
}
