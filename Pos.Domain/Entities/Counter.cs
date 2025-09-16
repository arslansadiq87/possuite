// Pos.Domain/Entities/Counter.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    // NOTE: name 'Counter' can clash with generic types; we’ll alias in DbContext later.
    public class Counter : BaseEntity
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;

        public int OutletId { get; set; }
        public Outlet Outlet { get; set; } = null!;
    }
}
