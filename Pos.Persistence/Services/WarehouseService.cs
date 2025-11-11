using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;            // <-- interface lives in Domain
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class WarehouseService : IWarehouseService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public WarehouseService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // -------------------- LIST --------------------
        public async Task<List<Warehouse>> SearchAsync(
            string? term,
            bool showInactive,
            int take = 1000,
            CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            term = (term ?? "").Trim();

            var q = db.Warehouses.AsNoTracking();

            if (!showInactive)
                q = q.Where(w => w.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
            {
                var t = term.ToLower();
                q = q.Where(w =>
                    (w.Name ?? "").ToLower().Contains(t) ||
                    (w.Code ?? "").ToLower().Contains(t) ||
                    (w.City ?? "").ToLower().Contains(t) ||
                    (w.Phone ?? "").ToLower().Contains(t) ||
                    (w.Note ?? "").ToLower().Contains(t));
            }

            return await q
                .OrderByDescending(w => w.IsActive)
                .ThenBy(w => w.Name)
                .Take(take)
                .ToListAsync(ct);
        }

        // -------------------- ENABLE / DISABLE --------------------
        public async Task SetActiveAsync(int warehouseId, bool active, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var w = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == warehouseId, ct)
                    ?? throw new InvalidOperationException("Warehouse not found.");

            w.IsActive = active;
            w.UpdatedAtUtc = DateTime.UtcNow;

            // enqueue + save in one transaction boundary
            await _outbox.EnqueueUpsertAsync(db, w);
            await db.SaveChangesAsync(ct);
        }

        public async Task<Warehouse> SaveWarehouseAsync(Warehouse input, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var codeTaken = await db.Warehouses
                .AsNoTracking()
                .AnyAsync(x => x.Code == input.Code && x.Id != input.Id, ct);

            if (codeTaken)
                throw new InvalidOperationException("This warehouse code already exists.");

            Warehouse entity;
            if (input.Id == 0)
            {
                entity = new Warehouse
                {
                    Code = input.Code,
                    Name = input.Name,
                    IsActive = input.IsActive,
                    City = input.City,
                    Phone = input.Phone,
                    Note = input.Note,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Warehouses.Add(entity);
            }
            else
            {
                entity = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == input.Id, ct)
                      ?? throw new InvalidOperationException("Warehouse not found.");

                entity.Code = input.Code;
                entity.Name = input.Name;
                entity.IsActive = input.IsActive;
                entity.City = input.City;
                entity.Phone = input.Phone;
                entity.Note = input.Note;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _outbox.EnqueueUpsertAsync(db, entity);
            await db.SaveChangesAsync(ct);
            return entity;
        }

        public async Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        }
    }
}
