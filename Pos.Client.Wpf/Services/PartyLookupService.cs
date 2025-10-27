using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public sealed class PartyLookupService
    {
        private readonly PosClientDbContext _db;
        public PartyLookupService(PosClientDbContext db) => _db = db;

        // --------------------------
        // EXISTING METHODS (unchanged API)
        // --------------------------
        public async Task<List<Party>> SearchSuppliersAsync(string term, int outletId, int take = 30, CancellationToken ct = default)
        {
            term = term?.Trim() ?? "";

            // Base: active suppliers with outlet visibility (shared OR explicitly active at outlet)
            var q =
                from p in _db.Parties.AsNoTracking().Where(p => p.IsActive)
                join r in _db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            if (!string.IsNullOrWhiteSpace(term))
            {
                term = term.Trim();

                // Escape wildcards (optional safety)
                string Escape(string s) => s
                    .Replace("[", "[[]")
                    .Replace("%", "[%]")
                    .Replace("_", "[_]")
                    .Replace("'", "''");

                var like = $"%{Escape(term)}%";

                q = q.Where(p =>
                    EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), like) ||
                    (p.Phone != null && EF.Functions.Like(EF.Functions.Collate(p.Phone, "NOCASE"), like)) ||
                    (p.Email != null && EF.Functions.Like(EF.Functions.Collate(p.Email, "NOCASE"), like)) ||
                    (p.TaxNumber != null && EF.Functions.Like(EF.Functions.Collate(p.TaxNumber, "NOCASE"), like))
                );
            }

            return await q
                .OrderBy(p => p.Name)
                .Distinct()
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<Party?> FindSupplierByExactNameAsync(string name, int outletId, CancellationToken ct = default)
        {
            name = name?.Trim() ?? "";
            if (name == "") return null;

            var q =
                from p in _db.Parties.AsNoTracking()
                    .Where(p => p.IsActive && EF.Functions.Collate(p.Name, "NOCASE") == name)
                join r in _db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            return await q.FirstOrDefaultAsync(ct);
        }

        // --------------------------
        // NEW: generic + customer helpers
        // --------------------------

        /// <summary>
        /// Generic party search with optional role (Customer/Supplier/null = both) and outlet visibility.
        /// Returns active parties, respecting shared/Outlet mapping. Includes Name/Phone/Email/Tax search.
        /// </summary>
        public async Task<List<Party>> SearchPartiesAsync(
            string term,
            RoleType? roleFilter,
            int outletId,
            int take = 30,
            CancellationToken ct = default)
        {
            term = term?.Trim() ?? "";

            // Base: active parties with role filter
            var q =
                from p in _db.Parties.AsNoTracking().Where(p => p.IsActive)
                join r in _db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where !roleFilter.HasValue || r.Role == roleFilter.Value
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(p =>
                    p.Name.Contains(term) ||
                    (p.Phone != null && p.Phone.Contains(term)) ||
                    (p.Email != null && p.Email.Contains(term)) ||
                    (p.TaxNumber != null && p.TaxNumber.Contains(term)));
            }

            return await q
                .OrderBy(p => p.Name)
                .Distinct()
                .Take(take)
                .ToListAsync(ct);
        }

        public Task<List<Party>> SearchCustomersAsync(string term, int outletId, int take = 30, CancellationToken ct = default) =>
            SearchPartiesAsync(term, RoleType.Customer, outletId, take, ct);

        public async Task<Party?> FindCustomerByExactNameAsync(string name, int outletId, CancellationToken ct = default)
        {
            name = name?.Trim() ?? "";
            if (name == "") return null;

            var q =
                from p in _db.Parties.AsNoTracking().Where(p => p.IsActive && p.Name.ToLower() == name.ToLower())
                join r in _db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Customer
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            return await q.FirstOrDefaultAsync(ct);
        }
    }
}
