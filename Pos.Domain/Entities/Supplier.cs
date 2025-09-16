// Pos.Domain/Entities/Supplier.cs
using System;
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    // Replace BaseEntity with your actual base class name if different
    public class Supplier : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string? Phone { get; set; }
        public string? Email { get; set; }

        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }

        public bool IsActive { get; set; } = true;

        // Optional—handy for initial balances, we’ll use later in ledger
        public decimal OpeningBalance { get; set; }
        public DateTime? OpeningBalanceDate { get; set; }
    }
}
