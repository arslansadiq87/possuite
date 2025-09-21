using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class SupplierCredit : BaseEntity
    {
        public int SupplierId { get; set; }
        public int? OutletId { get; set; }     // optional scoping per outlet
        public decimal Amount { get; set; }    // positive = credit available
        public string Source { get; set; } = "";  // e.g., "Return PR-20250922-001"
    }
}
