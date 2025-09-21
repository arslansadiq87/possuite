// Pos.Persistence/Seed.cs
using System;
using System.Linq;
// REMOVE: using System.Security.Cryptography;
using Pos.Domain.Entities;                 // for User, Item, Product, StockEntry, etc.
// If you still have a Pos.Domain.UserRole, alias to avoid ambiguity:
using UserRoleEnum = Pos.Domain.UserRole;

namespace Pos.Persistence
{
    public static class Seed
    {
        /// <summary>
        /// Entry point: call once at app startup after ensuring DB is created/migrated.
        /// Idempotent:
        ///   - Users are created only if none exist
        ///   - Items are upserted by SKU
        ///   - Product + variants are upserted (by Product name/brand + SKU)
        ///   - Opening stock is added only for items that have no stock for the outlet yet
        /// </summary>
        public static void Ensure(PosClientDbContext db)
        {
            var now = DateTime.UtcNow;
            FixDuplicateProducts(db);

            // 0) Users (one-time default admin + salesman if Users is empty)
            EnsureUsers(db);
            // 0.1) Suppliers  ✅ add this
            EnsureSuppliers(db);
            // 1) Your existing items
            EnsureBasicItems(db, now);

            // 2) Your existing products/variants
            EnsureProductWithVariants(db, now);
            EnsureProductWithVariants_Jeans(db, now);  // NEW: Jeans

            // 3) Your existing opening stock
            EnsureOpeningStock(db, outletId: 1, openingQty: 50, now);

            EnsureOutlets(db);

        }

        // ------------------------------
        // 0) USERS  (BCrypt string hashes)
        // ------------------------------

        private static void FixDuplicateProducts(PosClientDbContext db)
        {
            // Group by "effective" uniqueness key: Name + BrandId + CategoryId
            // Use in-memory grouping to keep it simple.
            var groups = db.Products
                .Select(p => new
                {
                    Product = p,
                    p.Id,
                    p.Name,
                    BrandKey = p.BrandId ?? -1,
                    CategoryKey = p.CategoryId ?? -1,
                    VariantsCount = p.Variants.Count
                })
                .AsEnumerable()
                .GroupBy(x => (Name: x.Name.Trim(), x.BrandKey, x.CategoryKey))
                .Where(g => g.Count() > 1)
                .ToList();

            if (groups.Count == 0) return;

            foreach (var g in groups)
            {
                // Pick the canonical row: the one with most variants, then lowest Id
                var canonical = g.OrderByDescending(x => x.VariantsCount)
                                 .ThenBy(x => x.Id)
                                 .First()
                                 .Product;

                var duplicateIds = g.Select(x => x.Id)
                                    .Where(id => id != canonical.Id)
                                    .ToList();

                if (duplicateIds.Count == 0) continue;

                // Move variants from duplicates to canonical, then remove duplicates
                var duplicates = db.Products
                                   .Where(p => duplicateIds.Contains(p.Id))
                                   .ToList();

                foreach (var dup in duplicates)
                {
                    var items = db.Items.Where(i => i.ProductId == dup.Id).ToList();
                    foreach (var it in items)
                        it.ProductId = canonical.Id;

                    db.Products.Remove(dup);
                }
            }

            db.SaveChanges();
        }


