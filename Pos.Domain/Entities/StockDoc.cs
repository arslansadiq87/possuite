using Pos.Domain.Abstractions;
using System;
using System.Collections.Generic;

namespace Pos.Domain.Entities
{
    public enum TransferStatus
    {
        Draft = 0,
        Dispatched = 1,
        Received = 2
    }

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

        // EXISTING: Opening uses ledger Lines (StockEntry) directly
        public ICollection<StockEntry> Lines { get; set; } = new List<StockEntry>();

        // NEW: Transfer lines (intent) live in a separate collection
        public ICollection<StockDocLine> TransferLines { get; set; } = new List<StockDocLine>();

        // Destination for Transfers
        public InventoryLocationType? ToLocationType { get; set; }
        public int? ToLocationId { get; set; }

        // Transfer-only fields
        public TransferStatus? TransferStatus { get; set; }
        public DateTime? ReceivedAtUtc { get; set; }

        // Human-readable number (unique). Format: TR-{FROMCODE}-{yyyy}-{00001}
        public string? TransferNo { get; set; }
    }

    public enum StockDocType { Opening = 1, Transfer = 10 }
    public enum StockDocStatus { Draft = 0, Locked = 1 }
    public enum InventoryLocationType { Outlet = 1, Warehouse = 2 }
}
