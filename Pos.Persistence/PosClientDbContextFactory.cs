// Pos.Persistence/PosClientDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Persistence
{
    public class PosClientDbContextFactory : IDesignTimeDbContextFactory<PosClientDbContext>
    {
        public PosClientDbContext CreateDbContext(string[] args)
        {
            var opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;
            return new PosClientDbContext(opts);
        }
    }
}
