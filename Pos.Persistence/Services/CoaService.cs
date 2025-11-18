// Pos.Persistence/Services/CoaService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Domain.Models.Accounting;
using Pos.Domain.Services;
using Pos.Persistence;
using Pos.Persistence.Sync;
using static System.Net.Mime.MediaTypeNames;

namespace Pos.Persistence.Services
{
    internal static class CoaCode
    {
        public const string CASH_HEADER = "111";         // company-level header for Cash
        public const string CASH_CHILD = "11101";       // Cash in Hand
        public const string TILL_CHILD = "11102";       // Cash in Till
        public const string COMPANY_CODE = "COMPANY";
    }

    public sealed class CoaService : ICoaService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;

        public CoaService(IDbContextFactory<PosClientDbContext> dbf, IOutboxWriter outbox)
        {
            _dbf = dbf;
            _outbox = outbox;
        }

        private static NormalSide DefaultNormalFor(AccountType t) => t switch
        {
            AccountType.Asset => NormalSide.Debit,
            AccountType.Expense => NormalSide.Debit,
            AccountType.Liability => NormalSide.Credit,
            AccountType.Equity => NormalSide.Credit,
            AccountType.Income => NormalSide.Credit,
            AccountType.Parties => NormalSide.Debit,
            AccountType.System => NormalSide.Debit,
            _ => NormalSide.Debit
        };

