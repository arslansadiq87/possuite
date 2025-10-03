// Pos.Persistence/AppDbContext.cs
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Abstractions;
using Pos.Domain.Entities;
using CounterEntity = Pos.Domain.Entities.Counter; // avoid generic Counter<T> clash

namespace Pos.Persistence // ← keep a namespace (recommended)
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<User> Users => Set<User>();
        public DbSet<Outlet> Outlets => Set<Outlet>();
        public DbSet<CounterEntity> Counters => Set<CounterEntity>();
        public DbSet<UserOutlet> UserOutlets => Set<UserOutlet>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<Item> Items => Set<Item>();
        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleLine> SaleLines => Set<SaleLine>();
        public DbSet<TillSession> TillSessions => Set<TillSession>();
        public DbSet<Purchase> Purchases { get; set; }
        public DbSet<PurchaseLine> PurchaseLines { get; set; }
        public DbSet<PurchasePayment> PurchasePayments => Set<PurchasePayment>();
        public DbSet<CashLedger> CashLedgers => Set<CashLedger>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            foreach (var e in b.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(e.ClrType))
                {
                    b.Entity(e.ClrType).Property("PublicId")
                        .HasDefaultValueSql("NEWID()")     // SQL Server GUID
                        .IsRequired();
                    b.Entity(e.ClrType).Property("CreatedAtUtc")
                        .HasDefaultValueSql("GETUTCDATE()"); // server-side UTC
                    b.Entity(e.ClrType).Property("RowVersion")
                        .IsRowVersion(); // optimistic concurrency
                }
            }
            base.OnModelCreating(b);
        }
    }
}
