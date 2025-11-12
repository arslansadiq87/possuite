using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;       // Account, AccountType, NormalSide
using Pos.Domain.Hr;             // Staff
using Pos.Domain.Services;       // IStaffService
using Pos.Domain.Utils;          // GuidUtility
using Pos.Persistence;
using Pos.Persistence.Sync;      // IOutboxWriter

namespace Pos.Persistence.Services
{
    /// <summary>
    /// EF-based implementation of IStaffService.
    /// House rules: no EF in UI, all async, pass ct everywhere, clear messages, outbox before final SaveChanges.
    /// </summary>
    public sealed class StaffService : IStaffService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public StaffService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // ───────────────────────── Queries ─────────────────────────
        public async Task<List<Staff>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Staff.AsNoTracking()
                .OrderBy(s => s.FullName)
                .ToListAsync(ct);
        }

        public async Task<List<Staff>> GetAllActiveStaffAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive)       // ✅ Only active staff
                .OrderBy(s => s.FullName)
                .ToListAsync(ct);
        }


        public async Task<Staff?> GetAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        public async Task<bool> IsNameTakenAsync(string name, int? excludingId = null, CancellationToken ct = default)
        {
            var norm = (name ?? string.Empty).Trim().ToLowerInvariant();
            await using var db = await _dbf.CreateDbContextAsync(ct);

            return await db.Staff.AnyAsync(s =>
                s.FullName.ToLower() == norm &&
                (!excludingId.HasValue || s.Id != excludingId.Value), ct);
        }

        public async Task<string> GenerateNextStaffCodeAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GenerateNextStaffCodeCoreAsync(db, ct);
        }

        // ───────────────────────── Commands ─────────────────────────
        public async Task<int> CreateOrUpdateAsync(Staff input, CancellationToken ct = default)
        {
            if (input is null) throw new InvalidOperationException("Input is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var isCreate = input.Id == 0;
            Staff entity;

            if (isCreate)
            {
                entity = new Staff();
                await db.Staff.AddAsync(entity, ct);
            }
            else
            {
                entity = await db.Staff.FirstOrDefaultAsync(x => x.Id == input.Id, ct)
                         ?? throw new InvalidOperationException("Staff not found.");
            }

            // Map fields
            entity.Code = string.IsNullOrWhiteSpace(input.Code)
                ? await GenerateNextStaffCodeCoreAsync(db, ct)   // use same DbContext
                : input.Code!.Trim();

            entity.FullName = (input.FullName ?? string.Empty).Trim();
            entity.JoinedOnUtc = input.JoinedOnUtc;      // assume caller provided UTC
            entity.BasicSalary = input.BasicSalary;
            entity.ActsAsSalesman = input.ActsAsSalesman;

            // Ensure CoA account under "63" (Staff)
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "63", ct);
            if (parent == null)
                throw new InvalidOperationException("Chart of Accounts header 63 (Staff) not found. Seed CoA first.");

            bool accountCreated = false;
            bool accountUpdated = false;

            Account? linked = null;
            if (entity.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == entity.AccountId.Value, ct);
                if (linked == null) entity.AccountId = null; // dangling → treat as missing
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, ct);
                linked = new Account
                {
                    Code = code,
                    Name = entity.FullName,
                    Type = AccountType.Parties,
                    NormalSide = NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                await db.Accounts.AddAsync(linked, ct);
                await db.SaveChangesAsync(ct);               // need Id for FK
                entity.AccountId = linked.Id;
                accountCreated = true;
            }
            else
            {
                if (linked.Name != entity.FullName) { linked.Name = entity.FullName; accountUpdated = true; }
                if (linked.ParentId != parent.Id) { linked.ParentId = parent.Id; accountUpdated = true; }
                if (linked.NormalSide != NormalSide.Debit) { linked.NormalSide = NormalSide.Debit; accountUpdated = true; }
            }

            // Persist entity + any account edits
            await db.SaveChangesAsync(ct);

            // Outbox upserts BEFORE final SaveChanges
            await _outbox.EnqueueUpsertAsync(db, entity, ct);
            if (accountCreated || accountUpdated)
                await _outbox.EnqueueUpsertAsync(db, linked!, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return entity.Id;
        }

        public async Task DeleteAsync(int staffId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var ent = await db.Staff.FirstOrDefaultAsync(x => x.Id == staffId, ct)
                      ?? throw new InvalidOperationException("Staff not found.");

            db.Staff.Remove(ent);
            await db.SaveChangesAsync(ct);

            // Tombstone with stable Guid
            await _outbox.EnqueueDeleteAsync(db, nameof(Staff), GuidUtility.FromString($"staff:{ent.Id}"), ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ───────────────────────── Helpers ─────────────────────────
        private static async Task<string> GenerateNextStaffCodeCoreAsync(PosClientDbContext db, CancellationToken ct)
        {
            var codes = await db.Staff.AsNoTracking()
                .Select(s => s.Code)
                .ToListAsync(ct);

            var max = 0;
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var last = c!.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"STF-{(max + 1).ToString("D3", CultureInfo.InvariantCulture)}";
        }

        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, CancellationToken ct)
        {
            var sibs = await db.Accounts.AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync(ct);

            var max = 0;
            foreach (var code in sibs)
            {
                var last = code?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"{parent.Code}-{(max + 1).ToString("D3", CultureInfo.InvariantCulture)}";
        }
    }
}
