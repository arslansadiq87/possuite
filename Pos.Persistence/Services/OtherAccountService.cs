using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models;
using Pos.Domain.Services;
using Pos.Domain.Utils;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class OtherAccountService : IOtherAccountService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public OtherAccountService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // Fetch all for grid
        public async Task<List<OtherAccount>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.OtherAccounts.AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync(ct);
        }

        // Fetch single record by id
        public async Task<OtherAccount?> GetAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.OtherAccounts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);
        }

        // Public generator (new context)
        public async Task<string> GenerateNextOtherCodeAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GenerateNextOtherCodeInternalAsync(db, ct);
        }

        // Create or update
        public async Task UpsertAsync(OtherAccountUpsertDto dto, CancellationToken ct = default)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            OtherAccount row;
            if (dto.Id is null)
            {
                row = new OtherAccount();
                await db.OtherAccounts.AddAsync(row, ct);
            }
            else
            {
                row = await db.OtherAccounts.FirstAsync(x => x.Id == dto.Id.Value, ct);
            }

            // Map fields
            row.Code = string.IsNullOrWhiteSpace(dto.Code)
                ? await GenerateNextOtherCodeInternalAsync(db, ct)
                : dto.Code!.Trim();
            row.Name = dto.Name.Trim();
            row.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone!.Trim();
            row.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email!.Trim();
            row.IsActive = true;

            // Ensure linked CoA account under header "64"
            var parent = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == "64", ct);
            if (parent == null)
                throw new InvalidOperationException("Chart of Accounts header 64 (Others) not found.");

            Account? linked = null;
            if (row.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == row.AccountId.Value, ct);
                if (linked == null) row.AccountId = null;
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, forHeader: false, ct);
                linked = new Account
                {
                    Code = code,
                    Name = row.Name,
                    Type = AccountType.Parties,
                    NormalSide = NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                await db.Accounts.AddAsync(linked, ct);
                await db.SaveChangesAsync(ct); // get linked.Id
                row.AccountId = linked.Id;
            }
            else
            {
                linked.Name = row.Name;
                if (linked.ParentId != parent.Id) linked.ParentId = parent.Id;
                linked.NormalSide = NormalSide.Debit;
            }

            await db.SaveChangesAsync(ct);

            // enqueue outbox before final save
            if (linked != null)
                await _outbox.EnqueueUpsertAsync(db, linked, ct);
            await _outbox.EnqueueUpsertAsync(db, row, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Delete account safely
        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var entity = await db.OtherAccounts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity == null) return false;

            // If linked to GL
            if (entity.AccountId.HasValue)
            {
                var usedInGl = await db.JournalLines
                    .AnyAsync(l => l.AccountId == entity.AccountId.Value, ct);
                if (usedInGl) return false;
            }

            Account? linked = null;
            if (entity.AccountId.HasValue)
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == entity.AccountId.Value, ct);

            if (linked?.IsSystem == true) return false;

            if (linked != null) db.Accounts.Remove(linked);
            db.OtherAccounts.Remove(entity);
            await db.SaveChangesAsync(ct);

            // Outbox delete events
            if (linked != null)
            {
                var topic = nameof(Account);
                var publicId = GuidUtility.FromString($"{topic}:{linked.Id}");
                await _outbox.EnqueueDeleteAsync(db, topic, publicId, ct);
            }

            {
                var topic = nameof(OtherAccount);
                var publicId = GuidUtility.FromString($"{topic}:{entity.Id}");
                await _outbox.EnqueueDeleteAsync(db, topic, publicId, ct);
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        // ─────────────────────────  helpers  ─────────────────────────

        private static async Task<string> GenerateNextOtherCodeInternalAsync(PosClientDbContext db, CancellationToken ct)
        {
            var codes = await db.OtherAccounts.AsNoTracking()
                .Select(s => s.Code)
                .ToListAsync(ct);

            int max = 0;
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var last = c!.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"OTH-{(max + 1):D3}";
        }

        private static async Task<string> GenerateNextChildCodeAsync(
            PosClientDbContext db, Account parent, bool forHeader, CancellationToken ct)
        {
            var sibs = await db.Accounts.AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync(ct);

            int max = 0;
            foreach (var code in sibs)
            {
                var last = code?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }
    }
}
