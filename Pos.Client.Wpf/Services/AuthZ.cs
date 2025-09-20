// Pos.Client.Wpf/Services/AuthZ.cs
using Pos.Domain.Entities;
using Pos.Domain;


namespace Pos.Client.Wpf.Services
{
    public static class AuthZ
    {
        // Just reuse UserRole directly
        public static User? CurrentUser => AppState.Current.CurrentUser;
        public static UserRole CurrentRole => CurrentUser?.Role ?? UserRole.Cashier;

        public static bool IsAdmin() => CurrentRole >= UserRole.Admin;
        public static bool IsManagerOrAbove() => CurrentRole >= UserRole.Manager;
        public static bool IsSupervisorOrAbove() => CurrentRole >= UserRole.Supervisor;
        public static bool IsCashierOrAbove() => CurrentRole >= UserRole.Cashier;

        public static bool Has(Perm permission)
        {
            if (!_policy.TryGetValue(permission, out var minRole)) return false;
            return CurrentRole >= minRole;
        }

        // Permissions stay the same, but map against UserRole
        public enum Perm
        {
            Purchases_View_All,
            Purchases_Edit,
            PurchaseReturns_Process,
            Sales_View_All,
            Sales_Returns_Process,
            Sales_Void,
            Shift_Open,
            Shift_Close,
            Drawer_Kick,
            Users_Manage,
            Catalog_Manage,
            Outlets_Manage,
            Reports_View_All
        }

        private static readonly Dictionary<Perm, UserRole> _policy = new()
        {
            { Perm.Purchases_View_All,      UserRole.Admin      },
            { Perm.Purchases_Edit,          UserRole.Supervisor },
            { Perm.PurchaseReturns_Process, UserRole.Supervisor },
            { Perm.Sales_View_All,          UserRole.Manager    },
            { Perm.Sales_Returns_Process,   UserRole.Supervisor },
            { Perm.Sales_Void,              UserRole.Manager    },
            { Perm.Shift_Open,              UserRole.Cashier    },
            { Perm.Shift_Close,             UserRole.Supervisor },
            { Perm.Drawer_Kick,             UserRole.Cashier    },
            { Perm.Users_Manage,            UserRole.Admin      },
            { Perm.Catalog_Manage,          UserRole.Manager    },
            { Perm.Outlets_Manage,          UserRole.Manager    },
            { Perm.Reports_View_All,        UserRole.Manager    },
        };
    }
}
