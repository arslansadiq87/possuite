using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class LookupService : ILookupService
    {
        private readonly PosClientDbContext _db;
        public LookupService(PosClientDbContext db) => _db = db;

        public async Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken ct = default)
            => await _db.Warehouses.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);

        public async Task<IReadOnlyList<Outlet>> GetOutletsAsync(CancellationToken ct = default)
            => await _db.Outlets.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);

        public async Task<IReadOnlyList<int>> GetUserOutletIdsAsync(int userId, CancellationToken ct = default)
            => await _db.Set<UserOutlet>().AsNoTracking()
                     .Where(uo => uo.UserId == userId)
                     .Select(uo => uo.OutletId)
                     .ToListAsync(ct);

        // ✅ NEW method required by ILookupService
        public async Task<IReadOnlyList<Account>> GetAccountsAsync(int? outletId, CancellationToken ct = default)
        {
            var q = _db.Accounts.AsNoTracking()
                .Where(a => a.AllowPosting && a.IsActive);   // only selectable/postable accounts

            if (outletId is null)
            {
                // When no outlet context, show only global posting accounts
                q = q.Where(a => a.OutletId == null);
            }
            else
            {
                // In an outlet context, show BOTH outlet-specific and global (null) posting accounts
                q = q.Where(a => a.OutletId == outletId || a.OutletId == null);
            }

            return await q
                .OrderBy(a => a.Code)
                .ThenBy(a => a.Name)
                .ToListAsync(ct);
        }

    }
}
