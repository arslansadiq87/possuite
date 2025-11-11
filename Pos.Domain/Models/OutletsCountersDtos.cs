namespace Pos.Domain.Models
{
    // Lightweight, read-only DTOs for safe binding across UI/server.
    public sealed class OutletRow
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
        public string Name { get; init; } = "";
        public string? Address { get; init; }
        public bool IsActive { get; init; }
    }

    public sealed class CounterRow
    {
        public int Id { get; init; }
        public int OutletId { get; init; }
        public string Name { get; init; } = "";
        public bool IsActive { get; init; }
        public string? AssignedTo { get; init; }
    }
}
