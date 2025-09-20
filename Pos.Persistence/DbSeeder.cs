// Pos.Persistence/DbSeeder.cs
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain;

namespace Pos.Persistence;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        db.Database.Migrate(); // safe; ensures DB is up-to-date

        if (!db.Users.Any())
        {
            var admin = new User
            {
                Username = "admin",
                DisplayName = "Administrator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                Role = UserRole.Admin,
                IsActive = true
            };
            db.Users.Add(admin);
        }

        if (!db.Outlets.Any())
        {
            var outlet = new Outlet { Name = "Main Outlet", Code = "MAIN", IsActive = true };
            db.Outlets.Add(outlet);
            db.Counters.Add(new Counter { Name = "Counter 1", Outlet = outlet, IsActive = true });
        }

        db.SaveChanges();
    }
}
