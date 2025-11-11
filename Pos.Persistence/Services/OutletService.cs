// Pos.Persistence/Services/OutletService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;          // IOutletService, ICoaService (interface in Domain)
using Pos.Persistence;
using Pos.Persistence.Sync;         // IOutboxWriter

namespace Pos.Persistence.Services
{
    public sealed class OutletService : IOutletService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ICoaService _coa;
        private readonly IOutboxWriter _outbox;

        public OutletService(
            IDbContextFactory<PosClientDbContext> dbf,
            ICoaService coa,
            IOutboxWriter outbox)
        {
            _dbf = dbf;
            _coa = coa;
            _outbox = outbox;
        }

        public async Task<int> CreateAsync(Outlet outlet, CancellationToken ct = default)
        {
            if (outlet is null) throw new InvalidOperationException("Outlet payload is required.");
            if (string.IsNullOrWhiteSpace(outlet.Name)) throw new InvalidOperationException("Outlet name is required.");
            if (string.IsNullOrWhiteSpace(outlet.Code)) throw new InvalidOperationException("Outlet code is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            db.Outlets.Add(outlet);
            await db.SaveChangesAsync(ct);

            // Ensure COA accounts (idempotent)
            await _coa.EnsureOutletCashAccountAsync(outlet.Id, ct);
            await _coa.EnsureOutletTillAccountAsync(outlet.Id, ct);

            // Outbox before final save/commit
            await _outbox.EnqueueUpsertAsync(db, outlet, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return outlet.Id;
        }

        public async Task<List<Outlet>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Outlets
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .ToListAsync(ct);
        }

        public async Task UpdateAsync(Outlet outlet, CancellationToken ct = default)
        {
            if (outlet is null) throw new InvalidOperationException("Outlet payload is required.");
            if (outlet.Id <= 0) throw new InvalidOperationException("Valid outlet id is required.");
            if (string.IsNullOrWhiteSpace(outlet.Name)) throw new InvalidOperationException("Outlet name is required.");
            if (string.IsNullOrWhiteSpace(outlet.Code)) throw new InvalidOperationException("Outlet code is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.Outlets.FirstOrDefaultAsync(x => x.Id == outlet.Id, ct)
                         ?? throw new InvalidOperationException("Outlet not found.");

            entity.Code = outlet.Code.Trim();
            entity.Name = outlet.Name.Trim();
            entity.Address = outlet.Address;
            entity.IsActive = outlet.IsActive;

            await db.SaveChangesAsync(ct);

            // Re-ensure (idempotent, handles code/name changes)
            await _coa.EnsureOutletCashAccountAsync(entity.Id, ct);
            await _coa.EnsureOutletTillAccountAsync(entity.Id, ct);

            // Outbox before final save/commit
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
