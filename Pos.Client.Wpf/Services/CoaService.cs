using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface ICoaService
    {
        Task<int> EnsureOutletCashAccountAsync(int outletId);
        Task<int> GetCashAccountIdAsync(int? outletId);

        // NEW: add Till helpers (non-breaking addition)
        Task<int> EnsureOutletTillAccountAsync(int outletId);
        Task<int> GetTillAccountIdAsync(int outletId);

        // NEW: db-aware overloads to avoid second-writer locks
        Task<int> EnsureOutletCashAccountAsync(PosClientDbContext db, int outletId);
        Task<int> GetCashAccountIdAsync(PosClientDbContext db, int? outletId);
        Task<int> EnsureOutletTillAccountAsync(PosClientDbContext db, int outletId);
        Task<int> GetTillAccountIdAsync(PosClientDbContext db, int outletId);
    }

    public sealed class CoaService : ICoaService
    {
        private readonly PosClientDbContext _db;
        public CoaService(PosClientDbContext db) => _db = db;

        private const string CASH_CODE = "111"; // company-level header code for Cash in Hand
        private const string TILL_CODE = "112";

        public async Task<int> EnsureOutletCashAccountAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            // Ensure company-level CASH header exists and is marked as header/system
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_CODE && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_CODE,
                    Name = "Cash in Hand",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                _db.Accounts.Add(header);
                await _db.SaveChangesAsync();
            }
            else
            {
                // normalize semantics in case someone edited it
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await _db.SaveChangesAsync();
            }

            // Child code is “111-{OutletCode}”
            var childCode = $"{CASH_CODE}-{outlet.Code}";
            var child = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Code == childCode && a.OutletId == outletId);

            if (child != null) return child.Id;

            child = new Account
            {
                Code = childCode,
                Name = $"Cash in Hand – {outlet.Name}",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true, // lock against delete/rename
                ParentId = header.Id,
                OutletId = outletId,
                IsActive = true
            };
            _db.Accounts.Add(child);
            await _db.SaveChangesAsync();
            return child.Id;
        }

        // NEW: ensure outlet-scoped "Cash in Till – {Outlet}" posting account under a "Cash in Till" header
        public async Task<int> EnsureOutletTillAccountAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            // Ensure "Cash in Till" HEADER exists (company-level)
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == TILL_CODE && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = TILL_CODE,
                    Name = "Cash in Till",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                _db.Accounts.Add(header);
                await _db.SaveChangesAsync();
            }
            else
            {
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await _db.SaveChangesAsync();
            }

            // Child code is “112-{OutletCode}”
            var childCode = $"{TILL_CODE}-{outlet.Code}";
            var child = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Code == childCode && a.OutletId == outletId);

            if (child != null) return child.Id;

            child = new Account
            {
                Code = childCode,
                Name = $"Cash in Till – {outlet.Name}",
                Type = AccountType.Asset,
                NormalSide = NormalSide.Debit,
                IsHeader = false,
                AllowPosting = true,
                IsSystem = true,
                ParentId = header.Id,
                OutletId = outletId,
                IsActive = true
            };
            _db.Accounts.Add(child);
            await _db.SaveChangesAsync();
            return child.Id;
        }

        // If outletId is null (company-scope docs), keep your existing behavior
        public async Task<int> GetCashAccountIdAsync(int? outletId)
        {
            if (outletId == null)
            {
                var h = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Code == CASH_CODE && a.OutletId == null);
                return h.Id;
            }

            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId.Value);
            var code = $"{CASH_CODE}-{outlet.Code}";
            var acc = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            if (acc != null) return acc.Id;

            return await EnsureOutletCashAccountAsync(outlet.Id);
        }

        // NEW: quick resolver for Till (always outlet-scoped)
        public async Task<int> GetTillAccountIdAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"{TILL_CODE}-{outlet.Code}";
            var acc = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            if (acc != null) return acc.Id;

            return await EnsureOutletTillAccountAsync(outlet.Id);
        }

        // -------- NEW: db-aware implementations (use provided db ONLY) ----
        public async Task<int> EnsureOutletCashAccountAsync(PosClientDbContext db, int outletId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            var header = await db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_CODE && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_CODE,
                    Name = "Cash in Hand",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                db.Accounts.Add(header);
                await db.SaveChangesAsync();
            }
            else
            {
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await db.SaveChangesAsync();
            }

            var childCode = $"{CASH_CODE}-{outlet.Code}";
            var child = await db.Accounts
                .FirstOrDefaultAsync(a => a.Code == childCode && a.OutletId == outletId);

            if (child != null) return child.Id;

            child = new Account
            {
                Code = childCode,
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
            await db.SaveChangesAsync();
            return child.Id;
        }

        public async Task<int> EnsureOutletTillAccountAsync(PosClientDbContext db, int outletId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            var header = await db.Accounts.FirstOrDefaultAsync(a => a.Code == TILL_CODE && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = TILL_CODE,
                    Name = "Cash in Till",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = true,
                    AllowPosting = false,
                    IsSystem = true,
                    OutletId = null,
                    IsActive = true
                };
                db.Accounts.Add(header);
                await db.SaveChangesAsync();
            }
            else
            {
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await db.SaveChangesAsync();
            }

            var childCode = $"{TILL_CODE}-{outlet.Code}";
            var child = await db.Accounts
                .FirstOrDefaultAsync(a => a.Code == childCode && a.OutletId == outletId);

            if (child != null) return child.Id;

            child = new Account
            {
                Code = childCode,
                Name = $"Cash in Till – {outlet.Name}",
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
            await db.SaveChangesAsync();
            return child.Id;
        }

        public async Task<int> GetCashAccountIdAsync(PosClientDbContext db, int? outletId)
        {
            if (outletId == null)
            {
                var h = await db.Accounts.AsNoTracking()
                    .FirstAsync(a => a.Code == CASH_CODE && a.OutletId == null);
                return h.Id;
            }

            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId.Value);
            var code = $"{CASH_CODE}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            return acc?.Id ?? await EnsureOutletCashAccountAsync(db, outlet.Id);
        }

        public async Task<int> GetTillAccountIdAsync(PosClientDbContext db, int outletId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"{TILL_CODE}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            return acc?.Id ?? await EnsureOutletTillAccountAsync(db, outlet.Id);
        }
    }
}
