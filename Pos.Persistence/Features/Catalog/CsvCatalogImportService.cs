// Pos.Persistence/Features/Catalog/CsvCatalogImportService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Models.Catalog;
using Pos.Domain.Services;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Persistence.Features.Catalog
{
    public interface ICsvCatalogImportService
    {
        Task<CsvImportResult> ParseAndValidateAsync(string csvPath, CancellationToken ct = default);
        Task<int> SaveAsync(IEnumerable<CsvImportRow> rows, bool createMissingBrandCategory, CancellationToken ct = default);
    }

    public sealed class CsvCatalogImportService : ICsvCatalogImportService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox;
        private readonly ICatalogService _catalog;
        private readonly IBrandService _brands;
        private readonly ICategoryService _categories;

        public CsvCatalogImportService(
            IDbContextFactory<PosClientDbContext> dbf,
            IOutboxWriter outbox,
            ICatalogService catalog,
            IBrandService brands,
            ICategoryService categories)
        {
            _dbf = dbf; _outbox = outbox; _catalog = catalog;
            _brands = brands; _categories = categories;
        }

        // --- Simple CSV reader (no extra NuGet) with quoted fields support ---
        private static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            bool inQ = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (c == ',' && !inQ) { res.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            res.Add(cur.ToString());
            return res;
        }

        private static decimal ParseDec(string? s)
            => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static bool ParseBool(string? s)
            => bool.TryParse(s, out var b) ? b : (s?.Trim() == "1");

        public async Task<CsvImportResult> ParseAndValidateAsync(string csvPath, CancellationToken ct = default)
        {
            var result = new CsvImportResult();
            if (!File.Exists(csvPath)) throw new FileNotFoundException(csvPath);

            string[] lines = File.ReadAllLines(csvPath);
            if (lines.Length == 0) return result;

            var header = SplitCsv(lines[0]).Select(h => h.Trim()).ToList();
            // Expected headers (order can be any, we map by name)
            string[] expected = {
                "Type","ProductName","ItemName","SKU","Barcode","Price",
                "TaxCode","TaxRatePct","TaxInclusive","Brand","Category",
                "Variant1Name","Variant1Value","Variant2Name","Variant2Value"
            };
            foreach (var h in expected)
                if (!header.Any(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Missing column: {h}");

            int Col(string name)
                => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = SplitCsv(lines[i]);
                // normalize length
                while (cols.Count < header.Count) cols.Add("");
                var row = new CsvImportRow
                {
                    RowNo = i + 1,
                    Type = cols[Col("Type")].Trim(),
                    ProductName = cols[Col("ProductName")].Trim(),
                    ItemName = cols[Col("ItemName")].Trim(),
                    SKU = cols[Col("SKU")].Trim(),
                    Barcode = cols[Col("Barcode")].Trim(),
                    Price = ParseDec(cols[Col("Price")]),
                    TaxCode = cols[Col("TaxCode")].Trim(),
                    TaxRatePct = ParseDec(cols[Col("TaxRatePct")]),
                    TaxInclusive = ParseBool(cols[Col("TaxInclusive")]),
                    Brand = cols[Col("Brand")].Trim(),
                    Category = cols[Col("Category")].Trim(),
                    Variant1Name = cols[Col("Variant1Name")].Trim(),
                    Variant1Value = cols[Col("Variant1Value")].Trim(),
                    Variant2Name = cols[Col("Variant2Name")].Trim(),
                    Variant2Value = cols[Col("Variant2Value")].Trim(),
                };

                ValidateRow(row, result);
            }

            // Check duplicate SKU / barcode within the file
            var skuGroups = result.Rows.Where(r => r.Status != "Error" && !string.IsNullOrEmpty(r.SKU))
                                       .GroupBy(r => r.SKU, StringComparer.OrdinalIgnoreCase)
                                       .Where(g => g.Count() > 1);
            foreach (var g in skuGroups)
                foreach (var r in g) SetError(r, $"Duplicate SKU in file: {g.Key}");

            var barcodeGroups = result.Rows.Where(r => r.Status != "Error" && !string.IsNullOrEmpty(r.Barcode))
                                           .GroupBy(r => r.Barcode, StringComparer.OrdinalIgnoreCase)
                                           .Where(g => g.Count() > 1);
            foreach (var g in barcodeGroups)
                foreach (var r in g) SetError(r, $"Duplicate Barcode in file: {g.Key}");

            // Check conflicts against DB
            await using var db = await _dbf.CreateDbContextAsync(ct);
            var fileSkus = result.Rows.Select(r => r.SKU).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var fileBarcodes = result.Rows.Select(r => r.Barcode).Where(s => !string.IsNullOrEmpty(s)).ToList();

            var existingSkuSet = await db.Items
                .Where(i => fileSkus.Contains(i.Sku))
                .Select(i => i.Sku)
                .ToHashSetAsync(ct);

            var existingBarSet = await db.ItemBarcodes
                .Where(b => fileBarcodes.Contains(b.Code))
                .Select(b => b.Code)
                .ToHashSetAsync(ct);

            foreach (var r in result.Rows.Where(x => x.Status != "Error"))
            {
                if (!string.IsNullOrEmpty(r.SKU) && existingSkuSet.Contains(r.SKU))
                    SetError(r, $"SKU already exists: {r.SKU}");
                if (!string.IsNullOrEmpty(r.Barcode) && existingBarSet.Contains(r.Barcode))
                    SetError(r, $"Barcode already exists: {r.Barcode}");
            }

            result.ValidCount = result.Rows.Count(x => x.Status == "Valid");
            result.ErrorCount = result.Rows.Count(x => x.Status == "Error");
            return result;

            static void ValidateRow(CsvImportRow r, CsvImportResult agg)
            {
                if (!string.Equals(r.Type, "Standalone", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.Type, "Variant", StringComparison.OrdinalIgnoreCase))
                { SetError(r, "Type must be Standalone or Variant"); agg.Rows.Add(r); return; }

                if (string.IsNullOrWhiteSpace(r.ItemName)) { SetError(r, "ItemName required"); agg.Rows.Add(r); return; }
                if (string.IsNullOrWhiteSpace(r.SKU)) { SetError(r, "SKU required"); agg.Rows.Add(r); return; }
                if (r.Price < 0) { SetError(r, "Price must be >= 0"); agg.Rows.Add(r); return; }
                if (r.TaxRatePct < 0) { SetError(r, "TaxRatePct must be >= 0"); agg.Rows.Add(r); return; }

                if (string.Equals(r.Type, "Variant", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(r.ProductName))
                    { SetError(r, "ProductName required for Variant rows"); agg.Rows.Add(r); return; }
                }
                r.Status = "Valid";
                agg.Rows.Add(r);
            }

            static void SetError(CsvImportRow r, string msg) { r.Status = "Error"; r.Error = msg; }
        }

        public async Task<int> SaveAsync(IEnumerable<CsvImportRow> rows, bool createMissingBrandCategory, CancellationToken ct = default)
        {
            int saved = 0;
            await using var db = await _dbf.CreateDbContextAsync(ct);
            using var trx = await db.Database.BeginTransactionAsync(ct);

            // Cache of product name -> id
            var productIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in rows.Where(x => x.Status == "Valid"))
            {
                // brand/category resolve (create if allowed)
                int? brandId = null, categoryId = null;
                if (!string.IsNullOrWhiteSpace(r.Brand))
                {
                    var b = await _brands.GetOrCreateAsync(r.Brand!, createIfMissing: createMissingBrandCategory, ct);
                    brandId = b?.Id;
                }
                if (!string.IsNullOrWhiteSpace(r.Category))
                {
                    var c = await _categories.GetOrCreateAsync(r.Category!, createIfMissing: createMissingBrandCategory, ct);
                    categoryId = c?.Id;
                }

                if (string.Equals(r.Type, "Standalone", StringComparison.OrdinalIgnoreCase))
                {
                    // Item without product
                    var item = new Item
                    {
                        Sku = r.SKU,
                        Name = r.ItemName,
                        Price = r.Price,
                        TaxCode = r.TaxCode,
                        DefaultTaxRatePct = r.TaxRatePct,
                        TaxInclusive = r.TaxInclusive,
                        BrandId = brandId,
                        CategoryId = categoryId,
                        IsActive = true,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Items.Add(item);
                    await db.SaveChangesAsync(ct);
                    if (!string.IsNullOrWhiteSpace(r.Barcode))
                    {
                        db.ItemBarcodes.Add(new ItemBarcode { ItemId = item.Id, Code = r.Barcode! });
                        await db.SaveChangesAsync(ct);
                    }

                    await _outbox.EnqueueUpsertAsync(db, item, ct);
                    await db.SaveChangesAsync(ct);
                    r.Status = "Saved"; saved++;
                }
                else
                {
                    // Ensure product exists
                    int productId;
                    if (!productIdByName.TryGetValue(r.ProductName!, out productId))
                    {
                        var existing = await db.Products.Where(p => p.Name == r.ProductName!).Select(p => new { p.Id }).FirstOrDefaultAsync(ct);
                        if (existing is null)
                        {
                            var p = new Product
                            {
                                Name = r.ProductName!,
                                BrandId = brandId,
                                CategoryId = categoryId,
                                IsActive = true,
                                UpdatedAt = DateTime.UtcNow
                            };
                            db.Products.Add(p);
                            await db.SaveChangesAsync(ct);
                            await _outbox.EnqueueUpsertAsync(db, p, ct);
                            await db.SaveChangesAsync(ct);
                            productId = p.Id;
                        }
                        else productId = existing.Id;

                        productIdByName[r.ProductName!] = productId;
                    }

                    var item = new Item
                    {
                        ProductId = productId,
                        Sku = r.SKU,
                        Name = r.ItemName,
                        Price = r.Price,
                        TaxCode = r.TaxCode,
                        DefaultTaxRatePct = r.TaxRatePct,
                        TaxInclusive = r.TaxInclusive,
                        Variant1Name = string.IsNullOrWhiteSpace(r.Variant1Name) ? null : r.Variant1Name,
                        Variant1Value = string.IsNullOrWhiteSpace(r.Variant1Value) ? null : r.Variant1Value,
                        Variant2Name = string.IsNullOrWhiteSpace(r.Variant2Name) ? null : r.Variant2Name,
                        Variant2Value = string.IsNullOrWhiteSpace(r.Variant2Value) ? null : r.Variant2Value,
                        BrandId = brandId,     // optional per item
                        CategoryId = categoryId,
                        IsActive = true,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Items.Add(item);
                    await db.SaveChangesAsync(ct);
                    if (!string.IsNullOrWhiteSpace(r.Barcode))
                    {
                        db.ItemBarcodes.Add(new ItemBarcode { ItemId = item.Id, Code = r.Barcode! });
                        await db.SaveChangesAsync(ct);
                    }

                    await _outbox.EnqueueUpsertAsync(db, item, ct);
                    await db.SaveChangesAsync(ct);
                    r.Status = "Saved"; saved++;
                }
            }

            await trx.CommitAsync(ct);
            return saved;
        }
    }
}
