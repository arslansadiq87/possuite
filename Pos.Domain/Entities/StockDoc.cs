using Pos.Domain.Abstractions;
using System.Collections.Generic;

namespace Pos.Domain.Entities
{
    public sealed class StockDoc : BaseEntity
    {
        public StockDocType DocType { get; set; } = StockDocType.Opening;
        public StockDocStatus Status { get; set; } = StockDocStatus.Draft;

        public InventoryLocationType LocationType { get; set; }
        public int LocationId { get; set; }

        public DateTime EffectiveDateUtc { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }

        public int CreatedByUserId { get; set; }
        public int? LockedByUserId { get; set; }
        public DateTime? LockedAtUtc { get; set; }

        public ICollection<StockEntry> Lines { get; set; } = new List<StockEntry>();
    }

    public enum StockDocType { Opening = 1 }
    public enum StockDocStatus { Draft = 0, Locked = 1 }
    public enum InventoryLocationType { Outlet = 1, Warehouse = 2 }
}
