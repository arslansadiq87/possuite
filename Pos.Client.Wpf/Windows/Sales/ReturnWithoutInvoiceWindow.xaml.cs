using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;                // CollectionViewSource
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Formatting;
using Pos.Domain.Pricing;
using Pos.Persistence;
using Pos.Domain.Services;
using Pos.Client.Wpf.Models;


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class ReturnWithoutInvoiceWindow : Window
    {
        // ====== DB ======
        private readonly DbContextOptions<PosClientDbContext> _dbOptions;

        // ====== Context (aligns with Sale screen) ======
        private readonly ObservableCollection<ReturnLine> _cart = new();
        public ObservableCollection<ItemIndexDto> DataContextItemIndex { get; } = new();
        private ICollectionView? _scanView;

        private readonly int _outletId;
        private readonly int _counterId;

        // Cashier & Salesman (basic snapshot; wire to login if you have it)
        private int cashierId = 1;
        private string cashierDisplay = "Admin";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;

        // Invoice-level discount
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;

        // Footer
        private string _footer = "Return processed — thank you!";

        // Scan helpers
        private DateTime _lastScanTextAt = DateTime.MinValue;
        private int _scanBurstCount = 0;
        private bool _suppressDropdown = false;
        private readonly DispatcherTimer _burstResetTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        private string _typedQuery = "";

        // ====== Constructors (pick the one your caller uses) ======
        // Designer-safe
        public ReturnWithoutInvoiceWindow() : this(1, 1) { }

        // Main
        public ReturnWithoutInvoiceWindow(int outletId, int counterId)
        {
            _outletId = outletId;
            _counterId = counterId;

            _dbOptions = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

            Init();
        }

        // Compatibility ctor for InvoiceCenterWindow (fixes CS1739)
        public ReturnWithoutInvoiceWindow(IReturnsService _unused, int outletId, int counterId, int? _unusedTill, int _unusedUser)
            : this(outletId, counterId) { }

        // ====== Init (shared by all ctors) ======
        private void Init()
        {
            InitializeComponent();

            // Keyboard shortcuts
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F9) { RefundButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.F5) { ClearButton_Click(s, e); e.Handled = true; return; }

                if (e.Key == Key.Down)
                {
                    _scanView?.Refresh();
                    if (_scanView != null && _scanView.Cast<object>().Any())
                        ScanPopup.IsOpen = true;
                    if (ScanPopup.IsOpen && ScanList.Items.Count > 0 && ScanList.SelectedIndex < 0)
                        ScanList.SelectedIndex = 0;
                }
            };

            // Cart/grid
            CartGrid.ItemsSource = _cart;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;

            // Item index for scan/search dropdown
            LoadItemIndex();
            _scanView = CollectionViewSource.GetDefaultView(DataContextItemIndex);
            _scanView.Filter = ScanFilter;
            ScanList.ItemsSource = _scanView;

            _burstResetTimer.Tick += (_, __) =>
            {
                _suppressDropdown = false; _scanBurstCount = 0; _burstResetTimer.Stop();
            };

            // Defaults
            FooterBox.Text = _footer;
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;

            Loaded += (_, __) =>
            {
                UpdateTillStatusUi();
                UpdateInvoicePreview();
                UpdateInvoiceDateNow();
                LoadSalesmen();
                ScanText.Focus();
                UpdateTotal();
            };
        }

        // ====== Till helpers (same pattern as Sale) ======
        private TillSession? GetOpenTill(PosClientDbContext db)
            => db.TillSessions.OrderByDescending(t => t.Id)
                              .FirstOrDefault(t => t.OutletId == _outletId && t.CounterId == _counterId && t.CloseTs == null);

        private void UpdateTillStatusUi()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);

            TillStatusText.Text = open == null
                ? "Closed"
                : $"OPEN (Id={open.Id}, Opened {open.OpenTs:HH:mm})";
        }


        private void UpdateInvoicePreview()
        {
            using var db = new PosClientDbContext(_dbOptions);
            var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == _counterId);
            if (seq == null)
            {
                seq = new CounterSequence { CounterId = _counterId, NextInvoiceNumber = 1 };
                db.CounterSequences.Add(seq);
                db.SaveChanges();
            }
            InvoicePreviewText.Text = $"{_counterId}-{seq.NextInvoiceNumber}";
        }

        private void UpdateInvoiceDateNow()
            => InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");

        // ====== Salesmen / cashier ======
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

            CashierNameText.Text = cashierDisplay;
        }

        private void SalesmanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SalesmanBox.SelectedItem is User sel)
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

        // ====== Scan / search (Sale-like behavior) ======
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
                ProductNameComposer.Compose(ProductName, Name, Variant1Name, Variant1Value, Variant2Name, Variant2Value);
        }

        private void LoadItemIndex()
        {
            using var db = new PosClientDbContext(_dbOptions);

            var list =
                (from i in db.Items.AsNoTracking()
                 let primaryBarcode =
                     db.ItemBarcodes
                       .Where(b => b.ItemId == i.Id)
                       .OrderByDescending(b => b.IsPrimary)   // prefer primary
                       .ThenBy(b => b.Id)                      // stable fallback
                       .Select(b => b.Code)
                       .FirstOrDefault()
                 join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                 from p in gp.DefaultIfEmpty()
                 orderby i.Name
                 select new ItemIndexDto(
                     i.Id,
                     i.Name,
                     i.Sku,
                     primaryBarcode ?? "",        // <-- was i.Barcode
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
            // burst detection (barcode scanners)
            var now = DateTime.UtcNow;
            if ((now - _lastScanTextAt).TotalMilliseconds <= 40) _scanBurstCount++;
            else _scanBurstCount = 1;
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
            if (e.Key == Key.Enter) { e.Handled = true; AddFromScanBox(); return; }

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
                    ScanList.Focus();
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

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFromScanBox();

        private void AddFromScanBox()
        {
            var text = ScanText.Text?.Trim() ?? string.Empty;
            ItemIndexDto? pick = null;

            // 1) selected from dropdown
            if (ScanPopup.IsOpen && ScanList.SelectedItem is ItemIndexDto sel) pick = sel;

            // 2) exact barcode / SKU
            if (pick is null && text.Length > 0)
            {
                pick = DataContextItemIndex.FirstOrDefault(i => i.Barcode == text)
                    ?? DataContextItemIndex.FirstOrDefault(i => i.Sku == text);
            }

            // 3) starts-with on display name
            if (pick is null && text.Length > 0)
            {
                pick = DataContextItemIndex.FirstOrDefault(i =>
                    (i.DisplayName ?? i.Name).StartsWith(text, StringComparison.OrdinalIgnoreCase));
            }

            // 4) DB fallback
            // 4) DB fallback (now checks ItemBarcodes instead of Items.Barcode)
            if (pick is null && text.Length > 0)
            {
                using var db = new PosClientDbContext(_dbOptions);

                var dbItem =
                    (from i in db.Items.AsNoTracking()
                     let primaryBarcode =
                         db.ItemBarcodes
                           .Where(b => b.ItemId == i.Id)
                           .OrderByDescending(b => b.IsPrimary)
                           .ThenBy(b => b.Id)
                           .Select(b => b.Code)
                           .FirstOrDefault()
                     join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                     from p in gp.DefaultIfEmpty()
                     where
                         // exact barcode hit on ANY barcode for the item
                         db.ItemBarcodes.Any(b => b.ItemId == i.Id && b.Code == text)
                         // or exact SKU
                         || i.Sku == text
                         // or starts-with on names/variants/product
                         || EF.Functions.Like(EF.Functions.Collate(i.Name, "NOCASE"), text + "%")
                         || (i.Variant1Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant1Value, "NOCASE"), text + "%"))
                         || (i.Variant2Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant2Value, "NOCASE"), text + "%"))
                         || (p != null && EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), text + "%"))
                     orderby i.Name
                     select new ItemIndexDto(
                         i.Id,
                         i.Name,
                         i.Sku,
                         primaryBarcode ?? "",   // present a primary (or first) barcode in the UI
                         i.Price,
                         i.TaxCode,
                         i.DefaultTaxRatePct,
                         i.TaxInclusive,
                         i.DefaultDiscountPct,
                         i.DefaultDiscountAmt,
                         p != null ? p.Name : null,
                         i.Variant1Name, i.Variant1Value,
                         i.Variant2Name, i.Variant2Value
                     )).FirstOrDefault();

                if (dbItem is not null)
                {
                    DataContextItemIndex.Add(dbItem);
                    pick = dbItem;
                }
            }


            if (pick is null) { MessageBox.Show("Item not found."); ScanText.Focus(); return; }

            AddItemToCart(pick);
            ClearScan();
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

            var rl = new ReturnLine
            {
                ItemId = item.Id,
                Sku = item.Sku,
                DisplayName = ProductNameComposer.Compose(item.ProductName, item.Name, item.Variant1Name, item.Variant1Value, item.Variant2Name, item.Variant2Value),
                UnitPrice = item.Price,
                Qty = 1,
                TaxCode = item.TaxCode,
                TaxRatePct = item.DefaultTaxRatePct,
                TaxInclusive = item.TaxInclusive,
                DiscountPct = item.DefaultDiscountPct ?? 0m,
                DiscountAmt = item.DefaultDiscountAmt ?? 0m
            };
            if ((rl.DiscountAmt ?? 0) > 0) rl.DiscountPct = null;

            RecalcLineShared(rl);
            _cart.Add(rl);
            UpdateTotal();
        }

        // ====== Cart / pricing ======
        private static void RecalcLineShared(ReturnLine l)
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
                if (e.Row?.Item is ReturnLine l)
                {
                    var header = (e.Column.Header as string) ?? string.Empty;
                    if (header.Contains("Disc %")) { if ((l.DiscountPct ?? 0) > 0) l.DiscountAmt = null; }
                    else if (header.Contains("Disc Amt")) { if ((l.DiscountAmt ?? 0) > 0) l.DiscountPct = null; }

                    RecalcLineShared(l);
                    UpdateTotal();
                }
            }), DispatcherPriority.Background);
        }

        private void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ReturnLine l)
            {
                l.Qty += 1;
                RecalcLineShared(l);
                UpdateTotal();
            }
        }

        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ReturnLine l)
            {
                if (l.Qty > 1) { l.Qty -= 1; RecalcLineShared(l); }
                else { _cart.Remove(l); }
                UpdateTotal();
            }
        }

        private void DeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ReturnLine l)
            {
                _cart.Remove(l);
                UpdateTotal();
            }
        }

        private void InvoiceDiscountChanged(object sender, TextChangedEventArgs e)
        {
            decimal.TryParse(InvDiscPctBox.Text, out _invDiscPct);
            decimal.TryParse(InvDiscAmtBox.Text, out _invDiscAmt);
            if (_invDiscPct > 100m) _invDiscPct = 100m;
            if (_invDiscPct < 0m) _invDiscPct = 0m;
            if (_invDiscAmt < 0m) _invDiscAmt = 0m;
            UpdateTotal();
        }

        private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)
            => _footer = FooterBox.Text ?? "";

        private void WalkInCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool isWalkIn = (sender as CheckBox)?.IsChecked == true;
            if (!isWalkIn && WalkInCheck != null) isWalkIn = WalkInCheck.IsChecked == true;

            var enable = !isWalkIn;
            if (CustNameBox != null) CustNameBox.IsEnabled = enable;
            if (CustPhoneBox != null) CustPhoneBox.IsEnabled = enable;

            if (isWalkIn)
            {
                if (CustNameBox != null) CustNameBox.Text = string.Empty;
                if (CustPhoneBox != null) CustPhoneBox.Text = string.Empty;
            }
        }

        private void UpdateTotal()
        {
            var lines = _cart.ToList();
            var lineNetSum = lines.Sum(l => l.LineNet);
            var lineTaxSum = lines.Sum(l => l.LineTax);

            // invoice-level discount on Subtotal (net)
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

            SubtotalText.Text = subtotal.ToString("N2", CultureInfo.CurrentCulture);
            DiscountText.Text = (-invDiscValue).ToString("N2", CultureInfo.CurrentCulture);
            TaxText.Text = tax.ToString("N2", CultureInfo.CurrentCulture);
            TotalText.Text = grand.ToString("N2", CultureInfo.CurrentCulture);

            ItemsCountText.Text = _cart.Count.ToString();
            QtySumText.Text = _cart.Sum(l => l.Qty).ToString();
        }

        // ====== Buttons ======
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cart.Count == 0 && string.IsNullOrWhiteSpace(InvDiscPctBox.Text) && string.IsNullOrWhiteSpace(InvDiscAmtBox.Text))
            {
                // nothing to clear
            }
            else
            {
                var ok = MessageBox.Show("Clear the current return (cart, discounts, customer fields)?",
                                         "Confirm Clear", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (ok != MessageBoxResult.OK) return;
            }

            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            ReasonBox.Text = "";
            UpdateTotal();
            ScanText.Focus();
        }

        private void RefundButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Nothing to return — cart is empty."); return; }

            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null) { MessageBox.Show("Till is CLOSED. Please open till before refund.", "Till Closed"); return; }

            // recompute lines
            foreach (var l in _cart) RecalcLineShared(l);

            // totals with invoice-level discount
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

            var subtotal = adjNetSum;
            var tax = adjTaxSum;
            var grand = subtotal + tax;
            if (grand <= 0m) { MessageBox.Show("Refund must be greater than 0."); return; }

            var itemsCount = _cart.Count;
            var qtySum = _cart.Sum(l => l.Qty);

            // customer snapshot
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = null; // attach if you have a customers table/id
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());

            // cashier
            var cashier = db.Users.AsNoTracking()
                .FirstOrDefault(u => u.Id == cashierId)
                ?? db.Users.AsNoTracking().FirstOrDefault(u => u.Username == "admin")
                ?? db.Users.AsNoTracking().FirstOrDefault(u => u.IsActive);
            if (cashier == null) { MessageBox.Show("No active users found."); return; }

            int cashierIdLocal = cashier.Id;
            string cashierDisplayLocal = cashier.DisplayName ?? "Unknown";

            int? salesmanIdLocal = _selectedSalesmanId;
            if (!salesmanIdLocal.HasValue && SalesmanBox?.SelectedItem is User sel2)
                salesmanIdLocal = (sel2.Id == 0) ? (int?)null : sel2.Id;

            if (salesmanIdLocal.HasValue &&
                !db.Users.AsNoTracking().Any(u => u.Id == salesmanIdLocal.Value))
                salesmanIdLocal = null;

            // Pay window (refund split)
            var pay = new Pos.Client.Wpf.Windows.Sales.PayWindow(subtotal, invDiscValue, tax, grand, itemsCount, qtySum)
            { Owner = this };
            var ok = pay.ShowDialog() == true && pay.Confirmed;
            if (!ok) { ScanText.Focus(); return; }

            var refundCash = pay.Cash;
            var refundCard = pay.Card;
            if (refundCash + refundCard + 0.01m < grand)
            {
                MessageBox.Show("Refund split is less than total."); return;
            }

            var paymentMethod =
                (refundCash > 0 && refundCard > 0) ? PaymentMethod.Mixed :
                (refundCash > 0) ? PaymentMethod.Cash : PaymentMethod.Card;

            // Save inside a transaction
            using var tx = db.Database.BeginTransaction();

            var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == _counterId)
                      ?? db.CounterSequences.Add(new CounterSequence { CounterId = _counterId, NextInvoiceNumber = 1 }).Entity;
            db.SaveChanges();

            var allocatedInvoiceNo = seq.NextInvoiceNumber;
            seq.NextInvoiceNumber++;
            db.SaveChanges();

            var sale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = _outletId,
                CounterId = _counterId,
                TillSessionId = open.Id,

                IsReturn = true,
                OriginalSaleId = null,
                Status = SaleStatus.Final,
                Revision = 0,
                InvoiceNumber = allocatedInvoiceNo,

                // invoice-level discounts
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,
                DiscountBeforeTax = true,

                // POSITIVE magnitudes (Z-report subtracts returns)
                Subtotal = subtotal,
                TaxTotal = tax,
                Total = grand,

                // users
                CashierId = cashierIdLocal,
                SalesmanId = salesmanIdLocal,

                // customer snapshot
                CustomerKind = customerKind,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,

                // refund split (positive)
                CashAmount = refundCash,
                CardAmount = refundCard,
                PaymentMethod = paymentMethod,

                // footer/note
                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text,
                Note = string.IsNullOrWhiteSpace(ReasonBox?.Text) ? null : ReasonBox!.Text
            };
            db.Sales.Add(sale);
            db.SaveChanges();

            // Lines: NEGATIVE qty, NEGATIVE amounts; Stock: IN (+qty)
            foreach (var line in _cart)
            {
                db.SaleLines.Add(new SaleLine
                {
                    SaleId = sale.Id,
                    ItemId = line.ItemId,
                    Qty = -line.Qty,                   // NEGATIVE for returns
                    UnitPrice = line.UnitPrice,
                    DiscountPct = line.DiscountPct,
                    DiscountAmt = line.DiscountAmt,
                    TaxCode = line.TaxCode,
                    TaxRatePct = line.TaxRatePct,
                    TaxInclusive = line.TaxInclusive,

                    UnitNet = -line.UnitNet,
                    LineNet = -line.LineNet,
                    LineTax = -line.LineTax,
                    LineTotal = -line.LineTotal
                });

                db.StockEntries.Add(new StockEntry
                {
                    OutletId = _outletId,
                    ItemId = line.ItemId,
                    QtyChange = +line.Qty,             // stock IN from customer
                    RefType = "SaleReturn",
                    RefId = sale.Id,
                    Ts = DateTime.UtcNow
                });
            }

            db.SaveChanges();
            tx.Commit();

            MessageBox.Show($"Return saved. ID: {sale.Id}\nInvoice: {_counterId}-{sale.InvoiceNumber}\nRefund: {sale.Total:0.00}", "Success");

            try
            {
                // Map ReturnLine -> CartLine for the printer
                var printLines = _cart.Select(l => new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = l.Sku,
                    DisplayName = l.DisplayName,
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
                }).ToList();

                Pos.Client.Wpf.Printing.ReceiptPrinter.PrintSale(
                    sale,
                    printLines,
                    open,
                    cashierDisplayLocal,
                    _selectedSalesmanName
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printed with error: " + ex.Message, "Print");
            }

            // Reset UI for next return
            _cart.Clear();
            _invDiscPct = 0m; _invDiscAmt = 0m;
            InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
            if (WalkInCheck != null) WalkInCheck.IsChecked = true;
            if (CustNameBox != null) CustNameBox.Text = "";
            if (CustPhoneBox != null) CustPhoneBox.Text = "";
            ReasonBox.Text = "";
            UpdateTotal();
            UpdateInvoicePreview();
            UpdateInvoiceDateNow();
            ScanText.Focus();
        }

        // ====== Till open/close (simple MVP) ======
                
        // ====== ReturnLine (mirrors CartLine) ======
        public class ReturnLine : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";

            private int _qty;
            public int Qty { get => _qty; set { if (_qty == value) return; _qty = value; OnPropertyChanged(); } }

            private decimal _unitPrice;
            public decimal UnitPrice { get => _unitPrice; set { if (_unitPrice == value) return; _unitPrice = value; OnPropertyChanged(); } }

            private decimal? _discountPct;
            public decimal? DiscountPct
            {
                get => _discountPct;
                set { if (_discountPct == value) return; _discountPct = value; if ((_discountPct ?? 0) > 0) DiscountAmt = null; OnPropertyChanged(); }
            }

            private decimal? _discountAmt;
            public decimal? DiscountAmt
            {
                get => _discountAmt;
                set { if (_discountAmt == value) return; _discountAmt = value; if ((_discountAmt ?? 0) > 0) DiscountPct = null; OnPropertyChanged(); }
            }

            public string? TaxCode { get; set; }
            public decimal TaxRatePct { get; set; }
            public bool TaxInclusive { get; set; }

            private decimal _unitNet;
            public decimal UnitNet { get => _unitNet; set { if (_unitNet == value) return; _unitNet = value; OnPropertyChanged(); } }

            private decimal _lineNet;
            public decimal LineNet { get => _lineNet; set { if (_lineNet == value) return; _lineNet = value; OnPropertyChanged(); } }

            private decimal _lineTax;
            public decimal LineTax { get => _lineTax; set { if (_lineTax == value) return; _lineTax = value; OnPropertyChanged(); } }

            private decimal _lineTotal;
            public decimal LineTotal { get => _lineTotal; set { if (_lineTotal == value) return; _lineTotal = value; OnPropertyChanged(); } }
        }
    }
}
