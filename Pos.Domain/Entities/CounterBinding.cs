using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    /// <summary>
    /// One-to-one association between a physical PC (MachineId) and a Counter.
    /// Enforced by unique indexes (MachineId) and (CounterId).
    /// </summary>
    public class CounterBinding : BaseEntity
    {
        public string MachineId { get; set; } = "";  // stable GUID for the PC
        public string MachineName { get; set; } = ""; // optional: Environment.MachineName
        public int OutletId { get; set; }
        public int CounterId { get; set; }

        public bool IsActive { get; set; } = true;    // soft toggle if you prefer
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        public Outlet Outlet { get; set; } = null!;
        public Counter Counter { get; set; } = null!;
    }
}
