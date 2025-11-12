using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Domain.Services.Hr;

namespace Pos.Persistence.Services.Hr
{
    /// <summary>
    /// EF Core-backed staff read service using IDbContextFactory.
    /// </summary>
    public sealed class StaffReadService : IStaffReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public StaffReadService(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        public async Task<List<Staff>> GetSalesmenAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive && s.ActsAsSalesman)
                .OrderBy(s => s.FullName)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        public async Task<List<Staff>> GetAllActiveAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.FullName)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
    }
}
