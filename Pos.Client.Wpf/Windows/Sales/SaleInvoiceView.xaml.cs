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
using Microsoft.Extensions.DependencyInjection; // for App.Services.GetRequiredService


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class SaleInvoiceView : UserControl
    {
        private readonly DbContextOptions<PosClientDbContext> _dbOptions;
        private readonly ObservableCollection<CartLine> _cart = new();
        private readonly IDialogService _dialogs;

        // For demo: fixed outlet/counter
        private const int OutletId = 1;
        private const int CounterId = 1;

        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;

        // --- Payment / attribution / customer UI state ---
        //private decimal _enteredCash = 0m;
        //private decimal _enteredCard = 0m;

        private bool _isWalkIn = true;
        private string? _enteredCustomerName;
        private string? _enteredCustomerPhone;

        private int cashierId = 1;                // TODO: wire to your login/session
        private string cashierDisplay = "Admin";   // shows on screen/receipt
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;

        //private bool _isReturnFlow = false;
        // optional: you can scan a return receipt later and set this
        //private Sale? _returnReferenceSale = null;

        private string _invoiceFooter = "Thank you for shopping with us!";
        private TextBox? _activePayBox = null;    // keypad target

        private DateTime _lastEsc = DateTime.MinValue;
        private int _escCount = 0;
        private const int EscChordMs = 450; // double-press window

        private ICollectionView? _scanView;

        // scanner-burst detection
        private DateTime _lastScanTextAt = DateTime.MinValue;
        private int _scanBurstCount = 0;
        private bool _suppressDropdown = false;
        private readonly DispatcherTimer _burstResetTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };

        private string _typedQuery = "";

        // Track if we loaded a held draft (so we can mark it closed later)
        private int? _currentHeldSaleId = null;

        public SaleInvoiceView(IDialogService dialogs)
        {
            InitializeComponent();
            _dialogs = dialogs;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;
            ScanText.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; AddFromScanBox(); return; }

                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    // Let ComboBox move selection, but don't let its text-change re-run our filter

                    // Also restore the user’s typed text after the selection change
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ScanText.Template?.FindName("PART_EditableTextBox", ScanText) is TextBox tb)
                        {
                            if (tb.Text != _typedQuery)
                            {
                                tb.Text = _typedQuery;
                                tb.CaretIndex = tb.Text.Length;
                                tb.SelectionLength = 0;
                            }
                        }
                    }), DispatcherPriority.Background);
                }
            };

            _dbOptions = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)   // <-- use the SAME connection string
                .Options;

            CartGrid.ItemsSource = _cart;
            UpdateTotal();
            LoadItemIndex();
            // Build a filtered view over the in-memory index
            _scanView = CollectionViewSource.GetDefaultView(DataContextItemIndex);
            _scanView.Filter = ScanFilter;
            ScanList.ItemsSource = _scanView;   // <- ListBox shows the filtered view

            // Reset scanner-burst flag after a short idle
            _burstResetTimer.Tick += (_, __) => { _suppressDropdown = false; _scanBurstCount = 0; _burstResetTimer.Stop(); };


            // Show cashier & load salesman list (if you have a Users table)
            CashierNameText.Text = cashierDisplay;
            LoadSalesmen();

            // Keyboard shortcuts
            this.PreviewKeyDown += (s, e) =>
            {
                // Existing shortcuts:
                if (e.Key == Key.F9) { PayButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.Delete)
                {
                    if (CartGrid.SelectedItem is CartLine l) { _cart.Remove(l); UpdateTotal(); }
                    return;
                }
                if (e.Key == Key.F6) { StockReport_Click(s, e); return; }

                // NEW: double ESC focuses ScanBox from anywhere
                if (e.Key == Key.F7) { OpenInvoiceCenter_Click(s, e); e.Handled = true; return; }

                // Clear current invoice (no dialog)
                if (e.Key == Key.F5) { ClearCurrentInvoice(confirm: true); e.Handled = true; return; }

                // Hold current invoice quickly (with a tag prompt)
                if (e.Key == Key.F8) { HoldCurrentInvoiceQuick(); e.Handled = true; return; }

                // F10 is treated as a system key; catch both paths
                if (e.Key == Key.F10 || e.SystemKey == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10))
                {
                    ShowTillSummary_Click(s, e);
                    e.Handled = true;   // prevent menu activation
                    return;
                }

                if (e.Key == Key.Escape)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastEsc).TotalMilliseconds <= EscChordMs) _escCount++;
                    else _escCount = 1;
                    _lastEsc = now;
                    if (_escCount >= 2)
                    {
                        _escCount = 0;
                        if (ScanText.IsFocused)
                        {
                            ScanText.Clear();
                        }
                        else
                        {
                            ScanText.Focus();
                            if (ScanText.Template?.FindName("PART_EditableTextBox", ScanText) is TextBox tb)
                            {
                                tb.CaretIndex = tb.Text?.Length ?? 0;
                            }
                        }
                        e.Handled = true;
                    }
                }
            };


            // Start with default footer
            FooterBox.Text = _invoiceFooter;

            // Customer fields disabled for walk-in
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;
            Loaded += (_, __) =>
            {
                UpdateTillStatusUi();
                UpdateInvoicePreview();
                UpdateInvoiceDateNow();
                // Sync once when everything exists
                ScanText.Focus();
            };

        }

        private async void ShowTillSummary_Click(object? sender, RoutedEventArgs e)
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null)
            {
                //MessageBox.Show("No open till session. Open the till first.", "Till Summary");
                //return;
                await _dialogs.AlertAsync(
                    "No open till session. Open the till first.",
                    "Till Summary");
                return;
            }
            var win = new TillSessionSummaryWindow(_dbOptions, open.Id, OutletId, CounterId) { };
            win.ShowDialog();
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
                    // nothing to clear
                }
                else
                {
                    //var ok = MessageBox.Show("Clear the current invoice (cart, discounts, customer fields)?",
                    //                         "Confirm Clear", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
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
            ScanText.Focus();
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
            // Build a Draft Sale (no TillSessionId, no invoice number yet)
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


        private bool ScanFilter(object o)
        {
            if (o is not ItemIndexDto i) return false;
            var term = (_typedQuery ?? "").Trim();
            if (term.Length == 0) return true;
            return (i.DisplayName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (i.Sku?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (i.Barcode?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ScanText_TextChanged(object sender, TextChangedEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastScanTextAt).TotalMilliseconds <= 40) _scanBurstCount++; else _scanBurstCount = 1;
            _lastScanTextAt = now;
            if (_scanBurstCount >= 4) { _suppressDropdown = true; _burstResetTimer.Stop(); _burstResetTimer.Start(); }
            _typedQuery = ScanText.Text ?? "";
            _scanView?.Refresh();
            bool hasMatches = _scanView != null && _scanView.Cast<object>().Any();
            ScanPopup.IsOpen = !_suppressDropdown && _typedQuery.Length > 0 && hasMatches;
            if (ScanPopup.IsOpen && ScanList.Items.Count > 0 && ScanList.SelectedIndex < 0)
                ScanList.SelectedIndex = 0;
        }

        private void ScanText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddFromScanBox();    // will use selected list item or exact match
                return;
            }

            if (e.Key == Key.Down)
            {
                if (!ScanPopup.IsOpen)
                {
                    _scanView?.Refresh();
                    if (_scanView != null && _scanView.Cast<object>().Any())
                        ScanPopup.IsOpen = true;
                }
                if (ScanPopup.IsOpen && ScanList.Items.Count > 0)
                {
                    if (ScanList.SelectedIndex < 0) ScanList.SelectedIndex = 0;
                    ScanList.Focus();       // arrow navigation in the list
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && ScanPopup.IsOpen)
            {
                ScanPopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }

        private void ScanList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ScanList.SelectedItem is ItemIndexDto pick)
                {
                    AddItemToCart(pick);
                    ClearScan();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ScanPopup.IsOpen = false;
                ScanText.Focus();
                e.Handled = true;
            }
        }

        private void ScanList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ScanList.SelectedItem is ItemIndexDto pick)
            {
                AddItemToCart(pick);
                ClearScan();
            }
        }

        private void ClearScan()
        {
            _typedQuery = "";
            ScanText.Text = string.Empty;
            ScanPopup.IsOpen = false;
            _suppressDropdown = false;
            _scanBurstCount = 0;
            ScanText.Focus();
        }


        private void UpdateInvoicePreview()
        {
            using var db = new PosClientDbContext(_dbOptions);
            // Ensure sequence row exists for this counter
            var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == CounterId);
            if (seq == null)
            {
                seq = new CounterSequence { CounterId = CounterId, NextInvoiceNumber = 1 };
                db.CounterSequences.Add(seq);
                db.SaveChanges();
            }
            // Preview shows what will be assigned to the NEXT sale on this counter
            InvoicePreviewText.Text = $"{CounterId}-{seq.NextInvoiceNumber}";
        }

        private void UpdateInvoiceDateNow()
        {
            // Local time for operator clarity
            InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");
        }
        // ===================== Helpers =====================
        private void LoadSalesmen()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var list = db.Users
                .AsQueryable()
                .Where(u => u.IsActive && (u.Role == UserRole.Salesman || u.Role == UserRole.Cashier))
                .OrderBy(u => u.DisplayName)
                .ToList();
            list.Insert(0, new User { Id = 0, DisplayName = "-- None --", Username = "__none__", Role = UserRole.Salesman });
            SalesmanBox.ItemsSource = list;
            SalesmanBox.DisplayMemberPath = "DisplayName";
            SalesmanBox.SelectedValuePath = "Id";
            SalesmanBox.SelectedIndex = 0;
        }

        private void SalesmanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SalesmanBox.SelectedItem is Pos.Domain.Entities.User sel)
            {
                if (sel.Id == 0) { _selectedSalesmanId = null; _selectedSalesmanName = null; }
                else { _selectedSalesmanId = sel.Id; _selectedSalesmanName = sel.DisplayName; }
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
            // Prefer the control that raised the event
            bool isWalkIn = (sender as CheckBox)?.IsChecked == true;

            // Fallback to the field if available
            if (!isWalkIn && WalkInCheck != null)
                isWalkIn = WalkInCheck.IsChecked == true;
            _isWalkIn = isWalkIn;
            bool enable = !isWalkIn;
            // Null-safe enable/clear (controls may not be created yet when this fires)
            if (CustNameBox != null) CustNameBox.IsEnabled = enable;
            if (CustPhoneBox != null) CustPhoneBox.IsEnabled = enable;
            if (isWalkIn)
            {
                if (CustNameBox != null) CustNameBox.Text = string.Empty;
                if (CustPhoneBox != null) CustPhoneBox.Text = string.Empty;
            }
        }

        private void ReturnCheck_Changed(object sender, RoutedEventArgs e)
        {
            // Nothing to compute here; we read ReturnCheck.IsChecked at Pay time.
        }

        private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _invoiceFooter = FooterBox.Text ?? "";
        }

        // In MainWindow.xaml.cs (or a shared DTO file)
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

                     // Pull PRIMARY first; if none, fall back to any barcode for display
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


        // ===== Till helpers =====
        private TillSession? GetOpenTill(PosClientDbContext db)
                => db.TillSessions.OrderByDescending(t => t.Id)
                       .FirstOrDefault(t => t.OutletId == OutletId && t.CounterId == CounterId && t.CloseTs == null);

        private void UpdateTillStatusUi()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);

            TillStatusText.Text = open == null
                ? "Closed"
                : $"OPEN (Id={open.Id}, Opened {open.OpenTs:HH:mm})";
            // Show only one button at a time
            bool isOpen = open != null;
            if (OpenTillBtn != null) OpenTillBtn.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
            if (CloseTillBtn != null) CloseTillBtn.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void InvoiceDiscountChanged(object sender, TextChangedEventArgs e)
        {
            decimal.TryParse(InvDiscPctBox.Text, out _invDiscPct);
            decimal.TryParse(InvDiscAmtBox.Text, out _invDiscAmt);
            UpdateTotal(); // recompute overall total shown
        }

        private void OpenTill_Click(object sender, RoutedEventArgs e)
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open != null)
            {
                MessageBox.Show($"Till already open (Id={open.Id}).", "Info");
                return;
            }
            // Minimal input: opening float = 0; you can make a dialog here
            var session = new TillSession
            {
                OutletId = OutletId,
                CounterId = CounterId,
                OpenTs = DateTime.UtcNow,
                OpeningFloat = 0m
            };
            db.TillSessions.Add(session);
            db.SaveChanges();
            UpdateTillStatusUi();
            MessageBox.Show($"Till opened. Id={session.Id}", "Till");
        }

        private void CloseTill_Click(object sender, RoutedEventArgs e)
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null)
            {
                MessageBox.Show("No open till to close.", "Info");
                return;
            }
            // Compute simple Z totals (cash-only in this MVP)
            var all = db.Sales
                .Where(s => s.TillSessionId == open.Id && s.Status == SaleStatus.Final)
                .AsNoTracking()
                .ToList();
            var sales = all.Where(s => !s.IsReturn).ToList();
            var returns = all.Where(s => s.IsReturn).ToList();
            var salesCount = sales.Count;
            var returnsCount = returns.Count;
            var salesTotal = sales.Sum(s => s.Total);
            var returnsTotal = returns.Sum(s => s.Total); // positive number = refunded
            var netTotal = salesTotal - returnsTotal;
            var expectedCash = open.OpeningFloat
                             + sales.Sum(s => s.CashAmount)
                             - returns.Sum(s => s.CashAmount); // cash refunds reduce cash
            // Ask user for declared cash
            var declaredStr = Microsoft.VisualBasic.Interaction.InputBox(
                $"Z Report\nSales count: {salesCount}\nSales total: {salesTotal:0.00}\n\nEnter DECLARED CASH:",
                "Close Till (Z Report)", expectedCash.ToString("0.00"));
            if (!decimal.TryParse(declaredStr, out var declaredCash))
            {
                MessageBox.Show("Invalid amount. Till not closed.");
                return;
            }
            var overShort = declaredCash - expectedCash;
            open.CloseTs = DateTime.UtcNow;
            open.DeclaredCash = declaredCash;
            open.OverShort = overShort;
            db.SaveChanges();
            // Build a simple Z report string (you can print later via ESC/POS)
            var z = new StringBuilder();
            z.AppendLine($"=== Z REPORT (Till {open.Id}) ===");
            z.AppendLine($"Outlet/Counter: {OutletId}/{CounterId}");
            z.AppendLine($"Opened (local): {open.OpenTs.ToLocalTime()}");
            z.AppendLine($"Closed (local): {open.CloseTs?.ToLocalTime()}");
            z.AppendLine($"Opening Float : {open.OpeningFloat:0.00}");
            z.AppendLine($"Sales Count   : {salesCount}");
            z.AppendLine($"Sales Total   : {salesTotal:0.00}");
            z.AppendLine($"Expected Cash : {expectedCash:0.00}");
            z.AppendLine($"Declared Cash : {declaredCash:0.00}");
            z.AppendLine($"Over/Short    : {overShort:+0.00;-0.00;0.00}");
            MessageBox.Show(z.ToString(), "Z Report");
            UpdateTillStatusUi();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFromScanBox();

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

        private void AddFromScanBox()
        {
            var text = ScanText.Text?.Trim() ?? string.Empty;
            ItemIndexDto? pick = null;
            // 1) If user has a selection in the dropdown, prefer that
            if (ScanPopup.IsOpen && ScanList.SelectedItem is ItemIndexDto sel)
                pick = sel;
            // 2) Exact match by barcode/SKU
            if (pick is null && text.Length > 0)
            {
                pick = DataContextItemIndex.FirstOrDefault(i => i.Barcode == text)
                    ?? DataContextItemIndex.FirstOrDefault(i => i.Sku == text);
            }
            // 3) Starts-with on DisplayName
            if (pick is null && text.Length > 0)
            {
                pick = DataContextItemIndex.FirstOrDefault(i =>
                    (i.DisplayName ?? i.Name).StartsWith(text, StringComparison.OrdinalIgnoreCase));
            }
            // 4) DB fallback (copy your existing query body here; only change
            //    'ScanBox.Text' -> 'text' and keep the projection to ItemIndexDto)
            // 4) DB fallback (now matches ANY barcode via ItemBarcodes)
            if (pick is null && text.Length > 0)
            {
                using var db = new PosClientDbContext(_dbOptions);

                var q =
                    from i in db.Items.AsNoTracking()
                    join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                    from p in gp.DefaultIfEmpty()

                    where
                        // exact barcode match against any barcode for the item
                        db.ItemBarcodes.Any(b => b.ItemId == i.Id && b.Code == text)
                        // or SKU match
                        || i.Sku == text
                        // or starts-with on item/variant/product names (case-insensitive)
                        || EF.Functions.Like(EF.Functions.Collate(i.Name, "NOCASE"), text + "%")
                        || (i.Variant1Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant1Value, "NOCASE"), text + "%"))
                        || (i.Variant2Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant2Value, "NOCASE"), text + "%"))
                        || (p != null && EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), text + "%"))

                    orderby i.Name

                    // project PRIMARY barcode (or any) into DTO.Barcode for UI
                    select new ItemIndexDto(
                        i.Id,
                        i.Name,
                        i.Sku,
                        db.ItemBarcodes.Where(b => b.ItemId == i.Id && b.IsPrimary).Select(b => b.Code).FirstOrDefault()
                        ?? db.ItemBarcodes.Where(b => b.ItemId == i.Id).Select(b => b.Code).FirstOrDefault()
                        ?? "",
                        i.Price,
                        i.TaxCode,
                        i.DefaultTaxRatePct,
                        i.TaxInclusive,
                        i.DefaultDiscountPct,
                        i.DefaultDiscountAmt,
                        p != null ? p.Name : null,
                        i.Variant1Name, i.Variant1Value,
                        i.Variant2Name, i.Variant2Value
                    );

                var dbItem = q.FirstOrDefault();
                if (dbItem is not null)
                {
                    DataContextItemIndex.Add(dbItem);
                    pick = dbItem;
                }
            }

            if (pick is null)
            {
                MessageBox.Show("Item not found.");
                ScanText.Focus();
                return;
            }
            AddItemToCart(pick);
            ClearScan();
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
            //    Use existing line tax-to-net ratio to scale fairly.
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
            // ===================== OPEN PAY WINDOW =====================
            // ===================== OPEN PAY DIALOG (overlay) =====================
            var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
            var payResult = await paySvc.ShowAsync(
                subtotal, invDiscValue, taxtotal, grand, itemsCount, qtySum,
                differenceMode: false, amountDelta: 0m, title: "Take Payment");

            if (!payResult.Confirmed)
            {
                // user cancelled; return focus to scan
                ScanText.Focus();
                return;
            }
            var enteredCash = payResult.Cash;
            var enteredCard = payResult.Card;

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
            if (!salesmanIdLocal.HasValue && SalesmanBox?.SelectedItem is User sel2)
                salesmanIdLocal = (sel2.Id == 0) ? (int?)null : sel2.Id;
            if (salesmanIdLocal.HasValue &&
                !db.Users.AsNoTracking().Any(u => u.Id == salesmanIdLocal.Value))
                salesmanIdLocal = null;
            if (!db.Users.AsNoTracking().Any(x => x.Id == cashierIdLocal))
            {
                MessageBox.Show("Cashier not found in Users table."); return;
            }
            if (salesmanIdLocal.HasValue &&
                !db.Users.AsNoTracking().Any(x => x.Id == salesmanIdLocal.Value))
            {
                MessageBox.Show("Selected salesman not found in Users table."); return;
            }
            // ========== Customer ==========
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = null;
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());
            // ========== Return flag ==========
            bool isReturnFlow = (ReturnCheck?.IsChecked == true);
            // ========== E-receipt & footer ==========
            string eReceiptToken = Guid.NewGuid().ToString("N");
            string? eReceiptUrl = null;
            string? invoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text;
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
                //db.StockEntries.Add(new StockEntry
                //{
                //    OutletId = OutletId,
                //    ItemId = line.ItemId,
                //    QtyChange = -line.Qty,
                //    RefType = "Sale",
                //    RefId = sale.Id,
                //    Ts = DateTime.UtcNow,
                //    StockDocId = null

                //});
                db.StockEntries.Add(new StockEntry
                {
                    // NEW normalized location (use these going forward)
                    LocationType = InventoryLocationType.Outlet,
                    LocationId = OutletId,

                    // Legacy mirror (ok to keep for now; plan to remove later)
                    OutletId = OutletId,

                    ItemId = line.ItemId,

                    // QtyChange is decimal; sale reduces stock
                    QtyChange = -Convert.ToDecimal(line.Qty),

                    // If you track cost at sale time, set UnitCost snapshot; else keep 0
                    UnitCost = 0m,

                    RefType = "Sale",
                    RefId = sale.Id,

                    // IMPORTANT: no StockDocId for sales
                    StockDocId = null,

                    Ts = DateTime.UtcNow,
                    Note = null
                });

            }
            db.SaveChanges();
            tx.Commit();
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
            UpdateInvoicePreview();  // show the next invoice that will be used
            UpdateInvoiceDateNow();  // timestamp of current screen state
            try
            {
                Pos.Client.Wpf.Printing.ReceiptPrinter.PrintSale(
                    sale, _cart, open /* TillSession */, cashierDisplay, _selectedSalesmanName
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printed with error: " + ex.Message, "Print");
            }
            // --- Reset UI for next sale ---
            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            // NOTE: CashBox/CardBox live in PayWindow now, so do NOT reference them here
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            if (ReturnCheck != null) ReturnCheck.IsChecked = false;
            // keep FooterBox as-is (so cashier can set shop message once and reuse)
            UpdateTotal();
            ScanText.Focus();
        }

        private void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CartLine l)
            {
                l.Qty += 1;
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

        private void StockReport_Click(object sender, RoutedEventArgs e)
        {
            new StockReportWindow { }.ShowDialog();
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (e.Row?.Item is CartLine l)
                {
                    var header = (e.Column.Header as string) ?? string.Empty;
                    if (header.Contains("Disc %"))
                    {
                        if ((l.DiscountPct ?? 0) > 0) l.DiscountAmt = null;   // % wins
                    }
                    else if (header.Contains("Disc Amt"))
                    {
                        if ((l.DiscountAmt ?? 0) > 0) l.DiscountPct = null;   // amount wins
                    }
                    RecalcLineShared(l);
                    UpdateTotal();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }



        private void OpenInvoiceCenter_Click(object sender, RoutedEventArgs e)
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null) { MessageBox.Show("Till is CLOSED. Open till first."); return; }
            var win = new InvoiceCenterWindow(OutletId, CounterId) { };
            if (win.ShowDialog() == true)
            {
                // If a held invoice was chosen there, resume it here
                if (win.SelectedHeldSaleId.HasValue)
                {
                    ResumeHeld(win.SelectedHeldSaleId.Value);
                }
            }
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
            _selectedSalesmanId = s.SalesmanId;
            // optional: set SalesmanBox.SelectedValue = s.SalesmanId ?? 0;
            _currentHeldSaleId = s.Id;
            UpdateTotal();
            ScanText.Focus();
        }

    }
}
    