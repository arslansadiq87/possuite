using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain;
using Pos.Domain.DTO.Security;
using Pos.Domain.Services.Security;

namespace Pos.Persistence.Services.Security
{
    public sealed class AuthorizationService : IAuthorizationService
    {
        private static readonly IReadOnlyDictionary<Perm, UserRole> _policy = new Dictionary<Perm, UserRole>
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

        private static UserRole EffectiveRole(UserInfoDto? user, int? outletId)
        {
            if (user is null) return UserRole.Cashier; // safest default

            if (user.IsGlobalAdmin)
                return UserRole.Admin;

            if (outletId is int oid)
            {
                var or = user.OutletRoles.FirstOrDefault(r => r.OutletId == oid);
                if (or is not null)
                    return (UserRole)or.Role;
            }

            // fall back to legacy/global role
            return user.Role;
        }

        public Task<bool> HasAsync(UserInfoDto? user, Perm permission, int? outletId = null, CancellationToken ct = default)
        {
            var role = EffectiveRole(user, outletId);
            if (!_policy.TryGetValue(permission, out var minRole)) return Task.FromResult(false);
            return Task.FromResult(role >= minRole);
        }

        public Task<bool> IsAdminAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default)
            => Task.FromResult(EffectiveRole(user, outletId) >= UserRole.Admin);

        public Task<bool> IsManagerOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default)
            => Task.FromResult(EffectiveRole(user, outletId) >= UserRole.Manager);

        public Task<bool> IsSupervisorOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default)
            => Task.FromResult(EffectiveRole(user, outletId) >= UserRole.Supervisor);

        public Task<bool> IsCashierOrAboveAsync(UserInfoDto? user, int? outletId = null, CancellationToken ct = default)
            => Task.FromResult(EffectiveRole(user, outletId) >= UserRole.Cashier);
    }
}
