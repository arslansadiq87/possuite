// Pos.Domain/Accounting/OpeningBalance.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Accounting
{
    public class OpeningBalance : BaseEntity
    {
        public DateTime AsOfDate { get; set; }           // date of opening
        public int? OutletId { get; set; }               // optional, if outlet-scoped
        public string? Memo { get; set; }
        public bool IsPosted { get; set; }
        public List<OpeningBalanceLine> Lines { get; set; } = new();
    }

    public class OpeningBalanceLine : BaseEntity
    {
        public int OpeningBalanceId { get; set; }
        public OpeningBalance OpeningBalance { get; set; } = null!;

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;

        // Enter as positive amounts on their natural side
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
