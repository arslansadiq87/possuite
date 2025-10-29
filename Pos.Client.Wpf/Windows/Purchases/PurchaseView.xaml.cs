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
using System.Windows.Data;
using Microsoft.VisualBasic;
using System.Linq;            // at top of PurchasesService.cs
using System.Windows.Media;


namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseView : UserControl
    {
        public enum PurchaseEditorMode { Auto, Draft, Amend }
        public static readonly DependencyProperty ModeProperty =
           DependencyProperty.Register(
               nameof(Mode),
               typeof(PurchaseEditorMode),
               typeof(PurchaseView),
               new PropertyMetadata(PurchaseEditorMode.Auto, OnModeChanged));

        public PurchaseEditorMode Mode
        {
            get => (PurchaseEditorMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        private readonly PartyLookupService _partySvc;
        private bool _suppressSupplierPopup;
        private static bool IsNewItemPlaceholder(object? o) => Equals(o, CollectionView.NewItemPlaceholder);

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

        public PurchaseView()
        {
            InitializeComponent();
            // -- ensure DataContext --
            if (DataContext is not PurchaseEditorVM) DataContext = new PurchaseEditorVM();
            // -------- INIT CORE SERVICES (so _db isn't null) --------
            _db = new PosClientDbContext(_opts);
            _purchaseSvc = new PurchasesService(_db);
            _partySvc = new PartyLookupService(_db);
            _itemsSvc = new ItemsService(_db);
            // -------- INIT UI BINDINGS / EVENTS (same as your db-ctor) --------
            DatePicker.SelectedDate = DateTime.Now;
            OtherChargesBox.Text = "0.00";
            LinesGrid.ItemsSource = _lines;
            SupplierList.ItemsSource = _supplierResults;
            //ItemList.ItemsSource = _itemResults;
            DatePicker.PreviewKeyDown += DatePicker_PreviewKeyDown;

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
                ApplyDestinationPermissionGuard();
                SupplierText.Focus();
                SupplierText.CaretIndex = SupplierText.Text?.Length ?? 0;
            };

            //ItemList.PreviewKeyDown += ItemList_PreviewKeyDown;
            SupplierList.PreviewKeyDown += SupplierList_PreviewKeyDown;

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F5) { ClearCurrentPurchase(confirm: true); e.Handled = true; return; }
                if (e.Key == Key.F8) { _ = HoldCurrentPurchaseQuickAsync(); e.Handled = true; return; }
                if (e.Key == Key.F9) { BtnSaveFinal_Click(s, e); e.Handled = true; return; }
            };
        }

        private void ApplyDestinationPermissionGuard()
        {
            bool canPick = CanSelectDestination();
            // If user cannot pick, force Outlet = current outlet and disable all destination controls.
            if (!canPick)
            {
                try
                {
                    DestWarehouseRadio.IsEnabled = false;
                    WarehouseBox.IsEnabled = false;
                    DestOutletRadio.IsEnabled = false;
                    OutletBox.IsEnabled = false;
                    // Force to current outlet
                    var currentOutletId = AppState.Current.CurrentOutletId;
                    var match = _outletResults.FirstOrDefault(o => o.Id == currentOutletId);
                    DestOutletRadio.IsChecked = true;
                    OutletBox.IsEnabled = true;   // enable just long enough to set selection
                    OutletBox.SelectedItem = match ?? _outletResults.FirstOrDefault();
                    OutletBox.IsEnabled = false;
                    // Keep UI consistent
                    //WarehouseBox.IsEnabled = false;
                }
                catch { /* controls might not exist in some XAML variants */ }
            }
        }

        private void DatePicker_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                FocusItemSearchBox();
            }
        }

        public PurchaseView(PosClientDbContext db) : this()  // CHANGED: chain to parameterless
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
            //ItemList.ItemsSource = _itemResults;
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
                ApplyDestinationPermissionGuard();
                SupplierText.Focus();
                SupplierText.CaretIndex = SupplierText.Text?.Length ?? 0;
            };
            //ItemList.PreviewKeyDown += ItemList_PreviewKeyDown;
            SupplierList.PreviewKeyDown += SupplierList_PreviewKeyDown;
            this.PreviewKeyDown += (s, e) =>
            {
                //if (e.Key == Key.F7) { InvoicesButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.F5) { ClearCurrentPurchase(confirm: true); e.Handled = true; return; }
                if (e.Key == Key.F8) { _ = HoldCurrentPurchaseQuickAsync(); e.Handled = true; return; }
                if (e.Key == Key.F9) { BtnSaveFinal_Click(s, e); e.Handled = true; return; }
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
            try { OutletBox.Items.Refresh(); } catch { }
            try { WarehouseBox.Items.Refresh(); } catch { }
            // Basic defaults: if you have warehouses, default to Warehouse; else Outlet
            if (!(_model != null && _model.Id > 0))
            {
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
        }
        // Put this inside the PurchaseWindow class
        // Admins (and only them) can choose destination.
        // Handles: "Admin", "admin", "Administrator", "SuperAdmin", or role lists like "Admin,Manager".
        private static bool CanSelectDestination()
        {
            // 1) Username fallback (you already expose CurrentUserName)
            var uname = AppState.Current?.CurrentUserName;
            if (!string.IsNullOrWhiteSpace(uname) &&
                uname.Equals("admin", StringComparison.OrdinalIgnoreCase))
                return true;
            // 2) Role string (supports single or comma/semicolon/pipe-separated lists)
            var rolesRaw = AppState.Current?.CurrentUserRole ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rolesRaw)) return false;
            var roles = rolesRaw
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim());
            foreach (var r in roles)
            {
                if (r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("Administrator", StringComparison.OrdinalIgnoreCase) ||
                    r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false; // default: no permission
        }

        // Put this inside the PurchaseWindow class
        private void EnforceDestinationPolicy(ref StockTargetType target, ref int? outletId, ref int? warehouseId)
        {
            if (!CanSelectDestination())
            {
                target = StockTargetType.Outlet;
                outletId = AppState.Current.CurrentOutletId;
                warehouseId = null;
            }
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

        private void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var pick = ((Pos.Client.Wpf.Controls.ItemSearchBox)sender).SelectedItem;
            if (pick is null) return;

            //await EnsurePurchasePersistedAsync(); // if your flow needs a header first
            AddItemToLines(pick);                 // everything else is handled inside
        }



        // ===================== Grid fast entry (kept) =====================
        private void LinesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (LinesGrid.CurrentItem is not PurchaseLineVM row) return;

            e.Handled = true;

            var col = LinesGrid.CurrentColumn;
            var isQty = string.Equals(col?.Header as string, "Qty", StringComparison.OrdinalIgnoreCase);
            var isNotes = string.Equals(col?.Header as string, "Notes", StringComparison.OrdinalIgnoreCase);

            // Commit current cell first so bindings update
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);

            //if (isQty)
            //{
            //    BeginEditOn(row, "Notes");                   // move to Notes
            //    return;
            //}
            if (isNotes)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                LinesGrid.CurrentCell = new DataGridCellInfo(); // avoid snap-back
                FocusItemSearchBox();                            // jump to search
                return;
            }

            // Default: move forward in your flow (Qty → Price → Disc → Tax % → Notes)
            var flow = new[] { "Qty", "Price", "Disc", "Tax %", "Notes" };
            string current = (col?.Header as string ?? "").Trim();
            int i = Array.FindIndex(flow, h => h.Equals(current, StringComparison.OrdinalIgnoreCase));
            string? next = (i >= 0 && i < flow.Length - 1) ? flow[i + 1] : null;

            if (next == null)
            {
                FocusItemSearchBox();
                return;
            }
            BeginEditOn(row, next);
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var res = FindDescendant<T>(child);
                if (res != null) return res;
            }
            return null;
        }

        private void FocusItemSearchBox()
        {
            try
            {
                var tb = FindDescendant<TextBox>(ItemSearch); // ItemSearch = your ItemSearchBox
                if (tb != null) { tb.Focus(); tb.SelectAll(); }
                else ItemSearch.Focus();
            }
            catch { }
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
                //ItemSearchText.Focus();
                //ItemSearchText.SelectAll();
                FocusItemSearchBox();
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

        private async Task LoadSuppliersAsync(string term, bool allowPopup = true)
        {
            var outletId = AppState.Current.CurrentOutletId;
            var list = await _partySvc.SearchSuppliersAsync(term, outletId);

            _supplierResults.Clear();
            foreach (var p in list) _supplierResults.Add(p);

            // only open popup if allowed, not suppressed, and user actually typed/focused
            SupplierPopup.IsOpen = allowPopup
                                   && !_suppressSupplierPopup
                                   && _supplierResults.Count > 0
                                   && !string.IsNullOrWhiteSpace(SupplierText.Text)
                                   && SupplierText.IsKeyboardFocusWithin;

            if (_supplierResults.Count > 0 && SupplierList.SelectedIndex < 0)
                SupplierList.SelectedIndex = 0;
        }



        private async void SupplierText_TextChanged(object sender, TextChangedEventArgs e)
        {
            _selectedPartyId = null;

            if (_suppressSupplierPopup)
                return; // don’t search/open while we’re programmatically setting text

            await LoadSuppliersAsync(SupplierText.Text ?? "", allowPopup: true);
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

       
        //private async Task LoadItemsAsync(string term)
        //{
        //    var list = await _itemsSvc.SearchAsync(term);
        //    _itemResults.Clear();
        //    foreach (var it in list) _itemResults.Add(it);
        //    ItemPopup.IsOpen = _itemResults.Count > 0 && !string.IsNullOrWhiteSpace(ItemSearchText.Text);
        //    if (_itemResults.Count > 0) ItemList.SelectedIndex = 0;
        //}

        
             
       
        //private void FinishItemAdd()
        //{
        //    RecomputeAndUpdateTotals();
        //    ItemPopup.IsOpen = false;
        //    ItemSearchText.Clear();
        //    ItemList.SelectedItem = null;
        //    ItemSearchText.Focus();
        //}

        private async Task ApplyLastDefaultsAsync(PurchaseLineVM vm)
        {
            var last = await _purchaseSvc.GetLastPurchaseDefaultsAsync(vm.ItemId);
            if (last is null) return;
            vm.UnitCost = last.Value.unitCost;
            vm.Discount = last.Value.discount;
            vm.TaxRate = last.Value.taxRate;
            vm.ForceRecalc();
        }

        // using Pos.Domain.DTO;

        private void AddItemToLines(Pos.Domain.DTO.ItemIndexDto dto)
        {
            // If row already exists → bump qty, recalc, totals, focus Qty
            var existing = _lines.FirstOrDefault(l => l.ItemId == dto.Id);
            if (existing != null)
            {
                existing.Qty += 1m;
                existing.ForceRecalc();
                RecomputeAndUpdateTotals();
                BeginEditOn(existing, "Qty");
                return;
            }

            // Map only what the grid needs; fall back safely if DTO lacks some fields
            var vm = new PurchaseLineVM
            {
                ItemId = dto.Id,
                Sku = dto.Sku ?? "",
                Name = dto.DisplayName ?? dto.Name ?? $"Item #{dto.Id}",
                Qty = 1m,
                UnitCost = dto.Price,                 // DTO price may be null → use 0
                Discount = 0m,
                TaxRate = dto.DefaultTaxRatePct,     // DTO tax may be null → use 0
                Notes = null
            };

            vm.ForceRecalc();                // keep Line Total correct immediately
            _lines.Add(vm);
            RecomputeAndUpdateTotals();

            // Optionally pull “last purchase defaults” and refresh totals after they arrive
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await ApplyLastDefaultsAsync(vm);          // may adjust UnitCost/Discount/TaxRate
                vm.ForceRecalc();
                RecomputeAndUpdateTotals();
            });

            // Focus Qty for fast entry
            Dispatcher.BeginInvoke(() =>
            {
                LinesGrid.UpdateLayout();
                LinesGrid.SelectedItem = vm;
                LinesGrid.ScrollIntoView(vm);
                var qtyCol = LinesGrid.Columns.First(c =>
                    string.Equals(c.Header as string, "Qty", StringComparison.OrdinalIgnoreCase));
                LinesGrid.CurrentCell = new DataGridCellInfo(vm, qtyCol);
                LinesGrid.BeginEdit();
                if (qtyCol.GetCellContent(vm) is TextBox tb) tb.SelectAll();
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
            MessageBox.Show($"Purchase held as Draft. Purchase Id: #{_model.Id}", "Held");
            await ResetFormAsync(keepDestination: true);

        }

        // Button: Save (FINAL)
        private async void BtnSaveFinal_Click(object sender, RoutedEventArgs e)
        {
            // optional: disable save UI while working
            try { ((FrameworkElement)sender).IsEnabled = false; } catch { }

            try
            {
                // ---------- keep your existing pre-save validations ----------
                RecomputeAndUpdateTotals();

                if (_lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }
                if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
                { MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative."); return; }

                foreach (var l in _lines)
                {
                    var baseAmt = l.Qty * l.UnitCost;
                    if (l.Discount > baseAmt)
                    { MessageBox.Show($"Discount exceeds base amount for item '{l.Name}'."); return; }
                }

                if (!await EnsureSupplierSelectedAsync())
                { MessageBox.Show("Please pick a Supplier (press Enter after typing, or choose from the list)."); return; }

                if (_selectedPartyId == null)
                { MessageBox.Show("Please pick a Supplier (type and press Enter or double-click from list)."); return; }

                // ---------- destination ----------
                int? outletId = null, warehouseId = null;
                StockTargetType target;
                try
                {
                    if (DestWarehouseRadio.IsChecked == true)
                    {
                        if (WarehouseBox.SelectedItem is not Warehouse wh) { MessageBox.Show("Please pick a warehouse."); return; }
                        warehouseId = wh.Id; target = StockTargetType.Warehouse;
                    }
                    else if (DestOutletRadio.IsChecked == true)
                    {
                        if (OutletBox.SelectedItem is not Outlet ot) { MessageBox.Show("Please pick an outlet."); return; }
                        outletId = ot.Id; target = StockTargetType.Outlet;
                    }
                    else { target = StockTargetType.Outlet; }
                }
                catch { target = StockTargetType.Outlet; }

                EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);

                // ---------- header ----------
                _model.PartyId = _selectedPartyId.Value;
                _model.TargetType = target;
                _model.OutletId = outletId;
                _model.WarehouseId = warehouseId;
                _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
                _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now;
                _model.Status = PurchaseStatus.Final;

                // ---------- lines ----------
                var lines = _lines.Select(l => new PurchaseLine
                {
                    ItemId = l.ItemId,
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    Notes = l.Notes
                });

                var user = AppState.Current?.CurrentUserName ?? "admin";

                // ---------- SAVE (catch business-rule exceptions cleanly) ----------
                try
                {
                    _model = await _purchaseSvc.ReceiveAsync(_model, lines, user);

                    MessageBox.Show(
                        $"Purchase finalized.\nDoc #: {(_model.DocNo ?? $"#{_model.Id}")}\nTotal: {_model.GrandTotal:N2}",
                        "Saved (Final)", MessageBoxButton.OK, MessageBoxImage.Information);

                    await ResetFormAsync(keepDestination: true);
                }
                catch (InvalidOperationException ex)
                {
                    // e.g., “amendment would make stock negative” or “below returned qty”
                    MessageBox.Show(ex.Message, "Amendment blocked",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                catch (DbUpdateException ex)
                {
                    MessageBox.Show("Save failed:\n" + ex.GetBaseException().Message,
                        "Database error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unexpected error:\n" + ex.Message,
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            finally
            {
                try { ((FrameworkElement)sender).IsEnabled = true; } catch { }
            }
        }



        //private void BtnAddItem_Click(object sender, RoutedEventArgs e)
        //{
        //    Item? pick = ItemList.SelectedItem as Item;

        //    if (pick == null && _itemResults.Count == 1)
        //        pick = _itemResults[0];

        //    if (pick == null && !string.IsNullOrWhiteSpace(ItemSearchText.Text))
        //    {
        //        var t = ItemSearchText.Text.Trim();

        //        // Ensure _itemResults contains Barcodes; if it's populated from EF, include .Include(i => i.Barcodes)
        //        pick = _itemResults.FirstOrDefault(i =>
        //            string.Equals(i.Sku, t, StringComparison.OrdinalIgnoreCase) ||
        //            string.Equals(i.Name, t, StringComparison.OrdinalIgnoreCase) ||
        //            (i.Barcodes != null && i.Barcodes.Any(b => string.Equals(b.Code, t, StringComparison.OrdinalIgnoreCase)))
        //        );
        //    }

        //    if (pick == null)
        //    {
        //        MessageBox.Show("Type to search and pick an item (Enter), or click Add after selecting.");
        //        return;
        //    }

        //    // If AddItemToLines expects a barcode string, use the primary from the collection:
        //    // var primary = pick.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
        //    //               ?? pick.Barcodes?.FirstOrDefault()?.Code ?? "";

        //    AddItemToLines(pick);
        //    FinishItemAdd();
        //}


        //private async void BtnNewItem_Click(object sender, RoutedEventArgs e)
        //{
        //    var dlg = new ItemQuickDialog { };
        //    if (dlg.ShowDialog() == true)
        //    {
        //        var now = DateTime.UtcNow;

        //        // Build barcode list (primary if provided)
        //        var barcodes = new List<ItemBarcode>();
        //        var code = (dlg.BarcodeVal ?? "").Trim();
        //        if (!string.IsNullOrWhiteSpace(code))
        //        {
        //            barcodes.Add(new ItemBarcode
        //            {
        //                Code = code,
        //                Symbology = BarcodeSymbology.Ean13, // or map from dialog if you expose it
        //                QuantityPerScan = 1,
        //                IsPrimary = true,
        //                CreatedAt = now,
        //                UpdatedAt = now
        //            });
        //        }

        //        var item = new Pos.Domain.Entities.Item
        //        {
        //            Sku = dlg.Sku,
        //            Name = dlg.NameVal,
        //            // Barcode = dlg.BarcodeVal, // ❌ legacy – remove
        //            Price = dlg.PriceVal,
        //            UpdatedAt = now,
        //            TaxCode = dlg.TaxCodeVal,
        //            DefaultTaxRatePct = dlg.TaxPctVal,
        //            TaxInclusive = dlg.TaxInclusiveVal,
        //            DefaultDiscountPct = dlg.DiscountPctVal,
        //            DefaultDiscountAmt = dlg.DiscountAmtVal,
        //            Variant1Name = dlg.Variant1NameVal,
        //            Variant1Value = dlg.Variant1ValueVal,
        //            Variant2Name = dlg.Variant2NameVal,
        //            Variant2Value = dlg.Variant2ValueVal,
        //            ProductId = null,
        //            Barcodes = barcodes
        //        };

        //        try
        //        {
        //            item = await _itemsSvc.CreateAsync(item); // must save children (Barcodes) too

        //            // If AddItemToLines/UI needs the barcode string, pick primary:
        //            // var primary = item.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
        //            //               ?? item.Barcodes?.FirstOrDefault()?.Code ?? "";

        //            AddItemToLines(item);
        //            FinishItemAdd();
        //        }
        //        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        //        {
        //            // Most common cause: barcode duplicate (unique index on ItemBarcodes.Code)
        //            MessageBox.Show(
        //                ex.InnerException?.Message?.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
        //                    ? "This barcode already exists. Please use a unique code."
        //                    : "Couldn’t save item. " + ex.Message);
        //        }
        //    }
        //}


        private async void BtnNewSupplier_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SupplierQuickDialog { };
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

            EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);

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
            //try { ItemPopup.IsOpen = false; } catch { }
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
            //ItemSearchText.Clear();
            OtherChargesBox.Text = "0.00";
            SubtotalText.Text = "0.00";
            DiscountText.Text = "0.00";
            TaxText.Text = "0.00";
            GrandTotalText.Text = "0.00";
            await LoadSuppliersAsync("");
            //await LoadItemsAsync("");
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

        //private async void InvoicesButton_Click(object sender, RoutedEventArgs e)
        //{
        //    var center = new PurchaseCenterView() { }; // pass options
        //    var ok = center.ShowDialog() == true;

        //    if (ok && center.SelectedHeldPurchaseId.HasValue)
        //    {
        //        await ResumeHeldAsync(center.SelectedHeldPurchaseId.Value);
        //    }
        //}

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

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            // Confirm before clearing, optional
            if (MessageBox.Show("Clear this purchase form?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ClearCurrentPurchase(confirm: true);
            }
        }

        private async void HoldButton_Click(object sender, RoutedEventArgs e)
        {
            // Hold (save as draft) without prompt
            await HoldCurrentPurchaseQuickAsync();
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

            EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);

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
                _suppressSupplierPopup = true;           // ← start suppressing
                var p = await _db.Set<Party>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == draft.PartyId);
                _selectedPartyId = draft.PartyId;
                SupplierText.Text = p?.Name ?? $"Supplier #{draft.PartyId}";
                SupplierPopup.IsOpen = false;            // force closed just in case
            }
            finally
            {
                _suppressSupplierPopup = false;          // ← end suppressing
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

            // >>> INSERT THIS BLOCK *RIGHT HERE* <<<
            try
            {
                if (!CanSelectDestination())
                {
                    // Lock destination controls for non-privileged users
                    DestWarehouseRadio.IsEnabled = false;
                    WarehouseBox.IsEnabled = false;
                    DestOutletRadio.IsEnabled = false;
                    OutletBox.IsEnabled = false;

                    // Optional info if draft belongs to a different outlet than the current user
                    var myOutletId = AppState.Current.CurrentOutletId;
                    var draftOutletId = draft.TargetType == StockTargetType.Outlet ? draft.OutletId : null;
                    if (draft.TargetType == StockTargetType.Outlet &&
                        draftOutletId.HasValue && draftOutletId.Value != myOutletId)
                    {
                        // Info only; do not force-change destination to avoid surprises.
                        // MessageBox.Show("This draft belongs to a different outlet. Destination is locked.", "Info");
                    }

                    // (Optional) If you also want to avoid confusion around draft toggle:
                    //try { IsDraftBox.IsEnabled = false; } catch { }
                }
            }
            catch { /* swallow UI timing issues */ }
            // <<< END OF INSERT >>>

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
            await RefreshPaymentsAsync();  // loads & binds payments for this draft

        }

        private void DeleteLineButton_Click(object sender, RoutedEventArgs e)
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if ((sender as Button)?.Tag is PurchaseLineVM vm && _lines.Contains(vm))
            {
                _lines.Remove(vm);
                RecomputeAndUpdateTotals();
            }
        }


        private void DeleteSelectedMenu_Click(object sender, RoutedEventArgs e)
            => RemoveSelectedLines();

        // Replace your RemoveSelectedLines with this
        private void RemoveSelectedLines(bool askConfirm = false)
        {
            // Commit any in-progress edit first
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            // Filter out the “new item” placeholder and any non-line objects
            var rows = LinesGrid.SelectedItems
                .OfType<PurchaseLineVM>()             // only real line VMs
                .Where(vm => _lines.Contains(vm))     // belt & suspenders
                .ToList();

            // Also handle case where the only selection is the placeholder
            if (rows.Count == 0)
            {
                // if placeholder is selected, just unselect and bail
                var onlyPlaceholderSelected = LinesGrid.SelectedItems.Count == 1 &&
                                              IsNewItemPlaceholder(LinesGrid.SelectedItems[0]);
                if (onlyPlaceholderSelected)
                    LinesGrid.UnselectAll();
                return;
            }

            if (askConfirm && rows.Count >= 5)
            {
                var ok = MessageBox.Show($"Delete {rows.Count} selected rows?",
                                         "Confirm delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }

            foreach (var r in rows) _lines.Remove(r);
            RecomputeAndUpdateTotals();
        }

        public async Task UpdatePaymentAsync(int paymentId, decimal newAmount, TenderMethod newMethod, string? newNote, string user)
        {
            var pay = await _db.PurchasePayments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (pay is null) throw new InvalidOperationException($"Payment #{paymentId} not found.");

            pay.Amount = newAmount;
            pay.Method = newMethod;
            pay.Note = string.IsNullOrWhiteSpace(newNote) ? null : newNote.Trim();
            pay.UpdatedAtUtc = DateTime.UtcNow;
            pay.UpdatedBy = user;

            await _db.SaveChangesAsync();
        }

        public async Task RemovePaymentAsync(int paymentId, string user)
        {
            var pay = await _db.PurchasePayments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (pay is null) return; // already gone

            _db.PurchasePayments.Remove(pay);
            await _db.SaveChangesAsync();
        }

        private bool CanEditPayments()
        {
            return _model != null && _model.Id > 0 && _model.Status == PurchaseStatus.Draft;
        }

        private async void EditPayment_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditPayments()) { MessageBox.Show("Payments can be edited only for DRAFT purchases."); return; }
            if (PaymentsGrid.SelectedItem is not PurchasePayment pay)
            {
                MessageBox.Show("Select a payment first."); return;
            }

            var amtStr = Interaction.InputBox("Enter new amount:", "Edit Payment", pay.Amount.ToString("0.00"));
            if (string.IsNullOrWhiteSpace(amtStr)) return;
            if (!decimal.TryParse(amtStr, out var newAmt) || newAmt <= 0m)
            {
                MessageBox.Show("Invalid amount."); return;
            }

            var methodStr = Interaction.InputBox("Method (Cash, Card, Bank, MobileWallet, etc.):",
                                                 "Edit Payment Method", pay.Method.ToString());
            if (string.IsNullOrWhiteSpace(methodStr)) return;
            if (!Enum.TryParse<TenderMethod>(methodStr, true, out var newMethod))
            {
                MessageBox.Show("Unknown method."); return;
            }

            var newNote = Interaction.InputBox("Note (optional):", "Edit Payment Note", pay.Note ?? "");

            var user = AppState.Current?.CurrentUserName ?? "system";

            // ✅ Call your local method (not the service)
            await UpdatePaymentAsync(pay.Id, newAmt, newMethod, newNote, user);

            await RefreshPaymentsAsync();
        }

        private async void DeletePayment_Click(object sender, RoutedEventArgs e)
        {
            if (!CanEditPayments()) { MessageBox.Show("Payments can be deleted only for DRAFT purchases."); return; }
            if (PaymentsGrid.SelectedItem is not PurchasePayment pay)
            {
                MessageBox.Show("Select a payment first."); return;
            }

            var ok = MessageBox.Show(
                $"Delete payment #{pay.Id} ({pay.Method}, {pay.Amount:N2})?",
                "Confirm Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;

            var user = AppState.Current?.CurrentUserName ?? "system";

            // ✅ Call your local method (not the service)
            await RemovePaymentAsync(pay.Id, user);

            await RefreshPaymentsAsync();
        }

        // Optional: double-click row to edit
        private void PaymentsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            EditPayment_Click(sender, e);
        }

                
        private void PaymentsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var cm = (sender as DataGrid)?.ContextMenu;
            if (cm == null) return;
            var allowed = CanEditPayments();
            foreach (var item in cm.Items.OfType<MenuItem>()) item.IsEnabled = allowed;
        }



        //New Code
        public static readonly DependencyProperty PurchaseIdProperty =
    DependencyProperty.Register(
        nameof(PurchaseId),
        typeof(int?),
        typeof(PurchaseView),
        new PropertyMetadata(null, OnPurchaseIdChanged));

        public int? PurchaseId
        {
            get => (int?)GetValue(PurchaseIdProperty);
            set => SetValue(PurchaseIdProperty, value);
        }
               
        private static async void OnPurchaseIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var v = (PurchaseView)d;
            if (v.IsLoaded) await v.LoadExistingIfAny();
        }

        private static async void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var v = (PurchaseView)d;
            if (v.IsLoaded) await v.ApplyModeAsync();
        }

        private PurchaseStatus? _loadedStatus;
        private PurchaseEditorMode _currentMode = PurchaseEditorMode.Draft;

        private bool _firstLoadComplete;

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_firstLoadComplete) return;
            _firstLoadComplete = true;

            // your existing init code will run; then:
            await LoadExistingIfAny();
            await ApplyModeAsync();
        }

        private async Task LoadExistingIfAny()
        {
            if (PurchaseId is null or <= 0) return;

            var id = PurchaseId.Value;
            var p = await _db.Purchases
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (p == null)
            {
                MessageBox.Show($"Purchase {id} not found.");
                return;
            }

            // Map to your in-memory model/fields already used throughout this view
            _model = new Purchase
            {
                Id = p.Id,
                PartyId = p.PartyId,
                TargetType = p.TargetType,
                OutletId = p.OutletId,
                WarehouseId = p.WarehouseId,
                VendorInvoiceNo = p.VendorInvoiceNo,
                PurchaseDate = p.PurchaseDate,
                Status = p.Status,
                GrandTotal = p.GrandTotal,
                Discount = p.Discount,
                Tax = p.Tax,               // ✅ matches your entity
                Subtotal = p.Subtotal,
                OtherCharges = p.OtherCharges,
                IsReturn = false
            };

            // Header UI
            try { DatePicker.SelectedDate = _model.PurchaseDate; } catch { }
            // ===== ensure destination pickers reflect the saved invoice (NO defaults) =====
            
            await InitDestinationsAsync();


            await SetDestinationSelectionAsync();

            // ===== end destination restore =====

            // Supplier header textbox in your UI is a look-up TextBox + popup;
            // we only set its text to the Party name for display (selection logic already exists)
            try
            {
                var party = await _db.Set<Party>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == p.PartyId);
                if (party != null)
                {
                    _selectedPartyId = party.Id;         // keep your view-state in sync
                    SupplierText.Text = $"{party.Name}";
                }
            }
            catch { }

            // Lines
            _lines.Clear();
            var effective = await _purchaseSvc.GetEffectiveLinesAsync(id);
            foreach (var e in effective)
            {
                _lines.Add(new PurchaseLineVM
                {
                    ItemId = e.ItemId,
                    Sku = e.Sku ?? "",
                    Name = e.Name ?? "",
                    Qty = e.Qty,              // ← shows last-amended qty
                    UnitCost = e.UnitCost,
                    Discount = e.Discount,
                    TaxRate = e.TaxRate
                });
            }


            RecomputeAndUpdateTotals();
            _loadedStatus = p.Status;
        }

        private Task ApplyModeAsync()
        {
            var effective = Mode;
            if (effective == PurchaseEditorMode.Auto && _loadedStatus.HasValue)
            {
                effective = _loadedStatus == PurchaseStatus.Draft
                    ? PurchaseEditorMode.Draft
                    : PurchaseEditorMode.Amend;
            }

            SetUiForMode(effective);
            _currentMode = effective;
            return Task.CompletedTask;
        }

        private void SetUiForMode(PurchaseEditorMode mode)
        {
            var amend = (mode == PurchaseEditorMode.Amend);

            // In Amend mode, lock high-risk header fields (supplier/date/destination)
            try { SupplierText.IsEnabled = !amend; } catch { }
            try { DatePicker.IsEnabled = !amend; } catch { }
            try { WarehouseBox.IsEnabled = !amend; } catch { }
            try { OutletBox.IsEnabled = !amend; } catch { }

            // Optional: change the main Save button caption to communicate "Amend"
            try
            {
                // The "final save" button in XAML is wired to BtnSaveFinal_Click (no x:Name),
                // but the top-right named button is BtnSave (draft/hold). We won't rename controls;
                // just leave captions as-is to avoid breaking your styles.
                // If you want a caption tweak, add x:Name to the final button in XAML and set Content here.
            }
            catch { }
        }


        private async Task SetDestinationSelectionAsync()
        {
            // Make sure the ItemsSource is already set + measured
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

            try
            {
                if (_model.TargetType == StockTargetType.Outlet && _model.OutletId.HasValue)
                {
                    DestOutletRadio.IsChecked = true;
                    OutletBox.IsEnabled = true;
                    WarehouseBox.IsEnabled = false;

                    // Primary way: by value (needs SelectedValuePath="Id")
                    OutletBox.SelectedValue = _model.OutletId.Value;

                    // Fallback (if value didn’t resolve due to timing), set by reference
                    if (OutletBox.SelectedValue == null)
                    {
                        var ot = _outletResults.FirstOrDefault(o => o.Id == _model.OutletId.Value);
                        if (ot != null) OutletBox.SelectedItem = ot;
                    }
                }
                else if (_model.TargetType == StockTargetType.Warehouse && _model.WarehouseId.HasValue)
                {
                    DestWarehouseRadio.IsChecked = true;
                    WarehouseBox.IsEnabled = true;
                    OutletBox.IsEnabled = false;

                    WarehouseBox.SelectedValue = _model.WarehouseId.Value;
                    if (WarehouseBox.SelectedValue == null)
                    {
                        var wh = _warehouseResults.FirstOrDefault(w => w.Id == _model.WarehouseId.Value);
                        if (wh != null) WarehouseBox.SelectedItem = wh;
                    }
                }
            }
            catch
            {
                // ignore UI timing/layout edge cases
            }
        }




        private (Purchase model, List<PurchaseLine> lines) BuildPurchaseFromVm(bool isDraft)
        {
            var model = new Purchase
            {
                Id = VM.PurchaseId,           // 0 for new, >0 for amend
                PartyId = VM.SupplierId,
                TargetType = VM.TargetType,
                OutletId = VM.TargetType == StockTargetType.Outlet ? VM.OutletId : null,
                WarehouseId = VM.TargetType == StockTargetType.Warehouse ? VM.WarehouseId : null,
                PurchaseDate = VM.PurchaseDate,
                VendorInvoiceNo = VM.VendorInvoiceNo,
                IsReturn = false
            };

            var lines = _lines.Select(l => new PurchaseLine
            {
                ItemId = l.ItemId,
                Qty = Math.Max(0, l.Qty),
                UnitCost = l.UnitCost,
                Discount = l.Discount,
                TaxRate = l.TaxRate
            }).ToList();

            // If your VM keeps totals, you can copy them; otherwise let service recompute.
            return (model, lines);
        }

        // Wrappers so existing XAML Click handlers resolve
        private void SaveDraft_Click(object sender, RoutedEventArgs e) => BtnSaveDraft_Click(sender, e);
        private void PostReceive_Click(object sender, RoutedEventArgs e) => BtnSaveFinal_Click(sender, e);
        private void SaveAmend_Click(object sender, RoutedEventArgs e) => BtnSaveFinal_Click(sender, e);


        private DataGridColumn? GetColumnByHeader(string header) =>
    LinesGrid.Columns.FirstOrDefault(c =>
        string.Equals(c.Header as string, header, StringComparison.OrdinalIgnoreCase));

        private void BeginEditOn(object rowItem, string header)
        {
            var col = GetColumnByHeader(header);
            if (col == null) return;

            LinesGrid.UpdateLayout();
            LinesGrid.SelectedItem = rowItem;
            LinesGrid.ScrollIntoView(rowItem, col);
            LinesGrid.CurrentCell = new DataGridCellInfo(rowItem, col);
            LinesGrid.BeginEdit();

            Dispatcher.BeginInvoke(() =>
            {
                var content = col.GetCellContent(rowItem);
                if (content is TextBox tb) { tb.Focus(); tb.SelectAll(); }
                else (content as FrameworkElement)?.Focus();
            });
        }


    }
}
