using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class OutletReadService : IOutletReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public OutletReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<string> GetOutletNameAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var name = await db.Outlets.AsNoTracking()
                .Where(o => o.Id == outletId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(name) ? "(Unknown Outlet)" : name!;
        }

        public async Task<string> GetCounterNameAsync(int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var name = await db.Counters.AsNoTracking()
                .Where(c => c.Id == counterId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(name) ? "(Counter)" : name!;
        }
    }
}
