namespace Pos.Domain.DTO.Security
{
    public sealed class UserOutletRoleDto
    {
        public int OutletId { get; init; }
        public int Role { get; init; } // store as int to avoid coupling; cast to UserRole in consumer if needed
    }
}
