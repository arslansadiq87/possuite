//Pos.Client.Wpf/MainWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Domain.Pricing;
using Pos.Domain.Formatting;
using System.Windows.Data;                // CollectionViewSource
using System.Windows.Threading;           // DispatcherTimer
using System.Globalization;
using Pos.Client.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using Pos.Persistence.Services;
using System.Linq;
using Pos.Client.Wpf.Printing;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class SaleInvoiceView : UserControl
    {

        private readonly DbContextOptions<PosClientDbContext> _dbOptions;
        private readonly ObservableCollection<CartLine> _cart = new();
        private readonly IDialogService _dialogs;
        private readonly ITerminalContext _ctx;
        private int? _selectedCustomerId;
        private readonly IInvoiceSettingsService _invSettings;
        private bool _printOnSave;
        private bool _askBeforePrintOnSave;
        // --- NEW: card enable/disable flag based on invoice settings ---
        private bool _isCardEnabled = false;
        //public bool IsCardEnabled => _isCardEnabled;  // optional: for XAML binding if you add any indicator later
                                                      // --- END NEW ---
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
        private bool _isWalkIn = true;
        private string? _enteredCustomerName;
        private string? _enteredCustomerPhone;
        //private readonly IStaffDirectory _staff;
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
        public SaleInvoiceView(IDialogService dialogs, ITerminalContext ctx)
        {
            InitializeComponent();
            _ctx = ctx;
            _dialogs = dialogs;
            _invSettings = App.Services.GetRequiredService<IInvoiceSettingsService>();
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;
            _dbOptions = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)   // <-- use the SAME connection string
                .Options;
            CartGrid.ItemsSource = _cart;
            UpdateTotal();
            LoadItemIndex();
            CashierNameText.Text = cashierDisplay;
            LoadSalesmen();
            AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Global_PreviewKeyDown), /*handledEventsToo:*/ true);
            FooterBox.Text = _invoiceFooter;
            //CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;
            Loaded += (_, __) =>
            {
                var (s, _) = _invSettings.GetAsync(_ctx.OutletId, "en").GetAwaiter().GetResult();
                _printOnSave = s.PrintOnSave;
                _askBeforePrintOnSave = s.AskToPrintOnSave;
                // --- NEW: set card availability for this outlet ---
                IsCardEnabled = (s.SalesCardClearingAccountId != null);
                // --- END NEW ---

                UpdateInvoicePreview();
                UpdateInvoiceDateNow();
                UpdateLocationUi();
                WalkInCheck_Changed(WalkInCheck!, new RoutedEventArgs());
                if (Window.GetWindow(this) is Window w)
                    w.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(Global_PreviewKeyDown), true);
                FocusScan();
            };
        }

        private async Task MaybePrintReceiptAsync(Sale sale, TillSession? open)
        {
            var doPrint = _printOnSave;
            if (_askBeforePrintOnSave)
            {
                var ans = MessageBox.Show(
                    "Print receipt now?",
                    "Print",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                doPrint = (ans == MessageBoxResult.Yes);
            }
            if (!doPrint) return;
            try
            {
                await ReceiptPrinter.PrintSaleAsync(
                    sale: sale,
                    cart: _cart,
                    till: open,
                    cashierName: cashierDisplay,
                    salesmanName: _selectedSalesmanName,
                    settingsSvc: _invSettings);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print failed: " + ex.Message, "Receipt Print");
            }
        }
        private ItemIndexDto AdaptItem(Pos.Domain.DTO.ItemIndexDto src)
        {
            return new ItemIndexDto(
                Id: src.Id,
                Name: src.Name ?? src.DisplayName ?? "",
                Sku: src.Sku ?? "",
                Barcode: src.Barcode ?? "",
                Price: src.Price,
                TaxCode: null,                   // ItemSearchBox index may not carry these; keep existing defaults
                DefaultTaxRatePct: 0m,
                TaxInclusive: false,
                DefaultDiscountPct: null,
                DefaultDiscountAmt: null,
                ProductName: null,
                Variant1Name: null, Variant1Value: null,
                Variant2Name: null, Variant2Value: null
            );
        }

        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
            var pick = box.SelectedItem;
            if (pick is null) return;
            var itemId = pick.Id;
            var existing = _cart.FirstOrDefault(c => c.ItemId == itemId);
            var proposedTotal = (existing?.Qty ?? 0) + 1;
            if (!await GuardSaleQtyAsync(itemId, proposedTotal))
            {
                try { ItemSearch?.FocusSearch(); } catch { }
                return;
            }
            if (existing != null)
            {
                existing.Qty += 1;
                RecalcLineShared(existing);
            }
            else
            {
                AddItemToCart(AdaptItem(pick)); // your existing add
            }
            UpdateTotal();
            try { ItemSearch?.FocusSearch(); } catch { }
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

        private void SaveHold(string? holdTag)
        {
            var (subtotal, invDiscValue, tax, grand, items, qty) = ComputeTotalsSnapshot();
            if (grand <= 0m) throw new InvalidOperationException("Total must be > 0.");
            using var db = new PosClientDbContext(_dbOptions);
            var sale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = OutletId,
                CounterId = CounterId,
                TillSessionId = null,
                IsReturn = (ReturnCheck?.IsChecked == true),
                InvoiceNumber = 0,                // unassigned until finalization
                Status = SaleStatus.Draft,
                HoldTag = string.IsNullOrWhiteSpace(holdTag) ? null : holdTag.Trim(),
                // invoice-level discounts
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,
                DiscountBeforeTax = true,
                // totals
                Subtotal = subtotal,
                TaxTotal = tax,
                Total = grand,
                // users
                CashierId = cashierId,
                SalesmanId = _selectedSalesmanId,
                // customer snapshot
                CustomerKind = (WalkInCheck?.IsChecked == true) ? CustomerKind.WalkIn : CustomerKind.Registered,
                CustomerId = null, // you can attach if you later add a Customer table
                CustomerName = string.IsNullOrWhiteSpace(CustNameBox?.Text) ? null : CustNameBox!.Text.Trim(),
                CustomerPhone = string.IsNullOrWhiteSpace(CustPhoneBox?.Text) ? null : CustPhoneBox!.Text.Trim(),
                // payment not captured yet
                CashAmount = 0m,
                CardAmount = 0m,
                PaymentMethod = PaymentMethod.Cash,
                // e-receipt/footer not relevant yet
                EReceiptToken = null,
                EReceiptUrl = null,
                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text
            };
            db.Sales.Add(sale);
            db.SaveChanges();
            foreach (var l in _cart)
            {
                db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id,
                    ItemId = l.ItemId,
                    Qty = l.Qty,
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
            db.SaveChanges();
        }

        private void UpdateInvoicePreview()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == CounterId);
            if (seq == null)
            {
                seq = new CounterSequence { CounterId = CounterId, NextInvoiceNumber = 1 };
                db.CounterSequences.Add(seq);
                db.SaveChanges();
            }
            InvoicePreviewText.Text = $"{CounterId}-{seq.NextInvoiceNumber}";
        }

        private void UpdateInvoiceDateNow()
        {
            InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");
        }
        private void LoadSalesmen()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var list = db.Staff
                .AsNoTracking()
                .Where(s => s.IsActive && s.ActsAsSalesman)
                .OrderBy(s => s.FullName)
                .ToList();
            list.Insert(0, new Pos.Domain.Hr.Staff { Id = 0, FullName = "-- None --", IsActive = true });
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

        public record ItemIndexDto(
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
                Pos.Domain.Formatting.ProductNameComposer.Compose(
                    ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);
        }

        public ObservableCollection<ItemIndexDto> DataContextItemIndex { get; } = new();

        private void LoadItemIndex()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var list =
                (from i in db.Items.AsNoTracking()
                 join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                 from p in gp.DefaultIfEmpty()
                 let primaryBarcode =
                     db.ItemBarcodes
                       .Where(b => b.ItemId == i.Id && b.IsPrimary)
                       .Select(b => b.Code)
                       .FirstOrDefault()
                 let anyBarcode =
                     db.ItemBarcodes
                       .Where(b => b.ItemId == i.Id)
                       .Select(b => b.Code)
                       .FirstOrDefault()
                 orderby i.Name
                 select new ItemIndexDto(
                     i.Id,
                     i.Name,
                     i.Sku,
                     primaryBarcode ?? anyBarcode ?? "", // <- replaces i.Barcode
                     i.Price,
                     i.TaxCode,
                     i.DefaultTaxRatePct,
                     i.TaxInclusive,
                     i.DefaultDiscountPct,
                     i.DefaultDiscountAmt,
                     p != null ? p.Name : null,
                     i.Variant1Name, i.Variant1Value,
                     i.Variant2Name, i.Variant2Value
                 )).ToList();
            DataContextItemIndex.Clear();
            foreach (var it in list) DataContextItemIndex.Add(it);
        }

        private TillSession? GetOpenTill(PosClientDbContext db)
                => db.TillSessions.OrderByDescending(t => t.Id)
                       .FirstOrDefault(t => t.OutletId == OutletId && t.CounterId == CounterId && t.CloseTs == null);
              
        private void InvoiceDiscountChanged(object sender, TextChangedEventArgs e)
        {
            decimal.TryParse(InvDiscPctBox.Text, out _invDiscPct);
            decimal.TryParse(InvDiscAmtBox.Text, out _invDiscAmt);
            UpdateTotal(); // recompute overall total shown
        }
        private void AddItemToCart(ItemIndexDto item)
        {
            var existing = _cart.FirstOrDefault(c => c.ItemId == item.Id);
            if (existing != null)
            {
                existing.Qty += 1;
                RecalcLineShared(existing);
                UpdateTotal();
                return;
            }
            var cl = new CartLine
            {
                ItemId = item.Id,
                Sku = item.Sku,
                DisplayName = ProductNameComposer.Compose(item.ProductName, item.Name, item.Variant1Name, item.Variant1Value, item.Variant2Name, item.Variant2Value),          //item.Name,
                UnitPrice = item.Price,
                Qty = 1,
                TaxCode = item.TaxCode,
                TaxRatePct = item.DefaultTaxRatePct,
                TaxInclusive = item.TaxInclusive,
                DiscountPct = item.DefaultDiscountPct ?? 0m,
                DiscountAmt = item.DefaultDiscountAmt ?? 0m
            };
            if ((cl.DiscountAmt ?? 0) > 0) cl.DiscountPct = null;
            RecalcLineShared(cl);
            _cart.Add(cl);
            UpdateTotal();
        }

        private void UpdateLocationUi()
        {
            if (_ctx == null) return;

            InventoryLocationText.Text = $"Outlet: {_ctx.OutletName}";
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
            //RecomputeDueChange();
            var itemsCount = _cart.Count;
            var qtySum = _cart.Sum(l => l.Qty);
            ItemsCountText.Text = itemsCount.ToString();
            QtySumText.Text = qtySum.ToString();
        }

        private async void PayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Cart is empty."); return; }
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null)
            {
                MessageBox.Show("Till is CLOSED. Please open till before taking payment.", "Till Closed");
                return;
            }
            // --- Recalc current cart lines (ensure totals are fresh) ---
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
            var subtotal = adjNetSum;               // after invoice discount
            var taxtotal = adjTaxSum;
            var grand = subtotal + taxtotal;
            if (grand <= 0m) { MessageBox.Show("Total must be greater than 0."); return; }
            var itemsCount = _cart.Count;
            var qtySum = _cart.Sum(l => l.Qty);
            // ===================== OPEN PAY DIALOG (overlay) =====================
            var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
            var payResult = await paySvc.ShowAsync(
                subtotal, invDiscValue, taxtotal, grand, itemsCount, qtySum,
                differenceMode: false, amountDelta: 0m, title: "Take Payment");
            if (!payResult.Confirmed)
            {
                RestoreFocusToScan();
                return;
            }
            //var enteredCash = payResult.Cash;
            //var enteredCard = payResult.Card;
            //var paid = enteredCash + enteredCard;
            var enteredCash = payResult.Cash;
            var enteredCard = payResult.Card;

            // --- NEW: block card if not configured; force collect remaining as cash ---
            if (!_isCardEnabled && enteredCard > 0m)
            {
                MessageBox.Show(
                    "Card payments are disabled for this outlet. Please collect cash.",
                    "Card Disabled", MessageBoxButton.OK, MessageBoxImage.Information);

                // Ask for the card portion as cash using your difference mode
                var cashOnly = await paySvc.ShowAsync(
                    subtotal, invDiscValue, taxtotal, grand,
                    itemsCount, qtySum,
                    differenceMode: true,                 // collect only the difference
                    amountDelta: enteredCard,             // need to cover the previously-entered card amount
                    title: "Collect Remaining (Cash Only)");

                if (!cashOnly.Confirmed)
                {
                    RestoreFocusToScan();
                    return;
                }

                // Add the difference to cash; zero-out card since it’s not allowed
                enteredCash += cashOnly.Cash;
                enteredCard = 0m;
            }
            // --- END NEW ---

            var paid = enteredCash + enteredCard;

            if (paid + 0.01m < grand) // safety check; PayWindow already enforces this
            {
                MessageBox.Show("Payment is less than total.");
                return;
            }
            var paymentMethod =
                (enteredCash > 0 && enteredCard > 0) ? PaymentMethod.Mixed :
                (enteredCash > 0) ? PaymentMethod.Cash : PaymentMethod.Card;
            // ========== Cashier & salesman ==========
            var cashier = db.Users.AsNoTracking()
                .FirstOrDefault(u => u.Id == cashierId)
                ?? db.Users.AsNoTracking().FirstOrDefault(u => u.Username == "admin")
                ?? db.Users.AsNoTracking().FirstOrDefault(u => u.IsActive);
            if (cashier == null)
            {
                MessageBox.Show("No active users found. Seed users first.");
                return;
            }
            int cashierIdLocal = cashier.Id;
            string cashierDisplay = cashier.DisplayName ?? "Unknown";
            int? salesmanIdLocal = _selectedSalesmanId;
            // If not set via _selectedSalesmanId, try from the combobox
            if (!salesmanIdLocal.HasValue && SalesmanBox?.SelectedItem is Pos.Domain.Hr.Staff selStaff)
                salesmanIdLocal = (selStaff.Id == 0) ? (int?)null : selStaff.Id;
            // Validate salesman against Staff (ActsAsSalesman + IsActive)
            if (salesmanIdLocal.HasValue)
            {
                bool exists = db.Staff
                    .AsNoTracking()
                    .Any(s => s.Id == salesmanIdLocal.Value && s.IsActive && s.ActsAsSalesman);

                if (!exists) salesmanIdLocal = null;
            }
            if (!db.Users.AsNoTracking().Any(x => x.Id == cashierIdLocal))
            {
                MessageBox.Show("Cashier not found in Users table."); return;
            }
            // ========== Customer ==========
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = isWalkIn ? null : _selectedCustomerId;
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());
            // ===== Credit guard: on-account sales require a registered customer =====
                           // already computed above
            if (grand <= 0m) { MessageBox.Show("Total must be greater than 0."); return; }
            var creditPortion = grand - paid;              // amount to post to customer ledger
            bool wantsCredit = (creditPortion > 0.009m);
            if (wantsCredit && (customerId == null))
            {
                MessageBox.Show(
                    "This invoice has an unpaid (on-account) amount.\n\n" +
                    "Please select a registered customer to continue.",
                    "Customer required for credit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // ========== Return flag ==========
            bool isReturnFlow = (ReturnCheck?.IsChecked == true);
            // ========== E-receipt & footer ==========
            string eReceiptToken = Guid.NewGuid().ToString("N");
            string? eReceiptUrl = null;
            string? invoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text;
           
            // --- Negative stock guard (Outlet) BEFORE we allocate invoice or write anything
            {
                var guard = new StockGuard(db);
                var deltas = _cart
                    .Where(l => l.ItemId > 0)
                    .GroupBy(l => l.ItemId)
                    .Select(g => (
                        itemId: g.Key,
                        outletId: OutletId,
                        locType: InventoryLocationType.Outlet,
                        locId: OutletId,
                        // sale is OUT; negative delta
                        delta: -g.Sum(x => Convert.ToDecimal(x.Qty))
                    ))
                    .ToArray();
                // Will throw InvalidOperationException if any item would go < 0
                await guard.EnsureNoNegativeAtLocationAsync(deltas);
            }
            // ===== Start a transaction only AFTER the user confirms =====
            using var tx = db.Database.BeginTransaction();
            // ===================== Allocate invoice number per counter (inside tx) =====================
            var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == CounterId)
                      ?? db.CounterSequences.Add(new CounterSequence { CounterId = CounterId, NextInvoiceNumber = 1 }).Entity;
            db.SaveChanges();
            var allocatedInvoiceNo = seq.NextInvoiceNumber;
            seq.NextInvoiceNumber++;               // increment for next sale
            db.SaveChanges();
            // --- Create Sale ---
            var sale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = OutletId,
                CounterId = CounterId,
                TillSessionId = open.Id,
                Status = SaleStatus.Final,
                Revision = 0,
                RevisedFromSaleId = null,
                // returns & invoice sequencing
                IsReturn = isReturnFlow,
                InvoiceNumber = allocatedInvoiceNo,
                // invoice-level discounts you already compute
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,
                DiscountBeforeTax = true,
                // totals
                Subtotal = subtotal,
                TaxTotal = taxtotal,
                Total = grand,
                // attribution
                CashierId = cashierIdLocal,
                SalesmanId = salesmanIdLocal,
                // customer
                CustomerKind = customerKind,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                // payment split
                CashAmount = enteredCash,
                CardAmount = enteredCard,
                PaymentMethod = paymentMethod,
                // e-receipt & footer
                EReceiptToken = eReceiptToken,
                EReceiptUrl = eReceiptUrl,
                InvoiceFooter = invoiceFooter
            };
            db.Sales.Add(sale);
            db.SaveChanges();
            // --- Lines & stock ---
            foreach (var line in _cart)
            {
                db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id,
                    ItemId = line.ItemId,
                    Qty = line.Qty,
                    UnitPrice = line.UnitPrice,
                    DiscountPct = line.DiscountPct,
                    DiscountAmt = line.DiscountAmt,
                    TaxCode = line.TaxCode,
                    TaxRatePct = line.TaxRatePct,
                    TaxInclusive = line.TaxInclusive,
                    UnitNet = line.UnitNet,
                    LineNet = line.LineNet,
                    LineTax = line.LineTax,
                    LineTotal = line.LineTotal
                });
                db.StockEntries.Add(new StockEntry
                {
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = OutletId,
                    OutletId = OutletId,
                    ItemId = line.ItemId,
                    QtyChange = -Convert.ToDecimal(line.Qty),
                    UnitCost = 0m,
                    RefType = "Sale",
                    RefId = sale.Id,
                    StockDocId = null,
                    Ts = DateTime.UtcNow,
                    Note = null
                });
            }
            db.SaveChanges();
            tx.Commit();
            // === GL POST: Sale / SaleReturn (runs once per finalized doc) ===
            try
            {
                // (Optional) "post-once" guard — prevents accidental double posting
                using (var chk = new PosClientDbContext(_dbOptions))
                {
                    var already = chk.GlEntries.AsNoTracking()
                        .Any(g => (g.DocType == Pos.Domain.Accounting.GlDocType.Sale
                                    && !sale.IsReturn
                                    && g.DocId == sale.Id)
                               || (g.DocType == Pos.Domain.Accounting.GlDocType.SaleReturn
                                    && sale.IsReturn
                                    && g.DocId == sale.Id));
                    if (!already)
                    {
                        var gl = App.Services.GetRequiredService<IGlPostingService>();
                        if (!sale.IsReturn)
                            await gl.PostSaleAsync(sale);
                        else
                            await gl.PostSaleReturnAsync(sale);
                    }
                }
            }
            catch (Exception ex)
            {
                // Non-fatal: sale is saved; log and continue
                System.Diagnostics.Debug.WriteLine("GL post failed: " + ex);
            }
            // If this sale came from a held draft, mark the draft as voided (or delete it)
            if (_currentHeldSaleId.HasValue)
            {
                using var db2 = new PosClientDbContext(_dbOptions);
                var draft = db2.Sales.FirstOrDefault(x => x.Id == _currentHeldSaleId.Value && x.Status == SaleStatus.Draft);
                if (draft != null)
                {
                    draft.Status = SaleStatus.Voided;
                    draft.RevisedToSaleId = sale.Id;
                    draft.VoidedAtUtc = DateTime.UtcNow;
                    draft.VoidReason = "Finalized from held draft";
                    db2.SaveChanges();
                }
                _currentHeldSaleId = null;
            }
            // If this sale came from a held draft, mark the draft as voided (or delete it)
            if (_currentHeldSaleId.HasValue)
            {
                using var db2 = new PosClientDbContext(_dbOptions);
                var draft = db2.Sales.FirstOrDefault(x => x.Id == _currentHeldSaleId.Value && x.Status == SaleStatus.Draft);
                if (draft != null)
                {
                    // Keep for audit: set as Voided and link forward
                    draft.Status = SaleStatus.Voided;
                    draft.RevisedToSaleId = sale.Id;
                    draft.VoidedAtUtc = DateTime.UtcNow;
                    draft.VoidReason = "Finalized from held draft";
                    db2.SaveChanges();
                }
                _currentHeldSaleId = null;
            }
            MessageBox.Show(
                $"Sale saved. ID: {sale.Id}\nInvoice: {CounterId}-{sale.InvoiceNumber}\nTotal: {sale.Total:0.00}",
                "Success");
            // ===== Post credit (A/R) to customer ledger if unpaid portion exists =====
            try
            {
                var poster = App.Services.GetRequiredService<PartyPostingService>();
                // paidNow uses the same values you saved into the sale (non-nullable decimals)
                var paidNow = sale.CashAmount + sale.CardAmount;
                // Reuse the outer computed creditPortion, OR recompute from the saved sale.
                // Prefer recompute from the saved sale to avoid any future mismatch:
                var unpaid = sale.Total - paidNow;
                if (unpaid > 0.009m && sale.CustomerId.HasValue)
                {
                    await poster.PostAsync(
                        partyId: sale.CustomerId.Value,
                        scope: BillingScope.Outlet,
                        outletId: sale.OutletId,
                        docType: PartyLedgerDocType.Sale,
                        docId: sale.Id,
                        debit: unpaid,        // +A/R
                        credit: 0m,
                        memo: $"On-account sale #{sale.CounterId}-{sale.InvoiceNumber}"
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Sale saved, but posting to customer ledger failed:\n" + ex.Message,
                    "Ledger warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateInvoicePreview();  // show the next invoice that will be used
            UpdateInvoiceDateNow();  // timestamp of current screen state
            try
            {
                await MaybePrintReceiptAsync(sale, open);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printed with error: " + ex.Message, "Print");
            }
            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = false;
            // keep FooterBox as-is (so cashier can set shop message once and reuse)
            UpdateTotal();
            //ScanText.Focus();
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

        private static void RecalcLineShared(CartLine l)
        {
            var a = PricingMath.CalcLine(new LineInput(
                Qty: l.Qty,
                UnitPrice: l.UnitPrice,
                DiscountPct: l.DiscountPct,
                DiscountAmt: l.DiscountAmt,
                TaxRatePct: l.TaxRatePct,
                TaxInclusive: l.TaxInclusive));
            l.UnitNet = a.UnitNet;
            l.LineNet = a.LineNet;
            l.LineTax = a.LineTax;
            l.LineTotal = a.LineTotal;
        }

        private void CartGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            // Defer until edit box value is applied to the bound object
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (e.Row?.Item is CartLine l)
                {
                    var header = (e.Column.Header as string) ?? string.Empty;

                    if (header.Contains("Qty", StringComparison.OrdinalIgnoreCase))
                    {
                        if (l.Qty <= 0) { l.Qty = 1; }
                        var ok = await GuardSaleQtyAsync(l.ItemId, l.Qty);
                        if (!ok)
                        {
                            l.Qty -= 1;
                            if (l.Qty < 1) l.Qty = 1;
                        }
                    }

                    // Preserve your existing discount/price recompute logic
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
                }
            }), DispatcherPriority.Background);
        }

        private void ResumeHeld(int saleId)
        {
            using var db = new PosClientDbContext(_dbOptions);
            var s = db.Sales.AsNoTracking().FirstOrDefault(x => x.Id == saleId && x.Status == SaleStatus.Draft);
            if (s == null) { MessageBox.Show("Held invoice not found."); return; }
            var lines = db.SaleLines.AsNoTracking().Where(x => x.SaleId == saleId).ToList();
            // Load into UI
            _cart.Clear();
            foreach (var l in lines)
            {
                _cart.Add(new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = DataContextItemIndex.FirstOrDefault(i => i.Id == l.ItemId)?.Sku ?? "",
                    DisplayName = DataContextItemIndex.FirstOrDefault(i => i.Id == l.ItemId)?.DisplayName ?? "",
                    Qty = l.Qty,
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
            // Restore header fields (discounts, return flag, footer, customer snapshot)
            _invDiscPct = s.InvoiceDiscountAmt.HasValue ? 0m : (s.InvoiceDiscountPct ?? 0m);
            _invDiscAmt = s.InvoiceDiscountAmt ?? 0m;
            InvDiscPctBox.Text = (_invDiscPct > 0m) ? _invDiscPct.ToString() : "";
            InvDiscAmtBox.Text = (_invDiscAmt > 0m) ? _invDiscAmt.ToString() : "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = s.IsReturn;
            if (WalkInCheck != null) WalkInCheck.IsChecked = (s.CustomerKind == CustomerKind.WalkIn);
            if (CustNameBox != null) CustNameBox.Text = s.CustomerName ?? "";
            if (CustPhoneBox != null) CustPhoneBox.Text = s.CustomerPhone ?? "";
            if (!string.IsNullOrWhiteSpace(s.InvoiceFooter) && FooterBox != null) FooterBox.Text = s.InvoiceFooter;
            if (SalesmanBox != null)
                SalesmanBox.SelectedValue = _selectedSalesmanId ?? 0;
            // optional: set SalesmanBox.SelectedValue = s.SalesmanId ?? 0;
            _currentHeldSaleId = s.Id;
            UpdateTotal();
            //ScanText.Focus();
        }

        private void Global_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F9) { PayButton_Click(sender, e); e.Handled = true; return; }
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
            if (proposedQty <= 0) return true; // nothing to guard

            using var db = new PosClientDbContext(_dbOptions);
            var guard = new StockGuard(db);

            try
            {
                // Single-location, single-item check at the current outlet
                var outletId = OutletId; // (this property already exists in your Sale window)
                await guard.EnsureNoNegativeAtLocationAsync(new[]
                {
            (itemId: itemId,
             outletId: outletId,
             locType: InventoryLocationType.Outlet,
             locId: outletId,
             delta: -proposedQty) // OUT for sale
        });
                return true;
            }
            catch (InvalidOperationException ex)
            {
                // Show the same message the guard uses (keeps UX/messages consistent)
                MessageBox.Show(ex.Message, "Not enough stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
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
    