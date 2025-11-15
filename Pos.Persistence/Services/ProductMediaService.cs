using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    public sealed class ProductMediaService : IProductMediaService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public ProductMediaService(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        public string? GetPrimaryThumbPath(int productId)
        {
            using var db = _dbf.CreateDbContext();
            return db.ProductImages.AsNoTracking()
                .Where(x => x.ProductId == productId && x.IsPrimary)
                .Select(x => x.LocalThumbPath)
                .FirstOrDefault();
        }

        public async Task<string?> GetPrimaryThumbPathAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.ProductImages.AsNoTracking()
                .Where(x => x.ProductId == productId && x.IsPrimary)
                .Select(x => x.LocalThumbPath)
                .FirstOrDefaultAsync(ct);
        }
    }
}
