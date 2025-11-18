// Pos.Domain/Entities/Warehouse.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public sealed class Warehouse : BaseEntity
    {
        public string Code { get; set; } = "";   // e.g. MAIN, WH-N
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        // Optional metadata (handy in reports/transfers)
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Phone { get; set; }
        public string? Note { get; set; }
    }
}
