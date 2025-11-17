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
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public LookupService(IDbContextFactory<PosClientDbContext> dbf) { _dbf = dbf; }


        public async Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Warehouses
                .AsNoTracking()
                .OrderBy(w => w.Name)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Outlet>> GetOutletsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<int>> GetUserOutletIdsAsync(int userId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Set<UserOutlet>()
                .AsNoTracking()
                .Where(uo => uo.UserId == userId)
                .Select(uo => uo.OutletId)
                .ToListAsync(ct);
        }


        // ✅ NEW method required by ILookupService
        public async Task<IReadOnlyList<Account>> GetAccountsAsync(int? outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q = db.Accounts.AsNoTracking()
                .Where(a => a.AllowPosting && a.IsActive && !a.IsHeader);

            if (outletId is null)
            {
                // No outlet context → only global accounts
                q = q.Where(a => a.OutletId == null);
            }
            else
            {
                // In outlet context → include outlet-specific *and* global accounts
                q = q.Where(a => a.OutletId == outletId || a.OutletId == null);
            }

            return await q
                .OrderBy(a => a.Code)     // safe: Code can be null; EF will order nulls first
                .ThenBy(a => a.Name)
                .ToListAsync(ct);
        }


    }
}
