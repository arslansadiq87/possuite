// Pos.Domain/Entities/StockDoc.cs
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

    public enum StockDocType
    {
        Opening = 1,
        Transfer = 10
    }

    // UPDATED: add Posted and Voided to support the Opening Stock flow
    public enum StockDocStatus
    {
        Draft = 0,
        Posted = 1,
        Locked = 2,
        Voided = 3
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

        // Lock audit (already present)
        public int? LockedByUserId { get; set; }
        public DateTime? LockedAtUtc { get; set; }

        // NEW: Post audit (used by OpeningStockService.PostAsync)
        public int? PostedByUserId { get; set; }
        public DateTime? PostedAtUtc { get; set; }

        // NEW: Void audit (used by OpeningStockService.VoidAsync)
        public int? VoidedByUserId { get; set; }
        public DateTime? VoidedAtUtc { get; set; }
        public string? VoidReason { get; set; }

        // Opening uses ledger lines (StockEntry) directly
        public ICollection<StockEntry> Lines { get; set; } = new List<StockEntry>();

        // Transfer lines (intent) live in a separate collection
        public ICollection<StockDocLine> TransferLines { get; set; } = new List<StockDocLine>();

        // Destination for Transfers
        public InventoryLocationType? ToLocationType { get; set; }
        public int? ToLocationId { get; set; }

        // Transfer-only fields
        public TransferStatus? TransferStatus { get; set; }
        public DateTime? ReceivedAtUtc { get; set; }

        // Human-readable number (unique). Format: TR-{FROMCODE}-{yyyy}-{00001}
        public string? TransferNo { get; set; }
        public bool AutoReceiveOnDispatch { get; set; }    // UI toggle
    }

    public enum InventoryLocationType
    {
        Outlet = 1,
        Warehouse = 2
    }
}
