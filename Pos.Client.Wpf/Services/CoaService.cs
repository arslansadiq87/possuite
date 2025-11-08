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
        Task<int> GetSupplierAdvancesAccountIdAsync(int outletId);

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

        //private const string CASH_CODE = "111"; // company-level header code for Cash in Hand
        //private const string TILL_CODE = "112";
        private const string CASH_HEADER = "111";  // header under which both live
        private const string CASH_CHILD = "11101"; // Cash in Hand
        private const string TILL_CHILD = "11102"; // Cash in Till
        private const string COMPANY_CODE = "COMPANY"; // or your company short code

        

        public async Task<int> EnsureOutletCashAccountAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            // Ensure company-level CASH header exists and is marked as header/system
            // Ensure company-level CASH header exists and is marked as header/system
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_HEADER,
                    Name = "Cash",                 // <— unified name
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
                header.Name = "Cash";             // <— keep it consistent
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await _db.SaveChangesAsync();
            }


            // Child code is “111-{OutletCode}”
            var childCode = $"{CASH_CHILD}-{outlet.Code}";
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
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_HEADER,
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
            var childCode = $"{TILL_CHILD}-{outlet.Code}";
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

        private async Task<int> EnsureCompanyCashInHandAsync()
{
    // 11101 leaf without OutletId — company-scope posting account
    var code = $"{CASH_CHILD}-{COMPANY_CODE}";
    var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == null);
    if (acc != null) return acc.Id;

    // parent header 111 must exist
    var header = await _db.Accounts.FirstAsync(a => a.Code == CASH_HEADER && a.OutletId == null);

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
    _db.Accounts.Add(acc);
    await _db.SaveChangesAsync();
    return acc.Id;
}

        private async Task<int> EnsureCompanyCashInHandAsync(PosClientDbContext db)
        {
            var code = $"{CASH_CHILD}-{COMPANY_CODE}";
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Code == code && a.OutletId == null);
            if (acc != null) return acc.Id;

            var header = await db.Accounts.FirstAsync(a => a.Code == CASH_HEADER && a.OutletId == null);

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
            await db.SaveChangesAsync();
            return acc.Id;
        }

        // If outletId is null (company-scope docs), keep your existing behavior
        public async Task<int> GetCashAccountIdAsync(int? outletId)
        {
            if (outletId == null)
            {
                //var h = await _db.Accounts.AsNoTracking().FirstAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
                return await EnsureCompanyCashInHandAsync();   // <— was returning header 111

            }

            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId.Value);
            var code = $"{CASH_CHILD}-{outlet.Code}";
            var acc = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            if (acc != null) return acc.Id;

            return await EnsureOutletCashAccountAsync(outlet.Id);
        }

        // NEW: quick resolver for Till (always outlet-scoped)
        public async Task<int> GetTillAccountIdAsync(int outletId)
        {
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"{TILL_CHILD}-{outlet.Code}";
            var acc = await _db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            if (acc != null) return acc.Id;

            return await EnsureOutletTillAccountAsync(outlet.Id);
        }

        // -------- NEW: db-aware implementations (use provided db ONLY) ----
        public async Task<int> EnsureOutletCashAccountAsync(PosClientDbContext db, int outletId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);

            // Ensure company-level CASH header exists and is marked as header/system
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_HEADER,
                    Name = "Cash",                 // <— unified name
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
                header.Name = "Cash";             // <— keep it consistent
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await _db.SaveChangesAsync();
            }


            var childCode = $"{CASH_CHILD}-{outlet.Code}";
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

            // Ensure company-level CASH header exists and is marked as header/system
            var header = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
            if (header == null)
            {
                header = new Account
                {
                    Code = CASH_HEADER,
                    Name = "Cash",                 // <— unified name
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
                header.Name = "Cash";             // <— keep it consistent
                header.IsHeader = true;
                header.AllowPosting = false;
                header.IsSystem = true;
                header.OutletId = null;
                await _db.SaveChangesAsync();
            }


            var childCode = $"{TILL_CHILD}-{outlet.Code}";
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
                return await EnsureCompanyCashInHandAsync(db);
                //var h = await db.Accounts.AsNoTracking()
                //    .FirstAsync(a => a.Code == CASH_HEADER && a.OutletId == null);
                //return h.Id;
            }

            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId.Value);
            var code = $"{CASH_CHILD}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            return acc?.Id ?? await EnsureOutletCashAccountAsync(db, outlet.Id);
        }

        public async Task<int> GetSupplierAdvancesAccountIdAsync(int outletId)
        {
            // Code pattern: "113-{OUTLET}-ADV" (Assets: 11x range typically)
            var outlet = await _db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"113-{outlet.Code}-ADV";

            var existing = await _db.Accounts
                .AsNoTracking()
                .Where(a => a.Code == code && a.OutletId == outletId)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();

            if (existing != 0) return existing;

            // Create if missing (under Assets header). Adjust parent/header lookups to your structure if needed.
            var assetHeaderId = await _db.Accounts
                .Where(a => a.IsHeader && a.Code == "1") // your Asset root/header
                .Select(a => a.Id)
                .FirstAsync();

            var acc = new Pos.Domain.Entities.Account
            {
                OutletId = outletId,
                Code = code,
                Name = "Supplier Advances",
                Type = Pos.Domain.Entities.AccountType.Asset,
                IsHeader = false,
                AllowPosting = true,
                ParentId = assetHeaderId,
                IsSystem = true
            };
            _db.Accounts.Add(acc);
            await _db.SaveChangesAsync();
            return acc.Id;
        }


        public async Task<int> GetTillAccountIdAsync(PosClientDbContext db, int outletId)
        {
            var outlet = await db.Outlets.AsNoTracking().FirstAsync(o => o.Id == outletId);
            var code = $"{TILL_CHILD}-{outlet.Code}";
            var acc = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == code && a.OutletId == outlet.Id);
            return acc?.Id ?? await EnsureOutletTillAccountAsync(db, outlet.Id);
        }
    }
}
