// Pos.Domain/Entities/User.cs
using System.Collections.Generic;
using Pos.Domain.Abstractions;
//using Pos.Domain.Enums;       // for UserRole
using Pos.Domain.Entities;    // for UserOutlet

namespace Pos.Domain.Entities
{
    public class User : BaseEntity
    {
        // --- existing fields (kept for backward-compat) ---
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public UserRole Role { get; set; } = UserRole.Cashier;  // GLOBAL default role (legacy)
        public bool IsActive { get; set; } = true;
        public string PasswordHash { get; set; } = null!;       // string, not byte[]

        // --- new fields for outlet-scoped RBAC ---
        /// <summary>
        /// If true, this user bypasses outlet filters and can operate on any outlet.
        /// </summary>
        public bool IsGlobalAdmin { get; set; } = false;

        /// <summary>
        /// Per-outlet role assignments (Manager/Cashier/etc.) for this user.
        /// </summary>
        public ICollection<UserOutlet> UserOutlets { get; set; } = new List<UserOutlet>();
    }
}
