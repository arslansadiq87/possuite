// Pos.Domain/Entities/Counter.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Counter : BaseEntity
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public int OutletId { get; set; }
        public Outlet Outlet { get; set; } = null!;
        public ICollection<CounterBinding> CounterBindings { get; set; } = new List<CounterBinding>();

    }
}
