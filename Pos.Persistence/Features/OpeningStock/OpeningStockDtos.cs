using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Pos.Domain.Entities;

namespace Pos.Persistence.Features.OpeningStock
{
    public sealed class OpeningStockLineDto
    {
        [Required] public string Sku { get; set; } = "";
        [Range(typeof(decimal), "0.0001", "9999999999")] public decimal Qty { get; set; }  // > 0
        [Range(typeof(decimal), "0.0000", "9999999999")] public decimal UnitCost { get; set; } // required, 4dp
        public string? Note { get; set; }
    }

    public sealed class OpeningStockUpsertRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public List<OpeningStockLineDto> Lines { get; set; } = new();
        public bool ReplaceAll { get; set; } = true;  // replace vs. merge-add by SKU
    }

    public sealed class OpeningStockCreateRequest
    {
        [Required] public InventoryLocationType LocationType { get; set; }
        [Required] public int LocationId { get; set; }
        [Required] public DateTime EffectiveDateUtc { get; set; }
        public string? Note { get; set; }
        [Required] public int CreatedByUserId { get; set; }
    }

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
}
