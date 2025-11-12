using System;

namespace Pos.Domain.DTO.Admin
{
    public sealed class CounterBindingDto
    {
        public int Id { get; init; }
        public string MachineId { get; init; } = "";
        public string MachineName { get; init; } = "";
        public int OutletId { get; init; }
        public string OutletName { get; init; } = "";
        public int CounterId { get; init; }
        public string CounterName { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTime LastSeenUtc { get; init; }
    }
}
