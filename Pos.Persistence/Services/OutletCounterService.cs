using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models;              // moved DTOs
using Pos.Domain.Services;            // interface
using Pos.Domain.Utils;               // GuidUtility
using Pos.Persistence;
using Pos.Persistence.Sync;           // IOutboxWriter

namespace Pos.Persistence.Services
{
    public sealed class OutletCounterService : IOutletCounterService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public OutletCounterService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // ─────────────────────────  Queries  ─────────────────────────
        public async Task<List<OutletRow>> GetOutletsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets.AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new OutletRow
                {
                    Id = o.Id,
                    Code = o.Code,
                    Name = o.Name,
                    Address = o.Address,
                    IsActive = o.IsActive
                })
                .ToListAsync(ct);
        }

        public async Task<List<CounterRow>> GetCountersAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var counters = await db.Counters.AsNoTracking()
                .Where(c => c.OutletId == outletId)
                .OrderBy(c => c.Name)
                .Select(c => new CounterRow
                {
                    Id = c.Id,
                    OutletId = c.OutletId,
                    Name = c.Name,
                    IsActive = c.IsActive,
                    AssignedTo = db.CounterBindings
                                   .Where(b => b.CounterId == c.Id)
                                   .Select(b => b.MachineName)
                                   .FirstOrDefault()
                })
                .ToListAsync(ct);

            return counters;
        }

        // ─────────────────────────  Outlet Commands  ─────────────────────────
        public async Task<int> AddOrUpdateOutletAsync(Outlet outlet, string? user = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (outlet.Id == 0)
            {
                outlet.CreatedAtUtc = DateTime.UtcNow;
                outlet.CreatedBy = user;
                await db.Outlets.AddAsync(outlet, ct);
            }
            else
            {
                outlet.UpdatedAtUtc = DateTime.UtcNow;
                outlet.UpdatedBy = user;
                db.Outlets.Update(outlet);
            }

            // First save to get identity/rowversion for payloads
            await db.SaveChangesAsync(ct);

            // Enqueue outbox BEFORE final save (house rule)
            await _outbox.EnqueueUpsertAsync(db, outlet, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return outlet.Id;
        }

        public async Task DeleteOutletAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var outlet = await db.Outlets
                .Include(o => o.Counters)
                .FirstOrDefaultAsync(o => o.Id == outletId, ct);

            if (outlet == null)
                throw new InvalidOperationException("Outlet not found.");

            if (outlet.Counters.Any())
                throw new InvalidOperationException("Cannot delete: outlet still has counters. Delete counters first.");

            db.Outlets.Remove(outlet);
            await db.SaveChangesAsync(ct);

            var topic = nameof(Outlet);
            var streamId = GuidUtility.FromString($"{topic}:{outlet.Id}");
            await _outbox.EnqueueDeleteAsync(db, topic, streamId, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        // ─────────────────────────  Counter Commands  ─────────────────────────
        public async Task<int> AddOrUpdateCounterAsync(Counter counter, string? user = null, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            if (counter.Id == 0)
            {
                counter.CreatedAtUtc = DateTime.UtcNow;
                counter.CreatedBy = user;
                await db.Counters.AddAsync(counter, ct);
            }
            else
            {
                counter.UpdatedAtUtc = DateTime.UtcNow;
                counter.UpdatedBy = user;
                db.Counters.Update(counter);
            }

            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, counter, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return counter.Id;
        }

        public async Task DeleteCounterAsync(int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var counter = await db.Counters.FindAsync(new object?[] { counterId }, ct);
            if (counter == null)
                throw new InvalidOperationException("Counter not found.");

            db.Counters.Remove(counter);
            await db.SaveChangesAsync(ct);

            var topic = nameof(Counter);
            var streamId = GuidUtility.FromString($"{topic}:{counter.Id}");
            await _outbox.EnqueueDeleteAsync(db, topic, streamId, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        // ─────────────────────────  Binding (Assign / Unassign)  ─────────────────────────
        public async Task AssignThisPcAsync(int outletId, int counterId, string machine, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var exists = await db.Counters.AnyAsync(c => c.Id == counterId && c.OutletId == outletId, ct);
            if (!exists)
                throw new InvalidOperationException("Counter not found for the selected outlet.");

            // ensure one binding per machine (free previous)
            var existingForMachine = await db.CounterBindings
                .Where(b => b.MachineName == machine)
                .ToListAsync(ct);

            if (existingForMachine.Count > 0)
            {
                db.CounterBindings.RemoveRange(existingForMachine);
                await db.SaveChangesAsync(ct);

                foreach (var b in existingForMachine)
                {
                    var topicOld = nameof(CounterBinding);
                    var sidOld = GuidUtility.FromString($"{topicOld}:{b.CounterId}:{b.MachineName}");
                    await _outbox.EnqueueDeleteAsync(db, topicOld, sidOld, ct);
                }
                await db.SaveChangesAsync(ct);
            }

            var binding = new CounterBinding
            {
                CounterId = counterId,
                MachineName = machine,
                CreatedAtUtc = DateTime.UtcNow
            };
            await db.CounterBindings.AddAsync(binding, ct);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, binding, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        public async Task UnassignThisPcAsync(string machine, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var bindings = await db.CounterBindings.Where(b => b.MachineName == machine).ToListAsync(ct);
            if (bindings.Count == 0)
                return;

            db.CounterBindings.RemoveRange(bindings);
            await db.SaveChangesAsync(ct);

            foreach (var b in bindings)
            {
                var topic = nameof(CounterBinding);
                var streamId = GuidUtility.FromString($"{topic}:{b.CounterId}:{b.MachineName}");
                await _outbox.EnqueueDeleteAsync(db, topic, streamId, ct);
            }
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        // ─────────────────────────  Lookups  ─────────────────────────
        public async Task<Outlet?> GetOutletAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == outletId, ct);
        }

        public async Task<Counter?> GetCounterAsync(int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Counters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == counterId, ct);
        }

        // ─────────────────────────  Uniqueness checks  ─────────────────────────
        public async Task<bool> IsOutletCodeTakenAsync(string code, int? excludingId = null, CancellationToken ct = default)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets.AnyAsync(o =>
                o.Code.ToLower() == code && (excludingId == null || o.Id != excludingId.Value), ct);
        }

        public async Task<bool> IsCounterNameTakenAsync(int outletId, string name, int? excludingId = null, CancellationToken ct = default)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Counters.AnyAsync(c =>
                c.OutletId == outletId &&
                c.Name.ToLower() == name &&
                (excludingId == null || c.Id != excludingId.Value), ct);
        }

        // ─────────────────────────  Upsert-after-dialog convenience  ─────────────────────────
        public async Task UpsertOutletByIdAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var ent = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == outletId, ct);
            if (ent == null) return;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, ent, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task UpsertCounterByIdAsync(int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var ent = await db.Counters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == counterId, ct);
            if (ent == null) return;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, ent, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ─────────────────────────  USER–OUTLET ASSIGNMENTS  ─────────────────────────
        public async Task<List<UserOutlet>> GetUserOutletsAsync(int userId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.UserOutlets
                .Include(uo => uo.Outlet)
                .Where(uo => uo.UserId == userId)
                .AsNoTracking()
                .OrderBy(uo => uo.Outlet.Name)
                .ToListAsync(ct);
        }

        public async Task<UserOutlet?> GetUserOutletAsync(int userId, int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.UserOutlets.AsNoTracking()
                .FirstOrDefaultAsync(uo => uo.UserId == userId && uo.OutletId == outletId, ct);
        }

        public async Task AssignOutletAsync(int userId, int outletId, UserRole role, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var exists = await db.UserOutlets.AnyAsync(uo => uo.UserId == userId && uo.OutletId == outletId, ct);
            if (exists)
                throw new InvalidOperationException("User is already assigned to the selected outlet.");

            var entity = new UserOutlet
            {
                UserId = userId,
                OutletId = outletId,
                Role = role
            };

            await db.UserOutlets.AddAsync(entity, ct);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        public async Task UpdateUserOutletRoleAsync(int userId, int outletId, UserRole newRole, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.UserOutlets.FirstOrDefaultAsync(uo => uo.UserId == userId && uo.OutletId == outletId, ct)
                         ?? throw new InvalidOperationException("Assignment not found.");

            entity.Role = newRole;
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }

        public async Task RemoveUserOutletAsync(int userId, int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.UserOutlets.FirstOrDefaultAsync(uo => uo.UserId == userId && uo.OutletId == outletId, ct)
                         ?? throw new InvalidOperationException("Assignment not found.");

            db.UserOutlets.Remove(entity);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueDeleteAsync(
                db,
                nameof(UserOutlet),
                GuidUtility.FromString($"useroutlet:{userId}:{outletId}"),
                ct
            );

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
