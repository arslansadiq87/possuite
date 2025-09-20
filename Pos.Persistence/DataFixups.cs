using Microsoft.EntityFrameworkCore;

namespace Pos.Persistence
{
    public static class DataFixups
    {
        public static void NormalizeUsers(PosClientDbContext db)
        {
            // Map legacy string roles -> integers expected by enum UserRole
            var sql = new[]
            {
                "UPDATE Users SET Role = 4 WHERE CAST(Role AS TEXT) IN ('Admin','ADMIN','admin')",
                "UPDATE Users SET Role = 3 WHERE CAST(Role AS TEXT) IN ('Manager','MANAGER','manager')",
                "UPDATE Users SET Role = 2 WHERE CAST(Role AS TEXT) IN ('Supervisor','SUPERVISOR','supervisor')",
                "UPDATE Users SET Role = 1 WHERE CAST(Role AS TEXT) IN ('Cashier','CASHIER','cashier')",
                "UPDATE Users SET Role = 0 WHERE CAST(Role AS TEXT) IN ('Salesman','SALESMAN','salesman')",

                // Default anything unknown/null/empty to Cashier
                "UPDATE Users SET Role = 1 WHERE Role IS NULL OR TRIM(CAST(Role AS TEXT)) = ''",

                // Avoid null display names (your entity expects non-null)
                "UPDATE Users SET DisplayName = COALESCE(DisplayName, Username)"
            };

            foreach (var s in sql) db.Database.ExecuteSqlRaw(s);
        }
    }
}
