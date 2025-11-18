using System;
using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class StockDocLine : BaseEntity
    {
        // Header
        public int StockDocId { get; set; }
        // Item identity (canonical)
        public int ItemId { get; set; }
        // Snapshots for offline clarity / printing
        public string SkuSnapshot { get; set; } = "";
        public string ItemNameSnapshot { get; set; } = "";
        // Quantities (expected at dispatch; received at destination)
        public decimal QtyExpected { get; set; }     // decimal(18,4)
        public decimal? QtyReceived { get; set; }    // decimal(18,4), null until Received
        // Cost snapshot taken at Dispatch (source MA); audit only (NOT used to recalc at receive)
        public decimal? UnitCostExpected { get; set; }  // decimal(18,4)
        // Notes
        public string? Remarks { get; set; }
        public string? VarianceNote { get; set; }
        // Read-only helpers (not mapped; UI convenience)
        [NotMapped] public decimal ShortQty => Math.Max(QtyExpected - (QtyReceived ?? 0m), 0m);
        [NotMapped] public decimal OverQty => Math.Max((QtyReceived ?? 0m) - QtyExpected, 0m);
    }
}