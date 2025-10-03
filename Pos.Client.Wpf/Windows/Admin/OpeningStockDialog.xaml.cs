using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Features.OpeningStock;
using Pos.Persistence.Services;
using Microsoft.Win32;
using System.Text;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;



namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OpeningStockDialog : Window, INotifyPropertyChanged
    {
        private bool _dirty;
        public bool CanLock
        {
            get
            {
                if (StockDocId == 0) return false;
                if (Rows.Count == 0) return false;
                if (HasInvalid) return false;
                // all rows must be valid
                return Rows.All(r => !string.IsNullOrWhiteSpace(r.Sku) && r.Qty > 0 && r.UnitCost >= 0m);
            }
        }
        private void MarkDirty()
        {
            _dirty = true;
            Raise(nameof(FooterSummary));
            Raise(nameof(CanLock));
            Raise(nameof(HasInvalid));
            Raise(nameof(InvalidCount));
            Raise(nameof(LockHint));
        }

        private void MarkClean()
        {
            _dirty = false;
            Raise(nameof(CanLock));
            Raise(nameof(HasInvalid));
            Raise(nameof(InvalidCount));
            Raise(nameof(LockHint));
        }
        // --- Undo snapshot model ---
        private sealed class RowSnapshot
        {
            public string Sku { get; init; } = "";
            public string ItemName { get; init; } = "";
            public decimal Qty { get; init; }
            public decimal UnitCost { get; init; }
            public string? Note { get; init; }
        }

        public bool HasInvalid => InvalidCount > 0;
        public int InvalidCount => Rows.Count(r =>
            string.IsNullOrWhiteSpace(r.Sku) || r.Qty <= 0 || r.UnitCost < 0m);

        public string LockHint => HasInvalid
            ? $"{InvalidCount} invalid row(s) — fix before locking."
            : "Ready to lock.";


        private List<RowSnapshot>? _lastImportSnapshot;
        public bool HasUndo => _lastImportSnapshot != null;
        private void RaiseUndo() => Raise(nameof(HasUndo));

        private readonly IOpeningStockService _svc;
        private readonly IDbContextFactory<Pos.Persistence.PosClientDbContext> _dbf;
        private readonly CatalogService _catalog;

        private int _colSku = 0, _colItemName = 1, _colQty = 2, _colUnitCost = 3, _colSubtotal = 4, _colNote = 5;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public InventoryLocationType LocationType { get; }
        public int LocationId { get; }
        public string LocationDisplay { get; }
        private readonly AppState _state;

        private sealed class SuggestVM
        {
            public int ItemId { get; init; }
            public string Sku { get; init; } = "";
            public string Name { get; init; } = "";
            public string? Barcode { get; init; }
        }

        private sealed class CsvRow
        {
            public string Sku { get; init; } = "";
            public string? ItemName { get; init; }        // NEW: for human clarity (not a key)
            public decimal? Qty { get; init; }
            public decimal? UnitCost { get; init; }
            public string? Note { get; init; }
        }

        private static readonly string[] _hdrItemName = new[] { "itemname", "item name", "name", "product name", "productname" };
        private static readonly string[] _hdrSku = new[] { "sku" };
        private static readonly string[] _hdrQty = new[] { "qty", "quantity" };
        private static readonly string[] _hdrCost = new[] { "unitcost", "cost", "price" };
        private static readonly string[] _hdrNote = new[] { "note", "remarks", "remark" };


        private List<CsvRow> ParseCsvToLines(string csvText)
        {
            var lines = new List<CsvRow>();
            if (string.IsNullOrWhiteSpace(csvText)) return lines;

            using var sr = new StringReader(csvText);
            var first = sr.ReadLine();
            if (first == null) return lines;

            var headers = SplitCsvLine(first).Select(s => (s ?? "").Trim().ToLowerInvariant()).ToList();

            int idxSku = FindHeaderIndex(headers, _hdrSku);
            if (idxSku < 0) throw new InvalidOperationException("CSV must include a 'SKU' column.");

            int idxName = FindHeaderIndex(headers, _hdrItemName);  // NEW, optional
            int idxQty = FindHeaderIndex(headers, _hdrQty);
            int idxCost = FindHeaderIndex(headers, _hdrCost);
            int idxNote = FindHeaderIndex(headers, _hdrNote);

            string? line;
            var inv = CultureInfo.InvariantCulture;

            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = SplitCsvLine(line);

                string sku = GetCol(cols, idxSku)?.Trim() ?? "";
                if (sku.Length == 0) continue;

                string? name = GetCol(cols, idxName);

                decimal? q = null, c = null;
                var qs = GetCol(cols, idxQty)?.Trim();
                var cs = GetCol(cols, idxCost)?.Trim();

                if (!string.IsNullOrEmpty(qs) && decimal.TryParse(qs, NumberStyles.Any, inv, out var qv)) q = qv;
                if (!string.IsNullOrEmpty(cs) && decimal.TryParse(cs, NumberStyles.Any, inv, out var cv)) c = cv;

                var note = GetCol(cols, idxNote);

                lines.Add(new CsvRow { Sku = sku, ItemName = name?.Trim(), Qty = q, UnitCost = c, Note = note });
            }
            return lines;
        }


        private static int FindHeaderIndex(List<string> headers, string[] candidates)
        {
            for (int i = 0; i < headers.Count; i++)
                if (candidates.Contains(headers[i], StringComparer.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string? GetCol(List<string> cols, int idx) => idx >= 0 && idx < cols.Count ? cols[idx] : null;

        // Basic CSV split supporting commas and quoted fields
        private static List<string> SplitCsvLine(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (inQuotes)
                {
                    if (ch == '\"')
                    {
                        // double quote inside quoted string -> append a single "
                        if (i + 1 < line.Length && line[i + 1] == '\"')
                        {
                            sb.Append('\"'); i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else sb.Append(ch);
                }
                else
                {
                    if (ch == '\"') inQuotes = true;
                    else if (ch == ',') { res.Add(sb.ToString()); sb.Clear(); }
                    else sb.Append(ch);
                }
            }
            res.Add(sb.ToString());
            return res;
        }



        // Header / doc
        public int StockDocId { get; private set; } // 0 until draft created
        public DateTime EffectiveDateLocal
        {
            get => _effectiveDateLocal;
            set { _effectiveDateLocal = value; Raise(nameof(EffectiveDateLocal)); }
        }
        private DateTime _effectiveDateLocal = DateTime.Now;

        public ObservableCollection<RowVM> Rows { get; } = new();

        public string FooterSummary
        {
            get
            {
                var cnt = Rows.Count;
                var qty = Rows.Sum(r => r.Qty);
                var val = Rows.Sum(r => r.Subtotal);
                return $"Rows: {cnt}   Total Qty: {qty:F4}   Total Value: {val:F4}";
            }
        }
        void HookRow(RowVM r)
        {
            r.PropertyChanged += (_, __2) => MarkDirty();
        }

        public OpeningStockDialog(InventoryLocationType locationType, int locationId, string locationDisplay)
        {
            InitializeComponent();
            DataContext = this;

            _svc = App.Services.GetRequiredService<IOpeningStockService>();
            _dbf = App.Services.GetRequiredService<IDbContextFactory<Pos.Persistence.PosClientDbContext>>();
            _catalog = App.Services.GetRequiredService<CatalogService>();
            _state = App.Services.GetRequiredService<AppState>();

            Grid.PreviewKeyDown += Grid_PreviewKeyDown;

            LocationType = locationType;
            LocationId = locationId;
            LocationDisplay = locationDisplay;

            // default effective date = today (local)
            EffectiveDateLocal = DateTime.Now;

            // start with one empty row
            Rows.CollectionChanged += (_, __) => Raise(nameof(FooterSummary));
            Rows.CollectionChanged += (_, __) => { Raise(nameof(CanLock)); _ = 0; };
            

            // focus search on load
            Loaded += (_, __) =>
            {
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
            };
        }

        private async Task EnsureDraftAsync()
        {
            if (StockDocId != 0) return;

            if (!IsCurrentUserAdmin())
                throw new InvalidOperationException("Only Admin can create Opening Stock.");

            var s = AppState.Current;
            var createdBy = (s.CurrentUser?.Id > 0) ? s.CurrentUser.Id : s.CurrentUserId;

            var req = new OpeningStockCreateRequest
            {
                LocationType = LocationType,
                LocationId = LocationId,
                EffectiveDateUtc = EffectiveDateLocal.ToUniversalTime(),
                CreatedByUserId = createdBy
            };

            var doc = await _svc.CreateDraftAsync(req);
            StockDocId = doc.Id;
        }

        // === Toolbar handlers ===

        private async void DownloadSampleCsv_Click(object sender, RoutedEventArgs e)
        {
            // Step 1: include products or blank?
            var includeProducts = MessageBox.Show(
                "Include products in the sample CSV?\n\nYes = Include products\nNo = Blank template",
                "Download Sample CSV",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Save sample CSV",
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"OpeningSample_{LocationType}_{LocationId}_{DateTime.Now:yyyyMMdd}.csv",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (!includeProducts)
                {
                    if (sfd.ShowDialog(this) != true) return;
                    var csv1 = BuildBlankSampleCsv(); // already implemented earlier
                    await File.WriteAllTextAsync(sfd.FileName, csv1, Encoding.UTF8);
                    MessageBox.Show("Sample CSV saved.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Step 2: all products vs only missing for this location?
                var onlyMissing = MessageBox.Show(
                    "Which products should be included?\n\nYes = Only items WITHOUT Opening for this location\nNo = All active items",
                    "Choose scope",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;

                // Adjust filename suffix for clarity
                var suffix = onlyMissing ? "OnlyMissing" : "All";
                sfd.FileName = $"OpeningSample_{suffix}_{LocationType}_{LocationId}_{DateTime.Now:yyyyMMdd}.csv";

                if (sfd.ShowDialog(this) != true) return;

                string csv = onlyMissing
                    ? await BuildSampleCsvOnlyMissingAsync()
                    : await BuildSampleCsvIncludingProductsAsync(); // you already have this

                await File.WriteAllTextAsync(sfd.FileName, csv, Encoding.UTF8);
                MessageBox.Show("Sample CSV saved.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CSV export error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<string> BuildSampleCsvOnlyMissingAsync()
        {
            // Items that DO NOT have any StockEntry with RefType="Opening" for this LocationType/Id
            await using var db = await _dbf.CreateDbContextAsync();

            var rows = await db.Items
                .AsNoTracking()
                .Where(i => !i.IsVoided && i.IsActive)
                .Where(i => !db.StockEntries.Any(se =>
                    se.ItemId == i.Id
                    && se.RefType == "Opening"
                    && se.LocationType == LocationType
                    && se.LocationId == LocationId))
                .Include(i => i.Product)
                .OrderBy(i => i.Sku)
                .Select(i => new
                {
                    i.Sku,
                    ItemName = ComposeDisplayName(
                        i.Product != null ? i.Product.Name : null,
                        i.Name,
                        i.Variant1Name, i.Variant1Value,
                        i.Variant2Name, i.Variant2Value)
                })
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("SKU,ItemName,Qty,UnitCost,Note");
            foreach (var r in rows)
            {
                sb.Append(EscapeCsv(r.Sku)); sb.Append(',');
                sb.Append(EscapeCsv(r.ItemName)); sb.Append(',');
                sb.Append(','); // Qty (user fills)
                sb.Append(','); // UnitCost (user fills)
                                // Note (blank)
                sb.AppendLine();
            }

            // If no rows, still return headers so the user sees a valid template.
            return sb.ToString();
        }


        private string BuildBlankSampleCsv()
        {
            // Include ItemName for clarity (kept optional at import)
            return "SKU,ItemName,Qty,UnitCost,Note" + Environment.NewLine;
        }

        private async Task<string> BuildSampleCsvIncludingProductsAsync()
        {
            await using var db = await _dbf.CreateDbContextAsync();
            var rows = await db.Items
                .AsNoTracking()
                .Where(i => !i.IsVoided && i.IsActive)
                .Include(i => i.Product)
                .OrderBy(i => i.Sku)
                .Select(i => new
                {
                    i.Sku,
                    ItemName = ComposeDisplayName(
                        i.Product != null ? i.Product.Name : null,
                        i.Name,
                        i.Variant1Name, i.Variant1Value,
                        i.Variant2Name, i.Variant2Value)
                })
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("SKU,ItemName,Qty,UnitCost,Note");
            foreach (var r in rows)
            {
                // Leave Qty/UnitCost empty so user fills them
                sb.Append(EscapeCsv(r.Sku)); sb.Append(',');
                sb.Append(EscapeCsv(r.ItemName)); sb.Append(',');
                sb.Append(','); // Qty
                sb.Append(','); // UnitCost
                                // Note (blank)
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // Compose a user-facing item display: "ProductName - v1 - v2" (falls back to Item.Name)
        private static string ComposeDisplayName(string? productName, string itemName,
            string? v1Name, string? v1Value, string? v2Name, string? v2Value)
        {
            // Prefer product name as base when present, append variant values
            var baseName = string.IsNullOrWhiteSpace(productName) ? itemName?.Trim() ?? "" : productName.Trim();
            var parts = new List<string> { baseName };

            // If the item name already contains details, we don't duplicate; otherwise append simple variant values
            void add(string? n, string? v)
            {
                if (!string.IsNullOrWhiteSpace(v)) parts.Add(v.Trim());
            }
            add(v1Name, v1Value);
            add(v2Name, v2Value);

            // Collapse consecutive spaces/dashes later if needed
            return string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string EscapeCsv(string? s)
        {
            s ??= "";
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }



        private async void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new OpenFileDialog
                {
                    Title = "Import Opening Stock CSV",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    Multiselect = false
                };
                if (ofd.ShowDialog(this) != true) return;

                EnsureDraftAsync();
                if (StockDocId == 0)
                {
                    MessageBox.Show("Cannot import without a draft. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var raw = await File.ReadAllTextAsync(ofd.FileName, Encoding.UTF8);
                var parsed = ParseCsvToLines(raw);

                // Service-side check (SKUs exist, quantities & costs sane)
                var validation = await _svc.ValidateLinesAsync(StockDocId, parsed.Select(p => new OpeningStockLineDto
                {
                    Sku = p.Sku,
                    Qty = p.Qty ?? 0m,
                    UnitCost = p.UnitCost ?? 0m,
                    Note = p.Note
                }));
                if (!validation.Ok)
                {
                    var msg = string.Join(Environment.NewLine, validation.Errors.Select(er =>
                        $"Row {(er.RowIndex is int i ? (i + 1).ToString() : "?")} - {er.Field}: {er.Message} {(string.IsNullOrEmpty(er.Sku) ? "" : $"[{er.Sku}]")}"));
                    MessageBox.Show("Import blocked due to errors:\n\n" + msg, "Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Ensure provided ItemName (if any) matches DB display name for the SKU
                var skus = parsed.Select(p => p.Sku).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                await using var db = await _dbf.CreateDbContextAsync();
                var nameMap = await db.Items.AsNoTracking()
                    .Include(i => i.Product)
                    .Where(i => skus.Contains(i.Sku))
                    .Select(i => new
                    {
                        i.Sku,
                        Display = ComposeDisplayName(
                            i.Product != null ? i.Product.Name : null,
                            i.Name, i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value)
                    })
                    .ToListAsync();

                var displayBySku = nameMap.ToDictionary(x => x.Sku, x => x.Display, StringComparer.OrdinalIgnoreCase);

                var mismatches = parsed
                    .Where(p => !string.IsNullOrWhiteSpace(p.ItemName))
                    .Where(p =>
                    {
                        var dbn = displayBySku.TryGetValue(p.Sku, out var d) ? d : null;
                        if (dbn == null) return false;
                        // compare trimmed, case-insensitive
                        return !string.Equals((p.ItemName ?? "").Trim(), dbn.Trim(), StringComparison.OrdinalIgnoreCase);
                    })
                    .Take(20)
                    .Select(p =>
                    {
                        var dbn = displayBySku[p.Sku];
                        return $"SKU {p.Sku}: CSV '{p.ItemName}' ≠ System '{dbn}'";
                    })
                    .ToList();

                if (mismatches.Count > 0)
                {
                    MessageBox.Show(
                        "Import blocked due to ItemName mismatch(es). This prevents wrong assignments.\n\n" +
                        string.Join(Environment.NewLine, mismatches) +
                        (parsed.Count > mismatches.Count ? "\n\n(Showing first 20 mismatches.)" : ""),
                        "Name mismatch",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // ✅ Snapshot BEFORE mutating the grid (enables one-level Undo)
                _lastImportSnapshot = TakeSnapshot();
                RaiseUndo();

                // Populate grid (overwrite when present; else add)
                int updated = 0, added = 0;
                foreach (var p in parsed)
                {
                    var sku = p.Sku;
                    var dbName = displayBySku.TryGetValue(sku, out var nm) ? nm : (p.ItemName ?? "");

                    var existing = Rows.FirstOrDefault(r => string.Equals(r.Sku, sku, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        if (p.Qty.HasValue) existing.Qty = p.Qty.Value;
                        if (p.UnitCost.HasValue) existing.UnitCost = p.UnitCost.Value;
                        if (p.Note != null) existing.Note = p.Note;
                        if (!string.IsNullOrWhiteSpace(dbName)) existing.ItemName = dbName;
                        updated++;
                    }
                    else
                    {
                        var vm = new RowVM
                        {
                            Sku = sku,
                            ItemName = dbName,
                            Qty = p.Qty ?? 0m,
                            UnitCost = p.UnitCost ?? 0m,
                            Note = p.Note ?? ""
                        };
                        Rows.Add(vm);
                        HookRow(vm); // ensure change tracking/dirty gating for new rows
                        added++;
                    }
                }

                Raise(nameof(FooterSummary));
                MarkDirty();

                MessageBox.Show($"Import complete.\nAdded: {added}\nUpdated: {updated}", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }





        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var sel = Grid.SelectedItem as RowVM;
            if (sel == null) return;
            Rows.Remove(sel);
            Raise(nameof(FooterSummary));
        }

        private async void SaveDraft_Click(object sender, RoutedEventArgs e)
        {
            if (Rows.Count == 0)
            {
                MessageBox.Show("No rows to save.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await EnsureDraftAsync(); // create header if needed
                if (StockDocId == 0) return;

                // Basic local validation
                foreach (var r in Rows)
                {
                    if (string.IsNullOrWhiteSpace(r.Sku))
                    {
                        MessageBox.Show("SKU is required on all rows.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (r.Qty <= 0)
                    {
                        MessageBox.Show("Qty must be > 0.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (r.UnitCost < 0)
                    {
                        MessageBox.Show("UnitCost cannot be negative.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var req = new OpeningStockUpsertRequest
                {
                    StockDocId = StockDocId,
                    ReplaceAll = ReplaceAllBox.IsChecked == true,   // ⬅️ uses the toolbar toggle
                    Lines = Rows.Select(r => new OpeningStockLineDto
                    {
                        Sku = r.Sku.Trim(),
                        Qty = r.Qty,
                        UnitCost = r.UnitCost,
                        Note = r.Note
                    }).ToList()
                };

                // Persist EffectiveDate change on the header (still Draft)
                await using (var db = await _dbf.CreateDbContextAsync())
                {
                    var doc = await db.StockDocs.FirstAsync(d => d.Id == StockDocId);
                    if (doc.Status != StockDocStatus.Draft)
                        throw new InvalidOperationException("Document is locked.");

                    var newUtc = EffectiveDateLocal.ToUniversalTime();
                    if (doc.EffectiveDateUtc != newUtc)
                    {
                        doc.EffectiveDateUtc = newUtc;
                        await db.SaveChangesAsync();
                    }
                }


                // server-side validation (SKU exist, etc.)
                var val = await _svc.ValidateLinesAsync(StockDocId, req.Lines);
                if (!val.Ok)
                {
                    var msg = string.Join(Environment.NewLine, val.Errors.Select(er =>
                        $"Row {er.RowIndex}: {er.Field} - {er.Message} {(string.IsNullOrEmpty(er.Sku) ? "" : $"[{er.Sku}]")}"));
                    MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _svc.UpsertLinesAsync(req);
                MarkClean();
                MessageBox.Show("Draft saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Raise(nameof(FooterSummary));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Lock_Click(object sender, RoutedEventArgs e)
        {
            if (Rows.Count == 0)
            {
                MessageBox.Show("No rows to lock. Save first.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (StockDocId == 0)
            {
                MessageBox.Show("Nothing to lock. Save first.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!IsCurrentUserAdmin())
            {
                MessageBox.Show("Only Admin can lock Opening Stock.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var s = AppState.Current;
                var adminId = (s.CurrentUser?.Id > 0) ? s.CurrentUser.Id : s.CurrentUserId;

                await _svc.LockAsync(StockDocId, adminId);
                MessageBox.Show("Opening Stock locked.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                MarkClean();

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lock failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsCurrentUserAdmin()
        {
            var s = AppState.Current;
            if (s.CurrentUser != null) return s.CurrentUser.Role == UserRole.Admin;
            return string.Equals(s.CurrentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // === Row VM ===
        public sealed class RowVM : INotifyPropertyChanged, IDataErrorInfo
        {
            private string _sku = "";
            private string _itemName = "";
            private decimal _qty;
            private decimal _unitCost;
            private string? _note;

            public string Sku { get => _sku; set { _sku = value; OnChanged(nameof(Sku)); OnChanged(nameof(Subtotal)); } }
            public string ItemName { get => _itemName; set { _itemName = value; OnChanged(nameof(ItemName)); } }
            public decimal Qty { get => _qty; set { _qty = value; OnChanged(nameof(Qty)); OnChanged(nameof(Subtotal)); } }
            public decimal UnitCost { get => _unitCost; set { _unitCost = value; OnChanged(nameof(UnitCost)); OnChanged(nameof(Subtotal)); } }
            public string? Note { get => _note; set { _note = value; OnChanged(nameof(Note)); } }

            public decimal Subtotal => Qty * UnitCost;

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            // === IDataErrorInfo ===
            public string Error => "";
            public string this[string columnName]
            {
                get
                {
                    if (columnName == nameof(Sku))
                        return string.IsNullOrWhiteSpace(Sku) ? "SKU is required" : "";
                    if (columnName == nameof(Qty))
                        return Qty <= 0 ? "Qty must be > 0" : "";
                    if (columnName == nameof(UnitCost))
                        return UnitCost < 0 ? "UnitCost cannot be negative" : "";
                    return "";
                }
            }
        }

        // === Add items to grid (search → add/increment) ===

        private async Task AddByIdAsync(int itemId, decimal qty)
        {
            await using var db = await _dbf.CreateDbContextAsync();
            var it = await db.Items.AsNoTracking().FirstAsync(x => x.Id == itemId);
            await AddOrIncrementRowAsync(it, qty);
        }

        private async Task AddOrIncrementRowAsync(Item item, decimal qty)
        {
            // already there? increment + dirty
            var existing = Rows.FirstOrDefault(r => string.Equals(r.Sku, item.Sku, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Qty += qty;
                MarkDirty();                       // <-- mark change
                FocusCellForRow(existing, _colQty);
                return;
            }

            // compute a friendly display name (fallback to item.Name)
            string display = item.Name;
            try
            {
                // If you want product + variant in the name:
                await using var db = await _dbf.CreateDbContextAsync();
                var i = await db.Items.AsNoTracking()
                    .Include(x => x.Product)
                    .FirstAsync(x => x.Id == item.Id);
                display = ComposeDisplayName(
                    i.Product != null ? i.Product.Name : null,
                    i.Name, i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value);
            }
            catch { /* ignore name enrich errors */ }

            // NEW ROW → add + hook + dirty
            var vm = new RowVM { Sku = item.Sku, ItemName = display, Qty = qty, UnitCost = 0m, Note = "" };
            Rows.Add(vm);
            HookRow(vm);                           // <-- hook here
            MarkDirty();                           // <-- and here
            FocusCellForRow(vm, _colQty);          // focus Qty as per flow
        }


        private void FocusCellForRow(RowVM row, int colIndex)
        {
            Grid.SelectedItem = row;
            Grid.ScrollIntoView(row);
            Grid.UpdateLayout();

            Grid.CurrentCell = new DataGridCellInfo(row, Grid.Columns[colIndex]);
            Grid.BeginEdit();

            Dispatcher.InvokeAsync(() =>
            {
                var cell = GetCell(Grid, Grid.Items.IndexOf(row), colIndex);
                if (cell != null)
                {
                    Grid.BeginEdit(); // ensure editor exists
                    cell.Focus();
                    var tb = FindVisualChild<TextBox>(cell);
                    if (tb != null)
                    {
                        Keyboard.Focus(tb);
                        tb.SelectAll();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private static DataGridCell? GetCell(DataGrid grid, int row, int column)
        {
            if (row < 0 || column < 0) return null;
            var rowContainer = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(row);
            if (rowContainer == null)
            {
                grid.ScrollIntoView(grid.Items[row]);
                rowContainer = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(row);
                if (rowContainer == null) return null;
            }
            var presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
            var cell = presenter?.ItemContainerGenerator.ContainerFromIndex(column) as DataGridCell;
            return cell;
        }

        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                var res = FindVisualChild<T>(child);
                if (res != null) return res;
            }
            return null;
        }

        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            var info = Grid.CurrentCell;               // struct (never null)
            var col = info.Column;                     // can be null
            var rowObj = info.Item as RowVM ?? Grid.CurrentItem as RowVM;

            if (col == null || rowObj == null) return;

            e.Handled = true;

            var colIndex = Grid.Columns.IndexOf(col);
            if (colIndex == _colQty)
                FocusCellForRow(rowObj, _colUnitCost);
            else if (colIndex == _colUnitCost)
                FocusCellForRow(rowObj, _colNote);
            else if (colIndex == _colNote)
            {
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
                ItemPopup.IsOpen = false;
            }
        }

        // === Purchase-style search (TextBox + Popup List) ===

        private async Task AddFromSearchAsync()
        {
            var term = (ItemSearchText.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
                return;
            }

            // 1) Try barcode resolve (QuantityPerScan honored)
            try
            {
                var (item, qtyPerScan) = await _catalog.ResolveScanAsync(term);
                await AddOrIncrementRowAsync(item, qtyPerScan);
                ItemSearchText.Clear();
                ItemPopup.IsOpen = false;
                return;
            }
            catch { /* not barcode, continue */ }

            // 2) Use selected suggestion if popup open
            if (ItemPopup.IsOpen && ItemList.SelectedItem is SuggestVM sPick)
            {
                await AddByIdAsync(sPick.ItemId, 1m);
                ItemSearchText.Clear();
                ItemPopup.IsOpen = false;
                return;
            }

            // 3) If exactly one suggestion, use it
            if (ItemList.Items.Count == 1 && ItemList.Items[0] is SuggestVM s1)
            {
                await AddByIdAsync(s1.ItemId, 1m);
                ItemSearchText.Clear();
                ItemPopup.IsOpen = false;
                return;
            }

            // 4) Try exact SKU match
            await using (var db = await _dbf.CreateDbContextAsync())
            {
                var it = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Sku == term);
                if (it != null)
                {
                    await AddOrIncrementRowAsync(it, 1m);
                    ItemSearchText.Clear();
                    ItemPopup.IsOpen = false;
                    return;
                }
            }

            // 5) No unique match — keep popup open for manual pick
            if (!ItemPopup.IsOpen && ItemList.Items.Count > 0)
            {
                ItemPopup.IsOpen = true;
                ItemList.Focus();
            }
            else
            {
                MessageBox.Show("No unique match. Select an item from the list or scan a barcode.",
                    "Not found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void ItemSearchText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var term = (ItemSearchText.Text ?? "").Trim();
            if (term.Length < 2)
            {
                ItemPopup.IsOpen = false;
                ItemList.ItemsSource = null;
                return;
            }

            try
            {
                await using var db = await _dbf.CreateDbContextAsync();
                var like = $"%{term}%";
                var list = await db.Items
                    .AsNoTracking()
                    .Where(i =>
                        EF.Functions.Like(i.Sku, like) ||
                        EF.Functions.Like(i.Name, like) ||
                        i.Barcodes.Any(b => EF.Functions.Like(b.Code, like)))
                    .OrderBy(i => i.Sku)
                    .Take(30)
                    .Select(i => new SuggestVM
                    {
                        ItemId = i.Id,
                        Sku = i.Sku,
                        Name = i.Name,
                        Barcode = i.Barcodes
                            .OrderByDescending(b => b.IsPrimary)
                            .Select(b => b.Code)
                            .FirstOrDefault()
                    })
                    .ToListAsync();

                ItemList.ItemsSource = list;
                ItemPopup.IsOpen = list.Count > 0;
                if (list.Count > 0) ItemList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Search error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ItemSearchText_KeyDown(object sender, KeyEventArgs e)
        {
            // Open popup with Down if results exist
            if (!ItemPopup.IsOpen && e.Key == Key.Down && ItemList.Items.Count > 0)
            {
                ItemPopup.IsOpen = true;
                ItemList.SelectedIndex = Math.Max(0, ItemList.SelectedIndex);
                ItemList.Focus();
                e.Handled = true;
                return;
            }

            // Arrow keys: move selection in list when popup is open
            if (ItemPopup.IsOpen && (e.Key == Key.Down || e.Key == Key.Up))
            {
                if (ItemList.Items.Count == 0) return;
                int idx = ItemList.SelectedIndex;
                if (e.Key == Key.Down) idx = Math.Min(ItemList.Items.Count - 1, idx + 1);
                else idx = Math.Max(0, idx - 1);
                ItemList.SelectedIndex = idx;
                ItemList.ScrollIntoView(ItemList.SelectedItem);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && ItemPopup.IsOpen)
            {
                ItemPopup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await AddFromSearchAsync();
            }
        }

        private async void ItemList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ItemList.SelectedItem is SuggestVM s)
            {
                e.Handled = true;
                await AddByIdAsync(s.ItemId, 1m);
                ItemSearchText.Clear();
                ItemPopup.IsOpen = false;
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ItemPopup.IsOpen = false;
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
            }
        }

        private async void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is SuggestVM s)
            {
                await AddByIdAsync(s.ItemId, 1m);
                ItemSearchText.Clear();
                ItemPopup.IsOpen = false;
            }
        }

        private async void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            await AddFromSearchAsync();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadExistingDraftIfAnyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Load draft error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadExistingDraftIfAnyAsync()
        {
            await using var db = await _dbf.CreateDbContextAsync();

            // latest draft for this location
            var draft = await db.StockDocs
                .AsNoTracking()
                .Where(d => d.DocType == StockDocType.Opening
                         && d.Status == StockDocStatus.Draft
                         && d.LocationType == LocationType
                         && d.LocationId == LocationId)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync();

            if (draft == null) return;

            // load lines + display names
            var lines = await db.StockEntries
                .AsNoTracking()
                .Where(se => se.StockDocId == draft.Id)
                .Select(se => new { se.ItemId, se.QtyChange, se.UnitCost, se.Note })
                .ToListAsync();

            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var names = await db.Items
                .AsNoTracking()
                .Include(i => i.Product)
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Sku,
                    Display = ComposeDisplayName(
                        i.Product != null ? i.Product.Name : null,
                        i.Name, i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value)
                })
                .ToListAsync();

            var byId = names.ToDictionary(x => x.Id, x => x, EqualityComparer<int>.Default);

            Rows.Clear();
            foreach (var l in lines)
            {
                var meta = byId[l.ItemId];
                var row = new RowVM
                {
                    Sku = meta.Sku,
                    ItemName = meta.Display,
                    Qty = l.QtyChange,
                    UnitCost = l.UnitCost,
                    Note = l.Note ?? ""
                };
                Rows.Add(row);
                HookRow(row);
            }

            StockDocId = draft.Id;
            EffectiveDateLocal = draft.EffectiveDateUtc.ToLocalTime();

            MarkClean();
            Raise(nameof(FooterSummary));
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_dirty)
            {
                var r = MessageBox.Show(
                    "You have unsaved changes. Do you still want to close?",
                    "Unsaved changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (r == MessageBoxResult.No) e.Cancel = true;
            }
        }

        private List<RowSnapshot> TakeSnapshot()
        {
            return Rows.Select(r => new RowSnapshot
            {
                Sku = r.Sku,
                ItemName = r.ItemName,
                Qty = r.Qty,
                UnitCost = r.UnitCost,
                Note = r.Note
            }).ToList();
        }

        private void RestoreSnapshot(List<RowSnapshot> snap)
        {
            Rows.Clear();
            foreach (var s in snap)
            {
                var vm = new RowVM
                {
                    Sku = s.Sku,
                    ItemName = s.ItemName,
                    Qty = s.Qty,
                    UnitCost = s.UnitCost,
                    Note = s.Note ?? ""
                };
                Rows.Add(vm);
                HookRow(vm);   // you already have HookRow to track dirty
            }
            MarkDirty();
            Raise(nameof(FooterSummary));
        }

        private void ExportGrid_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sfd = new SaveFileDialog
                {
                    Title = "Export grid to CSV",
                    Filter = "CSV files (*.csv)|*.csv",
                    FileName = $"OpeningGrid_{LocationType}_{LocationId}_{DateTime.Now:yyyyMMdd}.csv",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (sfd.ShowDialog(this) != true) return;

                var inv = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine("SKU,ItemName,Qty,UnitCost,Note");

                foreach (var r in Rows)
                {
                    string esc(string? x) => EscapeCsv(x ?? "");
                    sb.Append(esc(r.Sku)); sb.Append(',');
                    sb.Append(esc(r.ItemName)); sb.Append(',');
                    sb.Append(r.Qty.ToString("F4", inv)); sb.Append(',');
                    sb.Append(r.UnitCost.ToString("F4", inv)); sb.Append(',');
                    sb.Append(esc(r.Note));
                    sb.AppendLine();
                }

                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Grid exported.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UndoImport_Click(object sender, RoutedEventArgs e)
        {
            if (_lastImportSnapshot == null) return;

            var r = MessageBox.Show(
                "Undo the last CSV import and restore the previous grid state?",
                "Undo last import",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r != MessageBoxResult.Yes) return;

            var snap = _lastImportSnapshot;
            _lastImportSnapshot = null;
            RaiseUndo();
            RestoreSnapshot(snap!);
        }

        private void FixFirstInvalid_Click(object sender, RoutedEventArgs e)
        {
            var row = Rows.FirstOrDefault(r =>
                string.IsNullOrWhiteSpace(r.Sku) || r.Qty <= 0 || r.UnitCost < 0m);
            if (row == null) return;

            int targetCol = string.IsNullOrWhiteSpace(row.Sku) ? _colSku :
                            (row.Qty <= 0 ? _colQty :
                            (row.UnitCost < 0m ? _colUnitCost : _colNote));

            FocusCellForRow(row, targetCol);
        }

        private async void OpenDraft_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpeningStockPickDialog(_dbf, LocationType, LocationId, OpeningStockPickDialog.Mode.Drafts)
            { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedDocId is int id)
            {
                await LoadSpecificDocAsync(id);
            }
        }

        private async void CloneFromLocked_Click(object sender, RoutedEventArgs e)
        {

            var user = _state.CurrentUser;
            if (user?.Role != UserRole.Admin)
            {
                MessageBox.Show("Only Admin can create Opening Stock.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var pick = new OpeningStockPickDialog(_dbf, LocationType, LocationId, OpeningStockPickDialog.Mode.Locked)
            { Owner = this };
            if (pick.ShowDialog() != true || pick.SelectedDocId is not int sourceId) return;

            try
            {
                // Load source doc + lines
                await using var db = await _dbf.CreateDbContextAsync();
                var src = await db.StockDocs.AsNoTracking().FirstAsync(d => d.Id == sourceId);
                var lines = await db.StockEntries.AsNoTracking()
                    .Where(se => se.StockDocId == sourceId)
                    .Select(se => new { se.ItemId, se.QtyChange, se.UnitCost, se.Note })
                    .ToListAsync();

                var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
                var skus = await db.Items.AsNoTracking()
                    .Where(i => itemIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.Sku })
                    .ToListAsync();
                var skuById = skus.ToDictionary(x => x.Id, x => x.Sku);

                // 1) Create new draft with same effective date
                var draft = await _svc.CreateDraftAsync(new OpeningStockCreateRequest
                {
                    LocationType = src.LocationType,
                    LocationId = src.LocationId,
                    EffectiveDateUtc = src.EffectiveDateUtc,
                    CreatedByUserId = user.Id,
                    Note = $"Cloned from #{src.Id}"
                });

                // 2) Upsert lines using SKUs
                var up = new OpeningStockUpsertRequest
                {
                    StockDocId = draft.Id,
                    ReplaceAll = true,
                    Lines = lines.Select(l => new OpeningStockLineDto
                    {
                        Sku = skuById[l.ItemId],
                        Qty = l.QtyChange,
                        UnitCost = l.UnitCost,
                        Note = l.Note
                    }).ToList()
                };
                await _svc.UpsertLinesAsync(up);

                // 3) Load new draft into UI
                await LoadSpecificDocAsync(draft.Id);
                MessageBox.Show($"Cloned from locked document #{src.Id} into new draft #{draft.Id}.",
                    "Cloned", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clone failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadSpecificDocAsync(int docId)
        {
            await using var db = await _dbf.CreateDbContextAsync();

            var doc = await db.StockDocs.AsNoTracking().FirstAsync(d => d.Id == docId);
            var lines = await db.StockEntries.AsNoTracking()
                .Where(se => se.StockDocId == docId)
                .Select(se => new { se.ItemId, se.QtyChange, se.UnitCost, se.Note })
                .ToListAsync();

            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var names = await db.Items
                .AsNoTracking()
                .Include(i => i.Product)
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => new
                {
                    i.Id,
                    i.Sku,
                    Display = ComposeDisplayName(
                        i.Product != null ? i.Product.Name : null,
                        i.Name, i.Variant1Name, i.Variant1Value, i.Variant2Name, i.Variant2Value)
                })
                .ToListAsync();

            var byId = names.ToDictionary(x => x.Id, x => x);

            Rows.Clear();
            foreach (var l in lines)
            {
                var meta = byId[l.ItemId];
                var row = new RowVM
                {
                    Sku = meta.Sku,
                    ItemName = meta.Display,
                    Qty = l.QtyChange,
                    UnitCost = l.UnitCost,
                    Note = l.Note ?? ""
                };
                Rows.Add(row);
                HookRow(row);
            }

            StockDocId = doc.Id;
            EffectiveDateLocal = doc.EffectiveDateUtc.ToLocalTime();

            MarkClean();
            Raise(nameof(FooterSummary));
        }


    }
}
