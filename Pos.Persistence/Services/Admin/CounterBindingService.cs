using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.DTO.Admin;
using Pos.Domain.Services.Admin;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services.Admin
{
    public sealed class CounterBindingService : ICounterBindingService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public CounterBindingService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        public async Task<CounterBindingDto?> GetCurrentBindingAsync(string machineId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(machineId)) return null;

            await using var db = await _dbf.CreateDbContextAsync(ct);

            var row = await db.CounterBindings
                .AsNoTracking()
                .Include(b => b.Outlet)
                .Include(b => b.Counter)
                .Where(b => b.MachineId == machineId && b.IsActive)
                .FirstOrDefaultAsync(ct);

            if (row is null) return null;

            return new CounterBindingDto
            {
                Id = row.Id,
                MachineId = row.MachineId,
                MachineName = row.MachineName ?? "",
                OutletId = row.OutletId,
                OutletName = row.Outlet?.Name ?? "",
                CounterId = row.CounterId,
                CounterName = row.Counter?.Name ?? "",
                IsActive = row.IsActive,
                LastSeenUtc = row.LastSeenUtc
            };
        }

        public async Task<CounterBindingDto> AssignAsync(
            string machineId,
            string machineName,
            int outletId,
            int counterId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                throw new InvalidOperationException("MachineId is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Validate outlet-counter consistency
            var counter = await db.Counters
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == counterId, ct);
            if (counter is null)
                throw new InvalidOperationException("Counter not found.");
            if (counter.OutletId != outletId)
                throw new InvalidOperationException("Counter does not belong to the selected outlet.");

            // Enforce 1:1 both ways (filter by IsActive)
            var existingForCounter = await db.CounterBindings
                .Where(b => b.CounterId == counterId && b.IsActive)
                .FirstOrDefaultAsync(ct);
            if (existingForCounter is not null && existingForCounter.MachineId != machineId)
                throw new InvalidOperationException("This counter is already assigned to another PC.");

            // Upsert the machine binding row
            var existingForMachine = await db.CounterBindings
                .Where(b => b.MachineId == machineId)
                .FirstOrDefaultAsync(ct);

            CounterBinding binding;
            if (existingForMachine is not null)
            {
                binding = existingForMachine;
                binding.OutletId = outletId;
                binding.CounterId = counterId;
                binding.MachineName = machineName;
                binding.IsActive = true;
                binding.LastSeenUtc = DateTime.UtcNow;
            }
            else
            {
                binding = new CounterBinding
                {
                    MachineId = machineId,
                    MachineName = machineName,
                    OutletId = outletId,
                    CounterId = counterId,
                    IsActive = true,
                    LastSeenUtc = DateTime.UtcNow
                };
                await db.CounterBindings.AddAsync(binding, ct);
            }

            await db.SaveChangesAsync(ct);

            // Enqueue sync (Upsert)
            await _outbox.EnqueueUpsertAsync(
                db,
                "counter_bindings",
                binding.Id,
                new
                {
                    binding.Id,
                    binding.MachineId,
                    binding.MachineName,
                    binding.OutletId,
                    binding.CounterId,
                    binding.IsActive,
                    binding.LastSeenUtc
                },
                ct);

            // Return DTO (hydrate names)
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(o => o.Id == outletId, ct);
            var counterName = (await db.Counters.AsNoTracking().FirstOrDefaultAsync(c => c.Id == counterId, ct))?.Name ?? "";

            return new CounterBindingDto
            {
                Id = binding.Id,
                MachineId = binding.MachineId,
                MachineName = binding.MachineName ?? "",
                OutletId = binding.OutletId,
                OutletName = outlet?.Name ?? "",
                CounterId = binding.CounterId,
                CounterName = counterName,
                IsActive = binding.IsActive,
                LastSeenUtc = binding.LastSeenUtc
            };
        }

        public async Task UnassignAsync(string machineId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(machineId)) return;

            await using var db = await _dbf.CreateDbContextAsync(ct);

            var row = await db.CounterBindings
                .Where(b => b.MachineId == machineId)
                .FirstOrDefaultAsync(ct);
            if (row is null) return;

            db.CounterBindings.Remove(row);
            await db.SaveChangesAsync(ct);

            // Enqueue sync (Delete)
            await _outbox.EnqueueDeleteAsync(db, "counter_bindings", row.Id, ct);
        }
    }
}
