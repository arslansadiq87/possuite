// Pos.Persistence/Seed.cs
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Persistence.Seeding;   // <-- add this


// REMOVE: using System.Security.Cryptography;
using Pos.Domain.Entities;                 // for User, Item, Product, StockEntry, etc.
// If you still have a Pos.Domain.UserRole, alias to avoid ambiguity:
//using UserRoleEnum = Pos.Domain.UserRole;

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

            EnsureUsers(db);
            // Ensure Chart of Accounts exists (idempotent)
            CoATemplateSeeder.SeedFromTemplateAsync(db).GetAwaiter().GetResult();

            EnsureSuppliers(db);

            // create locations first
            EnsureOutlets(db);
            EnsureWarehouse(db);

            // then items/products
            EnsureBasicItems(db, now);
            EnsureProductWithVariants(db, now);
            EnsureProductWithVariants_Jeans(db, now);
            
            
            // finally opening stock (uses new header+lines model)
            //EnsureOpeningStock_UsingHeader(db, outletId: 1, openingQty: 50m, now);
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
                Role = UserRole.Admin,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("1234"), // string hash
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                IsGlobalAdmin = true,
            });

            db.Users.Add(new User
            {
                Username = "ali",
                DisplayName = "Ali (Salesman)",
                Role = UserRole.Salesman,
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
                Price = 150m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 0m,
                DefaultDiscountAmt = 0m
            }, primaryBarcode: "1001");

            UpsertItem(db, new Item
            {
                Sku = "SKU-1002",
                Name = "Mineral Water 500ml",
                Price = 60m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 0m,
                DefaultDiscountAmt = 0m
            }, primaryBarcode: "1002");

            UpsertItem(db, new Item
            {
                Sku = "SKU-1003",
                Name = "Chocolate Bar",
                Price = 220m,
                UpdatedAt = now,
                TaxCode = "STD",
                DefaultTaxRatePct = 0m,
                TaxInclusive = false,
                DefaultDiscountPct = 5m,
                DefaultDiscountAmt = null
            }, primaryBarcode: "1003");

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
                }, primaryBarcode: v.Barcode);
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
                }, primaryBarcode: v.Barcode);

            }

            db.SaveChanges();
        }


        // ---------------------------------------------------------
        // 3) Opening Stock — add only for items that lack any stock
        // ---------------------------------------------------------
        private static void EnsureOpeningStock_UsingHeader(PosClientDbContext db, int outletId, decimal openingQty, DateTime now)
        {
            // If nothing to do, bail
            if (!db.Items.AsNoTracking().Any()) return;

            // Make sure outlet exists
            var outletExists = db.Outlets.AsNoTracking().Any(o => o.Id == outletId && o.IsActive);
            if (!outletExists) return;

            // Skip if we already have ANY Opening doc for this outlet (idempotent seed)
            var alreadySeeded = db.StockDocs
                .AsNoTracking()
                .Any(d => d.DocType == StockDocType.Opening
                       && d.LocationType == InventoryLocationType.Outlet
                       && d.LocationId == outletId);
            if (alreadySeeded) return;

            // Resolve an Admin user for audit fields
            var adminId = db.Users
                .Where(u => u.Role == UserRole.Admin && u.IsActive)
                .Select(u => u.Id)
                .FirstOrDefault();
            if (adminId == 0)
                adminId = db.Users.Select(u => u.Id).FirstOrDefault(); // fallback

            // Create header (Draft)
            var doc = new StockDoc
            {
                DocType = StockDocType.Opening,
                Status = StockDocStatus.Draft,
                LocationType = InventoryLocationType.Outlet,
                LocationId = outletId,
                EffectiveDateUtc = now,
                Note = "Seed: opening stock",
                CreatedByUserId = adminId
            };
            db.StockDocs.Add(doc);
            db.SaveChanges();

            // Prepare lines for all items that currently have no movements at this outlet
            var itemIdsWithAnyMovementHere = db.StockEntries
                .AsNoTracking()
                .Where(se => se.LocationType == InventoryLocationType.Outlet
                          && se.LocationId == outletId)
                .Select(se => se.ItemId)
                .Distinct()
                .ToHashSet();

            var items = db.Items.AsNoTracking().Select(i => new { i.Id, i.Price }).ToList();

            foreach (var it in items)
            {
                if (itemIdsWithAnyMovementHere.Contains(it.Id)) continue;

                db.StockEntries.Add(new StockEntry
                {
                    StockDocId = doc.Id,
                    ItemId = it.Id,
                    QtyChange = openingQty,          // decimal(18,4)
                    UnitCost = (it.Price <= 0 ? 0m : it.Price), // seed cost from item price (4 dp OK)
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = outletId,
                    RefType = "Opening",
                    RefId = doc.Id,
                    Ts = doc.EffectiveDateUtc,
                    Note = "Seed"
                });
            }

            db.SaveChanges();

            // Lock header (so it matches your rule “admin locks it”)
            doc.Status = StockDocStatus.Locked;
            doc.LockedByUserId = adminId;
            doc.LockedAtUtc = DateTime.UtcNow;
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
        // Helper: Upsert Item by unique SKU (+ optional primary barcode)
        // ----------------------------------------
        private static void UpsertItem(PosClientDbContext db, Item incoming, string? primaryBarcode = null)
        {
            // Get existing by SKU (no need to Include; we will query barcodes separately)
            var existing = db.Items.FirstOrDefault(i => i.Sku == incoming.Sku);
            if (existing == null)
            {
                // Attach as new
                existing = incoming;
                db.Items.Add(existing);
            }
            else
            {
                // Update scalar fields
                existing.Name = incoming.Name;
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

            // Ensure primary barcode (in ItemBarcodes) if provided
            if (!string.IsNullOrWhiteSpace(primaryBarcode))
            {
                EnsurePrimaryBarcode(db, existing, primaryBarcode!.Trim());
            }
        }

        private static void EnsurePrimaryBarcode(
    PosClientDbContext db,
    Item item,
    string code,
    BarcodeSymbology symbology = BarcodeSymbology.Ean13,
    int qtyPerScan = 1,
    string? label = null)
        {
            // If this code already exists globally on another item, skip to avoid UNIQUE violation.
            var global = db.ItemBarcodes.AsNoTracking().FirstOrDefault(b => b.Code == code);
            if (global != null && global.ItemId != item.Id)
            {
                // You can log/trace here if desired; we silently skip to keep seeding idempotent.
                return;
            }

            // Find barcode on this item
            var onThisItem = db.ItemBarcodes.FirstOrDefault(b => b.ItemId == item.Id && b.Code == code);
            if (onThisItem == null)
            {
                // Create it as primary
                db.ItemBarcodes.Add(new ItemBarcode
                {
                    Item = item,
                    Code = code,
                    Symbology = symbology,
                    QuantityPerScan = Math.Max(1, qtyPerScan),
                    IsPrimary = true,
                    Label = string.IsNullOrWhiteSpace(label) ? null : label,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // Already present on this item—just mark as primary
                onThisItem.IsPrimary = true;
                onThisItem.UpdatedAt = DateTime.UtcNow;
            }

            // Make sure no other barcode of this item is primary
            var others = db.ItemBarcodes
                .Where(b => b.ItemId == item.Id && b.Code != code && b.IsPrimary)
                .ToList();

            foreach (var b in others)
            {
                b.IsPrimary = false;
                b.UpdatedAt = DateTime.UtcNow;
            }
        }


        //private static void EnsureSuppliers(PosClientDbContext db)
        //{
        //    // Do we already have any supplier party?
        //    if (db.PartyRoles.Any(r => r.Role == RoleType.Supplier))
        //        return;

        //    // Create the Party row
        //    var party = new Party
        //    {
        //        Name = "Default Supplier",
        //        IsActive = true,
        //        IsSharedAcrossOutlets = true,     // shared supplier; no PartyOutlet rows required
        //        CreatedAtUtc = DateTime.UtcNow
        //    };
        //    db.Parties.Add(party);
        //    db.SaveChanges();

        //    // Attach the Supplier role
        //    db.PartyRoles.Add(new PartyRole
        //    {
        //        PartyId = party.Id,
        //        Role = RoleType.Supplier,
        //        CreatedAtUtc = DateTime.UtcNow
        //    });

        //    db.SaveChanges();
        //}

        private static void EnsureSuppliers(PosClientDbContext db)
        {
            // Skip if any supplier already exists
            if (db.PartyRoles.Any(r => r.Role == RoleType.Supplier))
                return;

            // ---- Create Party ----
            var party = new Party
            {
                Name = "Default Supplier",
                IsActive = true,
                IsSharedAcrossOutlets = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Parties.Add(party);
            db.SaveChanges();

            // ---- Attach Supplier Role ----
            db.PartyRoles.Add(new PartyRole
            {
                PartyId = party.Id,
                Role = RoleType.Supplier,
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            // ---- Ensure COA Account under Supplier Header ----
            // ---- Ensure COA Account under Supplier Header ----
            var supplierHeader = db.Accounts.FirstOrDefault(a => a.Code == "61");

            if (supplierHeader == null)
            {
                // Try to find a generic Parties header (code "6") if template changed
                var partiesHeader = db.Accounts.FirstOrDefault(a => a.Code == "6");

                if (partiesHeader == null)
                {
                    // As a last resort, create a minimal Parties root and Suppliers header
                    partiesHeader = new Account
                    {
                        Code = "6",
                        Name = "Parties",
                        Type = AccountType.Parties,
                        NormalSide = NormalSide.Debit,
                        IsHeader = true,
                        AllowPosting = false
                    };
                    db.Accounts.Add(partiesHeader);
                    db.SaveChanges();
                }

                supplierHeader = new Account
                {
                    Code = "61",
                    Name = "Suppliers",
                    Type = AccountType.Parties,
                    NormalSide = NormalSide.Credit,
                    IsHeader = true,
                    AllowPosting = false,
                    ParentId = partiesHeader.Id
                };
                db.Accounts.Add(supplierHeader);
                db.SaveChanges();
            }


            // Generate next subcode safely
            var siblingCodes = db.Accounts
                .Where(a => a.ParentId == supplierHeader.Id)
                .Select(a => a.Code)
                .ToList();

            int max = 0;
            foreach (var c in siblingCodes)
            {
                var last = c?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            var next = max + 1;
            var newCode = $"{supplierHeader.Code}-{next:D3}";  // ✅ renamed from 'code' to 'newCode'

            var acc = new Account
            {
                Code = newCode,
                Name = party.Name,
                Type = AccountType.Parties,
                NormalSide = NormalSide.Credit,
                IsHeader = false,
                AllowPosting = true,
                ParentId = supplierHeader.Id
            };

            db.Accounts.Add(acc);
            db.SaveChanges();

            // Link account back to party
            party.AccountId = acc.Id;
            db.SaveChanges();
        }



        public static void EnsureWarehouse(PosClientDbContext db)
        {
            // Make sure DB is created/migrated before seeding (optional if you do this elsewhere)
            //db.Database.Migrate();

            // If no warehouses at all, create one
            if (!db.Warehouses.AsNoTracking().Any())
            {
                var wh = new Warehouse
                {
                    Code = "MAIN",              // ✅ REQUIRED
                    Name = "Main Warehouse",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    // RowVersion will be Array.Empty<byte>() automatically ✅
                };
                db.Warehouses.Add(wh);
                db.SaveChanges();
                return;
            }

            // If warehouses exist but none is active, flip the first to active (safety net)
            if (!db.Warehouses.AsNoTracking().Any(w => w.IsActive))
            {
                var first = db.Warehouses.OrderBy(w => w.Id).First();
                first.IsActive = true;
                first.UpdatedAtUtc = DateTime.UtcNow;
                db.SaveChanges();
            }
        }

    }
}
