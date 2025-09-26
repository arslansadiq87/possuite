using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;          // AppState, PartyLookupService
using Pos.Domain.Entities;
using Pos.Domain.Formatting;            // ProductNameComposer
using Pos.Persistence;
using Pos.Persistence.Services;         // PurchasesService
using System.Collections.Generic;
using System.Timers;
using System.Windows.Input;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseReturnWindow : Window
    {
        // ----- Modes -----
        private readonly int? _refPurchaseId;   // Return With
        private readonly int? _returnId;        // Amend existing return
        private readonly bool _freeForm;        // Return Without
        // ----- Data -----
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        private readonly PartyLookupService _partySvc;
        // Supplier search
        private readonly System.Timers.Timer _supplierDebounce = new(250) { AutoReset = false };
        private List<Party> _supplierResults = new();
        private bool _suppressSupplierSearch = false;
        // Item search (free-form)
        private readonly System.Timers.Timer _itemDebounce = new(250) { AutoReset = false };
        private List<ItemPick> _itemResults = new();
        // Cache of the key column indexes so we resolve them once.
        private int? _colUnitCostIndex;
        private int? _colReturnQtyIndex;

        public ReturnVM VM { get; } = new();

        // ===== Constructors =====
        public PurchaseReturnWindow() // Return Without (free-form)
        {
            InitializeComponent();
            _freeForm = true;
            _supplierDebounce.Elapsed += SupplierDebounce_Elapsed;
            _itemDebounce.Elapsed += ItemDebounce_Elapsed;
            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;
            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));
            DataContext = VM;
            Loaded += async (_, __) =>
            {
                await InitFreeFormAsync();
                // Focus supplier after UI is ready
                await Dispatcher.InvokeAsync(() => SupplierText.Focus());
            };
        }

        public PurchaseReturnWindow(int refPurchaseId) // Return With base purchase
        {
            InitializeComponent();
            _refPurchaseId = refPurchaseId;
            _supplierDebounce.Elapsed += SupplierDebounce_Elapsed;
            _itemDebounce.Elapsed += ItemDebounce_Elapsed;
            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;
            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));
            DataContext = VM;
            Loaded += async (_, __) =>
            {
                await InitFromBaseAsync(refPurchaseId);
                await Dispatcher.InvokeAsync(() => SupplierText.Focus());
            };
        }

        public PurchaseReturnWindow(int returnId, bool isAmend = true) // Amend
        {
            InitializeComponent();
            _returnId = returnId;
            _supplierDebounce.Elapsed += SupplierDebounce_Elapsed;
            _itemDebounce.Elapsed += ItemDebounce_Elapsed;
            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;
            _svc = new PurchasesService(new PosClientDbContext(_opts));
            _partySvc = new PartyLookupService(new PosClientDbContext(_opts));
            DataContext = VM;
            Loaded += async (_, __) =>
            {
                await InitFromExistingReturnAsync(returnId);
                await Dispatcher.InvokeAsync(() => SupplierText.Focus());
            };
        }

        // ===== Supplier search helpers =====
        private void ApplySupplier(Party p)
        {
            if (p == null) return;
            _suppressSupplierSearch = true;      // prevent popup reopening due to TextChanged
            VM.SupplierId = p.Id;
            VM.SupplierDisplay = p.Name ?? $"Supplier #{p.Id}";
            _suppressSupplierSearch = false;
            SupplierPopup.IsOpen = false;
            SupplierText.CaretIndex = SupplierText.Text.Length;
            // Move focus to item search next
            MoveFocusToItemSearch();
        }

        private void MoveFocusToItemSearch()
        {
            // Make sure item search is visible only when allowed
            if (VM.CanAddItems)
                ItemText.Focus();
            else
                GridLines.Focus();
        }

        // Cache of the key column indexes so we resolve them once.
        
        private int EnsureColumnIndex(string bindingPath)
        {
            // Try cached first
            if (bindingPath == nameof(LineVM.UnitCost) && _colUnitCostIndex.HasValue) return _colUnitCostIndex.Value;
            if (bindingPath == nameof(LineVM.ReturnQty) && _colReturnQtyIndex.HasValue) return _colReturnQtyIndex.Value;

            // Resolve by binding path (DataGridBoundColumn)
            for (int i = 0; i < GridLines.Columns.Count; i++)
            {
                if (GridLines.Columns[i] is DataGridBoundColumn bc)
                {
                    if (bc.Binding is System.Windows.Data.Binding b &&
                        string.Equals(b.Path.Path, bindingPath, StringComparison.Ordinal))
                    {
                        if (bindingPath == nameof(LineVM.UnitCost)) _colUnitCostIndex = i;
                        if (bindingPath == nameof(LineVM.ReturnQty)) _colReturnQtyIndex = i;
                        return i;
                    }
                }
            }

            // Fallback: try by header text (if your headers are literally "UnitCost" / "ReturnQty")
            for (int i = 0; i < GridLines.Columns.Count; i++)
            {
                var header = GridLines.Columns[i].Header?.ToString() ?? "";
                if (string.Equals(header, bindingPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (bindingPath == nameof(LineVM.UnitCost)) _colUnitCostIndex = i;
                    if (bindingPath == nameof(LineVM.ReturnQty)) _colReturnQtyIndex = i;
                    return i;
                }
            }

            // If not found, just return first column to avoid crashes
            return 0;
        }

        private void FocusCell(LineVM rowVm, string bindingPath, bool beginEdit = true)
        {
            if (rowVm == null || GridLines.Items.Count == 0) return;

            GridLines.SelectedItem = rowVm;
            GridLines.UpdateLayout();
            GridLines.ScrollIntoView(rowVm);

            var colIndex = EnsureColumnIndex(bindingPath);
            if (colIndex < 0 || colIndex >= GridLines.Columns.Count) colIndex = 0;

            var col = GridLines.Columns[colIndex];
            GridLines.CurrentCell = new DataGridCellInfo(rowVm, col);

            if (beginEdit)
            {
                // BeginEdit twice helps when the row was not yet realized
                GridLines.BeginEdit();
                Dispatcher.InvokeAsync(() => GridLines.BeginEdit(), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void FocusUnitCost(LineVM vm) => FocusCell(vm, nameof(LineVM.UnitCost), beginEdit: true);
        private void FocusReturnQty(LineVM vm) => FocusCell(vm, nameof(LineVM.ReturnQty), beginEdit: true);


        private async void SupplierDebounce_Elapsed(object? sender, ElapsedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (VM.IsSupplierReadonly) { SupplierPopup.IsOpen = false; return; }
                var term = (SupplierText.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(term))
                {
                    SupplierList.ItemsSource = null;
                    SupplierPopup.IsOpen = false;
                    return;
                }
                var outletId = AppState.Current?.CurrentOutletId ?? 0;
                var list = await _partySvc.SearchSuppliersAsync(term, outletId);
                _supplierResults = list ?? new List<Party>();
                SupplierList.ItemsSource = _supplierResults;
                SupplierPopup.IsOpen = _supplierResults.Count > 0;
                if (SupplierPopup.IsOpen)
                    SupplierList.SelectedIndex = 0;
            });
        }

        private void SupplierText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (VM.IsSupplierReadonly || _suppressSupplierSearch) return;
            _supplierDebounce.Stop();
            _supplierDebounce.Start();
        }

        private void SupplierText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (VM.IsSupplierReadonly) return;
            if (e.Key == Key.Down && SupplierPopup.IsOpen && SupplierList.Items.Count > 0)
            {
                SupplierList.Focus();
                SupplierList.SelectedIndex = Math.Max(0, SupplierList.SelectedIndex);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && SupplierPopup.IsOpen)
            {
                var pick = SupplierList.SelectedItem as Party ?? _supplierResults.FirstOrDefault();
                if (pick != null) ApplySupplier(pick);
                e.Handled = true;
            }

            if (e.Key == Key.Escape && SupplierPopup.IsOpen)
            {
                SupplierPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void SupplierText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Popup closes via StaysOpen=False
        }

        private void SupplierList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SupplierList.SelectedItem is Party p) ApplySupplier(p);
        }

        private void SupplierList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SupplierList.SelectedItem is Party p)
            {
                ApplySupplier(p);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SupplierPopup.IsOpen = false;
                SupplierText.Focus();
                e.Handled = true;
            }
        }

        // ===== Item search (free-form) =====
        private sealed class ItemPick
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string Display { get; set; } = "";
            public decimal? LastCost { get; set; }
            public string LastCostDisplay => LastCost.HasValue ? $"Last: {LastCost.Value:N2}" : "";
        }

        private async void ItemDebounce_Elapsed(object? sender, ElapsedEventArgs e)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (!VM.CanAddItems) { ItemPopup.IsOpen = false; return; }

                var term = (ItemText.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(term))
                {
                    ItemList.ItemsSource = null;
                    ItemPopup.IsOpen = false;
                    return;
                }

                _itemResults = await SearchItemsAsync(term);
                ItemList.ItemsSource = _itemResults;
                ItemPopup.IsOpen = _itemResults.Count > 0;
                if (ItemPopup.IsOpen) ItemList.SelectedIndex = 0;
            });
        }

        private void ItemText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!VM.CanAddItems) return;
            _itemDebounce.Stop();
            _itemDebounce.Start();
        }

        private void ItemText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!VM.CanAddItems) return;
            if (e.Key == Key.Down && ItemPopup.IsOpen && ItemList.Items.Count > 0)
            {
                ItemList.Focus();
                ItemList.SelectedIndex = Math.Max(0, ItemList.SelectedIndex);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && ItemPopup.IsOpen)
            {
                var pick = ItemList.SelectedItem as ItemPick ?? _itemResults.FirstOrDefault();
                if (pick != null) AddOrBumpItem(pick);
                e.Handled = true;
            }
            if (e.Key == Key.Escape && ItemPopup.IsOpen)
            {
                ItemPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void ItemText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Popup closes via StaysOpen=False
        }

        private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is ItemPick p) AddOrBumpItem(p);
        }

        private void ItemList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ItemList.SelectedItem is ItemPick p)
            {
                AddOrBumpItem(p);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ItemPopup.IsOpen = false;
                ItemText.Focus();
                e.Handled = true;
            }
        }

        private async Task<List<ItemPick>> SearchItemsAsync(string term, int take = 50)
        {
            using var db = new PosClientDbContext(_opts);
            term = term.Trim();
            var q =
                from i in db.Items.AsNoTracking()
                join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                from p in gp.DefaultIfEmpty()
                where (EF.Functions.Like(i.Sku, $"%{term}%")
                       || EF.Functions.Like(i.Name, $"%{term}%")
                       || (p != null && EF.Functions.Like(p.Name, $"%{term}%")))
                orderby i.Sku
                select new { i.Id, i.Sku, ItemName = i.Name, ProductName = p != null ? p.Name : null };
            var rows = await q.Take(take).ToListAsync();
            // Last purchase cost per item (optional)
            var itemIds = rows.Select(r => r.Id).ToList();
            var lastCosts = await (
                from pl in db.PurchaseLines.AsNoTracking()
                join pu in db.Purchases.AsNoTracking() on pl.PurchaseId equals pu.Id
                where itemIds.Contains(pl.ItemId) && !pu.IsReturn
                orderby pu.PurchaseDate descending
                select new { pl.ItemId, pl.UnitCost, pu.PurchaseDate }
            )
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, LastCost = g.First().UnitCost })
            .ToDictionaryAsync(x => x.ItemId, x => (decimal?)x.LastCost);
            var list = rows.Select(r => new ItemPick
            {
                ItemId = r.Id,
                Sku = r.Sku ?? "",
                Display = ProductNameComposer.Compose(r.ProductName, r.ItemName, null, null, null, null),
                LastCost = lastCosts.TryGetValue(r.Id, out var c) ? c : null
            }).ToList();
            return list;
        }

        private void AddOrBumpItem(ItemPick pick)
        {
            LineVM targetLine;

            var existing = VM.Lines.FirstOrDefault(l => l.ItemId == pick.ItemId);
            if (existing != null)
            {
                existing.ReturnQty = existing.ReturnQty + 1;
                existing.ClampQty();
                existing.RecomputeLineTotal();
                VM.RecomputeTotals();
                targetLine = existing;
            }
            else
            {
                var unitCost = Math.Max(0, pick.LastCost ?? 0m);
                var line = new LineVM
                {
                    OriginalLineId = null,
                    ItemId = pick.ItemId,
                    DisplayName = pick.Display,
                    Sku = pick.Sku,
                    UnitCost = unitCost,
                    Discount = 0m,
                    TaxRate = 0m,
                    MaxReturnQty = 999999m,   // free-form cap
                    ReturnQty = 1m
                };
                VM.Lines.Add(line);
                VM.RecomputeTotals();
                targetLine = line;
            }

            ItemPopup.IsOpen = false;
            ItemText.Clear();

            // === New behavior: go directly to UnitCost (edit mode) for that line ===
            Dispatcher.InvokeAsync(() => FocusUnitCost(targetLine));

            // Do NOT refocus ItemText here; we'll come back after qty Enter.
            // ItemText.Focus();
        }

        private void GridLines_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            // Make sure any current edit is committed before moving on
            GridLines.CommitEdit(DataGridEditingUnit.Cell, true);
            GridLines.CommitEdit(DataGridEditingUnit.Row, true);

            if (GridLines.CurrentItem is not LineVM rowVm) return;

            var unitCostIdx = EnsureColumnIndex(nameof(LineVM.UnitCost));
            var returnQtyIdx = EnsureColumnIndex(nameof(LineVM.ReturnQty));

            var currentCol = GridLines.CurrentColumn;
            if (currentCol == null) return;

            // If we were in UnitCost → jump to ReturnQty
            if (GridLines.Columns.IndexOf(currentCol) == unitCostIdx)
            {
                e.Handled = true;
                FocusReturnQty(rowVm);
                return;
            }

            // If we were in ReturnQty → commit and go back to Item search box
            if (GridLines.Columns.IndexOf(currentCol) == returnQtyIdx)
            {
                e.Handled = true;

                // Recompute totals to reflect last edit (safe; you already do it on CellEditEnding too)
                rowVm.ClampQty();
                rowVm.RecomputeLineTotal();
                VM.RecomputeTotals();

                // Back to item search box for the next scan/type
                Dispatcher.InvokeAsync(() => ItemText.Focus());
                return;
            }
        }


        // ===== Initialization flows =====
        private async Task InitFromBaseAsync(int refPurchaseId)
        {
            using var db = new PosClientDbContext(_opts);
            var draft = await _svc.BuildReturnDraftAsync(refPurchaseId);
            var baseP = await db.Purchases
                .Include(p => p.Party)
                .FirstAsync(p => p.Id == refPurchaseId);
            VM.IsSupplierReadonly = true;
            VM.SupplierId = draft.PartyId;
            VM.SupplierDisplay = baseP.Party?.Name ?? $"Supplier #{draft.PartyId}";
            VM.TargetType = draft.TargetType;
            VM.OutletId = draft.OutletId;
            VM.WarehouseId = draft.WarehouseId;
            VM.RefPurchaseId = draft.RefPurchaseId;
            VM.BasePurchaseDisplay = !string.IsNullOrWhiteSpace(baseP.DocNo) ? baseP.DocNo : $"#{baseP.Id}";
            var original = await db.Purchases.Include(p => p.Lines).FirstAsync(p => p.Id == refPurchaseId);
            var itemIds = original.Lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = (
                from i in db.Items.AsNoTracking()
                join pr in db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku
                }
            ).ToList().ToDictionary(x => x.Id);
            VM.Lines.Clear();
            foreach (var d in draft.Lines)
            {
                meta.TryGetValue(d.ItemId, out var m);
                var display = ProductNameComposer.Compose(
                    m?.ProductName, m?.ItemName,
                    m?.Variant1Name, m?.Variant1Value,
                    m?.Variant2Name, m?.Variant2Value);
                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = d.OriginalLineId,
                    ItemId = d.ItemId,
                    DisplayName = display,
                    Sku = m?.Sku ?? "",
                    UnitCost = d.UnitCost,
                    OriginalUnitCost = d.UnitCost,     // <-- lock source price
                    Discount = 0m,
                    TaxRate = 0m,
                    MaxReturnQty = d.MaxReturnQty,
                    ReturnQty = d.ReturnQty
                });
            }
            VM.RecomputeTotals();
        }

        private async Task InitFreeFormAsync()
        {
            VM.IsSupplierReadonly = false;
            VM.BasePurchaseDisplay = "—";
            // Prefer outlet from AppState
            VM.TargetType = StockTargetType.Outlet;
            VM.OutletId = AppState.Current?.CurrentOutletId > 0 ? AppState.Current.CurrentOutletId : (int?)null;
            VM.WarehouseId = null;
            VM.Lines.Clear();
        }

        private async Task InitFromExistingReturnAsync(int returnId)
        {
            using var db = new PosClientDbContext(_opts);
            var ret = await db.Purchases
                .Include(p => p.Party)
                .Include(p => p.Lines)
                .FirstAsync(p => p.Id == returnId && p.IsReturn);
            VM.IsSupplierReadonly = true;
            VM.SupplierId = ret.PartyId;
            VM.SupplierDisplay = ret.Party?.Name ?? $"Supplier #{ret.PartyId}";
            VM.TargetType = ret.TargetType;
            VM.OutletId = ret.OutletId;
            VM.WarehouseId = ret.WarehouseId;
            VM.RefPurchaseId = ret.RefPurchaseId;
            VM.ReturnNoDisplay = string.IsNullOrWhiteSpace(ret.DocNo) ? $"#{ret.Id}" : ret.DocNo;
            VM.BasePurchaseDisplay = ret.RefPurchaseId is int rid ? $"#{rid}" : "—";
            var itemIds = ret.Lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = (
                from i in db.Items.AsNoTracking()
                join pr in db.Products.AsNoTracking() on i.ProductId equals pr.Id into gp
                from pr in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = pr != null ? pr.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku
                }
            ).ToList().ToDictionary(x => x.Id);
            VM.Lines.Clear();
            foreach (var l in ret.Lines)
            {
                meta.TryGetValue(l.ItemId, out var m);
                var display = ProductNameComposer.Compose(
                    m?.ProductName, m?.ItemName,
                    m?.Variant1Name, m?.Variant1Value,
                    m?.Variant2Name, m?.Variant2Value);
                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = l.RefPurchaseLineId,
                    ItemId = l.ItemId,
                    DisplayName = display,
                    Sku = m?.Sku ?? "",
                    UnitCost = l.UnitCost,
                    OriginalUnitCost = l.UnitCost,     // <-- lock to what prior return had
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    ReturnQty = Math.Abs(l.Qty),
                    MaxReturnQty = 999999m
                });
            }
            VM.OtherCharges = ret.OtherCharges;
            VM.RecomputeTotals();
        }

        // ===== Grid handlers =====
        private void GridLines_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row?.Item is LineVM vm)
            {
                vm.ClampQty();
                vm.RecomputeLineTotal();
                VM.RecomputeTotals();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VM.SupplierId <= 0)
                {
                    MessageBox.Show("Select a supplier.");
                    return;
                }
                if (VM.Lines.Count == 0 || VM.Lines.All(l => l.ReturnQty <= 0))
                {
                    MessageBox.Show("Add at least one line with Return Qty > 0.");
                    return;
                }
                var model = new Purchase
                {
                    Id = _returnId ?? 0,                     // 0 for new
                    IsReturn = true,
                    RefPurchaseId = _refPurchaseId ?? VM.RefPurchaseId,
                    PartyId = VM.SupplierId,
                    TargetType = VM.TargetType,
                    OutletId = VM.TargetType == StockTargetType.Outlet ? VM.OutletId : null,
                    WarehouseId = VM.TargetType == StockTargetType.Warehouse ? VM.WarehouseId : null,
                    PurchaseDate = DateTime.UtcNow,
                    ReceivedAtUtc = DateTime.UtcNow,
                    OtherCharges = Math.Round(VM.OtherCharges, 2),
                    Subtotal = VM.Subtotal,
                    Discount = VM.Discount,
                    Tax = VM.Tax,
                    GrandTotal = VM.GrandTotal
                };
                // Lines (service will enforce negative qty and compute)
                var lines = VM.Lines
                    .Where(l => l.ReturnQty > 0.0001m)
                    .Select(l =>
                    {
                        // If line is tied to a base purchase, force original unit cost; otherwise use current edited cost.
                        var unitCost = l.OriginalLineId.HasValue
                            ? Math.Max(0, l.OriginalUnitCost ?? l.UnitCost)  // locked price path
                            : Math.Max(0, l.UnitCost);                       // free-form path
                        return new PurchaseLine
                        {
                            ItemId = l.ItemId,
                            Qty = -Math.Abs(l.ReturnQty),          // returns are negative
                            UnitCost = unitCost,
                            Discount = Math.Max(0, l.Discount),
                            TaxRate = Math.Max(0, l.TaxRate),
                            RefPurchaseLineId = l.OriginalLineId
                        };
                    })
                    .ToList();
                // 👉 NEW: block mixing check — prevents “referenced lines without a base invoice”
                var hasRefPurchase = (_refPurchaseId ?? VM.RefPurchaseId).HasValue;
                var anyLineReferencesOriginal = lines.Any(l => l.RefPurchaseLineId.HasValue);
                if (!hasRefPurchase && anyLineReferencesOriginal)
                {
                    MessageBox.Show("This return has lines referencing an original purchase but no base invoice is selected.");
                    return;
                }
                // 👆 Place exactly here, before refunds and SaveReturnAsync
                // Auto-suggest refund = grand total if none entered
                if (VM.RefundAmount <= 0m && VM.GrandTotal > 0m)
                {
                    VM.RefundAmount = VM.GrandTotal;
                }
                // Build refunds list
                var refunds = new System.Collections.Generic.List<SupplierRefundSpec>();
                if (VM.RefundAmount > 0m)
                {
                    refunds.Add(new SupplierRefundSpec(VM.RefundMethod, VM.RefundAmount, "Refund on return"));
                }
                var user = AppState.Current?.CurrentUserName ?? "system";
                await _svc.SaveReturnAsync(
                    model,
                    lines,
                    user,
                    refunds,
                    tillSessionId: AppState.Current?.CurrentTillSessionId,
                    counterId: AppState.Current?.CurrentCounterId
                );
                MessageBox.Show("Purchase Return saved.");
                this.DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save return: " + ex.Message);
            }
        }

        // ===== View Models =====
        public class ReturnVM : INotifyPropertyChanged
        {
            // Header
            private int _supplierId;
            private string _supplierDisplay = "";
            private StockTargetType _targetType = StockTargetType.Outlet;
            private int? _outletId;
            private int? _warehouseId;
            private int? _refPurchaseId;
            private bool _isSupplierReadonly;
            private string _returnNoDisplay = "Auto";
            private string _basePurchaseDisplay = "—";
            private DateTime _date = DateTime.Now;
            // Totals
            private decimal _subtotal;
            private decimal _discount;
            private decimal _tax;
            private decimal _other = 0m;
            private decimal _grand;
            // Refund UI
            public ObservableCollection<TenderMethod> RefundMethods { get; } =
                new ObservableCollection<TenderMethod>(Enum.GetValues(typeof(TenderMethod)).Cast<TenderMethod>());
            private TenderMethod _refundMethod = TenderMethod.Cash;
            public TenderMethod RefundMethod { get => _refundMethod; set { _refundMethod = value; OnChanged(); } }
            private decimal _refundAmount = 0m;
            public decimal RefundAmount
            {
                get => _refundAmount;
                set { _refundAmount = Math.Max(0, value); OnChanged(); }
            }

            public ObservableCollection<LineVM> Lines { get; } = new();
            public int SupplierId { get => _supplierId; set { _supplierId = value; OnChanged(); } }
            public string SupplierDisplay { get => _supplierDisplay; set { _supplierDisplay = value; OnChanged(); } }
            // When these change, also notify CanAddItems
            public bool IsSupplierReadonly { get => _isSupplierReadonly; set { _isSupplierReadonly = value; OnChanged(); OnChanged(nameof(CanAddItems)); } }
            public int? RefPurchaseId { get => _refPurchaseId; set { _refPurchaseId = value; OnChanged(); OnChanged(nameof(CanAddItems)); } }
            public StockTargetType TargetType { get => _targetType; set { _targetType = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? OutletId { get => _outletId; set { _outletId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public string ReturnNoDisplay { get => _returnNoDisplay; set { _returnNoDisplay = value; OnChanged(); } }
            public string BasePurchaseDisplay { get => _basePurchaseDisplay; set { _basePurchaseDisplay = value; OnChanged(); } }
            // Show item search only when free-form (no base, supplier editable)
            public bool CanAddItems => !IsSupplierReadonly && RefPurchaseId is null;

            public string TargetDisplay =>
                TargetType == StockTargetType.Outlet
                    ? (OutletId is int o ? $"Outlet #{o}" : "Outlet —")
                    : (WarehouseId is int w ? $"Warehouse #{w}" : "Warehouse —");

            public string DateDisplay => _date.ToString("dd-MMM-yyyy HH:mm");
            public decimal Subtotal { get => _subtotal; set { _subtotal = value; OnChanged(); } }
            public decimal Discount { get => _discount; set { _discount = value; OnChanged(); } }
            public decimal Tax { get => _tax; set { _tax = value; OnChanged(); } }
            public decimal OtherCharges { get => _other; set { _other = value; OnChanged(); RecomputeTotals(); } }
            public decimal GrandTotal { get => _grand; set { _grand = value; OnChanged(); } }

            public void RecomputeTotals()
            {
                var subtotal = Lines.Sum(x => Math.Abs(x.ReturnQty) * Math.Max(0, x.UnitCost));
                var discount = Lines.Sum(x => Math.Max(0, x.Discount));
                var tax = Lines.Sum(x =>
                {
                    var taxable = Math.Max(0, Math.Abs(x.ReturnQty) * Math.Max(0, x.UnitCost) - Math.Max(0, x.Discount));
                    return Math.Round(taxable * (Math.Max(0, x.TaxRate) / 100m), 2);
                });
                Subtotal = Math.Round(subtotal, 2);
                Discount = Math.Round(discount, 2);
                Tax = Math.Round(tax, 2);
                GrandTotal = Math.Round(Subtotal - Discount + Tax + OtherCharges, 2);
                foreach (var l in Lines) l.RecomputeLineTotal();
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class LineVM : INotifyPropertyChanged
        {
            public int? OriginalLineId { get; set; }
            public int ItemId { get; set; }
            public string DisplayName { get; set; } = "";
            public string Sku { get; set; } = "";
            private decimal _unitCost;
            private decimal _discount;
            private decimal _taxRate;
            private decimal _maxReturnQty;
            private decimal _returnQty;
            private decimal _lineTotal;
            public decimal? OriginalUnitCost { get; set; }  // set for "Return With" lines; null for free-form
            public decimal UnitCost { get => _unitCost; set { _unitCost = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal Discount { get => _discount; set { _discount = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal TaxRate { get => _taxRate; set { _taxRate = Math.Max(0, value); OnChanged(); RecomputeLineTotal(); } }
            public decimal MaxReturnQty { get => _maxReturnQty; set { _maxReturnQty = Math.Max(0, value); OnChanged(); } }
            public decimal ReturnQty { get => _returnQty; set { _returnQty = Math.Max(0, value); ClampQty(); OnChanged(); RecomputeLineTotal(); } }
            public decimal LineTotal { get => _lineTotal; private set { _lineTotal = value; OnChanged(); } }

            public void ClampQty()
            {
                if (MaxReturnQty > 0 && ReturnQty > MaxReturnQty)
                    _returnQty = MaxReturnQty;
                if (_returnQty < 0) _returnQty = 0;
            }

            public void RecomputeLineTotal()
            {
                var qtyAbs = Math.Abs(ReturnQty);
                var baseAmt = qtyAbs * Math.Max(0, UnitCost);
                var taxable = Math.Max(0, baseAmt - Math.Max(0, Discount));
                var tax = Math.Round(taxable * (Math.Max(0, TaxRate) / 100m), 2);
                LineTotal = Math.Round(taxable + tax, 2);
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
