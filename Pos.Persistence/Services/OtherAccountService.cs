using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence.Sync;
using Pos.Domain.Utils;

namespace Pos.Persistence.Services
{
    public class OtherAccountService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public OtherAccountService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        // Fetch all for grid
        public async Task<List<OtherAccount>> GetAllAsync()
        {
            await using var db = _dbf.CreateDbContext();
            return await db.OtherAccounts.AsNoTracking()
                .OrderBy(x => x.Name)
                .ToListAsync();
        }

        // Delete account safely
        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = _dbf.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            var entity = await db.OtherAccounts.FirstOrDefaultAsync(x => x.Id == id);
            if (entity == null) return false;

            // If linked to GL
            if (entity.AccountId.HasValue)
            {
                var usedInGl = await db.JournalLines.AnyAsync(l => l.AccountId == entity.AccountId.Value);
                if (usedInGl) return false;
            }

            Account? linked = null;
            if (entity.AccountId.HasValue)
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == entity.AccountId.Value);

            if (linked?.IsSystem == true) return false;

            if (linked != null) db.Accounts.Remove(linked);
            db.OtherAccounts.Remove(entity);
            await db.SaveChangesAsync();

            // Outbox delete events
            if (linked != null)
            {
                var topic = nameof(Account);
                var publicId = GuidUtility.FromString($"{topic}:{linked.Id}");
                await _outbox.EnqueueDeleteAsync(db, topic, publicId, default);
            }

            {
                var topic = nameof(OtherAccount);
                var publicId = GuidUtility.FromString($"{topic}:{entity.Id}");
                await _outbox.EnqueueDeleteAsync(db, topic, publicId, default);
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        // Fetch single record by id
        public async Task<OtherAccount?> GetAsync(int id)
        {
            await using var db = _dbf.CreateDbContext();
            return await db.OtherAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        }

        // Generate next code (OTH-001, etc.)
        public async Task<string> GenerateNextOtherCodeAsync()
        {
            await using var db = _dbf.CreateDbContext();
            var codes = await db.OtherAccounts.AsNoTracking().Select(s => s.Code).ToListAsync();
            int max = 0;
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var last = c!.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"OTH-{(max + 1):D3}";
        }

        // Create or update an OtherAccount (used by dialog)
        public async Task UpsertAsync(OtherAccountDto dto)
        {
            await using var db = _dbf.CreateDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            OtherAccount row;
            if (dto.Id == null)
            {
                row = new OtherAccount();
                db.OtherAccounts.Add(row);
            }
            else
            {
                row = await db.OtherAccounts.FirstAsync(x => x.Id == dto.Id.Value);
            }

            // Map fields
            row.Code = string.IsNullOrWhiteSpace(dto.Code)
                ? await GenerateNextOtherCodeAsync()
                : dto.Code.Trim();
            row.Name = dto.Name.Trim();
            row.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            row.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            row.IsActive = true;

            // --- ensure linked CoA account under header "64" ---
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "64");
            if (parent == null)
                throw new InvalidOperationException("Chart of Accounts header 64 (Others) not found.");

            Account? linked = null;
            if (row.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == row.AccountId.Value);
                if (linked == null) row.AccountId = null;
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, false);
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
                db.Accounts.Add(linked);
                await db.SaveChangesAsync();
                row.AccountId = linked.Id;
            }
            else
            {
                linked.Name = row.Name;
                if (linked.ParentId != parent.Id)
                    linked.ParentId = parent.Id;
                linked.NormalSide = NormalSide.Debit;
            }

            await db.SaveChangesAsync();

            // --- sync ---
            if (linked != null)
                await _outbox.EnqueueUpsertAsync(db, linked);
            await _outbox.EnqueueUpsertAsync(db, row);

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, bool forHeader)
        {
            var sibs = await db.Accounts.AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync();
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

        // DTO for dialog input
        public class OtherAccountDto
        {
            public int? Id { get; set; }
            public string? Code { get; set; }
            public string Name { get; set; } = "";
            public string? Phone { get; set; }
            public string? Email { get; set; }
        }

    }
}
