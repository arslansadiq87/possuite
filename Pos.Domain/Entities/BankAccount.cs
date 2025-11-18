using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public sealed class BankAccount : BaseEntity
    {
        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;
        public string BankName { get; set; } = "";
        public string? Branch { get; set; }
        public string? AccountNumber { get; set; }
        public string? IBAN { get; set; }
        public string? SwiftBic { get; set; }
        public bool IsActive { get; set; } = true;
        public string? Notes { get; set; }
    }
}
