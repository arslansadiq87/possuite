using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models;
using Pos.Domain.Services;
using static Pos.Domain.Services.IBankAccountService;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Services
{
    public sealed class BankAccountService : IBankAccountService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ICoaService _coa;
        private readonly IOutboxWriter _outbox;   // ← ADD THIS


        public BankAccountService(IDbContextFactory<PosClientDbContext> dbf, ICoaService coa, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _coa = coa;
            _outbox = outbox;
        }

        public async Task<BankAccountViewDto?> GetByAccountIdAsync(int accountId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.BankAccounts.AsNoTracking()
                .Where(b => b.AccountId == accountId)
                .Select(b => new BankAccountViewDto(
                    b.Id, b.AccountId, b.Account.Code, b.Account.Name,
                    b.BankName, b.Branch, b.AccountNumber, b.IBAN, b.SwiftBic, b.Notes, b.IsActive))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<BankAccountViewDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.BankAccounts.AsNoTracking()
                .Where(b => b.Id == id)
                .Select(b => new BankAccountViewDto(
                    b.Id, b.AccountId, b.Account.Code, b.Account.Name,
                    b.BankName, b.Branch, b.AccountNumber, b.IBAN, b.SwiftBic, b.Notes, b.IsActive))
                .FirstOrDefaultAsync(ct);
        }

        public async Task<List<BankAccountViewDto>> SearchAsync(string? q = null, CancellationToken ct = default)
        {
            q = (q ?? "").Trim();
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var query = db.BankAccounts.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(b =>
                    EF.Functions.Like(b.Account.Code, $"%{q}%") ||
                    EF.Functions.Like(b.Account.Name, $"%{q}%") ||
                    EF.Functions.Like(b.BankName, $"%{q}%") ||
                    EF.Functions.Like(b.AccountNumber ?? "", $"%{q}%") ||
                    EF.Functions.Like(b.IBAN ?? "", $"%{q}%"));

            return await query
                .OrderBy(b => b.Account.Code)
                .Select(b => new BankAccountViewDto(
                    b.Id, b.AccountId, b.Account.Code, b.Account.Name,
                    b.BankName, b.Branch, b.AccountNumber, b.IBAN, b.SwiftBic, b.Notes, b.IsActive))
                .ToListAsync(ct);
        }

        public async Task<int> CreateAsync(int bankHeaderAccountId, BankAccountUpsertDto dto, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(dto.IBAN) && dto.IBAN!.Length < 10)
                throw new InvalidOperationException("IBAN looks too short.");

            // Phase 1: create the GL account (uses its own DbContext/transaction internally)
            int glId;
            try
            {
                var (glIdTemp, _) = await _coa.CreateAccountAsync(bankHeaderAccountId, dto.Name.Trim(), ct);
                glId = glIdTemp;
            }
            catch
            {
                // GL creation failed — nothing to clean up
                throw;
            }

            // Phase 2: create the BankAccount row with a fresh DbContext (no ambient tx)
            try
            {
                await using var db = await _dbf.CreateDbContextAsync(ct);
                var row = new BankAccount
                {
                    AccountId = glId,
                    BankName = dto.BankName.Trim(),
                    Branch = dto.Branch?.Trim(),
                    AccountNumber = dto.AccountNumber?.Trim(),
                    IBAN = dto.IBAN?.Trim(),
                    SwiftBic = dto.SwiftBic?.Trim(),
                    Notes = dto.Notes?.Trim(),
                    IsActive = dto.IsActive
                };
                db.BankAccounts.Add(row);
                await db.SaveChangesAsync(ct);
                await _outbox.EnqueueUpsertAsync(db, row, ct);
                await db.SaveChangesAsync(ct);                  // persist SyncOutbox row
                return row.Id;
            }
            catch
            {
                // Compensate: try to delete the just-created GL account so the system stays tidy
                try { await _coa.DeleteAsync(glId, ct); } catch { /* best effort */ }
                throw;
            }
        }


        public async Task UpdateAsync(BankAccountUpsertDto dto, CancellationToken ct = default)
        {
            if (dto.Id is null || dto.AccountId is null)
                throw new InvalidOperationException("Missing BankAccount Id or AccountId for update.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var row = await db.BankAccounts.FirstAsync(b => b.Id == dto.Id.Value, ct);
            var acc = await db.Accounts.FirstAsync(a => a.Id == dto.AccountId.Value, ct);

            // Update GL account name (code remains auto-generated)
            acc.Name = dto.Name.Trim();

            // Update bank metadata
            row.BankName = dto.BankName.Trim();
            row.Branch = dto.Branch?.Trim();
            row.AccountNumber = dto.AccountNumber?.Trim();
            row.IBAN = dto.IBAN?.Trim();
            row.SwiftBic = dto.SwiftBic?.Trim();
            row.Notes = dto.Notes?.Trim();
            row.IsActive = dto.IsActive;
            await _outbox.EnqueueUpsertAsync(db, row, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }


        // Persistence (optional)
        public async Task<IReadOnlyList<BankAccountPickDto>> GetAllPicksAsync(CancellationToken ct = default)
        {
            var list = await SearchAsync(null, ct); // reuse your existing SearchAsync
            return list
                .Where(b => b.IsActive)
                .Select(b => new BankAccountPickDto(b.AccountId, b.Id, $"{b.Code} — {b.Name} ({b.BankName})"))
                .ToList();
        }
    }
}
