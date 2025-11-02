// Pos.Domain/Entities/Account.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public enum AccountType          // for reporting buckets
    {
        Asset = 1, Liability = 2, Equity = 3, Income = 4, Expense = 5, // GAAP buckets
        Parties = 90,  // virtual root under which Customer/Supplier/Other party accounts live
        System = 99    // internal/system roots if needed
    }

    public enum NormalSide { Debit = 0, Credit = 1 }

    public class Account : BaseEntity
    {
        public string Code { get; set; } = "";         // unique per scope
        public string Name { get; set; } = "";
        public AccountType Type { get; set; }
        public NormalSide NormalSide { get; set; }     // reporting normal balance

        public bool IsHeader { get; set; }             // true = grouping node (no posting)
        public bool AllowPosting { get; set; } = true; // headers false; leaf true

        public int? ParentId { get; set; }
        public Account? Parent { get; set; }

        // Scope: NULL = company-level; value = specific outlet
        public int? OutletId { get; set; }
        public Outlet? Outlet { get; set; }

        // Opening balance (entered once then locked)
        public decimal OpeningDebit { get; set; }
        public decimal OpeningCredit { get; set; }
        public bool IsOpeningLocked { get; set; } = false;

        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; } = false;    // prevents delete/rename for seeded roots
    }

    // Simple GL journal; append-only
    public class Journal : BaseEntity
    {
        public DateTime TsUtc { get; set; } = DateTime.UtcNow;
        public string? Memo { get; set; }

        // Scope for outlet-level reporting (NULL = company)
        public int? OutletId { get; set; }
        public Outlet? Outlet { get; set; }

        // Cross-links for audit
        public string? RefType { get; set; }     // e.g., "Sale","Purchase","Till"
        public int? RefId { get; set; }
    }

    public class JournalLine : BaseEntity
    {
        public int JournalId { get; set; }
        public Journal Journal { get; set; } = null!;

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;

        public int? PartyId { get; set; }        // optional – link to AR/AP party
        public Party? Party { get; set; }

        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public string? Memo { get; set; }
    }
}
