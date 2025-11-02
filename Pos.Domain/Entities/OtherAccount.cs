using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    // Lightweight master for "Others" (non-party, non-staff accounts)
    public sealed class OtherAccount : BaseEntity
    {
        public string? Code { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;

        // Link to its GL account under 64
        public int? AccountId { get; set; }
    }
}
