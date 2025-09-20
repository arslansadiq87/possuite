using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Party : BaseEntity
    {
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? TaxNumber { get; set; }
        public bool IsActive { get; set; } = true;

        // Visibility & defaults
        public bool IsSharedAcrossOutlets { get; set; } = true;

        // Navigation
        public ICollection<PartyRole> Roles { get; set; } = new List<PartyRole>();
        public ICollection<PartyOutlet> Outlets { get; set; } = new List<PartyOutlet>();
    }

    public class PartyOutlet : BaseEntity
    {
        public int PartyId { get; set; }
        public int OutletId { get; set; }
        public bool AllowCredit { get; set; }     // per-outlet credit policy
        public decimal? CreditLimit { get; set; } // per-outlet override
        public Party Party { get; set; } = null!;
        public Outlet Outlet { get; set; } = null!;
        public bool IsActive { get; set; } = true;
    }

    public enum PartyLedgerDocType
    {
        Sale, SaleReturn, Receipt, WriteOff,
        Purchase, PurchaseReturn, Payment, Adjustment
    }

    public class PartyLedger : BaseEntity
    {
        public int PartyId { get; set; }
        public int? OutletId { get; set; }        // NULL = company-level
        public DateTime TimestampUtc { get; set; } // ← renamed
        public PartyLedgerDocType DocType { get; set; }
        public int DocId { get; set; }
        public string? Description { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        // public decimal RunningBalance { get; set; } // ← consider removing
        public Party Party { get; set; } = null!;
        public Outlet? Outlet { get; set; }
    }

    public class PartyBalance : BaseEntity
    {
        public int PartyId { get; set; }
        public int? OutletId { get; set; }      // NULL = company-level
        public decimal Balance { get; set; }
        public DateTime AsOfUtc { get; set; }   // ← renamed
    }

    public enum RoleType { Customer = 1, Supplier = 2 }

    public class PartyRole : BaseEntity
    {
        public int PartyId { get; set; }
        public RoleType Role { get; set; }
        public Party Party { get; set; } = null!;
    }

}
