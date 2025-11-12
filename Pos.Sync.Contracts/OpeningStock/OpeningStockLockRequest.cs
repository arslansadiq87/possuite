// Pos.Contracts/OpeningStock/OpeningStockLockRequest.cs
using System.ComponentModel.DataAnnotations;

namespace Pos.Contracts.OpeningStock
{
    public sealed class OpeningStockLockRequest
    {
        [Required] public int StockDocId { get; set; }
        [Required] public int LockedByUserId { get; set; }
    }
}
