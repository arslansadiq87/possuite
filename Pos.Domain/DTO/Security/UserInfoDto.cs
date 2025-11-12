using System.Collections.Generic;
using Pos.Domain;

namespace Pos.Domain.DTO.Security
{
    public sealed class UserInfoDto
    {
        public int Id { get; init; }
        public string Username { get; init; } = "";
        public string FullName { get; init; } = "";
        public bool IsActive { get; init; }
        public UserRole Role { get; init; }                 // legacy/global role
        public bool IsGlobalAdmin { get; init; }            // bypass outlet checks
        public IReadOnlyList<UserOutletRoleDto> OutletRoles { get; init; } = new List<UserOutletRoleDto>();
    }
}
