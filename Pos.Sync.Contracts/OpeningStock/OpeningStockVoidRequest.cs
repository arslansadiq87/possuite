// Pos.Contracts/OpeningStock/OpeningStockVoidRequest.cs
using System.ComponentModel.DataAnnotations;

namespace Pos.Contracts.OpeningStock
{
    public sealed class OpeningStockVoidRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int VoidedByUserId { get; set; }
        public string? Reason { get; set; }
    }
}
