using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public sealed class OtherAccount : BaseEntity
    {
        public string? Code { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int? AccountId { get; set; }
    }
}
