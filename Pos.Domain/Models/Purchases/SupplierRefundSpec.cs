using Pos.Domain.Entities;

namespace Pos.Domain.Models.Purchases
{
    /// <summary>
    /// Refund instruction used when saving a supplier return.
    /// </summary>
    public readonly record struct SupplierRefundSpec(TenderMethod Method, decimal Amount, string? Note);
}
