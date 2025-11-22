// Pos.Client.Wpf/Windows/Purchases/PurchaseView.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Domain.Entities;
using System.Runtime.CompilerServices;
using Pos.Client.Wpf.Services;
using System.Windows.Data;
using Microsoft.VisualBasic;
using System.Linq;            // at top of PurchasesService.cs
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Services;
using Pos.Persistence.Services;
using Pos.Persistence.Sync;
using Microsoft.EntityFrameworkCore; // for DbUpdateException only

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseView : UserControl
    {
        private bool _uiReady;
        private readonly ObservableCollection<Account> _bankAccounts = new();
        private bool _bankPaymentsAllowed; // true only if InvoiceSettings.PurchaseBankAccountId is set for the outlet
        private readonly List<(TenderMethod method, decimal amount, string? note)> _stagedPayments = new();
        private readonly ObservableCollection<PurchasePayment> _payments = new();
        
        public IReadOnlyList<TenderMethod> PaymentMethodItems { get; } = new[]
        {
            TenderMethod.Cash,
            TenderMethod.Bank,
        };

        public enum PurchaseEditorMode { Auto, Draft, Amend }
        public static readonly DependencyProperty ModeProperty =
           DependencyProperty.Register(
               nameof(Mode),
               typeof(PurchaseEditorMode),
               typeof(PurchaseView),
               new PropertyMetadata(PurchaseEditorMode.Auto, OnModeChanged));
        private int? SupplierId => SupplierSearch?.SelectedCustomerId;

        public PurchaseEditorMode Mode
        {
            get => (PurchaseEditorMode)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }
        
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
            public InventoryLocationType TargetType { get => _targetType; set { _targetType = value; OnChanged(); } }
            private InventoryLocationType _targetType;
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

        private readonly IPurchasesService _purchaseSvc;
        private readonly IInvoiceSettingsLocalService _invSettingSvc;
        private readonly IPartyLookupService _partySvc;
        private readonly ILookupService _lookup;  // ← interface
        private readonly IItemsReadService _itemsSvc;
        private readonly IGlPostingService _gl;
        //private readonly IUserPreferencesService _prefs;
        private readonly IDialogService _dialogs;   // overlay-based confirms, etc.
        private Purchase _model = new();
        private readonly ObservableCollection<PurchaseLineVM> _lines = new();
        private ObservableCollection<Item> _itemResults = new();
        private ObservableCollection<Outlet> _outletResults = new();
        private ObservableCollection<Warehouse> _warehouseResults = new();

        public PurchaseView()
        {
            InitializeComponent();
            _purchaseSvc = App.Services.GetRequiredService<IPurchasesService>();  // ✅
            _invSettingSvc = App.Services.GetRequiredService<IInvoiceSettingsLocalService>();  // ✅
            _partySvc = App.Services.GetRequiredService<IPartyLookupService>();
            _itemsSvc = App.Services.GetRequiredService<IItemsReadService>();
            _gl = App.Services.GetRequiredService<IGlPostingService>();
            //_prefs = App.Services.GetRequiredService<IUserPreferencesService>();
            _lookup = App.Services.GetRequiredService<ILookupService>(); // interface
            _dialogs = App.Services.GetRequiredService<IDialogService>();
            if (DataContext is not PurchaseEditorVM) DataContext = new PurchaseEditorVM();
            DatePicker.SelectedDate = DateTime.Now;
            OtherChargesBox.Text = "0.00";
            LinesGrid.ItemsSource = _lines;
            // Focus the new supplier search box
            Dispatcher.BeginInvoke(() => SupplierSearch.Focus());
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
                await ApplyUserPrefsToDestinationAsync();
                ApplyDestinationPermissionGuard();
                await LoadBanksForCurrentOutletAsync();  // now via service
                ApplyBankConfigToUi();
               
            };
            
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F5) { ClearCurrentPurchase(confirm: true); e.Handled = true; return; }
                if (e.Key == Key.F8) { _ = HoldCurrentPurchaseQuickAsync(); e.Handled = true; return; }
                if (e.Key == Key.F9) { BtnSaveFinal_Click(s, e); e.Handled = true; return; }
            };
        }

        private void AdvanceMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;
            if (sender is not ComboBox cb) return;
            var sel =
                (cb.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? cb.SelectedValue?.ToString()
                ?? cb.Text
                ?? "Cash";
            var isBank = string.Equals(sel, "Bank", StringComparison.OrdinalIgnoreCase);
            if (!_bankPaymentsAllowed && isBank)
            {
                //MessageBox.Show("Bank payments are disabled. Configure a Purchase Bank Account in Invoice Settings for this outlet.");
                _dialogs.AlertAsync($"Bank payments are disabled. Configure a Purchase Bank Account in Invoice Settings for this outlet.", "Bank Settings Disabled");
                try { cb.SelectedIndex = 0; } catch { }
                isBank = false;
            }
            try
            {
                BankPickerPanel.Visibility = isBank ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private int? GetCurrentOutletIdForHeader()
        {
            try
            {
                if (DestOutletRadio.IsChecked == true && OutletBox.SelectedItem is Outlet ot)
                    return ot.Id;
            }
            catch { }
            return _model?.OutletId ?? AppState.Current?.CurrentOutletId;
        }

        private async Task<bool> IsPurchaseBankConfiguredAsync(int outletId)
    => await _purchaseSvc.IsPurchaseBankConfiguredAsync(outletId);

        private async Task LoadBanksForCurrentOutletAsync()
        {
            BankAccountBox.ItemsSource = null;
            _bankAccounts.Clear();
            var outletId = GetCurrentOutletIdForHeader() ?? 0;
            if (outletId == 0) { _bankPaymentsAllowed = false; return; }
            _bankPaymentsAllowed = await _purchaseSvc.IsPurchaseBankConfiguredAsync(outletId);
            var banks = await _purchaseSvc.ListBankAccountsForOutletAsync(outletId);
            foreach (var b in banks) _bankAccounts.Add(b);
            BankAccountBox.ItemsSource = _bankAccounts;
            var defId = await _purchaseSvc.GetConfiguredPurchaseBankAccountIdAsync(outletId);
            if (defId is int id)
            {
                var match = _bankAccounts.FirstOrDefault(x => x.Id == id);
                if (match != null) BankAccountBox.SelectedItem = match;
            }
        }
        private void ApplyBankConfigToUi()
        {
            if (!_bankPaymentsAllowed)
            {
                try
                {
                    AdvanceMethodBankItem.IsEnabled = false;
                    BankPickerPanel.Visibility = Visibility.Collapsed;
                    var sel = (AdvanceMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
                    if (!string.Equals(sel, "Cash", StringComparison.OrdinalIgnoreCase))
                        AdvanceMethodBox.SelectedItem = AdvanceMethodCashItem;
                }
                catch { }
            }
            else
            {
                try { AdvanceMethodBankItem.IsEnabled = true; } catch { }
            }
        }

        private void ApplyDestinationPermissionGuard()
        {
            bool canPick = CanSelectDestination();
            if (!canPick)
            {
                try
                {
                    DestWarehouseRadio.IsEnabled = false;
                    WarehouseBox.IsEnabled = false;
                    DestOutletRadio.IsEnabled = false;
                    OutletBox.IsEnabled = false;
                    var currentOutletId = AppState.Current.CurrentOutletId;
                    var match = _outletResults.FirstOrDefault(o => o.Id == currentOutletId);
                    DestOutletRadio.IsChecked = true;
                    OutletBox.IsEnabled = true;   // enable just long enough to set selection
                    OutletBox.SelectedItem = match ?? _outletResults.FirstOrDefault();
                    OutletBox.IsEnabled = false;
                }
                catch { /* controls might not exist in some XAML variants */ }
            }
        }

        private void DatePickerTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                FocusItemSearchBox();
            }
        }


        private async Task InitDestinationsAsync()
        {
            var outlets = await _lookup.GetOutletsAsync();
            var warehouses = await _lookup.GetWarehousesAsync();
            _outletResults = new ObservableCollection<Outlet>(outlets ?? Array.Empty<Outlet>());
            _warehouseResults = new ObservableCollection<Warehouse>(warehouses ?? Array.Empty<Warehouse>());
            try { OutletBox.ItemsSource = _outletResults; } catch { }
            try { WarehouseBox.ItemsSource = _warehouseResults; } catch { }
            try { OutletBox.Items.Refresh(); } catch { }
            try { WarehouseBox.Items.Refresh(); } catch { }
            if (!(_model != null && _model.Id > 0))
            {
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
                catch { /* some templates may not have these controls */ }
            }
        }

        private static bool CanSelectDestination()
        {
            var uname = AppState.Current?.CurrentUserName;
            if (!string.IsNullOrWhiteSpace(uname) &&
                uname.Equals("admin", StringComparison.OrdinalIgnoreCase))
                return true;
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
        private void EnforceDestinationPolicy(ref InventoryLocationType target, ref int? outletId, ref int? warehouseId)
        {
            if (!CanSelectDestination())
            {
                target = InventoryLocationType.Outlet;
                outletId = AppState.Current.CurrentOutletId;
                warehouseId = null;
            }
        }

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
            AddItemToLines(pick);                 // everything else is handled inside
        }

        private void SupplierSearch_CustomerPicked(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (VendorInvBox != null && VendorInvBox.IsEnabled && VendorInvBox.IsVisible)
                {
                    VendorInvBox.IsTabStop = true;           // belt & suspenders
                    VendorInvBox.Focusable = true;
                    VendorInvBox.Focus();
                    VendorInvBox.SelectAll();
                }
                else
                {
                    FocusItemSearchBox();                    // your existing helper
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void LinesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (LinesGrid.CurrentItem is not PurchaseLineVM row) return;
            e.Handled = true;
            var col = LinesGrid.CurrentColumn;
            var isQty = string.Equals(col?.Header as string, "Qty", StringComparison.OrdinalIgnoreCase);
            var isNotes = string.Equals(col?.Header as string, "Notes", StringComparison.OrdinalIgnoreCase);
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            if (isNotes)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                LinesGrid.CurrentCell = new DataGridCellInfo(); // avoid snap-back
                FocusItemSearchBox();                            // jump to search
                return;
            }
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
     
        private void VendorInvBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DatePicker.Focus();
                e.Handled = true;
            }
        }

        private void DatePicker_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FocusItemSearchBox();
                e.Handled = true;
            }
        }

        private void Numeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^[0-9.]$");
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

        private void AddItemToLines(Pos.Domain.DTO.ItemIndexDto dto)
        {
            var existing = _lines.FirstOrDefault(l => l.ItemId == dto.Id);
            if (existing != null)
            {
                existing.Qty += 1m;
                existing.ForceRecalc();
                RecomputeAndUpdateTotals();
                BeginEditOn(existing, "Qty");
                return;
            }

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
            _ = Dispatcher.BeginInvoke(async () =>
            {
                await ApplyLastDefaultsAsync(vm);          // may adjust UnitCost/Discount/TaxRate
                vm.ForceRecalc();
                await LoadBanksForCurrentOutletAsync();
                ApplyBankConfigToUi();
                RecomputeAndUpdateTotals();
            });
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
                //MessageBox.Show("Add at least one item.");
                await _dialogs.AlertAsync($"Add at least one item.", "POS");
                return;
            }
            if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
            {
                //MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative.");
                await _dialogs.AlertAsync($"Please ensure Qty > 0 and Price/Discount are not negative.", "POS");
                return;
            }
            foreach (var l in _lines)
            {
                var baseAmt = l.Qty * l.UnitCost;
                if (l.Discount > baseAmt)
                {
                    //MessageBox.Show($"Discount exceeds base amount for item '{l.Name}'.");
                    await _dialogs.AlertAsync($"Discount exceeds base amount for item '{l.Name}'.", "POS");
                    return;
                }
            }

            if (SupplierId is null)
            {
                await _dialogs.AlertAsync($"Please pick a Supplier (type at least 2 letters and select).", "POS");
                //MessageBox.Show("Please pick a Supplier (type at least 2 letters and select).");
                return;
            }
            int? outletId = null;
            int? warehouseId = null;
            InventoryLocationType target;
            try
            {
                if (DestWarehouseRadio.IsChecked == true)
                {
                    if (WarehouseBox.SelectedItem is not Warehouse wh)
                    {
                        //MessageBox.Show("Please pick a warehouse.");
                        await _dialogs.AlertAsync($"Please pick a warehouse.", "POS");
                        return;
                    }
                    warehouseId = wh.Id;
                    target = InventoryLocationType.Warehouse;
                }
                else if (DestOutletRadio.IsChecked == true)
                {
                    if (OutletBox.SelectedItem is not Outlet ot)
                    {
                        await _dialogs.AlertAsync($"Please pick an outlet.", "POS");
                        //MessageBox.Show("Please pick an outlet.");
                        return;
                    }
                    outletId = ot.Id;
                    target = InventoryLocationType.Outlet;
                }
                else
                {
                    target = InventoryLocationType.Outlet; // will be set via legacy box below
                }
            }
            catch
            {
                target = InventoryLocationType.Outlet; // UI not present
            }

            _model.PartyId = SupplierId.Value;
            _model.LocationType = target;
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
            //MessageBox.Show($"Purchase held as Draft. Purchase Id: #{_model.Id}", "Held");
            await _dialogs.AlertAsync($"Purchase held as Draft. Purchase Id: #{_model.Id}", "Held");
            await ResetFormAsync(keepDestination: true);
        }

        private async void BtnSaveFinal_Click(object sender, RoutedEventArgs e)
        {
            try { ((FrameworkElement)sender).IsEnabled = false; } catch { }

            try
            {
                // --- validations ---
                RecomputeAndUpdateTotals();

                if (_lines.Count == 0)
                {
                    await _dialogs.AlertAsync($"Add at least one item.", "POS");
                    //MessageBox.Show("Add at least one item.");
                    return;
                }

                if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
                {
                    await _dialogs.AlertAsync($"Please ensure Qty > 0 and Price/Discount are not negative.", "POS");
                    //MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative.");
                    return;
                }

                foreach (var l in _lines)
                {
                    var baseAmt = l.Qty * l.UnitCost;
                    if (l.Discount > baseAmt)
                    {
                        await _dialogs.AlertAsync($"Discount exceeds base amount for item '{l.Name}'.", "POS");
                        //MessageBox.Show($"Discount exceeds base amount for item '{l.Name}'.");
                        return;
                    }
                }

                if (SupplierId is null)
                {
                    await _dialogs.AlertAsync($"Please pick a Supplier (type at least 2 letters and select).", "POS");
                    //MessageBox.Show("Please pick a Supplier (type at least 2 letters and select).");
                    return;
                }
                _model.PartyId = SupplierId.Value;

                // --- destination selection ---
                int? outletId = null, warehouseId = null;
                InventoryLocationType target;
                try
                {
                    if (DestWarehouseRadio.IsChecked == true)
                    {
                        if (WarehouseBox.SelectedItem is not Warehouse wh) { await _dialogs.AlertAsync($"Please pick a warehouse.", "POS"); return; }
                        warehouseId = wh.Id; target = InventoryLocationType.Warehouse;
                    }
                    else if (DestOutletRadio.IsChecked == true)
                    {
                        if (OutletBox.SelectedItem is not Outlet ot) { await _dialogs.AlertAsync($"Please pick an outlet.", "POS"); return; }
                        outletId = ot.Id; target = InventoryLocationType.Outlet;
                    }
                    else { target = InventoryLocationType.Outlet; }
                }
                catch { target =       InventoryLocationType.Outlet; }

                EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);

                // --- header fields ---
                _model.LocationType = target;
                _model.OutletId = outletId;
                _model.WarehouseId = warehouseId;
                _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
                _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now;
                _model.Status = PurchaseStatus.Final;

                // --- line payload (detached) ---
                var linePayload = _lines.Select(l => new PurchaseLine
                {
                    ItemId = l.ItemId,
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    Notes = l.Notes
                }).ToList();

                // --- optional "pay on save" list ---
                var onReceive = new List<(TenderMethod method, decimal amount, string? note)>(_stagedPayments);
                // UI guard: prevent overpay before calling the service
                if (onReceive.Count > 0)
                {
                    // make sure totals are up-to-date
                    RecomputeAndUpdateTotals();

                    if (!ValidateStagedPayments(onReceive))
                        return; // stop; user saw a message already
                }

                // 🔍 LOG payments to Output window
                // ✅ Correct tuple logging
                System.Diagnostics.Debug.WriteLine($"[UI] onReceive.Count={onReceive.Count}");
                System.Diagnostics.Debug.WriteLine($"[UI] onReceive.Sum={onReceive.Sum(x => x.amount)}");

                foreach (var (method, amount, note) in onReceive)
                {
                    System.Diagnostics.Debug.WriteLine($"[UI] Payment -> Method={method}, Amount={amount}, Note='{note}'");
                }

                // resolve current username once
                var user = AppState.Current?.CurrentUserName
                           ?? System.Environment.UserName
                           ?? "system";

                System.Diagnostics.Debug.WriteLine($"[UI] FinalizeReceiveAsync called with user={user}, outlet={_model.OutletId}, supplier={_model.PartyId}");

                // --- service call ---
                _model = await _purchaseSvc.FinalizeReceiveAsync(
                    purchase: _model,
                    lines: linePayload,
                    onReceivePayments: onReceive,
                    outletId: _model.OutletId ?? (GetCurrentOutletIdForHeader() ?? 0),
                    supplierId: _model.PartyId,
                    tillSessionId: null,
                    counterId: null,
                    user: user,
                    ct: default
                );

                System.Diagnostics.Debug.WriteLine($"[UI] FinalizeReceiveAsync returned Id={_model.Id} DocNo={_model.DocNo} Grand={_model.GrandTotal}");
                await _dialogs.AlertAsync($"Purchase finalized.\nDoc #: {(_model.DocNo ?? $"#{_model.Id}")}\nTotal: {_model.GrandTotal:N2}", "Saved (Final)");
                //MessageBox.Show(
                //    $"Purchase finalized.\nDoc #: {(_model.DocNo ?? $"#{_model.Id}")}\nTotal: {_model.GrandTotal:N2}",
                //    "Saved (Final)", MessageBoxButton.OK, MessageBoxImage.Information);

                await ResetFormAsync(keepDestination: true);
            }
            catch (InvalidOperationException ex)
            {
                await _dialogs.AlertAsync(ex.Message, "Amendment blocked");
                //MessageBox.Show(ex.Message, "Amendment blocked",
                //    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                await _dialogs.AlertAsync($"Save failed:\n" + ex.GetBaseException().Message, "Database error");
                //MessageBox.Show("Save failed:\n" + ex.GetBaseException().Message,
                //    "Database error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync($"Unexpected error:\n" + ex.Message, "Error");
                //MessageBox.Show("Unexpected error:\n" + ex.Message,
                //    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                try { ((FrameworkElement)sender).IsEnabled = true; } catch { }
            }
        }

        private decimal RemainingDueBeforeStaging()
        {
            var paidPersisted = _payments.Where(p => p.Id > 0 && p.IsEffective)
                                         .Sum(p => p.Amount);
            var due = decimal.Round(_model.GrandTotal - paidPersisted, 2, MidpointRounding.AwayFromZero);
            return Math.Max(0m, due);
        }
       
        private bool ValidateStagedPayments(IReadOnlyList<(TenderMethod method, decimal amount, string? note)> staged)
        {
            var remaining = RemainingDueBeforeStaging();
            for (int i = 0; i < staged.Count; i++)
            {
                var (method, amount, note) = staged[i];
                var label = $"Payment #{i + 1} ({method})";

                if (amount <= 0m)
                {
                    _dialogs.AlertAsync($"{label} must be greater than 0.", "Invalid payment");
                    //MessageBox.Show($"{label} must be greater than 0.", "Invalid payment",
                    //    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (amount > remaining + 0.0001m)
                {
                    //MessageBox.Show($"{label} exceeds remaining due ({remaining:N2}).",
                    //    "Overpay blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _dialogs.AlertAsync($"{label} exceeds remaining due ({remaining:N2}).", "Overpay blocked");
                    return false;
                }

                // subtract using same rounding mode as server
                remaining = Math.Max(0m, decimal.Round(remaining - amount, 2, MidpointRounding.AwayFromZero));
            }
            return true;
        }

        private async Task RefreshPaymentsAsync()
        {
            _payments.Clear();
            if (_model.Id > 0)
            {
                var pays = await _purchaseSvc.GetPaymentsAsync(_model.Id, CancellationToken.None);
                foreach (var p in pays) _payments.Add(p);
            }

            // append staged (if any)
            foreach (var sp in _stagedPayments)
            {
                _payments.Add(new PurchasePayment
                {
                    Id = 0,
                    PurchaseId = _model.Id,
                    TsUtc = DateTime.UtcNow,
                    Kind = (_model.Status == PurchaseStatus.Draft) ? PurchasePaymentKind.Advance : PurchasePaymentKind.OnReceive,
                    Method = sp.method,
                    Amount = sp.amount,
                    Note = "(staged — will post on Save)"
                });
            }

            PaymentsGrid.ItemsSource = _payments;
        }


        private void BtnAddAdvance_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(AdvanceAmtBox.Text, out var amt) || amt <= 0m)
            { _dialogs.AlertAsync($"Enter amount > 0", "Payment");  return; }
            var sel = (AdvanceMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
            var method = Enum.TryParse<TenderMethod>(sel, out var m) ? m : TenderMethod.Cash;
            _stagedPayments.Add((method, Math.Round(amt, 2), note: null));
            _payments.Add(new PurchasePayment
            {
                Id = 0, // staged marker
                PurchaseId = _model.Id,
                TsUtc = DateTime.UtcNow,
                Kind = (_model.Status == PurchaseStatus.Draft) ? PurchasePaymentKind.Advance : PurchasePaymentKind.OnReceive,
                Method = method,
                Amount = amt,
                Note = "(staged — will post on Save)"
            });
            PaymentsGrid.ItemsSource = _payments;
            AdvanceAmtBox.Clear();
            AdvanceMethodBox.SelectedItem = AdvanceMethodCashItem;
            BankPickerPanel.Visibility = Visibility.Collapsed;
        }

        private void PaymentsGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.Item is not PurchasePayment row) return;

            if (row.Id == 0)
            {
                var idx = _payments.IndexOf(row);
                if (idx >= 0)
                {
                    //var stagedIdx = 0;
                    for (int i = 0, s = 0; i < _payments.Count; i++)
                    {
                        if (_payments[i].Id == 0)
                        {
                            if (i == idx)
                            {
                                var amt = Math.Round(row.Amount, 2);
                                if (amt <= 0m) { _dialogs.AlertAsync($"Amount must be > 0", "POS"); row.Amount = 1m; }
                                // update staged entry
                                _stagedPayments[s] = (row.Method, Math.Round(row.Amount, 2), null);
                                break;
                            }
                            s++;
                        }
                    }
                }
            }
            else
            {
                // persisted row: push changes to service
                _ = UpdatePersistedPaymentAsync(row);
            }
        }

        private async Task UpdatePersistedPaymentAsync(PurchasePayment row)
        {
            try
            {
                await _purchaseSvc.UpdatePaymentAsync(row.Id, Math.Round(row.Amount, 2), row.Method, row.Note, AppState.Current?.CurrentUser?.DisplayName ?? "system");
            }
            catch (Exception ex)
            {
                await _dialogs.AlertAsync(ex.Message, "Update payment failed");
                //MessageBox.Show(ex.Message, "Update payment failed");
                await RefreshPaymentsAsync(); // revert view
            }
        }


        private async void BtnDeletePayment_Click(object? sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as PurchasePayment
                      ?? (PaymentsGrid.SelectedItem as PurchasePayment);
            if (row == null) return;

            if (row.Id == 0)
            {
                var idx = _payments.IndexOf(row);
                if (idx >= 0)
                {
                    int stagedIdx = -1;
                    for (int i = 0, s = 0; i < _payments.Count; i++)
                    {
                        if (_payments[i].Id == 0)
                        {
                            if (i == idx) { stagedIdx = s; break; }
                            s++;
                        }
                    }
                    if (stagedIdx >= 0) _stagedPayments.RemoveAt(stagedIdx);
                    _payments.RemoveAt(idx);
                }
            }
            else
            {
                try
                {
                    var user = AppState.Current?.CurrentUserName
                               ?? AppState.Current?.CurrentUser?.DisplayName
                               ?? "system";
                    await _purchaseSvc.RemovePaymentAsync(row.Id, user);
                    await ReloadPaymentsAsync();            // refresh from DB so you see the reversal row
                    RecomputeAndUpdateTotals();             // refresh header CashPaid/CreditDue on screen
                }
                catch (Exception ex)
                {
                    await _dialogs.AlertAsync(ex.Message, "Delete payment failed");
                    //MessageBox.Show(ex.Message, "Delete payment failed");
                }
            }

        }

        private async Task ReloadPaymentsAsync()
        {
            var list = await _purchaseSvc.GetPaymentsAsync(_model.Id, CancellationToken.None);
            _payments.Clear();
            foreach (var p in list) _payments.Add(p);
        }

        //private async Task<bool> EnsurePurchasePersistedAsync()
        //{
        //    if (_model.Id > 0) return true;
        //    if (_lines.Count == 0)
        //    {
        //        MessageBox.Show("Add at least one item before taking a payment.");
        //        return false;
        //    }
        //    if (SupplierId is null)
        //    {
        //        MessageBox.Show("Please pick a Supplier (type at least 2 letters and select).");
        //        return false;
        //    }
        //    int? outletId = null, warehouseId = null;
        //    var target = InventoryLocationType.Outlet;
        //    try
        //    {
        //        if (DestWarehouseRadio.IsChecked == true && WarehouseBox.SelectedItem is Warehouse wh)
        //        { warehouseId = wh.Id; target = InventoryLocationType.Warehouse; }
        //        else if (DestOutletRadio.IsChecked == true && OutletBox.SelectedItem is Outlet ot)
        //        { outletId = ot.Id; target = InventoryLocationType.Outlet; }
        //    }
        //    catch { /* ignore if controls not present */ }
        //    EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);
        //    _model.PartyId = SupplierId.Value;
        //    _model.LocationType = target;
        //    _model.OutletId = outletId;
        //    _model.WarehouseId = warehouseId;
        //    _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
        //    _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now;
        //    _model.Status = PurchaseStatus.Draft;
        //    var lines = _lines.Select(l => new PurchaseLine
        //    {
        //        ItemId = l.ItemId,
        //        Qty = l.Qty,
        //        UnitCost = l.UnitCost,
        //        Discount = l.Discount,
        //        TaxRate = l.TaxRate,
        //        Notes = l.Notes
        //    });
        //    _model = await _purchaseSvc.SaveDraftAsync(_model, lines, user: "admin");
        //    await RefreshPaymentsAsync(); // bind grid to the now-real purchase
        //    return _model.Id > 0;
        //}

        private async Task ResetFormAsync(bool keepDestination = true)
        {
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
            SupplierSearch.SelectedCustomer = null;
            SupplierSearch.SelectedCustomerId = null;
            SupplierSearch.Query = string.Empty;
            VendorInvBox.Clear();
            DatePicker.SelectedDate = DateTime.Now;
            _lines.Clear();
            _payments.Clear();
            PaymentsGrid.ItemsSource = null; // rebounded when a real purchase is loaded
            OtherChargesBox.Text = "0.00";
            SubtotalText.Text = "0.00";
            DiscountText.Text = "0.00";
            TaxText.Text = "0.00";
            GrandTotalText.Text = "0.00";
            if (!keepDestination)
            {
                await InitDestinationsAsync();
                await ApplyUserPrefsToDestinationAsync();   // << apply saved prefs on a fresh form
                ApplyDestinationPermissionGuard();
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
            _ = Dispatcher.BeginInvoke(() => SupplierSearch.Focus()); // explicit discard
        }

        //private async Task ResumeHeldAsync(int id)
        //{
        //    var draft = await _purchaseSvc.LoadDraftWithLinesAsync(id);
        //    if (draft == null)
        //    {
        //        MessageBox.Show($"Held purchase #{id} not found or not in Draft.", "Not found");
        //        return;
        //    }
        //    LoadDraft(draft);
        //}

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Clear this purchase form?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ClearCurrentPurchase(confirm: true);
            }
        }

        private async void HoldButton_Click(object sender, RoutedEventArgs e)
        {
            await HoldCurrentPurchaseQuickAsync();
        }

        private async void ClearCurrentPurchase(bool confirm)
        {
            var hasLines = _lines.Count > 0;
            var hasSupplier = (SupplierSearch?.SelectedCustomerId ?? 0) > 0
                  || !string.IsNullOrWhiteSpace(SupplierSearch?.Query);

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
            if (SupplierId is null)
            {
                MessageBox.Show("Please pick a Supplier (type at least 2 letters and select).");
                return;
            }

            int? outletId = null;
            int? warehouseId = null;
            InventoryLocationType target;
            try
            {
                if (DestWarehouseRadio.IsChecked == true)
                {
                    if (WarehouseBox.SelectedItem is not Warehouse wh)
                    {
                        MessageBox.Show("Please pick a warehouse."); return;
                    }
                    warehouseId = wh.Id;
                    target = InventoryLocationType.Warehouse;
                }
                else if (DestOutletRadio.IsChecked == true)
                {
                    if (OutletBox.SelectedItem is not Outlet ot)
                    {
                        MessageBox.Show("Please pick an outlet."); return;
                    }
                    outletId = ot.Id;
                    target = InventoryLocationType.Outlet;
                }
                else
                {
                    target = InventoryLocationType.Outlet; // legacy fallback
                }
            }
            catch
            {
                target = InventoryLocationType.Outlet; // UI not present
            }
            EnforceDestinationPolicy(ref target, ref outletId, ref warehouseId);
            _model.PartyId = SupplierId.Value;
            _model.LocationType = target;
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
            _model = await _purchaseSvc.SaveDraftAsync(_model, lines, user: "admin");
            MessageBox.Show($"Purchase held as Draft. Purchase Id: #{_model.Id}", "Held");
            await ResetFormAsync(keepDestination: true);
        }

        public async void LoadDraft(Purchase draft)
        {
            if (draft == null)
            {
                MessageBox.Show("Draft not found.");
                return;
            }
            if (draft.Lines == null || draft.Lines.Count == 0)
            {
                try
                {
                    var reloaded = await _purchaseSvc.LoadDraftWithLinesAsync(draft.Id);
                    if (reloaded != null) draft = reloaded;
                }
                catch
                {
                }
            }
            _model = draft;
            try { DatePicker.SelectedDate = draft.PurchaseDate; } catch { }
            try { VendorInvBox.Text = draft.VendorInvoiceNo ?? ""; } catch { }
            try { OtherChargesBox.Text = draft.OtherCharges.ToString("0.00"); } catch { }
            try
            {
                var partyName = await _partySvc.GetPartyNameAsync(draft.PartyId);
                SupplierSearch.SelectedCustomerId = draft.PartyId;
                SupplierSearch.Query = partyName ?? $"Supplier #{draft.PartyId}";

            }
            finally
            {
               
            }
            try
            {
                if (draft.LocationType == InventoryLocationType.Warehouse && draft.WarehouseId.HasValue)
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
            try
            {
                if (!CanSelectDestination())
                {
                    DestWarehouseRadio.IsEnabled = false;
                    WarehouseBox.IsEnabled = false;
                    DestOutletRadio.IsEnabled = false;
                    OutletBox.IsEnabled = false;
                }
            }
            catch { /* swallow UI timing issues */ }
            _lines.Clear();
            var lines = draft.Lines ?? new List<PurchaseLine>();
            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var metaDict = await _itemsSvc.GetDisplayMetaAsync(itemIds); // Dictionary<int,(display, sku)>
            foreach (var l in lines)
            {
                metaDict.TryGetValue(l.ItemId, out var m);
                var row = new PurchaseLineVM
                {
                    ItemId = l.ItemId,
                    Sku = m.sku,
                    Name = m.display,
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    Notes = l.Notes
                };
                row.ForceRecalc();
                _lines.Add(row);
            }
            SubtotalText.Text = draft.Subtotal.ToString("0.00");
            DiscountText.Text = draft.Discount.ToString("0.00");
            TaxText.Text = draft.Tax.ToString("0.00");
            GrandTotalText.Text = draft.GrandTotal.ToString("0.00");
            VM.PurchaseId = draft.Id;
            VM.SupplierId = draft.PartyId;
            VM.TargetType = draft.LocationType;
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
            await RefreshPaymentsAsync();
            await LoadBanksForCurrentOutletAsync();
            ApplyBankConfigToUi();
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

        private void RemoveSelectedLines(bool askConfirm = false)
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = LinesGrid.SelectedItems
                .OfType<PurchaseLineVM>()             // only real line VMs
                .Where(vm => _lines.Contains(vm))     // belt & suspenders
                .ToList();
            if (rows.Count == 0)
            {
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

            await _purchaseSvc.UpdatePaymentAsync(pay.Id, newAmt, newMethod, newNote, user);
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
            await _purchaseSvc.RemovePaymentAsync(pay.Id, user);
            await RefreshPaymentsAsync();
        }

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
            _uiReady = true; // <-- add this line
            if (_firstLoadComplete) return;
            _firstLoadComplete = true;
            await LoadExistingIfAny();
            await ApplyModeAsync();
        }

        private async Task LoadExistingIfAny()
        {
            if (PurchaseId is null or <= 0) return;
            var id = PurchaseId.Value;
            var p = await _purchaseSvc.LoadWithLinesAsync(id); // you already have this in service
            if (p == null)
            {
                MessageBox.Show($"Purchase {id} not found.");
                return;
            }
            _model = new Purchase
            {
                Id = p.Id,
                PartyId = p.PartyId,
                LocationType = p.LocationType,
                OutletId = p.OutletId,
                WarehouseId = p.WarehouseId,
                VendorInvoiceNo = p.VendorInvoiceNo,
                PurchaseDate = p.PurchaseDate,
                Status = p.Status,
                GrandTotal = p.GrandTotal,
                Discount = p.Discount,
                Tax = p.Tax,
                Subtotal = p.Subtotal,
                OtherCharges = p.OtherCharges,
                IsReturn = false
            };
            try { DatePicker.SelectedDate = _model.PurchaseDate; } catch { }
            await InitDestinationsAsync();
            await SetDestinationSelectionAsync();
            var partyName = await _partySvc.GetPartyNameAsync(p.PartyId);
            SupplierSearch.SelectedCustomerId = p.PartyId;
            SupplierSearch.Query = partyName ?? $"Supplier #{p.PartyId}";
            _lines.Clear();
            var effective = await _purchaseSvc.GetEffectiveLinesAsync(id);
            foreach (var e in effective)
            {
                _lines.Add(new PurchaseLineVM
                {
                    ItemId = e.ItemId,
                    Sku = e.Sku ?? "",
                    Name = e.Name ?? "",
                    Qty = e.Qty,
                    UnitCost = e.UnitCost,
                    Discount = e.Discount,
                    TaxRate = e.TaxRate
                });
            }
            RecomputeAndUpdateTotals();
            await RefreshPaymentsAsync();
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
            try { SupplierSearch.IsEnabled = !amend; } catch { }
            try { DatePicker.IsEnabled = !amend; } catch { }
            try { WarehouseBox.IsEnabled = !amend; } catch { }
            try { OutletBox.IsEnabled = !amend; } catch { }
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
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
            try
            {
                if (_model.LocationType == InventoryLocationType.Outlet && _model.OutletId.HasValue)
                {
                    DestOutletRadio.IsChecked = true;
                    OutletBox.IsEnabled = true;
                    WarehouseBox.IsEnabled = false;
                    OutletBox.SelectedValue = _model.OutletId.Value;
                    if (OutletBox.SelectedValue == null)
                    {
                        var ot = _outletResults.FirstOrDefault(o => o.Id == _model.OutletId.Value);
                        if (ot != null) OutletBox.SelectedItem = ot;
                    }
                }
                else if (_model.LocationType == InventoryLocationType.Warehouse && _model.WarehouseId.HasValue)
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
            }
        }
        private (Purchase model, List<PurchaseLine> lines) BuildPurchaseFromVm(bool isDraft)
        {
            var model = new Purchase
            {
                Id = VM.PurchaseId,           // 0 for new, >0 for amend
                PartyId = VM.SupplierId,
                LocationType = VM.TargetType,
                OutletId = VM.TargetType == InventoryLocationType.Outlet ? VM.OutletId : null,
                WarehouseId = VM.TargetType == InventoryLocationType.Warehouse ? VM.WarehouseId : null,
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

        private Task ApplyUserPrefsToDestinationAsync()
        {
            // If editing an existing doc or bound to an existing Purchase, do nothing
            if ((_model?.Id ?? 0) > 0) return Task.CompletedTask;
            if (PurchaseId is int pid && pid > 0) return Task.CompletedTask;

            try
            {
                // Force "Outlet" scope UI
                try { DestOutletRadio.IsChecked = true; } catch { }
                try
                {
                    OutletBox.IsEnabled = true;
                    WarehouseBox.IsEnabled = false;
                }
                catch { }

                // Select the first outlet if available
                if (_outletResults != null && _outletResults.Count > 0)
                {
                    var first = _outletResults[0];
                    try
                    {
                        OutletBox.SelectedValue = first.Id;
                    }
                    catch
                    {
                        try { OutletBox.SelectedItem = first; } catch { }
                    }
                }
                else
                {
                    try { OutletBox.SelectedIndex = -1; } catch { }
                }

                try { DestWarehouseRadio.IsChecked = false; } catch { }
                try { WarehouseBox.SelectedIndex = -1; } catch { }
            }
            catch
            {
                // swallow – safe defaults already applied above
            }

            return Task.CompletedTask;
        }


    }
}