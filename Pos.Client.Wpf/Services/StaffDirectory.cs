using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IStaffDirectory
    {
        Task<List<Staff>> GetSalesmenAsync();
    }

    public sealed class StaffDirectory : IStaffDirectory
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public StaffDirectory(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<List<Staff>> GetSalesmenAsync()
        {
            using var db = _dbf.CreateDbContext();
            return await db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive && s.ActsAsSalesman)
                .OrderBy(s => s.FullName)
                .ToListAsync();
        }
    }
}
