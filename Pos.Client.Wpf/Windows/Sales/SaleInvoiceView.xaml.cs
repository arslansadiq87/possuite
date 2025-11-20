//Pos.Client.Wpf/MainWindow.xaml.cs
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Pricing;
using Pos.Domain.Formatting;
using System.Windows.Threading;           // DispatcherTimer
using System.Globalization;
using Pos.Client.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using System.Windows.Media;
using Pos.Client.Wpf.Printing;
using Pos.Domain.Services;
using Pos.Domain.Models.Sales;
using System.Threading.Tasks;
using Pos.Domain.Accounting;
using Pos.Domain.Services.Accounting;
using Pos.Domain.Settings;
using Pos.Domain.Models.Settings;


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class SaleInvoiceView : UserControl
    {
        private readonly ISalesService _sales;
        private readonly ITerminalContext _ctx;
        private readonly IOutletReadService _outletRead;
        private readonly IPartyLookupService _partyLookup;
        private readonly IInventoryReadService _invRead;
        private readonly IInvoiceSettingsScopedService _scopedSettings;
        private readonly IInvoiceSettingsLocalService _localSettings;
        // ✦ Add these private fields in your window class (if not present)
        private readonly IItemsReadService _items;

        //private Pos.Domain.Models.Settings.InvoiceSettingsDto? _settings; // cache
        private InvoiceSettingsLocal? _settingsLocal; // cache new model
        private bool _useTill;
        private bool _printOnSave;
        private bool _askBeforePrintOnSave;

        private readonly ObservableCollection<CartLine> _cart = new();
        private readonly IDialogService _dialogs;
        private int? _selectedCustomerId;
        
        public static readonly DependencyProperty IsCardEnabledProperty =
            DependencyProperty.Register(nameof(IsCardEnabled), typeof(bool), typeof(SaleInvoiceView), new PropertyMetadata(false));

        public bool IsCardEnabled
        {
            get => (bool)GetValue(IsCardEnabledProperty);
            set => SetValue(IsCardEnabledProperty, value);
        }

        private int OutletId => AppState.Current?.CurrentOutletId ?? 1;
        private int CounterId => AppState.Current?.CurrentCounterId ?? 1;
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;
        #pragma warning disable CS0649
        private string? _enteredCustomerName;
        #pragma warning restore CS0649
        private int cashierId => AppState.Current?.CurrentUser?.Id ?? 1;
        private string cashierDisplay => AppState.Current?.CurrentUser?.DisplayName ?? "Cashier";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;
        private string _invoiceFooter = "Thank you for shopping with us!";
        private TextBox? _activePayBox = null;    // keypad target
        private DateTime _lastEsc = DateTime.MinValue;
        private int _escCount = 0;
        private const int EscChordMs = 450; // double-press window
        private int? _currentHeldSaleId = null;
        public SaleInvoiceView(IDialogService dialogs, ITerminalContext ctx, IOutletReadService outletRead, ISalesService sales, IInventoryReadService invRead, IPartyLookupService partyLookup)
        {
            InitializeComponent();
            _ctx = ctx;
            _outletRead = outletRead;
            _dialogs = dialogs;
            _sales = sales;
            _invRead = invRead;                     // if present
            _partyLookup = partyLookup;             // <-- store it
            _scopedSettings = App.Services.GetRequiredService<IInvoiceSettingsScopedService>(); // NEW
            _localSettings = App.Services.GetRequiredService<IInvoiceSettingsLocalService>();   // NEW
            _items = App.Services.GetRequiredService<IItemsReadService>();

            CartGrid.CellEditEnding += CartGrid_CellEditEnding;
            CartGrid.ItemsSource = _cart;
            UpdateTotal();
            //LoadItemIndex();
            CashierNameText.Text = cashierDisplay;
            LoadSalesmen();
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Global_PreviewKeyDown), /*handledEventsToo:*/ true);
            FooterBox.Text = _invoiceFooter;
            Loaded += async (_, __) =>
            {
                try
                {
                    var scoped = await _scopedSettings.GetForOutletAsync(_ctx.OutletId, default);
                    var local = await _localSettings.GetForCounterAsync(_ctx.CounterId, default);


                    //var settings = await _invSettings.GetForOutletAsync(_ctx.OutletId, default);
                    //_settingsLocal = settings;

                    // If you standardized the name to UseTillMethod, use that instead:
                    _useTill = scoped.UseTill; // or settings.UseTillMethod
                    _printOnSave = scoped.AutoPrintOnSave;
                    _askBeforePrintOnSave = scoped.AskBeforePrint;
                    IsCardEnabled = scoped.SalesCardClearingAccountId.HasValue;

                    // Footer for this view (sale)
                    //_invoiceFooter = string.IsNullOrWhiteSpace(settings.FooterSale)
                    //                         ? "Thank you for shopping with us!"
                    //                         : settings.FooterSale!;
                    //FooterBox.Text = _invoiceFooter;

                    UpdateInvoicePreview();
                    UpdateInvoiceDateNow();
                    UpdateLocationUi();
                    WalkInCheck_Changed(WalkInCheck!, new RoutedEventArgs());

                    if (Window.GetWindow(this) is Window w)
                        w.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Global_PreviewKeyDown), true);

                    FocusScan();
                }
                catch (Exception ex)
                {
                    _dialogs?.AlertAsync($"Failed to load invoice settings.\n{ex.Message}", "Invoice Settings");
                }
            };

        }

        // ✦ Add this local view-model to carry defaults from Items table
        private record ItemIndexDto(
            int Id,
            string Name,
            string Sku,
            string Barcode,
            decimal Price,
            string? TaxCode,
            decimal DefaultTaxRatePct,
            bool TaxInclusive,
            decimal? DefaultDiscountPct,
            decimal? DefaultDiscountAmt,
            string? ProductName,
            string? Variant1Name,
            string? Variant1Value,
            string? Variant2Name,
            string? Variant2Value
        )
        {
            public string DisplayName =>
                ProductNameComposer.Compose(ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);
        }

        private InvoiceSettingsDto BuildPrintDtoShim()
        {
            // We no longer want printing to depend on this DTO.
            // Until ReceiptPrinter is refactored, provide just what it needs.
            var s = _settingsLocal;

            return new InvoiceSettingsDto(
                PrintOnSave: _printOnSave,
                AskToPrintOnSave: _askBeforePrintOnSave
            );
        }


        private async Task MaybePrintReceiptAsync(Sale sale, TillSession? open)
        {
            var doPrint = _printOnSave;
            if (_askBeforePrintOnSave)
            {
                var ans = MessageBox.Show("Print receipt now?", "Print",
                                          MessageBoxButton.YesNo, MessageBoxImage.Question);
                doPrint = (ans == MessageBoxResult.Yes);
            }
            if (!doPrint) return;

            try
            {
                // TEMP: build DTO for the current printer API
                //var printDto = BuildPrintDtoShim();

                await ReceiptPrinter.PrintSaleAsync(
                    sale: sale,
                    cart: _cart,
                    till: open,
                    cashierName: cashierDisplay,
                    salesmanName: _selectedSalesmanName
                    
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print failed: " + ex.Message, "Receipt Print");
            }
        }


        private async Task<bool> TryAddOrIncrementWithGuardAsync(ItemIndexDto item, string displayName)
        {
            // If the item already exists in cart → propose +1; else → 1
            var existing = _cart.FirstOrDefault(c => c.ItemId == item.Id);
            var proposedTotal = (existing?.Qty ?? 0) + 1;

            // ✦ Guard against negative/insufficient stock at outlet
            if (!await GuardSaleQtyAsync(item.Id, proposedTotal))
                return false;

            if (existing != null)
            {
                existing.Qty = proposedTotal;
                RecalcLineShared(existing);
                UpdateTotal();
                return true;
            }

            // New line (with defaults for tax/discount)
            var line = new CartLine
            {
                ItemId = item.Id,
                Sku = item.Sku,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? item.DisplayName : displayName,
                UnitPrice = item.Price,
                Qty = 1,

                // tax defaults
                TaxCode = item.TaxCode,
                TaxRatePct = item.DefaultTaxRatePct,
                TaxInclusive = item.TaxInclusive,

                // discount defaults (mutually exclusive)
                DiscountPct = item.DefaultDiscountAmt.HasValue ? null : (item.DefaultDiscountPct ?? 0m),
                DiscountAmt = item.DefaultDiscountAmt.HasValue ? item.DefaultDiscountAmt : null
            };

            RecalcLineShared(line);
            _cart.Add(line);
            UpdateTotal();
            return true;
        }


        //private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        //{
        //    var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
        //    object? selected = box.SelectedItem;
        //    if (selected is null) return;
        //    Pos.Domain.Models.Sales.ItemIndexDto? pick = null;
        //    if (selected is Pos.Domain.Models.Sales.ItemIndexDto d1)
        //    {
        //        pick = d1;
        //    }
        //    // Case 2: legacy/other DTO the control might still be producing
        //    else if (selected is Pos.Domain.DTO.ItemIndexDto legacy)
        //    {
        //        // adapt legacy -> domain
        //        pick = new Pos.Domain.Models.Sales.ItemIndexDto(
        //            Id: legacy.Id,
        //            Name: legacy.Name ?? legacy.DisplayName ?? "",
        //            Sku: legacy.Sku ?? "",
        //            Barcode: legacy.Barcode ?? "",
        //            Price: legacy.Price,
        //            TaxCode: null,
        //            DefaultTaxRatePct: 0m,
        //            TaxInclusive: false,
        //            DefaultDiscountPct: null,
        //            DefaultDiscountAmt: null,
        //            ProductName: null,
        //            Variant1Name: null, Variant1Value: null,
        //            Variant2Name: null, Variant2Value: null
        //        );
        //    }
        //    else
        //    {
        //        MessageBox.Show($"Unsupported SelectedItem type: {selected.GetType().FullName}");
        //        return;
        //    }
        //    // ---- proceed with your existing logic using 'pick' ----
        //    var itemId = pick.Id;
        //    var existing = _cart.FirstOrDefault(c => c.ItemId == itemId);
        //    var proposedTotal = (existing?.Qty ?? 0) + 1;
        //    if (!await GuardSaleQtyAsync(itemId, proposedTotal))
        //    {
        //        try { ItemSearch?.FocusSearch(); } catch { }
        //        return;
        //    }
        //    if (existing != null)
        //    {
        //        existing.Qty += 1;
        //        RecalcLineShared(existing);
        //    }
        //    else
        //    {
        //        AddItemToCart(pick);
        //    }
        //    UpdateTotal();
        //    try { ItemSearch?.FocusSearch(); } catch { }
        //}

        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
            var pick = box.SelectedItem;                        // usually Pos.Domain.DTO.ItemIndexDto
            if (pick is null) return;

            // optional: if your ItemSearchBox already returns everything you need, you can skip meta
            var meta = await _items.GetItemMetaForReturnAsync(pick.Id); // (sku/display helper)
            var display = meta?.display
                ?? ProductNameComposer.Compose(pick.ProductName, pick.Name ?? pick.DisplayName ?? "",
                                               pick.Variant1Name, pick.Variant1Value, pick.Variant2Name, pick.Variant2Value);
            var sku = meta?.sku ?? (pick.Sku ?? "");

            // normalize into our local ItemIndexDto (so we carry defaults)
            var item = new ItemIndexDto(
                Id: pick.Id,
                Name: pick.Name ?? pick.DisplayName ?? "",
                Sku: sku,
                Barcode: pick.Barcode ?? "",
                Price: pick.Price,
                TaxCode: pick.TaxCode,
                DefaultTaxRatePct: pick.DefaultTaxRatePct,
                TaxInclusive: pick.TaxInclusive,
                DefaultDiscountPct: pick.DefaultDiscountPct,
                DefaultDiscountAmt: pick.DefaultDiscountAmt,
                ProductName: pick.ProductName,
                Variant1Name: pick.Variant1Name, Variant1Value: pick.Variant1Value,
                Variant2Name: pick.Variant2Name, Variant2Value: pick.Variant2Value
            );

            var ok = await TryAddOrIncrementWithGuardAsync(item, display);
            try { box.FocusSearch(); } catch { }
            if (!ok) return;
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

        private async void ClearHoldButton_Click(object sender, RoutedEventArgs e)
        {
            var res = await _dialogs.ShowAsync(
                "Choose an action:\n\nYes = CLEAR this invoice (reset form)\nNo = HOLD this invoice for later\nCancel = Do nothing",
                "Clear or Hold?",
                DialogButtons.YesNoCancel);
            switch (res)
            {
                case DialogResult.Yes:
                    ClearCurrentInvoice(confirm: true);
                    break;
                case DialogResult.No:
                    HoldCurrentInvoiceQuick();
                    break;
                default:
                    return;
            }
        }

        private async void ClearCurrentInvoice(bool confirm)
        {
            if (confirm)
            {
                if (_cart.Count == 0 && string.IsNullOrWhiteSpace(InvDiscPctBox.Text) && string.IsNullOrWhiteSpace(InvDiscAmtBox.Text))
                {
                }
                else
                {
                    var ok = await _dialogs.ShowAsync(
                        "Clear the current invoice (cart, discounts, customer fields)?",
                        "Confirm Clear?",
                        DialogButtons.OKCancel);
                    if (ok != DialogResult.OK) return;
                }
            }
            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = false;
            _currentHeldSaleId = null; // no longer editing a held draft
            UpdateTotal();
        }

        private (decimal Subtotal, decimal InvDiscValue, decimal TaxTotal, decimal Grand, int Items, int Qty)
            ComputeTotalsSnapshot()
        {
            var lines = _cart.ToList();
            var lineNetSum = lines.Sum(l => l.LineNet);
            var lineTaxSum = lines.Sum(l => l.LineTax);
            if (_invDiscPct > 100m) _invDiscPct = 100m;
            var baseForInvDisc = lineNetSum;
            var invDiscValue = 0m;
            if (baseForInvDisc > 0m)
            {
                invDiscValue = (_invDiscAmt > 0m)
                    ? Math.Min(_invDiscAmt, baseForInvDisc)
                    : PricingMath.RoundMoney(baseForInvDisc * (_invDiscPct / 100m));
            }
            var factor = (baseForInvDisc > 0m) ? (baseForInvDisc - invDiscValue) / baseForInvDisc : 1m;
            decimal adjNetSum = 0m, adjTaxSum = 0m;
            foreach (var l in lines)
            {
                var adjNet = PricingMath.RoundMoney(l.LineNet * factor);
                var taxPerNet = (l.LineNet > 0m) ? (l.LineTax / l.LineNet) : 0m;
                var adjTax = PricingMath.RoundMoney(adjNet * taxPerNet);
                adjNetSum += adjNet;
                adjTaxSum += adjTax;
            }
            var subtotal = adjNetSum;
            var tax = adjTaxSum;
            var grand = subtotal + tax;
            return (subtotal, invDiscValue, tax, grand, _cart.Count, _cart.Sum(l => l.Qty));
        }

        private void HoldCurrentInvoiceQuick()
        {
            if (!_cart.Any()) { MessageBox.Show("Nothing to hold — cart is empty."); return; }
            var tag = Microsoft.VisualBasic.Interaction.InputBox(
                "Optional tag for this hold (e.g., customer name, note):", "Hold Invoice", _enteredCustomerName ?? "");
            try
            {
                SaveHold(tag);
                MessageBox.Show("Invoice held. Find it under Held (F8) to resume.", "Held");
                ClearCurrentInvoice(confirm: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not hold invoice: " + ex.Message, "Error");
            }
        }

        private async void SaveHold(string? holdTag)
        {
            var (subtotal, invDiscValue, tax, grand, items, qty) = ComputeTotalsSnapshot();
            if (grand <= 0m) throw new InvalidOperationException("Total must be > 0.");
            var req = new SaleHoldRequest
            {
                OutletId = OutletId,
                CounterId = CounterId,
                IsReturn = (ReturnCheck?.IsChecked == true),
                CashierId = cashierId,
                SalesmanId = _selectedSalesmanId,
                CustomerKind = (WalkInCheck?.IsChecked == true) ? CustomerKind.WalkIn : CustomerKind.Registered,
                CustomerId = _selectedCustomerId,
                CustomerName = string.IsNullOrWhiteSpace(CustNameBox?.Text) ? null : CustNameBox!.Text.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(CustPhoneBox?.Text) ? null : CustPhoneBox!.Text.Trim(),
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : null,
                InvoiceDiscountValue = invDiscValue,
                Subtotal = subtotal,
                TaxTotal = tax,
                Total = grand,
                HoldTag = holdTag,
                Footer = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text
            };
            foreach (var l in _cart)
            {
                req.Lines.Add(new SaleHoldRequest.SaleLineInput(
                    l.ItemId, l.Qty, l.UnitPrice, l.DiscountPct, l.DiscountAmt,
                    l.TaxCode, l.TaxRatePct, l.TaxInclusive,
                    l.UnitNet, l.LineNet, l.LineTax, l.LineTotal));
            }
            await _sales.HoldAsync(req);
        }

        private async void UpdateInvoicePreview()
        {
            var prev = await _sales.GetInvoicePreviewAsync(CounterId);
            InvoicePreviewText.Text = prev.Human;
        }

        private void UpdateInvoiceDateNow()
        {
            InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");
        }
        private async void LoadSalesmen()
        {
            var list = await _sales.GetSalesmenAsync();
            SalesmanBox.ItemsSource = list;
            SalesmanBox.DisplayMemberPath = "FullName";
            SalesmanBox.SelectedValuePath = "Id";
            SalesmanBox.SelectedIndex = list.Count > 0 ? 0 : -1;
        }

        private void SalesmanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SalesmanBox.SelectedItem is Pos.Domain.Hr.Staff sel)
            {
                if (sel.Id == 0)
                {
                    _selectedSalesmanId = null;
                    _selectedSalesmanName = null;
                }
                else
                {
                    _selectedSalesmanId = sel.Id;
                    _selectedSalesmanName = sel.FullName;
                }
            }
            else
            {
                _selectedSalesmanId = null;
                _selectedSalesmanName = null;
            }
        }

        private void PayBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _activePayBox = sender as TextBox;
        }

        private void WalkInCheck_Changed(object sender, RoutedEventArgs e)
        {
            var isWalkIn = WalkInCheck?.IsChecked == true;
            // Enable name/phone for Walk-in; disable for Registered
            if (CustNameBox != null) CustNameBox.IsEnabled = isWalkIn;
            if (CustPhoneBox != null) CustPhoneBox.IsEnabled = isWalkIn;
            if (isWalkIn)
            {
                _selectedCustomerId = null;
                if (CustomerPicker != null)
                {
                    CustomerPicker.SelectedCustomer = null;
                    CustomerPicker.SelectedCustomerId = null;
                    CustomerPicker.Query = "";
                }
            }
            else
            {
                // Switching to Registered → hide walk-in fields (XAML triggers) and clear their text
                if (CustNameBox != null) CustNameBox.Text = "";
                if (CustPhoneBox != null) CustPhoneBox.Text = "";
            }
        }

        private void ReturnCheck_Changed(object sender, RoutedEventArgs e)
        {
        }

        private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _invoiceFooter = FooterBox.Text ?? "";
        }
      
        //public ObservableCollection<ItemIndexDto> DataContextItemIndex { get; } = new();

        //private async void LoadItemIndex()
        //{
        //    var list = await _sales.GetItemIndexAsync();
        //    DataContextItemIndex.Clear();
        //    foreach (var it in list) DataContextItemIndex.Add(it);
        //}
           
        private void InvoiceDiscountChanged(object sender, TextChangedEventArgs e)
        {
            decimal.TryParse(InvDiscPctBox.Text, out _invDiscPct);
            decimal.TryParse(InvDiscAmtBox.Text, out _invDiscAmt);
            UpdateTotal(); // recompute overall total shown
        }
        //private void AddItemToCart(Pos.Domain.Models.Sales.ItemIndexDto item)
        //{
        //    var existing = _cart.FirstOrDefault(c => c.ItemId == item.Id);
        //    if (existing != null)
        //    {
        //        existing.Qty += 1;
        //        RecalcLineShared(existing);
        //        UpdateTotal();
        //        return;
        //    }
        //    var cl = new CartLine
        //    {
        //        ItemId = item.Id,
        //        Sku = item.Sku,
        //        DisplayName = ProductNameComposer.Compose(item.ProductName, item.Name, item.Variant1Name, item.Variant1Value, item.Variant2Name, item.Variant2Value),
        //        UnitPrice = item.Price,
        //        Qty = 1,
        //        TaxCode = item.TaxCode,
        //        TaxRatePct = item.DefaultTaxRatePct,
        //        TaxInclusive = item.TaxInclusive,
        //        DiscountPct = item.DefaultDiscountPct ?? 0m,
        //        DiscountAmt = item.DefaultDiscountAmt ?? 0m
        //    };
        //    if ((cl.DiscountAmt ?? 0) > 0) cl.DiscountPct = null;
        //    RecalcLineShared(cl);
        //    _cart.Add(cl);
        //    UpdateTotal();
        //}
        private void AddItemToCart(ItemIndexDto item, string displayNameOverride)
        {
            // If you merge quantities for same item, keep your merge logic
            var existing = _cart.FirstOrDefault(c => c.ItemId == item.Id);
            if (existing != null)
            {
                existing.Qty += 1;
                RecalcLineShared(existing);
                UpdateTotal();
                return;
            }

            var line = new CartLine
            {
                ItemId = item.Id,
                Sku = item.Sku,
                DisplayName = displayNameOverride ?? item.DisplayName,
                UnitPrice = item.Price,
                Qty = 1,

                // ✦ Auto-pick tax defaults
                TaxCode = item.TaxCode,
                TaxRatePct = item.DefaultTaxRatePct,
                TaxInclusive = item.TaxInclusive,

                // ✦ Auto-pick discount defaults (mutually exclusive)
                DiscountPct = item.DefaultDiscountAmt.HasValue ? null : (item.DefaultDiscountPct ?? 0m),
                DiscountAmt = item.DefaultDiscountAmt.HasValue ? item.DefaultDiscountAmt : null
            };

            RecalcLineShared(line);
            _cart.Add(line);
            UpdateTotal();
        }

        private void UpdateLocationUi()
        {
            if (_ctx == null) return;
            _ = RefreshInventoryLocationAsync();   // fire-and-forget async populate
        }

        private async Task RefreshInventoryLocationAsync(CancellationToken ct = default)
        {
            try
            {
                var outletName = await _outletRead.GetOutletNameAsync(_ctx.OutletId, ct);
                var counterName = await _outletRead.GetCounterNameAsync(_ctx.CounterId, ct);
                InventoryLocationText.Text = $"Outlet: {outletName}";
            }
            catch (Exception)
            {
                InventoryLocationText.Text = $"Outlet: #{_ctx.OutletId}";
            }
        }

        private void UpdateTotal()
        {
            // 1) sum current lines (they already include per-line discounts/tax calculations)
            var lines = _cart.ToList();
            var lineNetSum = lines.Sum(l => l.LineNet);   // net before tax
            var lineTaxSum = lines.Sum(l => l.LineTax);   // tax as currently calculated
            var lineGross = lineNetSum + lineTaxSum;
            // 2) compute invoice-level discount value on Subtotal (net) BEFORE tax
            var baseForInvDisc = lineNetSum;
            if (baseForInvDisc < 0.01m) { TotalText.Text = lineGross.ToString("0.00"); return; }
            var invDiscValue = (_invDiscAmt > 0m)
                ? Math.Min(_invDiscAmt, baseForInvDisc)
                : PricingMath.RoundMoney(baseForInvDisc * (_invDiscPct / 100m));
            // 3) discount factor for proportional allocation across lines
            var factor = (baseForInvDisc - invDiscValue) / baseForInvDisc;
            // 4) recompute tax proportionally after invoice discount (keeps inclusive/exclusive behavior stable)
            decimal adjNetSum = 0m, adjTaxSum = 0m;
            foreach (var l in lines)
            {
                var adjNet = PricingMath.RoundMoney(l.LineNet * factor);
                var taxPerNet = (l.LineNet > 0m) ? (l.LineTax / l.LineNet) : 0m;
                var adjTax = PricingMath.RoundMoney(adjNet * taxPerNet);
                adjNetSum += adjNet;
                adjTaxSum += adjTax;
            }
            var subtotal = adjNetSum;       // AFTER invoice discount
            var taxtotal = adjTaxSum;
            var grand = subtotal + taxtotal;
            SubtotalText.Text = subtotal.ToString("N2", CultureInfo.CurrentCulture);
            var discountSigned = -invDiscValue; // show as negative with separators
            DiscountText.Text = discountSigned.ToString("N2", CultureInfo.CurrentCulture);
            TaxText.Text = taxtotal.ToString("N2", CultureInfo.CurrentCulture);
            TotalText.Text = grand.ToString("N2", CultureInfo.CurrentCulture);
            var itemsCount = _cart.Count;
            var qtySum = _cart.Sum(l => l.Qty);
            ItemsCountText.Text = itemsCount.ToString();
            QtySumText.Text = qtySum.ToString();
        }

        private async void PayButton_Click(object sender, RoutedEventArgs e)
        {
            // 0) basic guards
            if (!_cart.Any()) { MessageBox.Show("Cart is empty."); return; }
            // 1) ensure open till (via service)
            // OLD
            // var open = await _sales.GetOpenTillAsync(OutletId, CounterId);
            // if (open == null) { MessageBox.Show(...); return; }

            // NEW
            TillSession? open = null;
            if (_useTill)
            {
                open = await _sales.GetOpenTillAsync(OutletId, CounterId);
                if (open == null)
                {
                    MessageBox.Show("Till is CLOSED. Please open till before taking payment.", "Till Closed");
                    return;
                }
            }

            // 2) recompute current cart math (fresh per-line totals)
            foreach (var cl in _cart) RecalcLineShared(cl);
            var lineNetSum = _cart.Sum(l => l.LineNet);
            var lineTaxSum = _cart.Sum(l => l.LineTax);
            if (_invDiscPct > 100m) _invDiscPct = 100m;
            var baseForInvDisc = lineNetSum;
            var invDiscValue = 0m;
            if (baseForInvDisc > 0m)
            {
                invDiscValue = (_invDiscAmt > 0m)
                    ? Math.Min(_invDiscAmt, baseForInvDisc)
                    : PricingMath.RoundMoney(baseForInvDisc * (_invDiscPct / 100m));
            }

            var factor = (baseForInvDisc > 0m) ? (baseForInvDisc - invDiscValue) / baseForInvDisc : 1m;
            decimal adjNetSum = 0m, adjTaxSum = 0m;
            foreach (var l in _cart)
            {
                var adjNet = PricingMath.RoundMoney(l.LineNet * factor);
                var taxPerNet = (l.LineNet > 0m) ? (l.LineTax / l.LineNet) : 0m;
                var adjTax = PricingMath.RoundMoney(adjNet * taxPerNet);
                adjNetSum += adjNet;
                adjTaxSum += adjTax;
            }
            var subtotal = adjNetSum;               // after invoice-level discount
            var taxtotal = adjTaxSum;
            var grand = subtotal + taxtotal;
            if (grand <= 0m) { MessageBox.Show("Total must be greater than 0."); return; }
            var itemsCount = _cart.Count;
            var qtySum = _cart.Sum(l => l.Qty);
            // 3) take payment (overlay)
            var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
            var payResult = await paySvc.ShowAsync(
                subtotal, invDiscValue, taxtotal, grand, itemsCount, qtySum,
                differenceMode: false, amountDelta: 0m, title: "Take Payment");
            if (!payResult.Confirmed)
            {
                RestoreFocusToScan();
                return;
            }
            var enteredCash = payResult.Cash;
            var enteredCard = payResult.Card;
            // 3a) card toggle: if disabled, convert card portion to cash via "difference" screen
            if (!IsCardEnabled && enteredCard > 0m)
            {
                MessageBox.Show(
                    "Card payments are disabled for this outlet. Please collect cash.",
                    "Card Disabled", MessageBoxButton.OK, MessageBoxImage.Information);
                var cashOnly = await paySvc.ShowAsync(
                    subtotal, invDiscValue, taxtotal, grand,
                    itemsCount, qtySum,
                    differenceMode: true,
                    amountDelta: enteredCard,
                    title: "Collect Remaining (Cash Only)");
                if (!cashOnly.Confirmed)
                {
                    RestoreFocusToScan();
                    return;
                }
                enteredCash += cashOnly.Cash;
                enteredCard = 0m;
            }
            var paid = enteredCash + enteredCard;
            if (paid + 0.01m < grand) // safety net (dialog already enforces this)
            {
                MessageBox.Show("Payment is less than total.");
                return;
            }
            var paymentMethod =
                (enteredCash > 0m && enteredCard > 0m) ? PaymentMethod.Mixed
              : (enteredCash > 0m) ? PaymentMethod.Cash
                                                      : PaymentMethod.Card;
            // 4) customer & credit guards (UI-level rules)
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = isWalkIn ? null : _selectedCustomerId;
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());
            var creditPortion = grand - paid;                  // B2C on-account
            bool wantsCredit = (creditPortion > 0.009m);
            if (wantsCredit && (customerId == null))
            {
                MessageBox.Show(
                    "This invoice has an unpaid (on-account) amount.\n\n" +
                    "Please select a registered customer to continue.",
                    "Customer required for credit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Salesman (take from selected box if _selectedSalesmanId isn’t set)
            int? salesmanIdLocal = _selectedSalesmanId;
            if (!salesmanIdLocal.HasValue && SalesmanBox?.SelectedValue is int vFromBox)
                salesmanIdLocal = (vFromBox == 0) ? (int?)null : vFromBox;
            // 5) build finalize request for the service layer
            var req = new SaleFinalizeRequest
            {
                OutletId = OutletId,
                CounterId = CounterId,
                TillSessionId = _useTill ? open!.Id : (int?)null,  // <--- key line
                IsReturn = (ReturnCheck?.IsChecked == true),
                CashierId = cashierId,
                SalesmanId = salesmanIdLocal,
                CustomerKind = customerKind,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : null,
                InvoiceDiscountValue = invDiscValue,
                Subtotal = subtotal,
                TaxTotal = taxtotal,
                Total = grand,
                CashAmount = enteredCash,
                CardAmount = enteredCard,
                PaymentMethod = paymentMethod,
                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text,
                EReceiptToken = Guid.NewGuid().ToString("N"),
                HeldSaleId = _currentHeldSaleId
            };
            foreach (var line in _cart)
            {
                req.Lines.Add(new SaleFinalizeRequest.SaleLineInput(
                    line.ItemId, line.Qty, line.UnitPrice, line.DiscountPct, line.DiscountAmt,
                    line.TaxCode, line.TaxRatePct, line.TaxInclusive,
                    line.UnitNet, line.LineNet, line.LineTax, line.LineTotal));
            }
            // 6) finalize via service (this handles sequence, stock, outbox, GL, and voiding held)
            Sale sale;
            try
            {
                sale = await _sales.FinalizeAsync(req);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Finalize Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not finalize sale:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            // 7) OPTIONAL: A/R posting for unpaid portion (keep here, or move into SalesService)
            try
            {
                var poster = App.Services.GetRequiredService<IPartyPostingService>();
                var unpaid = sale.Total - (sale.CashAmount + sale.CardAmount);
                if (unpaid > 0.009m && sale.CustomerId.HasValue)
                {
                    await poster.PostAsync(
                        partyId: sale.CustomerId.Value,
                        scope: BillingScope.Outlet,
                        outletId: sale.OutletId,
                        docType: PartyLedgerDocType.Sale,
                        docId: sale.Id,
                        debit: unpaid, credit: 0m,
                        memo: $"On-account sale #{sale.CounterId}-{sale.InvoiceNumber}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Sale saved, but posting to customer ledger failed:\n" + ex.Message,
                    "Ledger warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            // 8) print (best effort)
            try
            {
                await MaybePrintReceiptAsync(sale, open);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printed with error: " + ex.Message, "Print");
            }
            // 9) success + UI reset
            MessageBox.Show(
                $"Sale saved. ID: {sale.Id}\nInvoice: {CounterId}-{sale.InvoiceNumber}\nTotal: {sale.Total:0.00}",
                "Success");
            _currentHeldSaleId = null;
            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = false;
            // keep FooterBox as-is so cashier’s message persists
            UpdateTotal();
            UpdateInvoicePreview();
            UpdateInvoiceDateNow();
            RestoreFocusToScan();
        }

        private async void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CartLine l)
            {
                var proposedTotal = l.Qty + 1;
                if (!await GuardSaleQtyAsync(l.ItemId, proposedTotal)) return;
                l.Qty = proposedTotal;
                RecalcLineShared(l);
                UpdateTotal();
            }
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CartLine l)
            {
                if (l.Qty > 1) { l.Qty -= 1; RecalcLineShared(l); }
                else { _cart.Remove(l); }        // remove when qty would hit 0
                UpdateTotal();
            }
        }

        private void DeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CartLine l)
            {
                _cart.Remove(l);
                UpdateTotal();
            }
        }

        //private static void RecalcLineShared(CartLine l)
        //{
        //    var t = LinePricing.Recalc(
        //        qty: l.Qty,
        //        unitPrice: l.UnitPrice,
        //        discountPct: l.DiscountPct ?? 0m,
        //        discountAmt: l.DiscountAmt ?? 0m,
        //        taxInclusive: l.TaxInclusive,
        //        taxRatePct: l.TaxRatePct
        //    );

        //    l.LineNet = t.Net;    // ex-tax net for the line
        //    l.LineTax = t.Tax;    // tax for the line
        //    l.LineTotal = t.Total;  // final line total
        //    l.UnitNet = (l.Qty > 0) ? PricingMath.RoundMoney(t.Net / l.Qty) : 0m;
        //}
        private static void RecalcLineShared(CartLine l)
        {
            var t = LinePricing.Recalc(
                qty: l.Qty,
                unitPrice: l.UnitPrice,
                discountPct: l.DiscountPct ?? 0m,
                discountAmt: l.DiscountAmt ?? 0m,
                taxInclusive: l.TaxInclusive,
                taxRatePct: l.TaxRatePct
            );

            l.LineNet = t.Net;
            l.LineTax = t.Tax;
            l.LineTotal = t.Total;
            l.UnitNet = (l.Qty > 0) ? PricingMath.RoundMoney(t.Net / l.Qty) : 0m;
        }

        private void CartGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // Defer until the binding pushes new value
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (e.Row?.Item is not CartLine l) return;

                var header = (e.Column.Header as string) ?? string.Empty;

                // ✦ Guard Qty increases typed by the user
                if (header.Contains("Qty", StringComparison.OrdinalIgnoreCase))
                {
                    if (l.Qty < 1) l.Qty = 1; // normalize
                    var ok = await GuardSaleQtyAsync(l.ItemId, l.Qty);
                    if (!ok)
                    {
                        // roll back 1 step (minimum 1)
                        l.Qty = Math.Max(1, l.Qty - 1);
                    }
                }

                // Enforce “either % or Amt” for discounts, then recalc
                if (header.Contains("Disc %"))
                {
                    if ((l.DiscountPct ?? 0) > 0) l.DiscountAmt = null;
                }
                else if (header.Contains("Disc Amt"))
                {
                    if ((l.DiscountAmt ?? 0) > 0) l.DiscountPct = null;
                }

                RecalcLineShared(l);
                UpdateTotal();
            }), DispatcherPriority.Background);
        }


        //private void CartGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        //{
        //    if (e.EditAction != DataGridEditAction.Commit) return;
        //    // Defer until edit box value is applied to the bound object
        //    Dispatcher.BeginInvoke(new Action(async () =>
        //    {
        //        if (e.Row?.Item is CartLine l)
        //        {
        //            var header = (e.Column.Header as string) ?? string.Empty;
        //            if (header.Contains("Qty", StringComparison.OrdinalIgnoreCase))
        //            {
        //                if (l.Qty <= 0) { l.Qty = 1; }
        //                var ok = await GuardSaleQtyAsync(l.ItemId, l.Qty);
        //                if (!ok)
        //                {
        //                    l.Qty -= 1;
        //                    if (l.Qty < 1) l.Qty = 1;
        //                }
        //            }
        //            // Preserve your existing discount/price recompute logic
        //            if (header.Contains("Disc %"))
        //            {
        //                if ((l.DiscountPct ?? 0) > 0) l.DiscountAmt = null;
        //            }
        //            else if (header.Contains("Disc Amt"))
        //            {
        //                if ((l.DiscountAmt ?? 0) > 0) l.DiscountPct = null;
        //            }
        //            RecalcLineShared(l);
        //            UpdateTotal();
        //        }
        //    }), DispatcherPriority.Background);
        //}

        private async void ResumeHeld(int saleId)
        {
            var s = await _sales.LoadHeldAsync(saleId);
            if (s == null) { MessageBox.Show("Held invoice not found."); return; }

            _cart.Clear();
            foreach (var l in s.Lines)
            {
                _cart.Add(new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = l.Sku,
                    DisplayName = l.DisplayName,
                    Qty = (int)decimal.Round(l.Qty, 0, MidpointRounding.AwayFromZero),
                    UnitPrice = l.UnitPrice,
                    DiscountPct = l.DiscountPct,
                    DiscountAmt = l.DiscountAmt,
                    TaxCode = l.TaxCode,
                    TaxRatePct = l.TaxRatePct,
                    TaxInclusive = l.TaxInclusive,
                    UnitNet = l.UnitNet,
                    LineNet = l.LineNet,
                    LineTax = l.LineTax,
                    LineTotal = l.LineTotal
                });
            }

            _invDiscPct = s.InvoiceDiscountAmt.HasValue ? 0m : (s.InvoiceDiscountPct ?? 0m);
            _invDiscAmt = s.InvoiceDiscountAmt ?? 0m;
            InvDiscPctBox.Text = (_invDiscPct > 0m) ? _invDiscPct.ToString() : "";
            InvDiscAmtBox.Text = (_invDiscAmt > 0m) ? _invDiscAmt.ToString() : "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = s.IsReturn;
            if (WalkInCheck != null) WalkInCheck.IsChecked = (s.CustomerKind == CustomerKind.WalkIn);
            if (CustNameBox != null) CustNameBox.Text = s.CustomerName ?? "";
            if (CustPhoneBox != null) CustPhoneBox.Text = s.CustomerPhone ?? "";
            if (!string.IsNullOrWhiteSpace(s.InvoiceFooter) && FooterBox != null) FooterBox.Text = s.InvoiceFooter;
            if (SalesmanBox != null) SalesmanBox.SelectedValue = s.SalesmanId ?? 0;

            _currentHeldSaleId = s.SaleId;
            UpdateTotal();
        }

        private void Global_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F9)
            {
                PayButton_Click(this, e);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete)
            {
                if (CartGrid.SelectedItem is CartLine l) { _cart.Remove(l); UpdateTotal(); }
                return;
            }
            if (e.Key == Key.F5) { ClearCurrentInvoice(confirm: true); e.Handled = true; return; }
            if (e.Key == Key.F8) { HoldCurrentInvoiceQuick(); e.Handled = true; return; }
            if (e.Key == Key.Escape)
            {
                var now = DateTime.UtcNow;
                _escCount = (now - _lastEsc).TotalMilliseconds <= EscChordMs ? _escCount + 1 : 1;
                _lastEsc = now;
                if (_escCount >= 2)
                {
                    _escCount = 0;
                    FocusScan();
                    e.Handled = true;
                }
            }
        }

        public void FocusScan()
        {
            try
            {
                CartGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                CartGrid.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch { }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var tb = FindDescendant<TextBox>(ItemSearch);
                if (tb != null) { Keyboard.Focus(tb); tb.Focus(); tb.SelectAll(); }
                else { Keyboard.Focus(ItemSearch); ItemSearch.Focus(); }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        public void RestoreFocusToScan() => FocusScan();

        private async Task<bool> GuardSaleQtyAsync(int itemId, decimal proposedQty)
        {
            var ok = await _sales.GuardSaleQtyAsync(OutletId, itemId, proposedQty);
            if (!ok)
                MessageBox.Show("Not enough stock at this outlet.", "Not enough stock", MessageBoxButton.OK, MessageBoxImage.Warning);
            return ok;
        }

        private void CustomerPicker_CustomerPicked(object sender, RoutedEventArgs e)
        {
            var picked = CustomerPicker.SelectedCustomer;
            if (picked == null) return;
            _selectedCustomerId = picked.Id;
            // Switch to Registered flow
            if (WalkInCheck != null) WalkInCheck.IsChecked = false;
            // Fill for receipt; still editable
            if (CustNameBox != null) CustNameBox.Text = picked.Name;
            if (CustPhoneBox != null) CustPhoneBox.Text = picked.Phone;
            FocusScan(); // focuses ItemSearch’s TextBox for instant scanning
        }

        private void CustomerClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _selectedCustomerId = null;
            if (WalkInCheck != null) WalkInCheck.IsChecked = true; // Walk-in
            // Clear UI
            CustomerPicker.SelectedCustomer = null;
            CustomerPicker.SelectedCustomerId = null;
            CustomerPicker.Query = "";
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
        }
    }
}
    