        // -------- Load --------
        public async Task<List<CoaAccount>> GetAllAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Accounts.AsNoTracking()
                .OrderBy(a => a.Code)
                .Select(a => new CoaAccount(
                    a.Id, a.Code, a.Name, a.Type, a.IsHeader, a.AllowPosting,
                    a.OpeningDebit, a.OpeningCredit, a.IsOpeningLocked, a.IsSystem,
                    a.SystemKey, a.ParentId, a.OutletId))
                .ToListAsync(ct);
        }

        // -------- Create --------
        public async Task<(int id, string code)> CreateHeaderAsync(int parentId, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Account name is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var parent = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == parentId, ct);

            var code = await GenerateNextChildCodeAsync(db, parent, forHeader: true, ct);
            var acc = new Account
            {
                Code = code,
                Name = name.Trim(),
                Type = parent.Type,
                NormalSide = DefaultNormalFor(parent.Type),
                IsHeader = true,
                AllowPosting = false,
                ParentId = parent.Id,
                OutletId = parent.OutletId
            };

            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (acc.Id, acc.Code);
        }

        public async Task<(int id, string code)> CreateAccountAsync(int parentId, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Account name is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var parent = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == parentId, ct);

            var code = await GenerateNextChildCodeAsync(db, parent, forHeader: false, ct);
            var acc = new Account
            {
                Code = code,
                Name = name.Trim(),
                Type = parent.Type,
                NormalSide = DefaultNormalFor(parent.Type),
                IsHeader = false,
                AllowPosting = true,
                ParentId = parent.Id,
                OutletId = parent.OutletId
            };

            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return (acc.Id, acc.Code);
        }

        // -------- Edit / Delete --------
        public async Task EditAsync(AccountEdit edit, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == edit.Id, ct)
                      ?? throw new InvalidOperationException("Account not found.");
            if (acc.IsSystem) throw new InvalidOperationException("System accounts cannot be edited.");

            if (!string.IsNullOrWhiteSpace(edit.Code)) acc.Code = edit.Code.Trim();
            if (!string.IsNullOrWhiteSpace(edit.Name)) acc.Name = edit.Name.Trim();
            acc.IsHeader = edit.IsHeader;
            acc.AllowPosting = edit.AllowPosting && !acc.IsHeader;

            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (acc == null) return;

            if (acc.IsSystem) throw new InvalidOperationException("Cannot delete system accounts.");
            if (acc.IsHeader || await db.Accounts.AnyAsync(a => a.ParentId == acc.Id, ct))
                throw new InvalidOperationException("Cannot delete headers or parents.");
            if (await db.JournalLines.AnyAsync(l => l.AccountId == acc.Id, ct))
                throw new InvalidOperationException("Account is used in GL.");
            if (await db.Parties.AnyAsync(p => p.AccountId == acc.Id, ct))
                throw new InvalidOperationException("Account is assigned to a party.");

            db.Accounts.Remove(acc);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueDeleteAsync(db, nameof(Account), acc.PublicId, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // -------- Openings --------
        public async Task SaveOpeningsAsync(IEnumerable<OpeningChange> changes, CancellationToken ct = default)
        {
            var list = changes?.ToList() ?? new List<OpeningChange>();
            if (list.Count == 0) return;

            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var ids = list.Select(x => x.AccountId).ToArray();
            var rows = await db.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync(ct);

            foreach (var a in rows)
            {
                if (a.IsOpeningLocked) continue;
                if (a.SystemKey == SystemAccountKey.CashInTillOutlet)
                    throw new InvalidOperationException("Opening balance is not allowed for 'Cash in Till' accounts.");

                var change = list.First(x => x.AccountId == a.Id);
                if (change.Debit != 0m && change.Credit != 0m)
                    throw new InvalidOperationException($"Opening for {a.Code} has both Dr and Cr. Keep only one side.");

                a.OpeningDebit = change.Debit;
                a.OpeningCredit = change.Credit;
            }

            await db.SaveChangesAsync(ct);

            foreach (var a in rows)
                await _outbox.EnqueueUpsertAsync(db, a, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        public async Task LockAllOpeningsAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rows = await db.Accounts.Where(a => !a.IsOpeningLocked).ToListAsync(ct);
            foreach (var a in rows) a.IsOpeningLocked = true;

            await db.SaveChangesAsync(ct);

            foreach (var a in rows)
                await _outbox.EnqueueUpsertAsync(db, a, ct);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // -------- Cash/Till helpers --------
        public async Task<int> EnsureOutletCashAccountAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await EnsureOutletCashAccountAsync(db, outletId, ct);
        }

        public async Task<int> EnsureOutletTillAccountAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await EnsureOutletTillAccountAsync(db, outletId, ct);
        }

        public async Task<int> GetCashAccountIdAsync(int? outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GetCashAccountIdAsync(db, outletId, ct);
        }

        public async Task<int> GetTillAccountIdAsync(int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GetTillAccountIdAsync(db, outletId, ct);
        }

        // ===== Private db-aware helpers (no exposure in Domain) =====
        private async Task<int> EnsureOutletCashAccountAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);

            var header = await db.Accounts.FirstOrDefaultAsync(a => a.Code == CoaCode.CASH_HEADER && a.OutletId == null, ct);
            if (header == null)
            {
                header = new Account
                {
                    Code = CoaCode.CASH_HEADER,
                    Name = "Cash",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                db.Accounts.Add(header);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                header.Name = "Cash";
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await db.SaveChangesAsync(ct);
            }

            var code = $"{CoaCode.CASH_CHILD}-{outlet.Code}";
            var child = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outletId, ct);
            if (child != null) return child.Id;

            child = new Account
            {
                Code = code,
                Name = $"Cash in Hand – {outlet.Name}",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                ParentId = header.Id,
                OutletId = outletId,
                IsActive = true
            };
            db.Accounts.Add(child);
            await db.SaveChangesAsync(ct);
            return child.Id;
        }

        private async Task<int> EnsureOutletTillAccountAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);

            var header = await db.Accounts.FirstOrDefaultAsync(a => a.Code == CoaCode.CASH_HEADER && a.OutletId == null, ct);
            if (header == null)
            {
                header = new Account
                {
                    Code = CoaCode.CASH_HEADER,
                    Name = "Cash",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                db.Accounts.Add(header);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                header.Name = "Cash";
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await db.SaveChangesAsync(ct);
            }

            var code = $"{CoaCode.TILL_CHILD}-{outlet.Code}";
            var child = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outletId, ct);
            if (child != null) return child.Id;

            child = new Account
            {
                Code = code,
                Name = $"Cash in Till – {outlet.Name}",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                ParentId = header.Id,
                OutletId = outletId,
                IsActive = true,
                SystemKey = SystemAccountKey.CashInTillOutlet
            };
            db.Accounts.Add(child);
            await db.SaveChangesAsync(ct);
            return child.Id;
        }

        private async Task<int> EnsureCompanyCashInHandAsync(PosClientDbContext db, CancellationToken ct)
        {
            var code = $"{CoaCode.CASH_CHILD}-{CoaCode.COMPANY_CODE}";
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == null, ct);
            if (acc != null) return acc.Id;

            var header = await db.Accounts.FirstAsync(a => a.Code == CoaCode.CASH_HEADER && a.OutletId == null, ct);
            acc = new Account
            {
                Code = code,
                Name = "Cash in Hand – Company",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                ParentId = header.Id,
                OutletId = null,
                IsActive = true
            };
            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);
            return acc.Id;
        }

        private async Task<int> GetCashAccountIdAsync(PosClientDbContext db, int? outletId, CancellationToken ct)
        {
            if (outletId == null)
                return await EnsureCompanyCashInHandAsync(db, ct);

            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId.Value, ct);
            var code = $"{CoaCode.CASH_CHILD}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id, ct);
            return acc?.Id ?? await EnsureOutletCashAccountAsync(db, outlet.Id, ct);
        }

        private async Task<int> GetTillAccountIdAsync(PosClientDbContext db, int outletId, CancellationToken ct)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);
            var code = $"{CoaCode.TILL_CHILD}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id, ct);
            return acc?.Id ?? await EnsureOutletTillAccountAsync(db, outlet.Id, ct);
        }

        private async Task<int> EnsureCompanyTillHeaderAsync(PosClientDbContext db, CancellationToken ct)
        {
            // parent cash header "111" at company level
            var cashHeader = await db.Accounts.FirstOrDefaultAsync(a => a.Code == CoaCode.CASH_HEADER && a.OutletId == null, ct);
            if (cashHeader == null)
            {
                cashHeader = new Account
                {
                    Code = CoaCode.CASH_HEADER,            // "111"
                    Name = "Cash",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Accounts.Add(cashHeader);
                await db.SaveChangesAsync(ct);
                await _outbox.EnqueueUpsertAsync(db, cashHeader, ct);
                await db.SaveChangesAsync(ct);
            }

            // company-level till header "11102" (no OutletId)
            var tillHeader = await db.Accounts.FirstOrDefaultAsync(a => a.Code == CoaCode.TILL_CHILD && a.OutletId == null, ct);
            if (tillHeader != null) return tillHeader.Id;

            tillHeader = new Account
            {
                Code = CoaCode.TILL_CHILD,                // "11102"
                Name = "Cash in Till",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = true,
                AllowPosting = false,
                IsSystem = true,
                ParentId = cashHeader.Id,
                OutletId = null,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Accounts.Add(tillHeader);
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, tillHeader, ct);
            await db.SaveChangesAsync(ct);
            return tillHeader.Id;
        }

        public async Task<int> EnsureCounterTillAccountAsync(int outletId, int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await EnsureCounterTillAccountAsync(db, outletId, counterId, ct);
        }

        public async Task<int> GetCounterTillAccountIdAsync(int outletId, int counterId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await GetCounterTillAccountIdAsync(db, outletId, counterId, ct);
        }

        private async Task<int> EnsureCounterTillAccountAsync(PosClientDbContext db, int outletId, int counterId, CancellationToken ct)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);
            var counter = await db.Counters.AsNoTracking().FirstAsync(c => c.Id == counterId, ct);

            var parentTillHeaderId = await EnsureCompanyTillHeaderAsync(db, ct);

            var code = $"{CoaCode.TILL_CHILD}-{outlet.Code}-C{counter.Id}"; // 11102-<OUT>-C<CounterId>
            var existing = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id, ct);
            if (existing != null) return existing.Id;

            var acc = new Account
            {
                Code = code,
                Name = $"Cash in Till – {outlet.Name} / {counter.Name}",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                SystemKey = SystemAccountKey.CashInTillOutlet, // reuse key family
                ParentId = parentTillHeaderId,
                OutletId = outlet.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);
            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);
            return acc.Id;
        }

        private async Task<int> GetCounterTillAccountIdAsync(PosClientDbContext db, int outletId, int counterId, CancellationToken ct)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId, ct);
            var code = $"{CoaCode.TILL_CHILD}-{outlet.Code}-C{counterId}";
            var acc = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id, ct);
            return acc?.Id ?? await EnsureCounterTillAccountAsync(db, outletId, counterId, ct);
        }


        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, bool forHeader, CancellationToken ct)
        {
            var siblingsCodes = await db.Accounts
                .AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync(ct);

            int max = 0;
            foreach (var code in siblingsCodes)
            {
                var lastSeg = code?.Split('-').LastOrDefault();
                if (int.TryParse(lastSeg, out var num) && num > max) max = num;
            }

            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }

        // -------- Toolbar convenience --------
        public async Task AddCashForOutletAsync(CancellationToken ct = default)
        {
                 await using var db = await _dbf.CreateDbContextAsync(ct);
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync(ct);
                if (outlet == null) return;
            var id = await EnsureOutletCashAccountAsync(db, outlet.Id, ct);
            var acc = await db.Accounts.AsNoTracking().FirstAsync(a => a.Id == id, ct);
            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);
        }

        public async Task AddStaffAccountAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            
                // 1) Ensure a company-level "Staff" header (system)
            var staffHeader = await db.Accounts.FirstOrDefaultAsync(
            a => a.Code == "63" && a.OutletId == null, ct); // pick whatever canonical code you use
                if (staffHeader == null)
                {
                staffHeader = new Account
                    {
                    Code = "63",
                    Name = "Staff",
                    Type = AccountType.Expense,            // or Asset if that’s your policy
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                    }
                ;
                db.Accounts.Add(staffHeader);
                await db.SaveChangesAsync(ct);
                    }
            
                // 2) Create one example staff posting account (idempotent)
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(ct);
                if (user == null) return;
            
            var exists = await db.Accounts.AnyAsync(a => a.ParentId == staffHeader.Id && a.Name == $"Staff — {user.Username}", ct);
                if (exists) return;
            
            var next = await GenerateNextChildCodeAsync(db, staffHeader, forHeader: false, ct);
            var leaf = new Account
                {
                Code = next,
                Name = $"Staff — {user.Username}",
                Type = staffHeader.Type,
                NormalSide = DefaultNormalFor(staffHeader.Type),
                IsHeader = false,
                AllowPosting = true,
                ParentId = staffHeader.Id,
                OutletId = null,
                IsSystem = true,
                IsActive = true
                }
            ;
            db.Accounts.Add(leaf);
            await db.SaveChangesAsync(ct);
            
            await _outbox.EnqueueUpsertAsync(db, staffHeader, ct);
            await _outbox.EnqueueUpsertAsync(db, leaf, ct);
            await db.SaveChangesAsync(ct);
        }

        // ---- Inventory / Supplier helpers (public) ----
        public async Task<int> EnsureInventoryAccountIdAsync(CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            // Try to find an existing company-level posting account named "Inventory"
            var inv = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a =>
                    a.OutletId == null &&
                    !a.IsHeader &&
                    a.AllowPosting &&
                    a.Type == AccountType.Asset &&
                    a.Name == "Inventory", ct);

            if (inv != null) return inv.Id;

            // Create a simple company-level posting account if missing
            var acc = new Account
            {
                Code = "INVENTORY",               // safe, non-numeric; won’t collide with your numeric scheme
                Name = "Inventory",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                OutletId = null,
                IsActive = true
            };
            db.Accounts.Add(acc);
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, acc, ct);
            await db.SaveChangesAsync(ct);

            return acc.Id;
        }

        public async Task<int> EnsureSupplierAccountIdAsync(int partyId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var party = await db.Parties.FirstOrDefaultAsync(p => p.Id == partyId, ct)
                        ?? throw new InvalidOperationException("Supplier/Party not found.");

            // If party already linked to an account, return it (and ensure it still exists)
            if (party.AccountId.HasValue)
            {
                var exists = await db.Accounts.AsNoTracking()
                    .AnyAsync(a => a.Id == party.AccountId.Value, ct);
                if (exists) return party.AccountId.Value;
                // fall through to (re)create if dangling
            }

            // Ensure a company-level "Parties" header (Type = Parties). Prefer an existing one.
            var partiesHeader = await db.Accounts.FirstOrDefaultAsync(a =>
                a.OutletId == null &&
                a.IsHeader &&
                a.Type == AccountType.Parties, ct);

            if (partiesHeader == null)
            {
                partiesHeader = new Account
                {
                    Code = "6",                    // conventional high-level code for Parties (company-scope)
                    Name = "Parties",
                    Type = AccountType.Parties,
                    NormalSide = DefaultNormalFor(AccountType.Parties),
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                db.Accounts.Add(partiesHeader);
                await db.SaveChangesAsync(ct);

                await _outbox.EnqueueUpsertAsync(db, partiesHeader, ct);
                await db.SaveChangesAsync(ct);
            }

            // Create a leaf posting account for this supplier
            var leafCode = await GenerateNextChildCodeAsync(db, partiesHeader, forHeader: false, ct);
            var leaf = new Account
            {
                Code = leafCode,
                Name = $"Supplier — {party.Name}",
                Type = AccountType.Parties,
                NormalSide = DefaultNormalFor(AccountType.Parties),
                IsHeader = false,
                AllowPosting = true,
                ParentId = partiesHeader.Id,
                OutletId = null,
                IsSystem = true,
                IsActive = true
            };
            db.Accounts.Add(leaf);
            await db.SaveChangesAsync(ct);

            // Link party → account
            party.AccountId = leaf.Id;
            await db.SaveChangesAsync(ct);

            await _outbox.EnqueueUpsertAsync(db, partiesHeader, ct);
            await _outbox.EnqueueUpsertAsync(db, leaf, ct);
            await _outbox.EnqueueUpsertAsync(db, party, ct);
            await db.SaveChangesAsync(ct);

            return leaf.Id;
        }


    }
}
