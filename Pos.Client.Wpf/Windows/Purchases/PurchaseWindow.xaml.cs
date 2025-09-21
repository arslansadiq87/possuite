// Pos.Client.Wpf/Windows/Purchases/PurchaseWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using System.Runtime.CompilerServices;
using Pos.Domain.Formatting;
using Pos.Client.Wpf.Windows.Sales;
using Pos.Client.Wpf.Services;


namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseWindow : Window
    {
        private readonly PartyLookupService _partySvc;

        private PurchaseEditorVM VM
        {
            get
            {
                if (DataContext is PurchaseEditorVM vm) return vm;
                vm = new PurchaseEditorVM();
                DataContext = vm;
                return vm;
            }
        }
        // ===================== Line VM (unchanged behavior) =====================
        public class PurchaseLineVM : INotifyPropertyChanged
        {
            public int ItemId { get; set; }

            private string _sku = "";
            public string Sku { get => _sku; set { _sku = value; OnPropertyChanged(nameof(Sku)); } }

            private string _name = "";
            public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

            private decimal _qty;
            public decimal Qty { get => _qty; set { _qty = value; Recalc(); OnPropertyChanged(nameof(Qty)); } }

            private decimal _unitCost;
            public decimal UnitCost { get => _unitCost; set { _unitCost = value; Recalc(); OnPropertyChanged(nameof(UnitCost)); } }

            private decimal _discount;
            public decimal Discount { get => _discount; set { _discount = value; Recalc(); OnPropertyChanged(nameof(Discount)); } }

            private decimal _taxRate;
            public decimal TaxRate { get => _taxRate; set { _taxRate = value; Recalc(); OnPropertyChanged(nameof(TaxRate)); } }

            private decimal _lineTotal;
            public decimal LineTotal { get => _lineTotal; private set { _lineTotal = value; OnPropertyChanged(nameof(LineTotal)); } }
            public string? Notes { get; set; }
            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            void Recalc()
            {
                var baseAmt = Qty * UnitCost;
                var taxable = Math.Max(0m, baseAmt - Discount);
                var tax = Math.Round(taxable * (TaxRate / 100m), 2);
                LineTotal = Math.Round(taxable + tax, 2);
            }
            public void ForceRecalc() => Recalc();
        }

        public class PurchaseEditorVM : INotifyPropertyChanged
        {
            public int PurchaseId { get => _purchaseId; set { _purchaseId = value; OnChanged(); } }
            private int _purchaseId;

            public int SupplierId { get => _supplierId; set { _supplierId = value; OnChanged(); } }
            private int _supplierId;

            public StockTargetType TargetType { get => _targetType; set { _targetType = value; OnChanged(); } }
            private StockTargetType _targetType;

            public int? OutletId { get => _outletId; set { _outletId = value; OnChanged(); } }
            private int? _outletId;

            public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(); } }
            private int? _warehouseId;

            public DateTime PurchaseDate { get => _purchaseDate; set { _purchaseDate = value; OnChanged(); } }
            private DateTime _purchaseDate = DateTime.UtcNow;

            public string? VendorInvoiceNo { get => _vendorInvoiceNo; set { _vendorInvoiceNo = value; OnChanged(); } }
            private string? _vendorInvoiceNo;

            public string? DocNo { get => _docNo; set { _docNo = value; OnChanged(); } }
            private string? _docNo;

            public decimal Subtotal { get => _subtotal; set { _subtotal = value; OnChanged(); } }
            private decimal _subtotal;

            public decimal Discount { get => _discount; set { _discount = value; OnChanged(); } }
            private decimal _discount;

            public decimal Tax { get => _tax; set { _tax = value; OnChanged(); } }
            private decimal _tax;

            public decimal OtherCharges { get => _otherCharges; set { _otherCharges = value; OnChanged(); } }
            private decimal _otherCharges;

            public decimal GrandTotal { get => _grandTotal; set { _grandTotal = value; OnChanged(); } }
            private decimal _grandTotal;

            public PurchaseStatus Status { get => _status; set { _status = value; OnChanged(); } }
            private PurchaseStatus _status = PurchaseStatus.Draft;

            public bool IsDirty { get => _isDirty; set { _isDirty = value; OnChanged(); } }
            private bool _isDirty;

            public ObservableCollection<PurchaseLineVM> Lines { get; } = new();

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? prop = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }


        // ===================== Fields & services =====================
        private readonly PosClientDbContext _db;
        private readonly PurchasesService _purchaseSvc;
        //private readonly SuppliersService _suppliersSvc;
        private readonly ItemsService _itemsSvc;
        private Purchase _model = new();
        private readonly ObservableCollection<PurchaseLineVM> _lines = new();
        private ObservableCollection<Party> _supplierResults = new();  // was ObservableCollection<Supplier>


        private ObservableCollection<Item> _itemResults = new();
        // NEW: destination pickers data (matches XAML group we added)
        private ObservableCollection<Outlet> _outletResults = new();
        private ObservableCollection<Warehouse> _warehouseResults = new();
        private int? _selectedPartyId;
        private readonly DbContextOptions<PosClientDbContext> _opts =
    new DbContextOptionsBuilder<PosClientDbContext>()
        .UseSqlite(DbPath.ConnectionString)
        .Options;
        // make the accessor resilient (no casts on null)

        public PurchaseWindow()  // NEW
        {
            InitializeComponent();
            if (DataContext is not PurchaseEditorVM) DataContext = new PurchaseEditorVM();
            // … leave the rest of your initialization in the db-ctor, or move it here if you prefer …
        }

        public PurchaseWindow(PosClientDbContext db) : this()  // CHANGED: chain to parameterless
        {
            _db = db;
            _purchaseSvc = new PurchasesService(_db);
            _partySvc = new PartyLookupService(_db);
            _itemsSvc = new ItemsService(_db);

            // init UI bindings you already had
            DatePicker.SelectedDate = DateTime.Now;
            OtherChargesBox.Text = "0.00";
            LinesGrid.ItemsSource = _lines;
            SupplierList.ItemsSource = _supplierResults;
            ItemList.ItemsSource = _itemResults;

            _ = LoadSuppliersAsync("");
            LinesGrid.CellEditEnding += (_, __) => Dispatcher.BeginInvoke(RecomputeAndUpdateTotals);
            LinesGrid.RowEditEnding += (_, __) => Dispatcher.BeginInvoke(RecomputeAndUpdateTotals);
            _lines.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null)
                    foreach (PurchaseLineVM vm in args.NewItems)
                        vm.PropertyChanged += (_, __) => RecomputeAndUpdateTotals();
                RecomputeAndUpdateTotals();
            };

            LinesGrid.PreviewKeyDown += LinesGrid_PreviewKeyDown;

            Loaded += async (_, __) =>
            {
                await InitDestinationsAsync();
                SupplierText.Focus();
                SupplierText.CaretIndex = SupplierText.Text?.Length ?? 0;
            };

            ItemList.PreviewKeyDown += ItemList_PreviewKeyDown;
            SupplierList.PreviewKeyDown += SupplierList_PreviewKeyDown;

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F7) { InvoicesButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.F5) { ClearCurrentPurchase(confirm: true); e.Handled = true; return; }
                if (e.Key == Key.F8) { _ = HoldCurrentPurchaseQuickAsync(); e.Handled = true; return; }
            };
        }

        // ===================== Destination init (NEW) =====================
        private async Task InitDestinationsAsync()
        {
            // Load outlets/warehouses if the tables exist; otherwise keep empty silently.
            try
            {
                _outletResults = new ObservableCollection<Outlet>(
                    await _db.Set<Outlet>().AsNoTracking().OrderBy(o => o.Name).ToListAsync());
            }
            catch { _outletResults = new ObservableCollection<Outlet>(); }
            try
            {
                _warehouseResults = new ObservableCollection<Warehouse>(
                    await _db.Set<Warehouse>().AsNoTracking().OrderBy(w => w.Name).ToListAsync());
            }
            catch { _warehouseResults = new ObservableCollection<Warehouse>(); }
            // Wire to XAML combo boxes if present
            try { OutletBox.ItemsSource = _outletResults; } catch { /* if control not present, ignore */ }
            try { WarehouseBox.ItemsSource = _warehouseResults; } catch { /* ignore */ }
            // Basic defaults: if you have warehouses, default to Warehouse; else Outlet
            var hasWarehouse = _warehouseResults.Any();
            try
            {
                if (hasWarehouse)
                {
                    DestWarehouseRadio.IsChecked = true;
                    if (_warehouseResults.Any()) WarehouseBox.SelectedIndex = 0;
                    WarehouseBox.IsEnabled = true;
                    OutletBox.IsEnabled = false;
                }
                else
                {
                    DestOutletRadio.IsChecked = true;
                    if (_outletResults.Any()) OutletBox.SelectedIndex = 0;
                    WarehouseBox.IsEnabled = false;
                    OutletBox.IsEnabled = true;
                }
            }
            catch { /* controls may not exist if not added yet */ }
        }

        // Called by both radios in XAML
        private void DestRadio_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                var toWarehouse = DestWarehouseRadio.IsChecked == true;
                WarehouseBox.IsEnabled = toWarehouse;
                OutletBox.IsEnabled = !toWarehouse;
            }
            catch { /* controls may not exist */ }
        }

        // ===================== Grid fast entry (kept) =====================
        private void LinesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            var dg = (DataGrid)sender;
            // Commit current edits first
            dg.CommitEdit(DataGridEditingUnit.Cell, true);
            dg.CommitEdit(DataGridEditingUnit.Row, true);
            // Flow (headers must match grid)
            var flow = new List<string> { "Qty", "Price", "Disc", "Tax %", "Notes" };
            var cellInfo = dg.CurrentCell;
            var col = cellInfo.Column;
            var rawHeader = col != null ? (col.Header as string ?? string.Empty) : string.Empty;
            var header = NormalizeHeader(rawHeader);
            var currentRowItem = dg.CurrentItem;
            if (col == null)
            {
                MoveToHeader(dg, currentRowItem, flow[0]);
                return;
            }
            bool backward = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            if (backward)
            {
                var prev = PrevHeader(flow, header);
                if (prev is null)
                {
                    ItemSearchText.Focus();
                    ItemSearchText.SelectAll();
                    return;
                }
                MoveToHeader(dg, currentRowItem, prev);
                return;
            }
            var next = NextHeader(flow, header);
            if (next is null)
            {
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
                return;
            }
            MoveToHeader(dg, currentRowItem, next);
        }

        private static string NormalizeHeader(string header)
        {
            header = header.Trim();
            if (string.Equals(header, "UnitCost", StringComparison.OrdinalIgnoreCase)) return "Price";
            if (string.Equals(header, "Discount", StringComparison.OrdinalIgnoreCase)) return "Disc";
            if (string.Equals(header, "Tax%", StringComparison.OrdinalIgnoreCase)) return "Tax %";
            return header;
        }

        private static string? NextHeader(IList<string> flow, string current)
        {
            var idx = flow.IndexOf(current);
            if (idx < 0) return flow.FirstOrDefault();
            if (idx >= flow.Count - 1) return null;
            return flow[idx + 1];
        }

        private static string? PrevHeader(IList<string> flow, string current)
        {
            var idx = flow.IndexOf(current);
            if (idx < 0) return null;
            if (idx == 0) return null;
            return flow[idx - 1];
        }

        private void SupplierText_LostFocus(object sender, RoutedEventArgs e)
        {
            SupplierPopup.IsOpen = false;            // close dropdown
            _ = EnsureSupplierSelectedAsync();       // fire & forget is fine here
        }

        private async void VendorInvBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Move to Date
                DatePicker.Focus();
                e.Handled = true;
            }
        }

        private void DatePicker_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Move to Scan/Search
                ItemSearchText.Focus();
                ItemSearchText.SelectAll();
                e.Handled = true;
            }
        }


        private void MoveToHeader(DataGrid dg, object rowItem, string header)
        {
            var targetCol = dg.Columns.FirstOrDefault(c =>
                string.Equals(NormalizeHeader(c.Header as string ?? string.Empty), header, StringComparison.OrdinalIgnoreCase));
            if (targetCol == null) return;
            dg.UpdateLayout();
            dg.SelectedItem = rowItem;
            dg.ScrollIntoView(rowItem, targetCol);
            dg.CurrentCell = new DataGridCellInfo(rowItem, targetCol);
            dg.BeginEdit();
            Dispatcher.BeginInvoke(() =>
            {
                var content = targetCol.GetCellContent(rowItem);
                if (content is TextBox tb)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
                else
                {
                    (content as FrameworkElement)?.Focus();
                }
            });
        }

        private void Numeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]$");
        }

        private async Task LoadSuppliersAsync(string term)
        {
            var outletId = AppState.Current.CurrentOutletId; // use your current outlet accessor
            var list = await _partySvc.SearchSuppliersAsync(term, outletId);
            _supplierResults.Clear();
            foreach (var p in list) _supplierResults.Add(p);
            SupplierPopup.IsOpen = _supplierResults.Count > 0 && !string.IsNullOrWhiteSpace(SupplierText.Text);
            if (_supplierResults.Count > 0 && SupplierList.SelectedIndex < 0)
                SupplierList.SelectedIndex = 0;
        }


        private async void SupplierText_TextChanged(object sender, TextChangedEventArgs e)
        {
            _selectedPartyId = null;
            await LoadSuppliersAsync(SupplierText.Text ?? "");
        }

        private async void SupplierText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (SupplierPopup.IsOpen)
                {
                    var pick = SupplierList.SelectedItem as Party ?? _supplierResults.FirstOrDefault();
                    if (pick != null) ChooseParty(pick);
                }
                else
                {
                    await EnsureSupplierSelectedAsync();
                }
                VendorInvBox.Focus();
                VendorInvBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && SupplierPopup.IsOpen)
            {
                SupplierPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private async Task<bool> EnsureSupplierSelectedAsync()
        {
            if (_selectedPartyId != null) return true;
            var typed = SupplierText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(typed)) return false;

            var outletId = AppState.Current.CurrentOutletId;
            var exact = await _partySvc.FindSupplierByExactNameAsync(typed, outletId);
            if (exact != null) { ChooseParty(exact); return true; }

            var hits = await _partySvc.SearchSuppliersAsync(typed, outletId);
            var pick = hits.FirstOrDefault(p => string.Equals(p.Name, typed, StringComparison.OrdinalIgnoreCase))
                       ?? (hits.Count == 1 ? hits[0] : null);
            if (pick != null) { ChooseParty(pick); return true; }

            return false;
        }


        private void SupplierList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (SupplierList.SelectedItem is Party p) ChooseParty(p);
                e.Handled = true; return;
            }
            if (e.Key == Key.Escape)
            {
                SupplierPopup.IsOpen = false;
                SupplierText.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up && SupplierList.SelectedIndex == 0)
            {
                SupplierText.Focus();
                SupplierText.CaretIndex = SupplierText.Text?.Length ?? 0;
                e.Handled = true;
                return;
            }
        }

        private void SupplierList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SupplierList.SelectedItem is Party p) ChooseParty(p);
        }


        private void ChooseParty(Party p)
        {
            _selectedPartyId = p.Id;
            SupplierText.Text = p.Name;
            SupplierPopup.IsOpen = false;
            SupplierText.Focus();
            SupplierText.SelectAll();
        }

       
        private async Task LoadItemsAsync(string term)
        {
            var list = await _itemsSvc.SearchAsync(term);
            _itemResults.Clear();
            foreach (var it in list) _itemResults.Add(it);
            ItemPopup.IsOpen = _itemResults.Count > 0 && !string.IsNullOrWhiteSpace(ItemSearchText.Text);
            if (_itemResults.Count > 0) ItemList.SelectedIndex = 0;
        }

        private async void ItemSearchText_TextChanged(object sender, TextChangedEventArgs e)
        {
            await LoadItemsAsync(ItemSearchText.Text ?? "");
        }

        private void ItemSearchText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (!ItemPopup.IsOpen)
                {
                    _ = LoadItemsAsync(ItemSearchText.Text ?? "");
                    ItemPopup.IsOpen = _itemResults.Count > 0;
                }
                if (ItemPopup.IsOpen && ItemList.Items.Count > 0)
                {
                    if (ItemList.SelectedIndex < 0) ItemList.SelectedIndex = 0;
                    ItemList.Focus();
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (!ItemPopup.IsOpen)
                {
                    _ = LoadItemsAsync(ItemSearchText.Text ?? "");
                    ItemPopup.IsOpen = _itemResults.Count > 0;
                }
                if (ItemPopup.IsOpen && ItemList.Items.Count > 0)
                {
                    if (ItemList.SelectedIndex < 0) ItemList.SelectedIndex = ItemList.Items.Count - 1;
                    ItemList.Focus();
                }
                e.Handled = true;
                return;
            }
            if (!ItemPopup.IsOpen)
            {
                if (e.Key == Key.Enter) { BtnAddItem_Click(sender, e); e.Handled = true; }
                return;
            }
            if (e.Key == Key.Enter)
            {
                var pick = ItemList.SelectedItem as Item ?? _itemResults.FirstOrDefault();
                if (pick != null)
                {
                    AddItemToLines(pick);
                    FinishItemAdd();
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ItemPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }

        private void ItemList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ItemList.SelectedItem is Item pick)
                {
                    AddItemToLines(pick);
                    FinishItemAdd();
                }
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                ItemPopup.IsOpen = false;
                ItemSearchText.Focus();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up && ItemList.SelectedIndex == 0)
            {
                ItemSearchText.Focus();
                ItemSearchText.CaretIndex = ItemSearchText.Text?.Length ?? 0;
                e.Handled = true;
                return;
            }
        }

        private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is Item i)
            {
                AddItemToLines(i);
                FinishItemAdd();
            }
        }

        private void FinishItemAdd()
        {
            RecomputeAndUpdateTotals();
            ItemPopup.IsOpen = false;
            ItemSearchText.Clear();
            ItemList.SelectedItem = null;
            ItemSearchText.Focus();
        }

        private async Task ApplyLastDefaultsAsync(PurchaseLineVM vm)
        {
            var last = await _purchaseSvc.GetLastPurchaseDefaultsAsync(vm.ItemId);
            if (last is null) return;
            vm.UnitCost = last.Value.unitCost;
            vm.Discount = last.Value.discount;
            vm.TaxRate = last.Value.taxRate;
            vm.ForceRecalc();
        }

        private void AddItemToLines(Item item)
        {
            var existing = _lines.FirstOrDefault(l => l.ItemId == item.Id);
            if (existing != null)
            {
                existing.Qty += 1;
                return;
            }

            var vm = new PurchaseLineVM
            {
                ItemId = item.Id,
                Sku = item.Sku ?? "",
                Name = item.Name ?? "",
                Qty = 1,
                UnitCost = item.Price,
                Discount = 0m,
                TaxRate = item.DefaultTaxRatePct,
                Notes = null
            };
            _lines.Add(vm);
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await ApplyLastDefaultsAsync(vm);
                RecomputeAndUpdateTotals();
            });
            Dispatcher.BeginInvoke(() =>
            {
                LinesGrid.UpdateLayout();
                LinesGrid.SelectedItem = vm;
                LinesGrid.ScrollIntoView(vm);
                var qtyCol = LinesGrid.Columns.First(c => (string?)c.Header == "Qty");
                LinesGrid.CurrentCell = new DataGridCellInfo(vm, qtyCol);
                LinesGrid.BeginEdit();
                if (LinesGrid.CurrentCell.Column.GetCellContent(vm) is TextBox tb) tb.SelectAll();
            });
        }

        // ===================== Totals (kept) =====================
        private void RecomputeAndUpdateTotals()
        {
            var subtotal = Math.Round(_lines.Sum(x => x.Qty * x.UnitCost), 2);
            var discount = Math.Round(_lines.Sum(x => x.Discount), 2);
            var taxSum = Math.Round(_lines.Sum(x =>
                              Math.Max(0m, x.Qty * x.UnitCost - x.Discount) * (x.TaxRate / 100m)), 2);
            if (!decimal.TryParse(OtherChargesBox.Text, out var other)) other = 0m;
            var grand = Math.Round(subtotal - discount + taxSum + other, 2);
            SubtotalText.Text = subtotal.ToString("N2");
            DiscountText.Text = discount.ToString("N2");
            TaxText.Text = taxSum.ToString("N2");
            GrandTotalText.Text = grand.ToString("N2");
            _model.Subtotal = subtotal;
            _model.Discount = discount;
            _model.Tax = taxSum;
            _model.OtherCharges = other;
            _model.GrandTotal = grand;
        }

        private void OtherChargesBox_TextChanged(object sender, TextChangedEventArgs e)
            => RecomputeAndUpdateTotals();

        private async void BtnSaveDraft_Click(object sender, RoutedEventArgs e)
        {
            if (_lines.Count == 0)
            {
                MessageBox.Show("Add at least one item.");
                return;
            }
            if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
            {
                MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative.");
                return;
            }
            foreach (var l in _lines)
            {
                var baseAmt = l.Qty * l.UnitCost;
                if (l.Discount > baseAmt)
                {
                    MessageBox.Show($"Discount exceeds base amount for item '{l.Name}'.");
                    return;
                }
            }
            if (!await EnsureSupplierSelectedAsync())   // NEW
            {
                MessageBox.Show("Please pick a Supplier (press Enter after typing, or choose from the list).");
                return;
            }
            if (_selectedPartyId == null)
            {
                MessageBox.Show("Please pick a Supplier (type and press Enter or double-click from list).");
                return;
            }
            int? outletId = null;
            int? warehouseId = null;
            StockTargetType target;
            bool usedNewDestinationUI = false;
            try
            {
                if (DestWarehouseRadio.IsChecked == true)
                {
                    if (WarehouseBox.SelectedItem is not Warehouse wh)
                    {
                        MessageBox.Show("Please pick a warehouse.");
                        return;
                    }
                    warehouseId = wh.Id;
                    target = StockTargetType.Warehouse;
                    usedNewDestinationUI = true;
                }
                else if (DestOutletRadio.IsChecked == true)
                {
                    if (OutletBox.SelectedItem is not Outlet ot)
                    {
                        MessageBox.Show("Please pick an outlet.");
                        return;
                    }
                    outletId = ot.Id;
                    target = StockTargetType.Outlet;
                    usedNewDestinationUI = true;
                }
                else
                {
                    target = StockTargetType.Outlet; // will be set via legacy box below
                }
            }
            catch
            {
                target = StockTargetType.Outlet; // UI not present
            }
        
        _model.PartyId = _selectedPartyId.Value;
            _model.TargetType = target;
            _model.OutletId = outletId;
            _model.WarehouseId = warehouseId;
            _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
            _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now; // use UI date
            _model.Status = PurchaseStatus.Draft;
            var lines = _lines.Select(l => new PurchaseLine
            {
                ItemId = l.ItemId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
                Discount = l.Discount,
                TaxRate = l.TaxRate,
                Notes = l.Notes
            });
            _model = await _purchaseSvc.SaveDraftAsync(_model, lines, user: "admin");
            MessageBox.Show($"Purchase Draft saved. Purchase Id: #{_model.Id}");
            await ResetFormAsync(keepDestination: true);

        }


        private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        {
            Item? pick = ItemList.SelectedItem as Item;
            if (pick == null && _itemResults.Count == 1)
                pick = _itemResults[0];
            if (pick == null && !string.IsNullOrWhiteSpace(ItemSearchText.Text))
            {
                var t = ItemSearchText.Text.Trim();
                pick = _itemResults.FirstOrDefault(i =>
                          string.Equals(i.Sku, t, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(i.Barcode, t, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(i.Name, t, StringComparison.OrdinalIgnoreCase));
            }
            if (pick == null)
            {
                MessageBox.Show("Type to search and pick an item (Enter), or click Add after selecting.");
                return;
            }
            AddItemToLines(pick);
            FinishItemAdd();
        }

        private async void BtnNewItem_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ItemQuickDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var now = DateTime.UtcNow;
                var item = new Pos.Domain.Entities.Item
                {
                    Sku = dlg.Sku,
                    Name = dlg.NameVal,
                    Barcode = dlg.BarcodeVal,                 
                    Price = dlg.PriceVal,
                    UpdatedAt = now,
                    TaxCode = dlg.TaxCodeVal,
                    DefaultTaxRatePct = dlg.TaxPctVal,
                    TaxInclusive = dlg.TaxInclusiveVal,
                    DefaultDiscountPct = dlg.DiscountPctVal,
                    DefaultDiscountAmt = dlg.DiscountAmtVal,
                    Variant1Name = dlg.Variant1NameVal,
                    Variant1Value = dlg.Variant1ValueVal,
                    Variant2Name = dlg.Variant2NameVal,
                    Variant2Value = dlg.Variant2ValueVal,
                    ProductId = null
                };
                item = await _itemsSvc.CreateAsync(item);
                AddItemToLines(item);
                FinishItemAdd();
            }
        }


        private async void BtnNewSupplier_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SupplierQuickDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var now = DateTime.UtcNow;
                // 1) create party
                var party = new Party
                {
                    Name = dlg.SupplierName,
                    Phone = dlg.SupplierPhone,
                    Email = dlg.SupplierEmail,
                    IsActive = true,
                    IsSharedAcrossOutlets = true, // or false + map below
                };
                _db.Parties.Add(party);
                await _db.SaveChangesAsync();

                // 2) add Supplier role
                _db.PartyRoles.Add(new PartyRole { PartyId = party.Id, Role = RoleType.Supplier });
                await _db.SaveChangesAsync();

                // 3) optionally map to current outlet if not shared
                // var outletId = AppState.Current.CurrentOutletId;
                // _db.PartyOutlets.Add(new PartyOutlet { PartyId = party.Id, OutletId = outletId, AllowCredit = false, IsActive = true });
                // await _db.SaveChangesAsync();

                _selectedPartyId = party.Id;
                SupplierText.Text = party.Name;
                SupplierPopup.IsOpen = false;
            }
        }


        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private ObservableCollection<PurchasePayment> _payments = new();

        private async Task RefreshPaymentsAsync()
        {
            if (_model.Id <= 0) { PaymentsGrid.ItemsSource = null; return; }
            var tuple = await _purchaseSvc.GetWithPaymentsAsync(_model.Id);
            _payments.Clear();
            foreach (var p in tuple.payments) _payments.Add(p);
            PaymentsGrid.ItemsSource = _payments;
        }

        private async void BtnAddAdvance_Click(object sender, RoutedEventArgs e)
        {
            if (!await EnsurePurchasePersistedAsync()) return;
            if (!decimal.TryParse(AdvanceAmtBox.Text, out var amt) || amt <= 0)
            {
                MessageBox.Show("Enter amount > 0"); return;
            }
            var sel = (AdvanceMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
            var method = Enum.TryParse<TenderMethod>(sel, out var m) ? m : TenderMethod.Cash;
            var outletId = _model.OutletId ?? 0;
            await _purchaseSvc.AddPaymentAsync(
                _model.Id,
                PurchasePaymentKind.Advance,
                method,
                amt,
                note: null,
                outletId: outletId,
                supplierId: _model.PartyId,
                tillSessionId: null,
                counterId: null,
                user: "admin");
            await RefreshPaymentsAsync();
            AdvanceAmtBox.Clear();
            AdvanceMethodBox.SelectedIndex = 0;
        }

        private async Task<bool> EnsurePurchasePersistedAsync()
        {
            if (_model.Id > 0) return true;
            if (_lines.Count == 0)
            {
                MessageBox.Show("Add at least one item before taking a payment.");
                return false;
            }
            if (!await EnsureSupplierSelectedAsync())
            {
                MessageBox.Show("Please pick a Supplier (press Enter after typing, or choose from the list).");
                return false;
            }
            int? outletId = null, warehouseId = null;
            var target = StockTargetType.Outlet;
            try
            {
                if (DestWarehouseRadio.IsChecked == true && WarehouseBox.SelectedItem is Warehouse wh)
                { warehouseId = wh.Id; target = StockTargetType.Warehouse; }
                else if (DestOutletRadio.IsChecked == true && OutletBox.SelectedItem is Outlet ot)
                { outletId = ot.Id; target = StockTargetType.Outlet; }
            }
            catch { /* ignore if controls not present */ }
            _model.PartyId = _selectedPartyId!.Value;
            _model.TargetType = target;
            _model.OutletId = outletId;
            _model.WarehouseId = warehouseId;
            _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
            _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now;
            _model.Status = PurchaseStatus.Draft;
            var lines = _lines.Select(l => new PurchaseLine
            {
                ItemId = l.ItemId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
                Discount = l.Discount,
                TaxRate = l.TaxRate,
                Notes = l.Notes
            });
            _model = await _purchaseSvc.SaveDraftAsync(_model, lines, user: "admin");
            await RefreshPaymentsAsync(); // bind grid to the now-real purchase
            return _model.Id > 0;
        }

        private async Task ResetFormAsync(bool keepDestination = true)
        {
            try { SupplierPopup.IsOpen = false; } catch { }
            try { ItemPopup.IsOpen = false; } catch { }
            bool chooseWarehouse = false;
            int? selWarehouseId = null, selOutletId = null;
            if (keepDestination)
            {
                try
                {
                    chooseWarehouse = DestWarehouseRadio.IsChecked == true;
                    selWarehouseId = (WarehouseBox.SelectedItem as Warehouse)?.Id;
                    selOutletId = (OutletBox.SelectedItem as Outlet)?.Id;
                }
                catch { }
            }
            _model = new Purchase();
            _selectedPartyId = null;
            SupplierText.Clear();
            VendorInvBox.Clear();
            DatePicker.SelectedDate = DateTime.Now;
            _lines.Clear();
            _payments.Clear();
            PaymentsGrid.ItemsSource = null; // rebounded when a real purchase is loaded
            ItemSearchText.Clear();
            OtherChargesBox.Text = "0.00";
            SubtotalText.Text = "0.00";
            DiscountText.Text = "0.00";
            TaxText.Text = "0.00";
            GrandTotalText.Text = "0.00";
            await LoadSuppliersAsync("");
            await LoadItemsAsync("");
            if (!keepDestination)
            {
                await InitDestinationsAsync();
            }
            else
            {
                try { OutletBox.ItemsSource = _outletResults; } catch { }
                try { WarehouseBox.ItemsSource = _warehouseResults; } catch { }
                try
                {
                    if (chooseWarehouse)
                    {
                        DestWarehouseRadio.IsChecked = true;
                        if (selWarehouseId != null)
                            WarehouseBox.SelectedItem = _warehouseResults.FirstOrDefault(w => w.Id == selWarehouseId);
                        WarehouseBox.IsEnabled = true;
                        OutletBox.IsEnabled = false;
                    }
                    else
                    {
                        DestOutletRadio.IsChecked = true;
                        if (selOutletId != null)
                            OutletBox.SelectedItem = _outletResults.FirstOrDefault(o => o.Id == selOutletId);
                        WarehouseBox.IsEnabled = false;
                        OutletBox.IsEnabled = true;
                    }
                }
                catch { /* ignore if controls not present */ }
            }
            SupplierText.Focus();
            SupplierText.CaretIndex = SupplierText.Text?.Length ?? 0;
        }

        private async void InvoicesButton_Click(object sender, RoutedEventArgs e)
        {
            var center = new PurchaseCenterWindow() { Owner = this }; // pass options
            var ok = center.ShowDialog() == true;

            if (ok && center.SelectedHeldPurchaseId.HasValue)
            {
                await ResumeHeldAsync(center.SelectedHeldPurchaseId.Value);
            }
        }

        private async Task ResumeHeldAsync(int id)
        {
            var draft = await _db.Purchases
                .Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == PurchaseStatus.Draft);

            if (draft == null)
            {
                MessageBox.Show($"Held purchase #{id} not found or not in Draft.", "Not found");
                return;
            }

            LoadDraft(draft); // loads into THIS window
        }

        private void ClearHoldButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Choose an action:\n\nYes = CLEAR this purchase (reset form)\nNo = HOLD this purchase as draft\nCancel = Do nothing",
                "Clear or Hold?",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ClearCurrentPurchase(confirm: true);
            }
            else if (result == MessageBoxResult.No)
            {
                _ = HoldCurrentPurchaseQuickAsync();
            }
        }

        private async void ClearCurrentPurchase(bool confirm)
        {
            // quick “is anything to clear?” check
            var hasLines = _lines.Count > 0;
            var hasSupplier = !string.IsNullOrWhiteSpace(SupplierText.Text);
            var hasVendorInv = !string.IsNullOrWhiteSpace(VendorInvBox.Text);
            var otherCharges = decimal.TryParse(OtherChargesBox.Text, out var oc) ? oc : 0m;
            var dirtyTotals = _model.Subtotal > 0m || _model.Tax > 0m || _model.Discount > 0m || _model.GrandTotal > 0m || otherCharges != 0m;

            if (confirm && (hasLines || hasSupplier || hasVendorInv || dirtyTotals))
            {
                var ok = MessageBox.Show("Clear the current purchase (lines, supplier, vendor inv#, totals)?",
                                         "Confirm Clear", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }

            await ResetFormAsync(keepDestination: true);   // keep destination like your Save Draft does
        }

        private async Task HoldCurrentPurchaseQuickAsync()
        {
            // Basic validations (mirror BtnSaveDraft_Click)
            if (_lines.Count == 0)
            {
                MessageBox.Show("Nothing to hold — add at least one item.");
                return;
            }
            if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
            {
                MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative.");
                return;
            }
            foreach (var l in _lines)
            {
                var baseAmt = l.Qty * l.UnitCost;
                if (l.Discount > baseAmt)
                {
                    MessageBox.Show($"Discount exceeds base amount for item '{l.Name}'.");
                    return;
                }
            }
            if (!await EnsureSupplierSelectedAsync())
            {
                MessageBox.Show("Please pick a Supplier (press Enter after typing, or choose from the list).");
                return;
            }
            if (_selectedPartyId == null)
            {
                MessageBox.Show("Please pick a Supplier (type and press Enter or double-click from list).");
                return;
            }

            // Destination (same logic you used in BtnSaveDraft_Click)
            int? outletId = null;
            int? warehouseId = null;
            StockTargetType target;
            try
            {
                if (DestWarehouseRadio.IsChecked == true)
                {
                    if (WarehouseBox.SelectedItem is not Warehouse wh)
                    {
                        MessageBox.Show("Please pick a warehouse."); return;
                    }
                    warehouseId = wh.Id;
                    target = StockTargetType.Warehouse;
                }
                else if (DestOutletRadio.IsChecked == true)
                {
                    if (OutletBox.SelectedItem is not Outlet ot)
                    {
                        MessageBox.Show("Please pick an outlet."); return;
                    }
                    outletId = ot.Id;
                    target = StockTargetType.Outlet;
                }
                else
                {
                    target = StockTargetType.Outlet; // legacy fallback
                }
            }
            catch
            {
                target = StockTargetType.Outlet; // UI not present
            }

            // Build model and lines exactly like BtnSaveDraft_Click
            _model.PartyId = _selectedPartyId.Value;
            _model.TargetType = target;
            _model.OutletId = outletId;
            _model.WarehouseId = warehouseId;
            _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
            _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now; // UI date
            _model.Status = PurchaseStatus.Draft;

            var lines = _lines.Select(l => new PurchaseLine
            {
                ItemId = l.ItemId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
                Discount = l.Discount,
                TaxRate = l.TaxRate,
                Notes = l.Notes
            });

            // Save draft (HOLD)
            _model = await _purchaseSvc.SaveDraftAsync(_model, lines, user: "admin");

            MessageBox.Show($"Purchase held as Draft. Purchase Id: #{_model.Id}", "Held");
            await ResetFormAsync(keepDestination: true);
        }

        public async void LoadDraft(Purchase draft)
        {
            if (draft == null) { MessageBox.Show("Draft not found."); return; }

            // Ensure lines are loaded (defensive)
            if (draft.Lines == null || draft.Lines.Count == 0)
            {
                try
                {
                    draft = await _db.Purchases
                        .Include(p => p.Lines)
                        .FirstAsync(p => p.Id == draft.Id);
                }
                catch { /* ignore, keep whatever came */ }
            }

            // Keep a copy as the working model
            _model = draft;

            // Header → UI (these are what your save/hold reads from)
            DatePicker.SelectedDate = draft.PurchaseDate;
            VendorInvBox.Text = draft.VendorInvoiceNo ?? "";
            OtherChargesBox.Text = draft.OtherCharges.ToString("0.00");

            // Supplier box text (lookup name)
            try
            {
                var p = await _db.Set<Party>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == draft.PartyId);
                _selectedPartyId = draft.PartyId;
                SupplierText.Text = p?.Name ?? $"Supplier #{draft.PartyId}";
            }
            catch
            {
                _selectedPartyId = draft.PartyId;
                SupplierText.Text = $"Supplier #{draft.PartyId}";
            }

            // Destination radios/combos
            try
            {
                if (draft.TargetType == StockTargetType.Warehouse && draft.WarehouseId.HasValue)
                {
                    DestWarehouseRadio.IsChecked = true;
                    WarehouseBox.IsEnabled = true; OutletBox.IsEnabled = false;
                    var wh = _warehouseResults.FirstOrDefault(w => w.Id == draft.WarehouseId);
                    if (wh != null) WarehouseBox.SelectedItem = wh;
                }
                else
                {
                    DestOutletRadio.IsChecked = true;
                    OutletBox.IsEnabled = true; WarehouseBox.IsEnabled = false;
                    if (draft.OutletId.HasValue)
                    {
                        var ot = _outletResults.FirstOrDefault(o => o.Id == draft.OutletId);
                        if (ot != null) OutletBox.SelectedItem = ot;
                    }
                }
            }
            catch { /* controls may not exist yet */ }

            // Lines → the grid's source (_lines), not VM.Lines
            _lines.Clear();
            // ---- pull item meta for all ItemIds in the draft lines (single query) ----
            var lines = draft.Lines ?? new List<PurchaseLine>();
            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();

            // SIMPLE version (safe and enough for SKU + Name):
            var metaDict = await _db.Set<Item>()
                .AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, Name = i.Name })
                .ToDictionaryAsync(x => x.Id);

            _lines.Clear();
            foreach (var l in lines)
            {
                metaDict.TryGetValue(l.ItemId, out var m);
                var row = new PurchaseLineVM
                {
                    ItemId = l.ItemId,
                    Sku = m?.Sku ?? "",
                    Name = m?.Name ?? $"Item #{l.ItemId}",
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    Notes = l.Notes
                };
                row.ForceRecalc();
                _lines.Add(row);
            }


            // Totals textboxes (what your save uses)
            SubtotalText.Text = draft.Subtotal.ToString("0.00");
            DiscountText.Text = draft.Discount.ToString("0.00");
            TaxText.Text = draft.Tax.ToString("0.00");
            GrandTotalText.Text = draft.GrandTotal.ToString("0.00");

            // (Optional) also mirror into VM if you have bindings elsewhere
            VM.PurchaseId = draft.Id;
            VM.SupplierId = draft.PartyId;
            VM.TargetType = draft.TargetType;
            VM.OutletId = draft.OutletId;
            VM.WarehouseId = draft.WarehouseId;
            VM.PurchaseDate = draft.PurchaseDate;
            VM.VendorInvoiceNo = draft.VendorInvoiceNo;
            VM.DocNo = draft.DocNo;
            VM.Subtotal = draft.Subtotal;
            VM.Discount = draft.Discount;
            VM.Tax = draft.Tax;
            VM.OtherCharges = draft.OtherCharges;
            VM.GrandTotal = draft.GrandTotal;
            VM.Status = PurchaseStatus.Draft;
            VM.IsDirty = false;
        }


        //private async Task LoadSupplierSuggestionsAsync(string term)
        //{
        //    using var db = new PosClientDbContext(_opts);
        //    var svc = new PartyLookupService(db);
        //    var list = await svc.SearchSuppliersAsync(term ?? string.Empty, AppState.Current.CurrentOutletId);
        //    SupplierText.SourceUpdated = list;          // your suppliers dropdown/list
        //}

        private async Task<Party?> FindSupplierByExactNameAsync(string name)
        {
            using var db = new PosClientDbContext(_opts);
            var svc = new PartyLookupService(db);
            return await svc.FindSupplierByExactNameAsync(name ?? string.Empty, AppState.Current.CurrentOutletId);
        }



    }
}
