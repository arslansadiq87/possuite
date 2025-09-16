// Pos.Domain/Entities/StockEntry.cs
namespace Pos.Domain.Entities;
using Pos.Domain.Abstractions;

public class StockEntry : BaseEntity
{
    public int OutletId { get; set; }
    public int ItemId { get; set; }
    public int QtyChange { get; set; }     // + for purchase/adjust-in, - for sale/adjust-out
    public string RefType { get; set; } = ""; // "Sale","SaleReturn","Adjust","TransferOut","TransferIn"
    public int? RefId { get; set; }        // e.g. SaleId
    public DateTime Ts { get; set; }
}
