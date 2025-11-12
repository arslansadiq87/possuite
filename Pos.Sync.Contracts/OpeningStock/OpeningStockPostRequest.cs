// Pos.Contracts/OpeningStock/OpeningStockPostRequest.cs
using System.ComponentModel.DataAnnotations;

namespace Pos.Contracts.OpeningStock
{
    public sealed class OpeningStockPostRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int PostedByUserId { get; set; }
    }
}