        private static void EnsureUsers(PosClientDbContext db)
        {
            if (db.Users.Any()) return; // already seeded

            db.Users.Add(new User
            {
                Username = "admin",
                DisplayName = "Admin",
                Role = UserRoleEnum.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234"), // string hash
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            db.Users.Add(new User
            {
                Username = "ali",
                DisplayName = "Ali (Salesman)",
                Role = UserRoleEnum.Salesman,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("1111"), // string hash
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            db.SaveChanges();
        }

        // ------------------------------
        // 1) Basic items (your originals)
        // ------------------------------
        private static void EnsureBasicItems(PosClientDbContext db, DateTime now)
        {
            UpsertItem(db, new Item
            {
                Sku = "SKU-1001",
                Name = "Biscuit Pack",
                Barcode = "1001",
                Price = 150m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 0m,
                DefaultDiscountAmt = 0m
            });

            UpsertItem(db, new Item
            {
                Sku = "SKU-1002",
                Name = "Mineral Water 500ml",
                Barcode = "1002",
                Price = 60m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 0m,
                DefaultDiscountAmt = 0m
            });

            UpsertItem(db, new Item
            {
                Sku = "SKU-1003",
                Name = "Chocolate Bar",
                Barcode = "1003",
                Price = 220m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 5m,
                DefaultDiscountAmt = null
            });

            db.SaveChanges();
        }

        // -------------------------------------------------
        // 2) Product + Variants (Items as variant SKUs)
        // -------------------------------------------------
        private static void EnsureProductWithVariants(PosClientDbContext db, DateTime now)
        {
            const string productName = "Basic T-Shirt";
            const string brandName = "One Dollar";
            const string sizeName = "Size";
            const string colorName = "Color";

            // ensure Brand row
            var brand = db.Brands.FirstOrDefault(b => b.Name == brandName);
            if (brand == null)
            {
                brand = new Brand { Name = brandName, IsActive = true };
                db.Brands.Add(brand);
                db.SaveChanges();
            }

            // product lookup by name + BrandId (NOT Brand string)
            var product = db.Products.FirstOrDefault(p => p.Name == productName && p.BrandId == brand.Id);
            if (product == null)
            {
                product = new Product
                {
                    Name = productName,
                    BrandId = brand.Id,   // << use FK
                    IsActive = true,
                    UpdatedAt = now
                };
                db.Products.Add(product);
                db.SaveChanges();
            }

            var variants = new[]
            {
        new { S="S", C="Red",   Sku="TSHIRT-S-RED", Barcode="900001", Price=1200m },
        new { S="M", C="Red",   Sku="TSHIRT-M-RED", Barcode="900002", Price=1200m },
        new { S="L", C="Red",   Sku="TSHIRT-L-RED", Barcode="900003", Price=1200m },
        new { S="S", C="Black", Sku="TSHIRT-S-BLK", Barcode="900004", Price=1200m },
        new { S="M", C="Black", Sku="TSHIRT-M-BLK", Barcode="900005", Price=1200m },
        new { S="L", C="Black", Sku="TSHIRT-L-BLK", Barcode="900006", Price=1200m },
    };

            foreach (var v in variants)
            {
                UpsertItem(db, new Item
                {
                    ProductId = product.Id,
                    Sku = v.Sku,
                    Name = productName,
                    Barcode = v.Barcode,
                    Price = v.Price,
                    UpdatedAt = now,

                    TaxCode = "STD",
                    DefaultTaxRatePct = 0m,
                    TaxInclusive = false,
                    DefaultDiscountPct = 0m,
                    DefaultDiscountAmt = 0m,

                    Variant1Name = sizeName,
                    Variant1Value = v.S,
                    Variant2Name = colorName,
                    Variant2Value = v.C
                    // BrandId/CategoryId optional on Items (can inherit from Product)
                });
            }

            db.SaveChanges();
        }

        private static void EnsureProductWithVariants_Jeans(PosClientDbContext db, DateTime now)
        {
            const string productName = "Jeans";
            const string brandName = "Denim Co";
            const string sizeName = "Waist";
            const string colorName = "Color";

            // Ensure brand
            var brand = db.Brands.FirstOrDefault(b => b.Name == brandName);
            if (brand == null)
            {
                brand = new Brand { Name = brandName, IsActive = true };
                db.Brands.Add(brand);
                db.SaveChanges();
            }

            // Prefer existing row by NAME; upgrade BrandId if missing
            var product = db.Products.FirstOrDefault(p => p.Name == productName);
            if (product == null)
            {
                product = new Product
                {
                    Name = productName,
                    BrandId = brand.Id,
                    IsActive = true,
                    UpdatedAt = now
                };
                db.Products.Add(product);
                db.SaveChanges();
            }
            else
            {
                if (!product.BrandId.HasValue) product.BrandId = brand.Id;
                product.UpdatedAt = now;
                db.SaveChanges();
            }

            var variants = new[]
            {
        new { W="30", C="Blue",  Sku="JEANS-30-BLU", Barcode="910001", Price=3500m },
        new { W="32", C="Blue",  Sku="JEANS-32-BLU", Barcode="910002", Price=3500m },
        new { W="34", C="Blue",  Sku="JEANS-34-BLU", Barcode="910003", Price=3500m },
        new { W="30", C="Black", Sku="JEANS-30-BLK", Barcode="910004", Price=3500m },
        new { W="32", C="Black", Sku="JEANS-32-BLK", Barcode="910005", Price=3500m },
        new { W="34", C="Black", Sku="JEANS-34-BLK", Barcode="910006", Price=3500m },
    };

            foreach (var v in variants)
            {
                UpsertItem(db, new Item
                {
                    ProductId = product.Id,
                    Sku = v.Sku,
                    Name = productName,
                    Barcode = v.Barcode,
                    Price = v.Price,
                    UpdatedAt = now,

                    TaxCode = "STD",
                    DefaultTaxRatePct = 0m,
                    TaxInclusive = false,
                    DefaultDiscountPct = 0m,
                    DefaultDiscountAmt = 0m,

                    Variant1Name = sizeName,
                    Variant1Value = v.W,
                    Variant2Name = colorName,
                    Variant2Value = v.C
                });
            }

            db.SaveChanges();
        }


        // ---------------------------------------------------------
        // 3) Opening Stock — add only for items that lack any stock
        // ---------------------------------------------------------
        private static void EnsureOpeningStock(PosClientDbContext db, int outletId, int openingQty, DateTime now)
        {
            var itemIdsWithStock = db.StockEntries
                .Where(se => se.OutletId == outletId)
                .Select(se => se.ItemId)
                .Distinct()
                .ToHashSet();

            var itemsNeedingStock = db.Items
                .Where(i => !itemIdsWithStock.Contains(i.Id))
                .Select(i => i.Id)
                .ToList();

            if (itemsNeedingStock.Count == 0) return;

            foreach (var itemId in itemsNeedingStock)
            {
                db.StockEntries.Add(new StockEntry
                {
                    OutletId = outletId,
                    ItemId = itemId,
                    QtyChange = openingQty,
                    RefType = "Adjust",
                    RefId = null,
                    Ts = now
                });
            }

            db.SaveChanges();
        }

        private static void EnsureOutlets(PosClientDbContext db)
        {
            if (!db.Outlets.Any())
            {
                var outlet = new Outlet { Name = "Main Outlet", Code = "MAIN", IsActive = true };
                db.Outlets.Add(outlet);
                db.SaveChanges();

                // (optional) a counter for the main outlet
                db.CounterSequences.Add(new CounterSequence { CounterId = 1 });
                db.SaveChanges();
            }
        }

        // ----------------------------------------
        // Helper: Upsert Item by unique SKU
        // ----------------------------------------
        private static void UpsertItem(PosClientDbContext db, Item incoming)
        {
            var existing = db.Items.FirstOrDefault(i => i.Sku == incoming.Sku);
            if (existing == null)
            {
                db.Items.Add(incoming);
                return;
            }

            existing.Name = incoming.Name;
            existing.Barcode = incoming.Barcode;
            existing.Price = incoming.Price;
            existing.UpdatedAt = incoming.UpdatedAt;

            existing.TaxCode = incoming.TaxCode;
            existing.DefaultTaxRatePct = incoming.DefaultTaxRatePct;
            existing.TaxInclusive = incoming.TaxInclusive;
            existing.DefaultDiscountPct = incoming.DefaultDiscountPct;
            existing.DefaultDiscountAmt = incoming.DefaultDiscountAmt;

            existing.ProductId = incoming.ProductId;
            existing.Variant1Name = incoming.Variant1Name;
            existing.Variant1Value = incoming.Variant1Value;
            existing.Variant2Name = incoming.Variant2Name;
            existing.Variant2Value = incoming.Variant2Value;
        }

        private static void EnsureSuppliers(PosClientDbContext db)
        {
            // Do we already have any supplier party?
            if (db.PartyRoles.Any(r => r.Role == RoleType.Supplier))
                return;

            // Create the Party row
            var party = new Party
            {
                Name = "Default Supplier",
                IsActive = true,
                IsSharedAcrossOutlets = true,     // shared supplier; no PartyOutlet rows required
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Parties.Add(party);
            db.SaveChanges();

            // Attach the Supplier role
            db.PartyRoles.Add(new PartyRole
            {
                PartyId = party.Id,
                Role = RoleType.Supplier,
                CreatedAtUtc = DateTime.UtcNow
            });

            db.SaveChanges();
        }

    }
}
