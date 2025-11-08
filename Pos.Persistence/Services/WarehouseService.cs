using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pos.Persistence.Services
{
    public class WarehouseService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public WarehouseService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // -------------------- LIST --------------------
        public async Task<List<Warehouse>> SearchAsync(string? term, bool showInactive, int take = 1000)
        {
            await using var db = _dbf.CreateDbContext();
            term = (term ?? "").Trim().ToLower();

            var q = db.Warehouses.AsNoTracking();

            if (!showInactive)
                q = q.Where(w => w.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
                q = q.Where(w =>
                    (w.Name ?? "").ToLower().Contains(term) ||
                    (w.Code ?? "").ToLower().Contains(term) ||
                    (w.City ?? "").ToLower().Contains(term) ||
                    (w.Phone ?? "").ToLower().Contains(term) ||
                    (w.Note ?? "").ToLower().Contains(term));

            return await q
                .OrderByDescending(w => w.IsActive)
                .ThenBy(w => w.Name)
                .Take(take)
                .ToListAsync();
        }

        // -------------------- ENABLE / DISABLE --------------------
        public async Task SetActiveAsync(int warehouseId, bool active)
        {
            await using var db = _dbf.CreateDbContext();
            var w = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == warehouseId);
            if (w == null) throw new InvalidOperationException("Warehouse not found.");

            w.IsActive = active;
            w.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await _outbox.EnqueueUpsertAsync(db, w);
            await db.SaveChangesAsync();
        }

        public async Task<Warehouse> SaveWarehouseAsync(Warehouse input)
        {
            await using var db = _dbf.CreateDbContext();

            // Uniqueness check
            bool codeTaken = await db.Warehouses
                .AsNoTracking()
                .AnyAsync(x => x.Code == input.Code && x.Id != input.Id);
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
                entity = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == input.Id)
                    ?? throw new InvalidOperationException("Warehouse not found.");

                entity.Code = input.Code;
                entity.Name = input.Name;
                entity.IsActive = input.IsActive;
                entity.City = input.City;
                entity.Phone = input.Phone;
                entity.Note = input.Note;
                entity.UpdatedAtUtc = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            await _outbox.EnqueueUpsertAsync(db, entity);
            await db.SaveChangesAsync();
            return entity;
        }

        public async Task<Warehouse?> GetWarehouseAsync(int id)
        {
            await using var db = _dbf.CreateDbContext();
            return await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        }

    }
}
