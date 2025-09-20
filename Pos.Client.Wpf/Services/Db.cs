using Microsoft.EntityFrameworkCore;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public static class Db
    {
        // TODO: if you use SQL Server locally, swap UseSqlServer(...)
        public static DbContextOptions<PosClientDbContext> ClientOptions { get; } =
            new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite("Data Source=pos_client.db")  // or your path
                .Options;
    }
}
