using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Persistence.Services
{
    /// <summary>
    /// EF Core implementation of IPartyLookupService.
    /// Uses IDbContextFactory&lt;PosClientDbContext&gt; via CreateDbContextAsync(ct) (house rule).
    /// </summary>
    public sealed class PartyLookupService : IPartyLookupService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public PartyLookupService(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf ?? throw new ArgumentNullException(nameof(dbf));
        }

        public async Task<List<Party>> SearchSuppliersAsync(string term, int outletId, int take = 30, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            var q =
                from p in db.Parties.AsNoTracking().Where(p => p.IsActive)
                join r in db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            q = ApplyTermFilter(q, term);

            return await q.OrderBy(p => p.Name)
                          .Distinct()
                          .Take(take)
                          .ToListAsync(ct);
        }

        public async Task<Party?> FindSupplierByExactNameAsync(string name, int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            var q =
                from p in db.Parties.AsNoTracking()
                    .Where(p => p.IsActive && EF.Functions.Collate(p.Name, "NOCASE") == name)
                join r in db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;
            return await q.FirstOrDefaultAsync(ct);
        }

        public async Task<List<Party>> SearchPartiesAsync(string term,RoleType? roleFilter,int outletId,int take = 30,CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var q =
                from p in db.Parties.AsNoTracking().Where(p => p.IsActive)
                join r in db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where !roleFilter.HasValue || r.Role == roleFilter.Value
                join map in db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;
            q = ApplyTermFilter(q, term);
            return await q.OrderBy(p => p.Name)
                          .Distinct()
                          .Take(take)
                          .ToListAsync(ct);
        }

        public Task<List<Party>> SearchCustomersAsync(string term, int outletId, int take = 30, CancellationToken ct = default) =>
            SearchPartiesAsync(term, RoleType.Customer, outletId, take, ct);

        public async Task<Party?> FindCustomerByExactNameAsync(string name, int outletId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);

            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();

            var q =
                from p in db.Parties.AsNoTracking()
                    .Where(p => p.IsActive && EF.Functions.Collate(p.Name, "NOCASE") == name)
                join r in db.PartyRoles.AsNoTracking() on p.Id equals r.PartyId
                where r.Role == RoleType.Customer
                join map in db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            return await q.FirstOrDefaultAsync(ct);
        }

        // --------------- helpers ---------------
        private static IQueryable<Party> ApplyTermFilter(IQueryable<Party> q, string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return q;

            static string Escape(string s) => s
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]")
                .Replace("'", "''");

            var like = $"%{Escape(term.Trim())}%";

            return q.Where(p =>
                EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), like) ||
                (p.Phone != null && EF.Functions.Like(EF.Functions.Collate(p.Phone, "NOCASE"), like)) ||
                (p.Email != null && EF.Functions.Like(EF.Functions.Collate(p.Email, "NOCASE"), like)) ||
                (p.TaxNumber != null && EF.Functions.Like(EF.Functions.Collate(p.TaxNumber, "NOCASE"), like))
            );
        }

        public async Task<string?> GetPartyNameAsync(int partyId, CancellationToken ct = default)
        {
            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Set<Party>().AsNoTracking()
                .Where(x => x.Id == partyId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
        }


    }
}
