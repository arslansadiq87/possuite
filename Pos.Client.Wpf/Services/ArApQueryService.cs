// Pos.Client.Wpf/Services/ArApQueryService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public record ArApRow(int PartyId, string PartyName, int? OutletId, string? OutletName, decimal Balance);

    public interface IArApQueryService
    {
        Task<List<ArApRow>> GetAccountsReceivableAsync(int? outletId = null, bool includeZero = false);
        Task<List<ArApRow>> GetAccountsPayableAsync(int? outletId = null, bool includeZero = false);
        Task<decimal> GetTotalAsync(RoleType role, int? outletId = null);
    }

    public sealed class ArApQueryService : IArApQueryService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public ArApQueryService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public Task<List<ArApRow>> GetAccountsReceivableAsync(int? outletId = null, bool includeZero = false)
            => QueryAsync(RoleType.Customer, outletId, includeZero);

        public Task<List<ArApRow>> GetAccountsPayableAsync(int? outletId = null, bool includeZero = false)
            => QueryAsync(RoleType.Supplier, outletId, includeZero);

        public async Task<decimal> GetTotalAsync(RoleType role, int? outletId = null)
        {
            using var db = await _dbf.CreateDbContextAsync();
            var baseQuery = from bal in db.PartyBalances.AsNoTracking()
                            join roleRow in db.PartyRoles.AsNoTracking() on bal.PartyId equals roleRow.PartyId
                            where roleRow.Role == role
                            select new { bal.OutletId, bal.Balance };
            if (outletId.HasValue)
                baseQuery = baseQuery.Where(x => x.OutletId == outletId.Value);

            return await baseQuery.SumAsync(x => (decimal?)x.Balance) ?? 0m;
        }

        private async Task<List<ArApRow>> QueryAsync(RoleType role, int? outletId, bool includeZero)
        {
            using var db = await _dbf.CreateDbContextAsync();

            // Build a fully SQL-translatable shape first
            var q =
                from bal in db.PartyBalances.AsNoTracking()
                join roleRow in db.PartyRoles.AsNoTracking() on bal.PartyId equals roleRow.PartyId
                join party in db.Parties.AsNoTracking() on bal.PartyId equals party.Id
                join o in db.Outlets.AsNoTracking() on bal.OutletId equals (int?)o.Id into oo
                from o in oo.DefaultIfEmpty()
                where roleRow.Role == role
                select new
                {
                    PartyId = bal.PartyId,
                    PartyName = party.Name,
                    OutletId = bal.OutletId,
                    OutletName = o != null ? o.Name : null,
                    Balance = bal.Balance
                };

            if (outletId.HasValue)
                q = q.Where(r => r.OutletId == outletId.Value);

            if (!includeZero)
                q = q.Where(r => r.Balance != 0m);

            // Run SQL, then order client-side (SQLite can't ORDER BY decimal)
            var raw = await q.ToListAsync();

            var rows = raw
                .OrderByDescending(r => Math.Abs(r.Balance))
                .ThenBy(r => r.PartyName)
                .Select(r => new ArApRow(r.PartyId, r.PartyName, r.OutletId, r.OutletName, r.Balance))
                .ToList();

            return rows;
        }


    }
}
