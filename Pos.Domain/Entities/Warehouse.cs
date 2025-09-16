// Pos.Domain/Entities/Warehouse.cs
namespace Pos.Domain.Entities
{
    public class Warehouse
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;

        // (optional) Audit – add if your BaseEntity is required here
        // public DateTime CreatedAtUtc { get; set; }
        // public DateTime? UpdatedAtUtc { get; set; }
    }
}
