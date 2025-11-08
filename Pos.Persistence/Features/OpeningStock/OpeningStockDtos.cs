using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Pos.Domain;
using Pos.Domain.Entities;

namespace Pos.Persistence.Features.OpeningStock
{
    //
    // ────────────────────────────────────────────────
    //  LINE DTO
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockLineDto
    {
        [Required] public string Sku { get; set; } = "";

        [Range(typeof(decimal), "0.0001", "9999999999", ErrorMessage = "Qty must be greater than zero.")]
        public decimal Qty { get; set; } // > 0

        [Range(typeof(decimal), "0.0000", "9999999999", ErrorMessage = "Invalid Unit Cost.")]
        public decimal UnitCost { get; set; } // required, 4dp

        public string? Note { get; set; }
    }

    //
    // ────────────────────────────────────────────────
    //  CREATE REQUEST (header creation)
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockCreateRequest
    {
        [Required] public InventoryLocationType LocationType { get; set; }
        [Required] public int LocationId { get; set; }

        [Required] public DateTime EffectiveDateUtc { get; set; }

        public string? Note { get; set; }

        [Required] public int CreatedByUserId { get; set; }
    }

    //
    // ────────────────────────────────────────────────
    //  UPSERT REQUEST (add/update lines on draft)
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockUpsertRequest
    {
        [Required] public int StockDocId { get; set; }

        [Required] public List<OpeningStockLineDto> Lines { get; set; } = new();

        /// <summary>
        /// True = replace all existing lines, false = merge/add by SKU
        /// </summary>
        public bool ReplaceAll { get; set; } = true;
    }

    //
    // ────────────────────────────────────────────────
    //  VIEW DTO (for grids or reports)
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockViewDto
    {
        public int Id { get; set; }
        public string DocNoOrId => $"OS-{Id:D5}"; // optional formatting

        public OpeningStockStatus Status { get; set; }

        public InventoryLocationType LocationType { get; set; }
        public int LocationId { get; set; }

        public DateTime EffectiveDateUtc { get; set; }
        public string? Note { get; set; }

        public DateTime? PostedAtUtc { get; set; }
        public DateTime? LockedAtUtc { get; set; }
        public DateTime? VoidedAtUtc { get; set; }

        public string? VoidReason { get; set; }

        public string? LocationName { get; set; } // for display
        public string? CreatedByName { get; set; }
    }

    //
    // ────────────────────────────────────────────────
    //  VALIDATION SUPPORT
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockValidationError
    {
        public int? RowIndex { get; set; }
        public string Field { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Sku { get; set; }
    }

    public sealed class OpeningStockValidationResult
    {
        public bool Ok => Errors.Count == 0;
        public List<OpeningStockValidationError> Errors { get; } = new();
    }

    //
    // ────────────────────────────────────────────────
    //  ACTION REQUESTS
    // ────────────────────────────────────────────────
    //
    public sealed class OpeningStockPostRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int PostedByUserId { get; set; }
    }

    public sealed class OpeningStockLockRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int LockedByUserId { get; set; }
    }

    public sealed class OpeningStockVoidRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int VoidedByUserId { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class OpeningDocSummaryDto
    {
        public int Id { get; init; }
        public DateTime EffectiveDateUtc { get; init; }
        public int LineCount { get; init; }
        public decimal TotalQty { get; init; }
        public decimal TotalValue { get; init; }
        public string? Note { get; init; }
        public StockDocStatus Status { get; init; }
    }
}
