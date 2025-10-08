using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
using Pos.Client.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;   // GetRequiredService
using Pos.Client.Wpf.Services;                    // IPaymentDialogService, PaymentResult


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class EditSaleWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _dbOptions;
        private readonly int _saleId;
        private readonly Dictionary<string, ItemIndexDto> _barcodeIndex =
    new(StringComparer.OrdinalIgnoreCase);
        // Loaded original sale snapshot (used for deltas)
        private Sale _orig = null!;
        private System.Collections.Generic.List<SaleLine> _origLines = null!;
        private decimal _origSubtotal, _origTax, _origGrand;

        // Re-use Sale UX
        private readonly ObservableCollection<CartLine> _cart = new();
        public ObservableCollection<ItemIndexDto> DataContextItemIndex { get; } = new();
        private ICollectionView? _scanView;

        // Cashier/salesman
        private int cashierId = 1;               // TODO: wire to login/session
        private string cashierDisplay = "Admin";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;

        // Invoice-level discount
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;

        // Footer
        private string _invoiceFooter = "";

        // Scan helpers
        private DateTime _lastScanTextAt = DateTime.MinValue;
        private int _scanBurstCount = 0;
        private bool _suppressDropdown = false;
        private readonly DispatcherTimer _burstResetTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
        private string _typedQuery = "";

        // Output for InvoiceCenter
        public bool Confirmed { get; private set; } = false;
        public int NewRevision { get; private set; } = 0;

        public EditSaleWindow(int saleId)
        {
            InitializeComponent();
            _saleId = saleId;

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F9) { SaveRevision_Click(s, e); e.Handled = true; }
                if (e.Key == Key.F5) { Revert_Click(s, e); e.Handled = true; }
            };

            _dbOptions = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

            CartGrid.ItemsSource = _cart;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;

            _burstResetTimer.Tick += (_, __) => { _suppressDropdown = false; _scanBurstCount = 0; _burstResetTimer.Stop(); };

            // Walk-in fields mirror Sale behavior
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;

            Loaded += (_, __) =>
            {
                LoadSale();
                LoadItemIndex();

                _scanView = CollectionViewSource.GetDefaultView(DataContextItemIndex);
                _scanView.Filter = ScanFilter;
                ScanList.ItemsSource = _scanView;

                CashierNameText.Text = cashierDisplay;
                LoadSalesmen();

                UpdateHeaderUi();
                UpdateTotal();
                ScanText.Focus();
            };
        }

        // ======== Load & header ========
        private void LoadSale()
        {
            using var db = new PosClientDbContext(_dbOptions);

            _orig = db.Sales.First(s => s.Id == _saleId);
            if (_orig.IsReturn) { MessageBox.Show("Returns cannot be amended here."); Close(); return; }
            if (_orig.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be amended."); Close(); return; }

            _origLines = db.SaleLines.Where(l => l.SaleId == _orig.Id).ToList();

            // Seed UI from original
            _invDiscPct = _orig.InvoiceDiscountAmt.HasValue ? 0m : (_orig.InvoiceDiscountPct ?? 0m);
            _invDiscAmt = _orig.InvoiceDiscountAmt ?? 0m;
            _invoiceFooter = _orig.InvoiceFooter ?? "";

            InvDiscPctBox.Text = (_invDiscPct > 0 ? _invDiscPct.ToString(CultureInfo.InvariantCulture) : "");
            InvDiscAmtBox.Text = (_invDiscAmt > 0 ? _invDiscAmt.ToString(CultureInfo.InvariantCulture) : "");
            FooterBox.Text = _invoiceFooter;

            // Customer snapshot
            WalkInCheck.IsChecked = (_orig.CustomerKind == CustomerKind.WalkIn);
            CustNameBox.Text = _orig.CustomerName ?? "";
            CustPhoneBox.Text = _orig.CustomerPhone ?? "";

            // Salesman
            _selectedSalesmanId = _orig.SalesmanId;

            // Original totals
            _origSubtotal = _orig.Subtotal;
            _origTax = _orig.TaxTotal;
            _origGrand = _orig.Total;

            // Load cart lines (positive amounts)
            _cart.Clear();
            foreach (var l in _origLines)
            {
                _cart.Add(new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = "", // will fill from index (optional)
                    DisplayName = "", // will fill from index (optional)
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
        }

        private void UpdateHeaderUi()
        {
            TitleText.Text = $"Amend Invoice  {_orig.CounterId}-{_orig.InvoiceNumber}";
            SubTitleText.Text = $"Original Rev {_orig.Revision}  •  {_orig.Ts.ToLocalTime():dd-MMM-yyyy HH:mm}";
            OrigTotalsText.Text = $"Original: Subtotal {_origSubtotal:N2}   Tax {_origTax:N2}   Total {_origGrand:N2}";
            InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");
            InvoicePreviewText.Text = $"{_orig.CounterId}-{_orig.InvoiceNumber} (Rev {_orig.Revision + 1})";
        }

        // ======== Salesmen ========
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
            SalesmanBox.SelectedValue = _selectedSalesmanId ?? 0;
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

        // ======== Scan/search (same as Sale) ========
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

            // Primary barcode per item
            var primaryByItemId = db.ItemBarcodes
                .AsNoTracking()
                .Where(b => b.IsPrimary)
                .Select(b => new { b.ItemId, b.Code })
                .ToList()
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.First().Code);

            // Items + product names
            var list =
                (from i in db.Items.AsNoTracking()
                 join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                 from p in gp.DefaultIfEmpty()
                 orderby i.Name
                 select new
                 {
                     i.Id,
                     i.Name,
                     i.Sku,
                     i.Price,
                     i.TaxCode,
                     i.DefaultTaxRatePct,
                     i.TaxInclusive,
                     i.DefaultDiscountPct,
                     i.DefaultDiscountAmt,
                     ProductName = p != null ? p.Name : null,
                     i.Variant1Name,
                     i.Variant1Value,
                     i.Variant2Name,
                     i.Variant2Value
                 }).ToList();

            DataContextItemIndex.Clear();

            foreach (var it in list)
            {
                var primary = primaryByItemId.TryGetValue(it.Id, out var code) ? code : "";
                DataContextItemIndex.Add(new ItemIndexDto(
                    it.Id, it.Name, it.Sku, primary, it.Price, it.TaxCode,
                    it.DefaultTaxRatePct, it.TaxInclusive, it.DefaultDiscountPct, it.DefaultDiscountAmt,
                    it.ProductName,
                    it.Variant1Name, it.Variant1Value, it.Variant2Name, it.Variant2Value
                ));
            }

            // Build fast barcode -> item index with ALL barcodes (primary + alternates)
            _barcodeIndex.Clear();
            var dtoById = DataContextItemIndex.ToDictionary(x => x.Id);
            var allCodes = db.ItemBarcodes
                .AsNoTracking()
                .Select(b => new { b.ItemId, b.Code })
                .ToList();

            foreach (var bc in allCodes)
                if (dtoById.TryGetValue(bc.ItemId, out var dto) && !string.IsNullOrWhiteSpace(bc.Code))
                    _barcodeIndex[bc.Code] = dto;

            // fill missing SKU/name for existing cart lines
            foreach (var cl in _cart)
            {
                if (dtoById.TryGetValue(cl.ItemId, out var meta))
                {
                    cl.Sku = meta.Sku;
                    cl.DisplayName = meta.DisplayName;
                }
            }
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

        private void ClearScan()
        {
            _typedQuery = "";
            ScanText.Text = string.Empty;
            ScanPopup.IsOpen = false;
            _suppressDropdown = false;
            _scanBurstCount = 0;
            ScanText.Focus();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) => AddFromScanBox();

        private void AddFromScanBox()
        {
            var text = ScanText.Text?.Trim() ?? string.Empty;
            ItemIndexDto? pick = null;

            if (ScanPopup.IsOpen && ScanList.SelectedItem is ItemIndexDto sel)
                pick = sel;

            // 1) Exact barcode hit from memory index
            if (pick is null && text.Length > 0 && _barcodeIndex.TryGetValue(text, out var viaBarcode))
                pick = viaBarcode;

            // 2) Exact SKU (memory)
            if (pick is null && text.Length > 0)
                pick = DataContextItemIndex.FirstOrDefault(i => string.Equals(i.Sku, text, StringComparison.OrdinalIgnoreCase));

            // 3) Prefix on display/name (memory)
            if (pick is null && text.Length > 0)
                pick = DataContextItemIndex.FirstOrDefault(i =>
                    ((i.DisplayName ?? i.Name) ?? string.Empty).StartsWith(text, StringComparison.OrdinalIgnoreCase));

            // 4) DB lookup (includes barcodes table)
            if (pick is null && text.Length > 0)
            {
                using var db = new PosClientDbContext(_dbOptions);

                var q =
                    from i in db.Items.AsNoTracking()
                    join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                    from p in gp.DefaultIfEmpty()
                    where db.ItemBarcodes.Any(b => b.ItemId == i.Id && b.Code == text)
                       || i.Sku == text
                       || EF.Functions.Like(EF.Functions.Collate(i.Name, "NOCASE"), text + "%")
                       || (i.Variant1Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant1Value, "NOCASE"), text + "%"))
                       || (i.Variant2Value != null && EF.Functions.Like(EF.Functions.Collate(i.Variant2Value, "NOCASE"), text + "%"))
                       || (p != null && EF.Functions.Like(EF.Functions.Collate(p.Name, "NOCASE"), text + "%"))
                    orderby i.Name
                    select new
                    {
                        i.Id,
                        i.Name,
                        i.Sku,
                        i.Price,
                        i.TaxCode,
                        i.DefaultTaxRatePct,
                        i.TaxInclusive,
                        i.DefaultDiscountPct,
                        i.DefaultDiscountAmt,
                        ProductName = p != null ? p.Name : null,
                        i.Variant1Name,
                        i.Variant1Value,
                        i.Variant2Name,
                        i.Variant2Value
                    };

                var found = q.FirstOrDefault();
                if (found is not null)
                {
                    // get primary barcode for display
                    var primary = db.ItemBarcodes.AsNoTracking()
                        .Where(b => b.ItemId == found.Id && b.IsPrimary)
                        .Select(b => b.Code)
                        .FirstOrDefault() ?? "";

                    var dbItem = new ItemIndexDto(
                        found.Id, found.Name, found.Sku, primary, found.Price, found.TaxCode,
                        found.DefaultTaxRatePct, found.TaxInclusive, found.DefaultDiscountPct, found.DefaultDiscountAmt,
                        found.ProductName,
                        found.Variant1Name, found.Variant1Value, found.Variant2Name, found.Variant2Value);

                    // cache into memory (list + barcode index for this scanned code)
                    DataContextItemIndex.Add(dbItem);
                    _barcodeIndex[text] = dbItem;

                    pick = dbItem;
                }
            }

            if (pick is null) { MessageBox.Show("Item not found."); ScanText.Focus(); return; }

            AddItemToCart(pick);
            ClearScan();
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
                DisplayName = ProductNameComposer.Compose(item.ProductName, item.Name, item.Variant1Name, item.Variant1Value, item.Variant2Name, item.Variant2Value),
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

        // ======== Pricing like Sale ========
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
                else { _cart.Remove(l); }
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

        private void InvoiceDiscountChanged(object sender, TextChangedEventArgs e)
        {
            decimal.TryParse(InvDiscPctBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _invDiscPct);
            decimal.TryParse(InvDiscAmtBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out _invDiscAmt);
            UpdateTotal();
        }

        private void FooterBox_TextChanged(object sender, TextChangedEventArgs e)
            => _invoiceFooter = FooterBox.Text ?? "";

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

            // Show diff vs original
            var delta = grand - _origGrand;
            DeltaText.Text = (delta >= 0 ? $"+{delta:N2}" : $"{delta:N2}");
            DeltaText.Foreground = (delta > 0) ? System.Windows.Media.Brushes.Green
                                   : (delta < 0) ? System.Windows.Media.Brushes.Red
                                   : System.Windows.Media.Brushes.Gray;
        }

        // ======== Save revision ========
        private TillSession? GetOpenTill(PosClientDbContext db)
            => db.TillSessions.OrderByDescending(t => t.Id)
               .FirstOrDefault(t => t.OutletId == _orig.OutletId && t.CounterId == _orig.CounterId && t.CloseTs == null);

        private async void SaveRevision_Click(object? sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Cart is empty."); return; }

            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null) { MessageBox.Show("Till is CLOSED. Open till before saving an amendment.", "Till Closed"); return; }

            // Recalc fresh
            foreach (var cl in _cart) RecalcLineShared(cl);

            // New totals (same math as UpdateTotal)
            var lineNetSum = _cart.Sum(l => l.LineNet);
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

            decimal newSub = 0m, newTax = 0m;
            foreach (var l in _cart)
            {
                var adjNet = PricingMath.RoundMoney(l.LineNet * factor);
                var taxPerNet = (l.LineNet > 0m) ? (l.LineTax / l.LineNet) : 0m;
                var adjTax = PricingMath.RoundMoney(adjNet * taxPerNet);
                newSub += adjNet;
                newTax += adjTax;
            }
            var newGrand = newSub + newTax;

            // Difference vs original
            var deltaSub = newSub - _origSubtotal;
            var deltaTax = newTax - _origTax;
            var deltaGrand = newGrand - _origGrand;

            if (deltaGrand < -0.005m)
            {
                MessageBox.Show("The amended total is LOWER than the original.\nUse 'Return (with invoice)' to issue a credit/return.");
                return;
            }
            if (deltaGrand < 0.005m) // basically no change
            {
                if (MessageBox.Show("No net change vs original. Save as a new revision anyway?", "No Change", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;
            }

            // Choose cashier/salesman (validate)
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
            if (salesmanIdLocal.HasValue && !db.Users.AsNoTracking().Any(u => u.Id == salesmanIdLocal.Value))
                salesmanIdLocal = null;

            // If there's a positive difference, collect extra payment via overlay PayDialog
            decimal addCash = 0m, addCard = 0m;
            PaymentMethod payMethod = PaymentMethod.Cash;
            if (deltaGrand > 0.005m)
            {
                var deltaDisc = invDiscValue - _orig.InvoiceDiscountValue;

                // ===== OPEN PAY DIALOG (overlay) =====
                var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
                var payResult = await paySvc.ShowAsync(
                    subtotal: Math.Abs(deltaSub),
                    discountValue: Math.Abs(deltaDisc),
                    tax: Math.Abs(deltaTax),
                    grandTotal: Math.Abs(deltaGrand),
                    items: _cart.Count,
                    qty: _cart.Sum(l => l.Qty),
                    differenceMode: false,
                    amountDelta: 0m,
                    title: "Collect Difference");

                if (!payResult.Confirmed) { ScanText.Focus(); return; }

                addCash = payResult.Cash;
                addCard = payResult.Card;

                if (addCash + addCard + 0.01m < Math.Abs(deltaGrand))
                {
                    MessageBox.Show("Collected payment is less than difference due.");
                    return;
                }

                payMethod = (addCash > 0 && addCard > 0) ? PaymentMethod.Mixed
                           : (addCash > 0) ? PaymentMethod.Cash
                           : PaymentMethod.Card;
            }

            using var tx = db.Database.BeginTransaction();

            // Create new revised sale (same invoice number, +1 revision)
            var newSale = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = _orig.OutletId,
                CounterId = _orig.CounterId,
                TillSessionId = open.Id,

                Status = SaleStatus.Final,
                IsReturn = false,

                InvoiceNumber = _orig.InvoiceNumber,
                Revision = _orig.Revision + 1,
                RevisedFromSaleId = _orig.Id,
                RevisedToSaleId = null,

                // Invoice-level discount (mirror UI)
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,
                DiscountBeforeTax = true,

                // Totals
                Subtotal = newSub,
                TaxTotal = newTax,
                Total = newGrand,

                // Users
                CashierId = cashierIdLocal,
                SalesmanId = salesmanIdLocal,

                // Customer snapshot
                CustomerKind = (WalkInCheck.IsChecked == true) ? CustomerKind.WalkIn : CustomerKind.Registered,
                CustomerId = null, // attach if you have customers table
                CustomerName = (WalkInCheck.IsChecked == true) ? null : (string.IsNullOrWhiteSpace(CustNameBox.Text) ? null : CustNameBox.Text.Trim()),
                CustomerPhone = (WalkInCheck.IsChecked == true) ? null : (string.IsNullOrWhiteSpace(CustPhoneBox.Text) ? null : CustPhoneBox.Text.Trim()),

                // Payment: record only the difference collected here
                CashAmount = addCash,
                CardAmount = addCard,
                PaymentMethod = (deltaGrand > 0.005m) ? payMethod : _orig.PaymentMethod,

                // Footer
                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox.Text) ? null : FooterBox.Text
            };
            db.Sales.Add(newSale);
            db.SaveChanges();

            // Lines: save full new state
            foreach (var l in _cart)
            {
                db.SaleLines.Add(new SaleLine
                {
                    SaleId = newSale.Id,
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

            // Stock adjustments: write ONLY deltas vs original
            var origQtyByItem = _origLines.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var newQtyByItem = _cart.GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var allItemIds = origQtyByItem.Keys.Union(newQtyByItem.Keys).Distinct();
            foreach (var itemId in allItemIds)
            {
                var oldQty = origQtyByItem.TryGetValue(itemId, out var oq) ? oq : 0;
                var newQty = newQtyByItem.TryGetValue(itemId, out var nq) ? nq : 0;
                var deltaQty = newQty - oldQty; // +ve means more sold than before

                if (deltaQty != 0)
                {
                    db.StockEntries.Add(new StockEntry
                    {
                        OutletId = newSale.OutletId,
                        ItemId = itemId,
                        QtyChange = -deltaQty, // sale removes stock
                        RefType = "SaleRev",
                        RefId = newSale.Id,
                        Ts = DateTime.UtcNow
                    });
                }
            }

            // Link the old sale forward
            _orig.RevisedToSaleId = newSale.Id;
            db.SaveChanges();

            tx.Commit();

            try
            {
                Pos.Client.Wpf.Printing.ReceiptPrinter.PrintSale(
                    newSale,
                    _cart, // IEnumerable<CartLine>
                    open,
                    cashierDisplayLocal,
                    _selectedSalesmanName
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printed with error: " + ex.Message, "Print");
            }

            Confirmed = true;
            NewRevision = newSale.Revision;

            MessageBox.Show($"Amendment saved.\nInvoice {_orig.CounterId}-{_orig.InvoiceNumber}\nRevision {newSale.Revision}\nDifference: {(deltaGrand >= 0 ? "+" : "")}{deltaGrand:N2}", "Success");
            DialogResult = true;
            Close();
        }


        private void Revert_Click(object? sender, RoutedEventArgs e)
        {
            // Restore original document state into UI
            _cart.Clear();
            foreach (var l in _origLines)
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

            _invDiscPct = _orig.InvoiceDiscountAmt.HasValue ? 0m : (_orig.InvoiceDiscountPct ?? 0m);
            _invDiscAmt = _orig.InvoiceDiscountAmt ?? 0m;
            InvDiscPctBox.Text = (_invDiscPct > 0m) ? _invDiscPct.ToString(CultureInfo.InvariantCulture) : "";
            InvDiscAmtBox.Text = (_invDiscAmt > 0m) ? _invDiscAmt.ToString(CultureInfo.InvariantCulture) : "";
            FooterBox.Text = _orig.InvoiceFooter ?? "";

            WalkInCheck.IsChecked = (_orig.CustomerKind == CustomerKind.WalkIn);
            CustNameBox.Text = _orig.CustomerName ?? "";
            CustPhoneBox.Text = _orig.CustomerPhone ?? "";

            SalesmanBox.SelectedValue = _orig.SalesmanId ?? 0;

            UpdateTotal();
            ScanText.Focus();
        }

        // ======== Small helpers ========
        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
