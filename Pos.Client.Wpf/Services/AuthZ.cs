// Pos.Client.Wpf/Services/AuthZ.cs
using System;
using System.Collections.Generic;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Services
{
    public static class AuthZ
    {
        // Rank order matters (bigger = more power)
        public enum Role
        {
            Salesman = 0,
            Cashier = 1,
            Supervisor = 2,
            Manager = 3,
            Admin = 4
        }

        // Permissions (expand as needed)
        public enum Perm
        {
            // Purchases
            Purchases_View_All,       // see all outlets’ purchases
            Purchases_Edit,           // create/amend/receive
            PurchaseReturns_Process,  // process purchase returns

            // Sales
            Sales_View_All,
            Sales_Returns_Process,
            Sales_Void,

            // Till/Shift
            Shift_Open,
            Shift_Close,
            Drawer_Kick,

            // Admin & Masters
            Users_Manage,
            Catalog_Manage,
            Outlets_Manage,
            Reports_View_All
        }

        // --- Public API ---

        // Handy accessor if you need the user anywhere
        public static User? CurrentUser => AppState.Current.CurrentUser;

        public static Role CurrentRole => ResolveRole(CurrentUser);

        public static bool IsAdmin() => CurrentRole >= Role.Admin;
        public static bool IsManagerOrAbove() => CurrentRole >= Role.Manager;
        public static bool IsSupervisorOrAbove() => CurrentRole >= Role.Supervisor;
        public static bool IsCashierOrAbove() => CurrentRole >= Role.Cashier;

        public static bool Has(Perm permission)
        {
            if (!_policy.TryGetValue(permission, out var minRole)) return false;
            return CurrentRole >= minRole;
        }

        // Single source of truth for who can do what
        private static readonly Dictionary<Perm, Role> _policy = new()
        {
            // Purchases
            { Perm.Purchases_View_All,      Role.Admin      }, // only Admin sees all outlets
            { Perm.Purchases_Edit,          Role.Supervisor },
            { Perm.PurchaseReturns_Process, Role.Supervisor },

            // Sales
            { Perm.Sales_View_All,          Role.Manager    },
            { Perm.Sales_Returns_Process,   Role.Supervisor },
            { Perm.Sales_Void,              Role.Manager    },

            // Till/Shift
            { Perm.Shift_Open,              Role.Cashier    },
            { Perm.Shift_Close,             Role.Supervisor },
            { Perm.Drawer_Kick,             Role.Cashier    },

            // Admin & Masters
            { Perm.Users_Manage,            Role.Admin      },
            { Perm.Catalog_Manage,          Role.Manager    },
            { Perm.Outlets_Manage,          Role.Manager    },
            { Perm.Reports_View_All,        Role.Manager    },
        };

        // Normalize your user shape → Role
        public static Role ResolveRole(User? user)
        {
            if (user is null) return Role.Cashier; // safe default

            return user.Role switch
            {
                UserRole.Admin => Role.Admin,
                UserRole.Manager => Role.Manager,
                UserRole.Cashier => Role.Cashier,
                UserRole.Salesman => Role.Salesman,
                _ => Role.Cashier
            };
        }

    }
}
