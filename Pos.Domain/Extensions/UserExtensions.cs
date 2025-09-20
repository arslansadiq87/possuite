// Pos.Domain/Extensions/UserExtensions.cs
using Pos.Domain.Entities;

namespace Pos.Domain.Extensions
{
    public static class UserExtensions
    {
        public static bool IsAdmin(this User u)
            => u.IsGlobalAdmin || u.Role == UserRole.Admin;
    }
}
