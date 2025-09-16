// Pos.Domain/Entities/Outlet.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Outlet : BaseEntity
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";   // short code like "MAIN"
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;

        public ICollection<Counter> Counters { get; set; } = new List<Counter>();
    }
}
