// Pos.Persistence/Services/SuppliersService.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;

namespace Pos.Persistence.Services
{
    public class SuppliersService
    {
        private readonly PosClientDbContext _db;
        public SuppliersService(PosClientDbContext db) => _db = db;

        public Task<List<Supplier>> SearchAsync(string? term, int take = 20)
        {
            term = (term ?? "").Trim();
            var q = _db.Suppliers.AsNoTracking().Where(s => s.IsActive);

            if (!string.IsNullOrEmpty(term))
                q = q.Where(s => EF.Functions.Like(s.Name, $"%{term}%"));

            return q.OrderBy(s => s.Name).Take(take).ToListAsync();
        }

        public async Task<Supplier> CreateAsync(Supplier s)
        {
            _db.Suppliers.Add(s);
            await _db.SaveChangesAsync();
            return s;
        }
    }
}
