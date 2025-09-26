// Pos.Domain/Entities/Warehouse.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Warehouse : BaseEntity
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;

        // (optional) Audit – add if your BaseEntity is required here
        // public DateTime CreatedAtUtc { get; set; }
        // public DateTime? UpdatedAtUtc { get; set; }
    }
}
