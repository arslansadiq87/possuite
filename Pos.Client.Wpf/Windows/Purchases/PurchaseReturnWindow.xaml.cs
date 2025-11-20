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
//using Pos.Persistence.Services;         // PurchasesService
using System.Timers;
using System.Windows.Input;
using System.Xml.Linq;
using Pos.Domain;
using Pos.Client.Wpf.Controls;          // ItemSearchBox user control
using Pos.Domain.DTO;                   // ItemIndexDto
using Microsoft.Extensions.DependencyInjection;         // for GetRequiredService
using Pos.Domain.Services;          // for IGlPostingService, IItemsReadService
using Pos.Domain.Models.Purchases;
using Pos.Client.Wpf.Security;
using Pos.Domain.Models;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseReturnWindow : Window
    {
        private readonly int? _refPurchaseId;   // Return With
        private readonly int? _returnId;        // Amend existing return
        private readonly bool _freeForm;        // Return Without
        private readonly IPurchaseReturnsService _svc;
   
        private readonly IPartyLookupService _partySvc;
        private readonly ILookupService _lookup;       // outlets/warehouses
        private readonly IGlPostingService _gl;        // GL posting
        private readonly IPartyService _party;
        private readonly IItemsReadService _itemsRead;   // ✅ add
        private readonly IInventoryReadService _invRead;
        private readonly IPurchasesService _purSvc;

        //private bool _uiReady;
        private readonly ObservableCollection<Account> _bankAccounts = new();
        private bool _bankPaymentsAllowed; // true only if InvoiceSettings.PurchaseBankAccountId is set for the outlet
        private readonly List<(TenderMethod method, decimal amount, string? note)> _stagedPayments = new();
        private readonly ObservableCollection<RefundRow> _refunds = new();


        private ObservableCollection<Outlet> _outletResults = new();
        private ObservableCollection<Warehouse> _warehouseResults = new();

        private readonly System.Timers.Timer _supplierDebounce = new(250) { AutoReset = false };
        private List<Party> _supplierResults = new();
        private bool _suppressSupplierSearch = false;
        private readonly System.Timers.Timer _itemDebounce = new(250) { AutoReset = false };
        private List<ItemPick> _itemResults = new();
        private int? _colUnitCostIndex;
        private int? _colReturnQtyIndex;
        private System.Threading.CancellationTokenSource? _availCts;
        public ReturnVM VM { get; } = new();

        // expose for XAML combo (cash + bank)
        public IReadOnlyList<TenderMethod> RefundMethodItems { get; } = new[]
        {
            TenderMethod.Cash,
            TenderMethod.Bank,
        };

        public PurchaseReturnWindow() // Return Without (free-form)
        {
            InitializeComponent();
            _freeForm = true;
            _svc = App.Services.GetRequiredService<IPurchaseReturnsService>();
            _purSvc = App.Services.GetRequiredService<IPurchasesService>();
            _invRead = App.Services.GetRequiredService<IInventoryReadService>();
            _partySvc = App.Services.GetRequiredService<IPartyLookupService>();
            _lookup = App.Services.GetRequiredService<ILookupService>();
            _gl = App.Services.GetRequiredService<IGlPostingService>();
            _party = App.Services.GetRequiredService<IPartyService>();
            _itemsRead = App.Services.GetRequiredService<IItemsReadService>();
            VM.IsSupplierReadonly = false;
            VM.BasePurchaseDisplay = "—";

            VM.TargetType = InventoryLocationType.Outlet;
            VM.OutletId = AppState.Current?.CurrentOutletId > 0
                ? AppState.Current.CurrentOutletId
                : (int?)null;
            VM.WarehouseId = null;

            GridLines.BeginningEdit += GridLines_BeginningEdit;
            GridLines.CurrentCellChanged += GridLines_CurrentCellChanged;
            DataContext = VM;
            VM.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName is nameof(ReturnVM.TargetType)
                    or nameof(ReturnVM.OutletId)
                    or nameof(ReturnVM.WarehouseId))
                {
                    await CapReturnWithAgainstOnHandAsync();
                }
                if (AvailablePanel.Visibility == Visibility.Visible && GridLines.CurrentItem is LineVM l)
                    await UpdateAvailablePanelAsync(l);
            };

            Loaded += async (_, __) =>
            {
                await InitFreeFormAsync();
                await LoadSourcesAsync();                 // now via _lookup
                //await Dispatcher.InvokeAsync(() => SupplierText.Focus());
                await Dispatcher.BeginInvoke(() => SupplierSearch.Focus());
            };
        }

        public PurchaseReturnWindow(int refPurchaseId) // Return With base purchase
        {
            InitializeComponent();
            _refPurchaseId = refPurchaseId;
            _svc = App.Services.GetRequiredService<IPurchaseReturnsService>();
            _invRead = App.Services.GetRequiredService<IInventoryReadService>();
            _partySvc = App.Services.GetRequiredService<IPartyLookupService>();
            _lookup = App.Services.GetRequiredService<ILookupService>();
            _gl = App.Services.GetRequiredService<IGlPostingService>();
            _party = App.Services.GetRequiredService<IPartyService>();
            _itemsRead = App.Services.GetRequiredService<IItemsReadService>();
            _purSvc = App.Services.GetRequiredService<IPurchasesService>();
            DataContext = VM;
            VM.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName is nameof(ReturnVM.TargetType)
                    or nameof(ReturnVM.OutletId)
                    or nameof(ReturnVM.WarehouseId))
                {
                    await CapReturnWithAgainstOnHandAsync();
                }
                if (AvailablePanel.Visibility == Visibility.Visible && GridLines.CurrentItem is LineVM l)
                    await UpdateAvailablePanelAsync(l);
            };
            Loaded += async (_, __) =>
            {
                await LoadSourcesAsync();                 // fill OutletList/WarehouseList
                await InitFromBaseAsync(refPurchaseId);   // set supplier + source + lines
                await Dispatcher.BeginInvoke(() => SupplierSearch.Focus());
            };

        }

        public PurchaseReturnWindow(int returnId, bool isAmend = true) // Amend
        {
            InitializeComponent();
            _returnId = returnId;
            _svc = App.Services.GetRequiredService<IPurchaseReturnsService>();
            _invRead = App.Services.GetRequiredService<IInventoryReadService>();
            _partySvc = App.Services.GetRequiredService<IPartyLookupService>();
            _lookup = App.Services.GetRequiredService<ILookupService>();
            _gl = App.Services.GetRequiredService<IGlPostingService>();
            _party = App.Services.GetRequiredService<IPartyService>();
            _itemsRead = App.Services.GetRequiredService<IItemsReadService>();
            _purSvc = App.Services.GetRequiredService<IPurchasesService>();
            DataContext = VM;
            VM.PropertyChanged += async (_, args) =>
            {
                if (args.PropertyName is nameof(ReturnVM.TargetType)
                    or nameof(ReturnVM.OutletId)
                    or nameof(ReturnVM.WarehouseId))
                {
                    await CapReturnWithAgainstOnHandAsync();
                }
                if (AvailablePanel.Visibility == Visibility.Visible && GridLines.CurrentItem is LineVM l)
                    await UpdateAvailablePanelAsync(l);
            };
            Loaded += async (_, __) =>
            {
                await InitFromExistingReturnAsync(returnId);  // now via services
                //await Dispatcher.InvokeAsync(() => SupplierText.Focus());
                await Dispatcher.BeginInvoke(() => SupplierSearch.Focus());
            };
        }

        private void ApplySupplier(Party p)
        {
            if (p == null) return;
            _suppressSupplierSearch = true;      // prevent popup reopening due to TextChanged
            VM.SupplierId = p.Id;
            VM.SupplierDisplay = p.Name ?? $"Supplier #{p.Id}";
            _suppressSupplierSearch = false;
            //SupplierPopup.IsOpen = false;
            //SupplierText.CaretIndex = SupplierText.Text.Length;
            MoveFocusToItemSearch();
        }
        private void SupplierSearch_CustomerPicked(object sender, RoutedEventArgs e)
        {
            // Defer until after the popup closes and internal TextBox regains focus.
            var pick = ((Pos.Client.Wpf.Controls.CustomerSearchBox)sender).SelectedCustomer;
            if (pick is null) return;
            VM.SupplierId = pick.Id;
            VM.SupplierDisplay = pick.Name ?? $"Supplier #{pick.Id}";
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Prefer VendorInvBox if usable, otherwise jump to items.
                    FocusItemSearchBox();                    // your existing helper
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private async Task LoadBanksForCurrentOutletAsync()
        {
            BankAccountBox.ItemsSource = null;
            _bankAccounts.Clear();
            //var outletId = GetCurrentOutletIdForHeader() ?? 0;
            //if (outletId == 0) { _bankPaymentsAllowed = false; return; }
            //_bankPaymentsAllowed = await _purSvc.IsPurchaseBankConfiguredAsync(outletId);
            //var banks = await _purSvc.ListBankAccountsForOutletAsync(outletId);
            //foreach (var b in banks) _bankAccounts.Add(b);
            //BankAccountBox.ItemsSource = _bankAccounts;
            //var defId = await _purSvc.GetConfiguredPurchaseBankAccountIdAsync(outletId);
            //if (defId is int id)
            //{
            //    var match = _bankAccounts.FirstOrDefault(x => x.Id == id);
            //    if (match != null) BankAccountBox.SelectedItem = match;
            //}
        }
        private void ApplyBankConfigToUi()
        {
            if (!_bankPaymentsAllowed)
            {
                try
                {
                    RefundMethodBankItem.IsEnabled = false;
                    BankPickerPanel.Visibility = Visibility.Collapsed;
                    var sel = (RefundMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
                    if (!string.Equals(sel, "Cash", StringComparison.OrdinalIgnoreCase))
                        RefundMethodBox.SelectedItem = RefundMethodCashItem;
                }
                catch { }
            }
            else
            {
                try { RefundMethodBankItem.IsEnabled = true; } catch { }
            }
        }
                
       
        private async Task<bool> IsPurchaseBankConfiguredAsync(int outletId)
  => await _purSvc.IsPurchaseBankConfiguredAsync(outletId);

        
        
                

    
     

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
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
                var tb = FindDescendant<TextBox>(ItemSearch);
                if (tb != null) { tb.Focus(); tb.SelectAll(); return; }
                ItemSearch.Focus();
            }
            catch { }
        }

        private void MoveFocusToItemSearch()
        {
            if (VM.CanAddItems) FocusItemSearchBox();
            else GridLines.Focus();
        }

        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var dto = ((ItemSearchBox)sender).SelectedItem as ItemIndexDto;
            if (dto is null) return;
            await AddOrBumpItem_ByItemIdAsync(dto.Id);
            _previewItemId = dto.Id;
            _ = RefreshPreviewOnHandAsync();   // this will call UpdateOnHandBadge()
        }

        // add: using System.Windows.Threading;

        private async Task AddOrBumpItem_ByItemIdAsync(int itemId)
        {
            var existing = VM.Lines.FirstOrDefault(l => l.ItemId == itemId);
            if (existing != null)
            {
                existing.ReturnQty = existing.ReturnQty + 1m;
                existing.ClampQty();
                existing.RecomputeLineTotal();
                UpdateTotalsUI();
                VM.RecomputeTotals();
                await SetLineMaxFromOnHandAsync(existing);

                await Dispatcher.InvokeAsync(() => FocusUnitCost(existing)); // << fix CS4014
                return;
            }

            var meta = await _itemsRead.GetItemMetaForReturnAsync(itemId);
            if (meta is not { } m) return;        // m is the non-null tuple
            var (display, sku, lastCost) = m;

            var line = new LineVM
            {
                OriginalLineId = null,
                ItemId = itemId,
                DisplayName = display,
                Sku = sku ?? "",
                UnitCost = Math.Max(0, lastCost ?? 0m),
                Discount = 0m,
                TaxRate = 0m,
                MaxReturnQty = 999999m,
                ReturnQty = 1m
            };

            var wasEmpty = VM.Lines.Count == 0;
            VM.Lines.Add(line);

            await SetLineMaxFromOnHandAsync(line);
            VM.RecomputeTotals();
            UpdateTotalsUI();
            if (wasEmpty && _freeForm) VM.IsSourceReadonly = true;

            await Dispatcher.InvokeAsync(() => FocusUnitCost(line)); // << fix CS4014
        }


        private int EnsureColumnIndex(string bindingPath)
        {
            if (bindingPath == nameof(LineVM.UnitCost) && _colUnitCostIndex.HasValue) return _colUnitCostIndex.Value;
            if (bindingPath == nameof(LineVM.ReturnQty) && _colReturnQtyIndex.HasValue) return _colReturnQtyIndex.Value;
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
                GridLines.BeginEdit();
                Dispatcher.InvokeAsync(() => GridLines.BeginEdit(), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void FocusUnitCost(LineVM vm) => FocusCell(vm, nameof(LineVM.UnitCost), beginEdit: true);
        private void FocusReturnQty(LineVM vm) => FocusCell(vm, nameof(LineVM.ReturnQty), beginEdit: true);
                      
        
        private sealed class ItemPick
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string Display { get; set; } = "";
            public decimal? LastCost { get; set; }
            public string LastCostDisplay => LastCost.HasValue ? $"Last: {LastCost.Value:N2}" : "";
        }


        private void GridLines_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            GridLines.CommitEdit(DataGridEditingUnit.Cell, true);
            GridLines.CommitEdit(DataGridEditingUnit.Row, true);
            if (GridLines.CurrentItem is not LineVM rowVm) return;
            var unitCostIdx = EnsureColumnIndex(nameof(LineVM.UnitCost));
            var returnQtyIdx = EnsureColumnIndex(nameof(LineVM.ReturnQty));
            var currentCol = GridLines.CurrentColumn;
            if (currentCol == null) return;
            if (GridLines.Columns.IndexOf(currentCol) == unitCostIdx)
            {
                e.Handled = true;
                FocusReturnQty(rowVm);
                return;
            }
            if (GridLines.Columns.IndexOf(currentCol) == returnQtyIdx)
            {
                e.Handled = true;
                rowVm.ClampQty();
                rowVm.RecomputeLineTotal();
                VM.RecomputeTotals();
                Dispatcher.InvokeAsync(() => FocusItemSearchBox());
                return;
            }
        }

        private async Task InitFromBaseAsync(int refPurchaseId)
        {
            var draft = await _svc.BuildReturnDraftFromInvoiceAsync(refPurchaseId);
            if (draft == null) throw new InvalidOperationException($"Base purchase #{refPurchaseId} not found.");
            var partyName = await _party.GetPartyNameAsync(draft.PartyId) ?? $"Supplier #{draft.PartyId}";

            VM.IsSupplierReadonly = true;
            VM.SupplierId = draft.PartyId;
            VM.SupplierDisplay = partyName;
            SupplierSearch.IsEnabled = false;                 // 🔒 lock search for WITH-INVOICE

            VM.TargetType = draft.LocationType;
            VM.OutletId = draft.OutletId;
            VM.WarehouseId = draft.WarehouseId;
            VM.RefPurchaseId = draft.RefPurchaseId;
            VM.BasePurchaseDisplay = draft.RefPurchaseId is int rid ? $"#{rid}" : $"#{refPurchaseId}";
            VM.IsSourceReadonly = true;                       // 🔒 lock source for WITH-INVOICE

            var itemIds = draft.Lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = await _itemsRead.GetDisplayMetaAsync(itemIds);
            VM.Lines.Clear();
            foreach (var d in draft.Lines)
            {
                meta.TryGetValue(d.ItemId, out var m);
                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = d.OriginalLineId,
                    ItemId = d.ItemId,
                    DisplayName = (m.display ?? $"Item #{d.ItemId}"),
                    Sku = m.sku ?? "",
                    UnitCost = d.UnitCost,
                    OriginalUnitCost = d.UnitCost,
                    Discount = 0m,
                    TaxRate = 0m,
                    MaxReturnQty = d.MaxReturnQty,
                    ReturnQty = d.ReturnQty
                });
            }
            VM.RecomputeTotals();
            UpdateTotalsUI();                                 // ✅ fill totals text
            await CapReturnWithAgainstOnHandAsync();
        }

        private void UpdateTotalsUI()
        {
            SubtotalText.Text = VM.Subtotal.ToString("N2");
            DiscountText.Text = VM.Discount.ToString("N2");
            TaxText.Text = VM.Tax.ToString("N2");
            GrandTotalText.Text = VM.GrandTotal.ToString("N2");
        }


        private Task InitFreeFormAsync()
        {
            VM.IsSupplierReadonly = false;
            VM.BasePurchaseDisplay = "—";
            VM.TargetType = InventoryLocationType.Outlet;
            VM.OutletId = AppState.Current?.CurrentOutletId > 0 ? AppState.Current.CurrentOutletId : (int?)null;
            VM.WarehouseId = null;
            VM.Lines.Clear();
            return Task.CompletedTask;
        }

        private async Task InitFromExistingReturnAsync(int returnId)
        {
            var (ret, lines) = await _svc.LoadReturnForAmendAsync(returnId);
            if (ret == null || !ret.IsReturn)
                throw new InvalidOperationException($"Return #{returnId} not found.");

            var partyName = await _party.GetPartyNameAsync(ret.PartyId) ?? $"Supplier #{ret.PartyId}";

            // Rules: supplier can be changed, source always locked
            VM.IsSupplierReadonly = false;
            VM.SupplierId = ret.PartyId;
            VM.SupplierDisplay = partyName;

            VM.TargetType = ret.LocationType;
            VM.OutletId = ret.OutletId;
            VM.WarehouseId = ret.WarehouseId;
            VM.RefPurchaseId = ret.RefPurchaseId;
            VM.ReturnNoDisplay = string.IsNullOrWhiteSpace(ret.DocNo) ? $"#{ret.Id}" : ret.DocNo;
            VM.BasePurchaseDisplay = ret.RefPurchaseId is int rid ? $"#{rid}" : "—";
            VM.IsSourceReadonly = true;

            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = await _itemsRead.GetDisplayMetaAsync(itemIds);

            VM.Lines.Clear();

            // We cap MaxReturnQty by on-hand (UI side), but real guard is in service
            var lt = ret.LocationType;
            int locId = lt == InventoryLocationType.Outlet ? ret.OutletId ?? 0 : ret.WarehouseId ?? 0;
            var onHandMap = await _invRead.GetOnHandBulkAsync(itemIds, lt, locId, CutoffUtc());

            foreach (var l in lines)
            {
                meta.TryGetValue(l.ItemId, out var m);
                var currentQty = Math.Abs(l.Qty);
                var onHand = onHandMap.TryGetValue(l.ItemId, out var v) ? v : 0m;
                var max = currentQty + onHand;

                VM.Lines.Add(new LineVM
                {
                    OriginalLineId = l.RefPurchaseLineId,
                    ItemId = l.ItemId,
                    DisplayName = m.display ?? $"Item #{l.ItemId}",
                    Sku = m.sku ?? "",
                    UnitCost = l.UnitCost,
                    OriginalUnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    ReturnQty = currentQty,
                    MaxReturnQty = Math.Max(0, max)
                });
            }

            VM.OtherCharges = ret.OtherCharges;
            VM.RecomputeTotals();
            UpdateTotalsUI();

            VM.Payments.Clear();

            // Only effective refunds belong in the editable grid
            var refunds = (ret.Payments ?? Enumerable.Empty<PurchasePayment>())
                .Where(p => p.IsEffective)                         // keep only live rows
                .OrderBy(p => p.Id)
                .Select(p => new RefundRow
                {
                    Method = p.Method,                             // TenderMethod.Cash/Bank
                    Amount = Math.Round(p.Amount, 2),
                    Note = p.Note
                })
                .ToList();

            foreach (var r in refunds)
                VM.Payments.Add(r);



            if (ret.RefPurchaseId is int)
                await CapReturnWithAgainstOnHandAsync();

        }



        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            Close();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_freeForm)
            {
                if (VM.TargetType == InventoryLocationType.Outlet)
                {
                    if (VM.OutletId is null || VM.OutletId <= 0)
                    { MessageBox.Show("Select the Outlet to return from."); return; }
                }
                else // Warehouse
                {
                    if (VM.WarehouseId is null || VM.WarehouseId <= 0)
                    { MessageBox.Show("Select the Warehouse to return from."); return; }
                }
            }

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

                var user = AppState.Current?.CurrentUserName ?? "system";
                var nowUtc = DateTime.UtcNow;

                var header = new Purchase
                {
                    Id = _returnId ?? 0,
                    IsReturn = true,
                    RefPurchaseId = _refPurchaseId ?? VM.RefPurchaseId,
                    PartyId = VM.SupplierId,
                    LocationType = VM.TargetType,
                    OutletId = VM.TargetType == InventoryLocationType.Outlet ? VM.OutletId : null,
                    WarehouseId = VM.TargetType == InventoryLocationType.Warehouse ? VM.WarehouseId : null,
                    PurchaseDate = nowUtc,
                    ReceivedAtUtc = nowUtc,
                    OtherCharges = Math.Round(VM.OtherCharges, 2),
                    Subtotal = VM.Subtotal,
                    Discount = VM.Discount,
                    Tax = VM.Tax,
                    GrandTotal = VM.GrandTotal
                };

                // refunds
                var refunds = (VM.Payments ?? Enumerable.Empty<RefundRow>())
                 .Where(p => p.Amount > 0m)
                 .Select(p => (p.Method, p.Amount, p.Note))
                 .ToList();


                var hasBaseInvoice = (_refPurchaseId ?? VM.RefPurchaseId).HasValue;
                var isAmend = _returnId.HasValue;

                // lines
                var uiLines = VM.Lines
                    .Where(l => l.ReturnQty > 0.0001m)
                    .ToList();

                if (uiLines.Count == 0)
                {
                    MessageBox.Show("Add at least one line with Return Qty > 0.");
                    return;
                }

                Purchase result;

                if (isAmend)
                {
                    // AMEND (with or without invoice)
                    var lines = uiLines.Select(l =>
                    {
                        var unitCost = l.OriginalLineId.HasValue
                            ? Math.Max(0, l.OriginalUnitCost ?? l.UnitCost)
                            : Math.Max(0, l.UnitCost);

                        return new PurchaseLine
                        {
                            ItemId = l.ItemId,
                            Qty = -Math.Abs(l.ReturnQty),
                            UnitCost = unitCost,
                            Discount = Math.Max(0, l.Discount),
                            TaxRate = Math.Max(0, l.TaxRate),
                            RefPurchaseLineId = l.OriginalLineId
                        };
                    }).ToList();

                    result = await _svc.FinalizeReturnAmendAsync(
                        header,
                        lines,
                        refunds,
                        user);
                }
                else if (hasBaseInvoice)
                {
                    // NEW return WITH invoice
                    var drafts = uiLines.Select(l =>
                        new PurchaseReturnDraftLine
                        {
                            OriginalLineId = l.OriginalLineId,
                            ItemId = l.ItemId,
                            ItemName = l.DisplayName,
                            UnitCost = l.OriginalUnitCost ?? l.UnitCost,
                            MaxReturnQty = l.MaxReturnQty,
                            ReturnQty = l.ReturnQty
                        }).ToList();

                    result = await _svc.FinalizeReturnFromInvoiceAsync(
                        header,
                        drafts,
                        refunds,
                        user);
                }
                else
                {
                    // NEW return WITHOUT invoice (free-form)
                    var lines = uiLines.Select(l =>
                    {
                        var unitCost = Math.Max(0, l.UnitCost);
                        return new PurchaseLine
                        {
                            ItemId = l.ItemId,
                            Qty = -Math.Abs(l.ReturnQty),
                            UnitCost = unitCost,
                            Discount = Math.Max(0, l.Discount),
                            TaxRate = Math.Max(0, l.TaxRate),
                            RefPurchaseLineId = null
                        };
                    }).ToList();

                    result = await _svc.FinalizeReturnWithoutInvoiceAsync(
                        header,
                        lines,
                        refunds,
                        user);
                }

                MessageBox.Show("Purchase Return saved.");
                this.DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save return: " + ex.Message);
            }
        }


        public class IdName
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed class RefundRow : INotifyPropertyChanged
        {
            private TenderMethod _method;
            public TenderMethod Method
            {
                get => _method;
                set { _method = value; OnPropertyChanged(nameof(Method)); }
            }

            private decimal _amount;
            public decimal Amount
            {
                get => _amount;
                set { _amount = value; OnPropertyChanged(nameof(Amount)); }
            }

            private string? _note;
            public string? Note
            {
                get => _note;
                set { _note = value; OnPropertyChanged(nameof(Note)); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class ReturnVM : INotifyPropertyChanged
        {
            // Header
            private int _supplierId;
            private string _supplierDisplay = "";
            private InventoryLocationType _targetType = InventoryLocationType.Outlet;
            private int? _outletId;
            private int? _warehouseId;
            private int? _refPurchaseId;
            private bool _isSupplierReadonly;
            private string _returnNoDisplay = "Auto";
            private string _basePurchaseDisplay = "—";
            private DateTime _date = DateTime.Now;
            public bool IsWithoutInvoice => !IsSupplierReadonly && RefPurchaseId is null;
            private bool _isSourceReadonly;
            public bool IsSourceReadonly
            {
                get => _isSourceReadonly;
                set { _isSourceReadonly = value; OnChanged(); OnChanged(nameof(IsSourceEditable)); }
            }
            public bool IsSourceEditable => !IsSourceReadonly;

            private decimal _subtotal;
            private decimal _discount;
            private decimal _tax;
            private decimal _other = 0m;
            private decimal _grand;


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
            public bool IsOutletSelected
            {
                get => TargetType == InventoryLocationType.Outlet;
                set
                {
                    if (value)
                    {
                        TargetType = InventoryLocationType.Outlet;
                        OnChanged();               // for this property
                        OnChanged(nameof(IsWarehouseSelected)); // keep pair consistent
                        OnChanged(nameof(ShowOutletPicker));
                        OnChanged(nameof(ShowWarehousePicker));
                    }
                }
            }
            public bool IsWarehouseSelected
            {
                get => TargetType == InventoryLocationType.Warehouse;
                set
                {
                    if (value)
                    {
                        TargetType = InventoryLocationType.Warehouse;
                        OnChanged();
                        OnChanged(nameof(IsOutletSelected));
                        OnChanged(nameof(ShowOutletPicker));
                        OnChanged(nameof(ShowWarehousePicker));
                    }
                }
            }

            public ObservableCollection<LineVM> Lines { get; } = new();
            public ObservableCollection<RefundRow> Payments { get; } = new();
            public int SupplierId { get => _supplierId; set { _supplierId = value; OnChanged(); } }
            public string SupplierDisplay { get => _supplierDisplay; set { _supplierDisplay = value; OnChanged(); } }
            public bool IsSupplierReadonly { get => _isSupplierReadonly; set { _isSupplierReadonly = value; OnChanged(); OnChanged(nameof(CanAddItems)); } }
            public int? RefPurchaseId { get => _refPurchaseId; set { _refPurchaseId = value; OnChanged(); OnChanged(nameof(CanAddItems)); } }
            public InventoryLocationType TargetType
            {
                get => _targetType;
                set
                {
                    _targetType = value;
                    OnChanged();
                    OnChanged(nameof(TargetDisplay));
                    OnChanged(nameof(ShowOutletPicker));
                    OnChanged(nameof(ShowWarehousePicker));
                }
            }

            public int? OutletId { get => _outletId; set { _outletId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public int? WarehouseId { get => _warehouseId; set { _warehouseId = value; OnChanged(); OnChanged(nameof(TargetDisplay)); } }
            public string ReturnNoDisplay { get => _returnNoDisplay; set { _returnNoDisplay = value; OnChanged(); } }
            public string BasePurchaseDisplay { get => _basePurchaseDisplay; set { _basePurchaseDisplay = value; OnChanged(); } }
            public bool CanAddItems => !IsSupplierReadonly && RefPurchaseId is null;

            public string TargetDisplay =>
                TargetType == InventoryLocationType.Outlet
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
            public ObservableCollection<IdName> OutletList { get; } = new();
            public ObservableCollection<IdName> WarehouseList { get; } = new();
            public bool ShowOutletPicker => TargetType == InventoryLocationType.Outlet;
            public bool ShowWarehousePicker => TargetType == InventoryLocationType.Warehouse;
        }

        private void RefundMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // During InitializeComponent this can fire before all named controls are wired.
            if (!IsLoaded)
                return;

            var sel =
                (RefundMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
                ?? RefundMethodBox.SelectedValue?.ToString()
                ?? RefundMethodBox.Text
                ?? "Cash";

            var isBank = string.Equals(sel, "Bank", StringComparison.OrdinalIgnoreCase);

            if (!_bankPaymentsAllowed && isBank)
            {
                MessageBox.Show("Bank payments are disabled. Configure a Purchase Bank Account in Invoice Settings for this outlet.");
                try { RefundMethodBox.SelectedItem = RefundMethodCashItem; } catch { }
                isBank = false;
            }

            // Guard against BankPickerPanel being null
            if (BankPickerPanel != null)
            {
                BankPickerPanel.Visibility = isBank ? Visibility.Visible : Visibility.Collapsed;
            }
        }


        // Totals -> push OtherCharges into VM and refresh UI
        private void OtherChargesBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return; // ignore designer / early events

            decimal other = 0m;
            if (!decimal.TryParse(OtherChargesBox.Text, out other))
                other = 0m;

            VM.OtherCharges = other;
            VM.RecomputeTotals();
            UpdateTotalsUI();
        }



        private void BtnAddRefund_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(AdvanceAmtBox.Text, out var amt) || amt <= 0m)
            {
                MessageBox.Show("Enter amount > 0");
                return;
            }

            // get method from the top combo
            var sel = (RefundMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Cash";
            var method = string.Equals(sel, "Bank", StringComparison.OrdinalIgnoreCase)
                ? TenderMethod.Bank
                : TenderMethod.Cash;

            VM.Payments.Add(new RefundRow
            {
                Method = method,
                Amount = Math.Round(amt, 2),
                Note = null
            });

            AdvanceAmtBox.Clear();
            RefundMethodBox.SelectedItem = RefundMethodCashItem;
            if (BankPickerPanel != null) BankPickerPanel.Visibility = Visibility.Collapsed;
        }

     
        private void RefundGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.Row?.Item is not RefundRow row) return;

            if (row.Amount <= 0m)
            {
                MessageBox.Show("Amount must be > 0");
                row.Amount = 1m;
            }
        }

        private void BtnDeleteRefund_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.DataContext as RefundRow
                      ?? (RefundGrid.SelectedItem as RefundRow);
            if (row == null) return;

            VM.Payments.Remove(row);
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

        private int? _previewItemId;               // currently highlighted item in the popup
        private decimal _previewOnHand;            // on-hand at the selected source


        private bool TryResolveInvLocation(out InventoryLocationType locType, out int locId)
        {
            if (VM.TargetType == InventoryLocationType.Outlet && VM.OutletId is int o && o > 0)
            {
                locType = InventoryLocationType.Outlet;
                locId = o;
                return true;
            }
            if (VM.TargetType == InventoryLocationType.Warehouse && VM.WarehouseId is int w && w > 0)
            {
                locType = InventoryLocationType.Warehouse;
                locId = w;
                return true;
            }
            locType = default;
            locId = 0;
            return false;
        }
                
        // For now, on-hand queries default to "now".
        private DateTime CutoffUtc() => DateTime.UtcNow;

        private async Task RefreshPreviewOnHandAsync()
        {
            if (!_freeForm) { _previewOnHand = 0; UpdateOnHandBadge(); return; }
            if (!VM.CanAddItems) { _previewOnHand = 0; UpdateOnHandBadge(); return; }
            if (_previewItemId is null || _previewItemId <= 0) { _previewOnHand = 0; UpdateOnHandBadge(); return; }
            if (VM.TargetType == InventoryLocationType.Outlet && (VM.OutletId is null || VM.OutletId <= 0))
            { _previewOnHand = 0; UpdateOnHandBadge(); return; }
            if (VM.TargetType == InventoryLocationType.Warehouse && (VM.WarehouseId is null || VM.WarehouseId <= 0))
            { _previewOnHand = 0; UpdateOnHandBadge(); return; }
            if (TryResolveInvLocation(out var lt, out var id))
                _previewOnHand = await _invRead.GetOnHandAtLocationAsync(_previewItemId!.Value, lt, id, CutoffUtc());
            else
                _previewOnHand = 0m;
            UpdateOnHandBadge();
        }

        private void UpdateOnHandBadge()
        {
            ItemSearch.Tag = $"On hand: {_previewOnHand:0.##}";
        }

        //private bool IsAdmin()
        //{
        //    var u = AppState.Current?.CurrentUser;
        //    if (u != null && (u.Role == UserRole.Admin)) return true;
        //    var roles = (AppState.Current?.CurrentUserRole ?? "")
        //                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
        //                .Select(r => r.Trim());
        //    return roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
        //                       || r.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
        //                       || r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase));
        //}

        private int? LinkedOutletId()
        {
            return AppState.Current?.CurrentOutletId > 0 ? AppState.Current.CurrentOutletId : (int?)null;
        }

        private async Task LoadSourcesAsync()
        {
            var outlets = await _lookup.GetOutletsAsync();
            VM.OutletList.Clear();
            foreach (var o in outlets.Select(x => new IdName { Id = x.Id, Name = x.Name }))
                VM.OutletList.Add(o);
            var warehouses = await _lookup.GetWarehousesAsync();
            VM.WarehouseList.Clear();
            foreach (var w in warehouses.Select(x => new IdName { Id = x.Id, Name = x.Name }))
                VM.WarehouseList.Add(w);
            if (_freeForm)
            {
                var admin = AuthZ.IsAdminCached();
                var linked = LinkedOutletId();
                if (admin)
                {
                    VM.IsSourceReadonly = false;
                    if (VM.TargetType == InventoryLocationType.Outlet && (VM.OutletId is null || VM.OutletId <= 0))
                        VM.OutletId = linked ?? VM.OutletList.FirstOrDefault()?.Id;
                    if (VM.TargetType == InventoryLocationType.Warehouse && (VM.WarehouseId is null || VM.WarehouseId <= 0))
                        VM.WarehouseId = VM.WarehouseList.FirstOrDefault()?.Id;
                }
                else
                {
                    if (linked is int lo && lo > 0)
                    {
                        VM.TargetType = InventoryLocationType.Outlet;
                        VM.OutletId = lo;
                        VM.WarehouseId = null;
                        VM.IsSourceReadonly = true;
                    }
                    else
                    {
                        VM.TargetType = InventoryLocationType.Outlet;
                        VM.OutletId = VM.OutletList.FirstOrDefault()?.Id;
                        VM.WarehouseId = null;
                        VM.IsSourceReadonly = true;
                    }
                }
            }
        }

        private async Task UpdateAvailablePanelAsync(LineVM? line, bool forceHideIfNoSource = true)
        {
            if (line == null || line.ItemId <= 0)
            {
                AvailablePanel.Visibility = Visibility.Collapsed;
                return;
            }
            int? outletId = null, warehouseId = null;
            if (VM.TargetType == InventoryLocationType.Outlet) outletId = VM.OutletId;
            else if (VM.TargetType == InventoryLocationType.Warehouse) warehouseId = VM.WarehouseId;
            if (forceHideIfNoSource && ((VM.TargetType == InventoryLocationType.Outlet && (outletId is null || outletId <= 0)) ||
                                        (VM.TargetType == InventoryLocationType.Warehouse && (warehouseId is null || warehouseId <= 0))))
            {
                AvailablePanel.Visibility = Visibility.Collapsed;
                return;
            }
            _availCts?.Cancel();
            _availCts = new System.Threading.CancellationTokenSource();
            var ct = _availCts.Token;
            try
            {
                if (!TryResolveInvLocation(out var lt, out var id)) { AvailablePanel.Visibility = Visibility.Collapsed; return; }
                var onHand = await _invRead.GetOnHandAtLocationAsync(line.ItemId, lt, id, CutoffUtc());
                if (ct.IsCancellationRequested) return;
                AvailableChipText.Text = $"Available: {onHand:0.##}";
                AvailablePanel.Visibility = Visibility.Visible;
            }
            catch
            {
                AvailablePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void GridLines_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row?.Item is LineVM vm)
            {
                var col = e.Column as DataGridBoundColumn;
                var path = (col?.Binding as System.Windows.Data.Binding)?.Path?.Path ?? "";
                if (string.Equals(path, nameof(LineVM.ReturnQty), StringComparison.Ordinal))
                {
                    _ = UpdateAvailablePanelAsync(vm);
                }
                else
                {
                    AvailablePanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void GridLines_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (GridLines.CurrentItem is not LineVM vm || GridLines.CurrentColumn is not DataGridBoundColumn col)
            {
                AvailablePanel.Visibility = Visibility.Collapsed;
                return;
            }
            var path = (col.Binding as System.Windows.Data.Binding)?.Path?.Path ?? "";
            if (string.Equals(path, nameof(LineVM.ReturnQty), StringComparison.Ordinal))
            {
                _ = UpdateAvailablePanelAsync(vm);
            }
            else
            {
                AvailablePanel.Visibility = Visibility.Collapsed;
            }
        }

        private void GridLines_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Row?.Item is LineVM vm)
            {
                vm.ClampQty();
                vm.RecomputeLineTotal();
                VM.RecomputeTotals();
            }
            UpdateTotalsUI();
            AvailablePanel.Visibility = Visibility.Collapsed;
        }

        private async Task SetLineMaxFromOnHandAsync(LineVM line)
        {
            if (!_freeForm) return; // only for Return-Without
            if (line == null) return;
            if (VM.TargetType == InventoryLocationType.Outlet && (VM.OutletId is null || VM.OutletId <= 0)) return;
            if (VM.TargetType == InventoryLocationType.Warehouse && (VM.WarehouseId is null || VM.WarehouseId <= 0)) return;
            if (!TryResolveInvLocation(out var lt, out var id)) return;
            var onHand = await _invRead.GetOnHandAtLocationAsync(line.ItemId, lt, id, CutoffUtc());
            line.MaxReturnQty = Math.Max(0, onHand);
            line.ClampQty();
            line.RecomputeLineTotal();
            VM.RecomputeTotals();
            UpdateTotalsUI();

        }

        private async Task CapReturnWithAgainstOnHandAsync()
        {
            // Applies only when we’re working against an original invoice.
            var hasBase = _refPurchaseId.HasValue || VM.RefPurchaseId.HasValue;
            if (!hasBase) return;

            // If we’re amending an existing return, we must allow “current returned qty + on-hand”.
            var isAmend = _returnId.HasValue;

            if (!TryResolveInvLocation(out var lt, out var locId)) return;

            var itemIds = VM.Lines.Select(l => l.ItemId).Distinct().ToList();
            var onHandMap = await _invRead.GetOnHandBulkAsync(itemIds, lt, locId, CutoffUtc());

            foreach (var l in VM.Lines)
            {
                var onHand = onHandMap.TryGetValue(l.ItemId, out var v) ? v : 0m;

                if (isAmend)
                {
                    // For Amend: cap = already-returned-on-this-doc + current on-hand
                    var currentQty = Math.Abs(l.ReturnQty);
                    l.MaxReturnQty = Math.Max(0, currentQty + onHand);
                }
                else
                {
                    // For NEW “Return With”: cap = min(invoice remaining, on-hand)
                    var invoiceCap = Math.Max(0, l.MaxReturnQty);
                    l.MaxReturnQty = Math.Min(invoiceCap, onHand);
                }

                l.ClampQty();
                l.RecomputeLineTotal();
            }

            VM.RecomputeTotals();
            UpdateTotalsUI();
        }












    }
}