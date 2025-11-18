// Pos.Domain/Entities/OpeningStock.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pos.Domain;
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class OpeningStock : BaseEntity
    {
        public OpeningStockStatus Status { get; set; } = OpeningStockStatus.Draft;
        // Location
        public InventoryLocationType LocationType { get; set; } // Outlet or Warehouse
        public int LocationId { get; set; }
        public DateTime TsUtc { get; set; } // document timestamp
        // Audit (posting, locking, voiding)
        public DateTime? PostedAtUtc { get; set; }
        public int? PostedByUserId { get; set; }
        public DateTime? LockedAtUtc { get; set; }
        public int? LockedByUserId { get; set; }
        public DateTime? VoidedAtUtc { get; set; }
        public int? VoidedByUserId { get; set; }
        public string? VoidReason { get; set; }
        // Posted lines (these reflect in stock after Post)
        public List<OpeningStockLine> Lines { get; set; } = new();
    }

    public class OpeningStockLine : BaseEntity
    {
        public int OpeningStockId { get; set; }
        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Qty { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitCost { get; set; }
        public string? Note { get; set; }
        // nav
        public OpeningStock OpeningStock { get; set; } = null!;
    }

    /// <summary>
    /// Lines for Opening Stock while the document is in Draft.
    /// Kept separate from posted Lines to ensure no on-hand impact until Post.
    /// NOTE: Draft lines are keyed to StockDoc (StockDocId), not OpeningStock.
    /// </summary>
    [Table("OpeningStockDraftLines")]
    public class OpeningStockDraftLine
    {
        [Key] public int Id { get; set; }
        [Required] public int StockDocId { get; set; }   // ← StockDoc-centric flow
        [Required] public int ItemId { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal Qty { get; set; }
        [Column(TypeName = "decimal(18,4)")]
        public decimal UnitCost { get; set; }
        public string? Note { get; set; }
    }
}