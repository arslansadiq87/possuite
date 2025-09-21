using System.Collections.Generic;
using System.Linq;
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

        public async Task<List<Party>> SearchSuppliersAsync(string term, int outletId)
        {
            term = term?.Trim() ?? "";
            var q =
                from p in _db.Parties.AsNoTracking().Where(p => p.IsActive)
                join r in _db.PartyRoles.AsNoTracking()
                    on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            if (!string.IsNullOrWhiteSpace(term))
                q = q.Where(p => p.Name.Contains(term));

            return await q
                .OrderBy(p => p.Name)
                .Distinct()
                .ToListAsync();
        }

        public async Task<Party?> FindSupplierByExactNameAsync(string name, int outletId)
        {
            name = name?.Trim() ?? "";
            if (name == "") return null;

            var q =
                from p in _db.Parties.AsNoTracking().Where(p => p.IsActive && p.Name.ToLower() == name.ToLower())
                join r in _db.PartyRoles.AsNoTracking()
                    on p.Id equals r.PartyId
                where r.Role == RoleType.Supplier
                join map in _db.PartyOutlets.AsNoTracking().Where(m => m.IsActive && m.OutletId == outletId)
                    on p.Id equals map.PartyId into maps
                from m in maps.DefaultIfEmpty()
                where p.IsSharedAcrossOutlets || m != null
                select p;

            return await q.FirstOrDefaultAsync();
        }
    }
}
