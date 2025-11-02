using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IOutletService
    {
        Task<int> CreateAsync(Outlet o, CancellationToken ct = default);
        Task UpdateAsync(Outlet o, CancellationToken ct = default);
    }

    public sealed class OutletService : IOutletService
    {
        private readonly PosClientDbContext _db;
        private readonly ICoaService _coa;
        public OutletService(PosClientDbContext db, ICoaService coa)
        {
            _db = db; _coa = coa;
        }

        public async Task<int> CreateAsync(Outlet o, CancellationToken ct = default)
        {
            _db.Outlets.Add(o);
            await _db.SaveChangesAsync(ct);

            await _coa.EnsureOutletCashAccountAsync(o.Id);
            await _coa.EnsureOutletTillAccountAsync(o.Id);

            return o.Id;
        }

        public async Task UpdateAsync(Outlet o, CancellationToken ct = default)
        {
            var entity = await _db.Outlets.FirstAsync(x => x.Id == o.Id, ct);
            entity.Code = o.Code;
            entity.Name = o.Name;
            entity.Address = o.Address;
            entity.IsActive = o.IsActive;

            await _db.SaveChangesAsync(ct);

            // Re-ensure (idempotent, handles code/name changes)
            await _coa.EnsureOutletCashAccountAsync(o.Id);
            await _coa.EnsureOutletTillAccountAsync(o.Id);
        }
    }
}
