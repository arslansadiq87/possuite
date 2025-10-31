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
using Pos.Persistence.Services;
using Pos.Client.Wpf.Models;
using Microsoft.Extensions.DependencyInjection;   // GetRequiredService
using Pos.Client.Wpf.Services;                    // IPaymentDialogService, PaymentResult
using System.Linq;


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
        private int cashierId => AppState.Current?.CurrentUser?.Id ?? 1;
        private string cashierDisplay => AppState.Current?.CurrentUser?.DisplayName ?? "Cashier";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;
        // Footer
        private string _invoiceFooter = "";

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
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;
            Loaded += (_, __) =>
            {
                LoadSale();
                LoadItemIndex();
                CashierNameText.Text = cashierDisplay;
                LoadSalesmen();
                UpdateHeaderUi();
                UpdateTotal();
                ItemSearch.FocusSearch();
            };
        }

        private ItemIndexDto AdaptItem(Pos.Domain.DTO.ItemIndexDto src)
        {
            return new ItemIndexDto(
                Id: src.Id,
                Name: src.Name ?? src.DisplayName ?? "",
                Sku: src.Sku ?? "",
                Barcode: src.Barcode ?? "",
                Price: src.Price,
                TaxCode: null,
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
            var pick = box.SelectedItem; // Pos.Domain.DTO.ItemIndexDto from the control
            if (pick is null) return;

            var adapted = AdaptItem(pick);

            // Proposed total in the edit cart (existing + 1)
            var existing = _cart.FirstOrDefault(c => c.ItemId == adapted.Id);
            var proposedCartQty = (existing?.Qty ?? 0m) + 1m;

            if (!await GuardEditLineQtyAsync(adapted.Id, proposedCartQty))
            {
                try { ItemSearch?.FocusSearch(); } catch { }
                return;
            }

            // OK – proceed
            AddItemToCart(adapted);
            try { ItemSearch?.FocusSearch(); } catch { }
        }


        private void LoadSale()
        {
            using var db = new PosClientDbContext(_dbOptions);
            _orig = db.Sales.First(s => s.Id == _saleId);
            if (_orig.IsReturn) { MessageBox.Show("Returns cannot be amended here."); Close(); return; }
            if (_orig.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be amended."); Close(); return; }
            _origLines = db.SaleLines.Where(l => l.SaleId == _orig.Id).ToList();
            _invDiscPct = _orig.InvoiceDiscountAmt.HasValue ? 0m : (_orig.InvoiceDiscountPct ?? 0m);
            _invDiscAmt = _orig.InvoiceDiscountAmt ?? 0m;
            _invoiceFooter = _orig.InvoiceFooter ?? "";
            InvDiscPctBox.Text = (_invDiscPct > 0 ? _invDiscPct.ToString(CultureInfo.InvariantCulture) : "");
            InvDiscAmtBox.Text = (_invDiscAmt > 0 ? _invDiscAmt.ToString(CultureInfo.InvariantCulture) : "");
            FooterBox.Text = _invoiceFooter;
            WalkInCheck.IsChecked = (_orig.CustomerKind == CustomerKind.WalkIn);
            CustNameBox.Text = _orig.CustomerName ?? "";
            CustPhoneBox.Text = _orig.CustomerPhone ?? "";
            _selectedSalesmanId = _orig.SalesmanId;
            _origSubtotal = _orig.Subtotal;
            _origTax = _orig.TaxTotal;
            _origGrand = _orig.Total;
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
            var primaryByItemId = db.ItemBarcodes
                .AsNoTracking()
                .Where(b => b.IsPrimary)
                .Select(b => new { b.ItemId, b.Code })
                .ToList()
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.First().Code);

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
            _barcodeIndex.Clear();
            var dtoById = DataContextItemIndex.ToDictionary(x => x.Id);
            var allCodes = db.ItemBarcodes
                .AsNoTracking()
                .Select(b => new { b.ItemId, b.Code })
                .ToList();
            foreach (var bc in allCodes)
                if (dtoById.TryGetValue(bc.ItemId, out var dto) && !string.IsNullOrWhiteSpace(bc.Code))
                    _barcodeIndex[bc.Code] = dto;
            foreach (var cl in _cart)
            {
                if (dtoById.TryGetValue(cl.ItemId, out var meta))
                {
                    cl.Sku = meta.Sku;
                    cl.DisplayName = meta.DisplayName;
                }
            }
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

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (e.Row?.Item is CartLine l)
                {
                    var header = (e.Column.Header as string) ?? string.Empty;

                    if (header.Contains("Qty", StringComparison.OrdinalIgnoreCase))
                    {
                        if (l.Qty <= 0) l.Qty = 1;                            // ← ints

                        var ok = await GuardEditLineQtyAsync(l.ItemId, l.Qty); // int → decimal (implicit) is OK
                        if (!ok)
                        {
                            l.Qty -= 1;                                       // ← ints
                            if (l.Qty < 1) l.Qty = 1;
                        }
                    }

                    // existing discount/price logic…
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


        private async void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is CartLine l)
            {
                var proposedCartQty = l.Qty + 1;                  // ← int
                if (!await GuardEditLineQtyAsync(l.ItemId, proposedCartQty)) return;

                l.Qty = proposedCartQty;                          // ← int to int
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
            var delta = grand - _origGrand;
            DeltaText.Text = (delta >= 0 ? $"+{delta:N2}" : $"{delta:N2}");
            DeltaText.Foreground = (delta > 0) ? System.Windows.Media.Brushes.Green
                                   : (delta < 0) ? System.Windows.Media.Brushes.Red
                                   : System.Windows.Media.Brushes.Gray;
        }

        private TillSession? GetOpenTill(PosClientDbContext db)
            => db.TillSessions.OrderByDescending(t => t.Id)
               .FirstOrDefault(t => t.OutletId == _orig.OutletId && t.CounterId == _orig.CounterId && t.CloseTs == null);

        private async void SaveRevision_Click(object? sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Cart is empty."); return; }
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null) { MessageBox.Show("Till is CLOSED. Open till before saving an amendment.", "Till Closed"); return; }
            foreach (var cl in _cart) RecalcLineShared(cl);
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
            decimal addCash = 0m, addCard = 0m;
            PaymentMethod payMethod = PaymentMethod.Cash;
            if (deltaGrand > 0.005m)
            {
                var deltaDisc = invDiscValue - _orig.InvoiceDiscountValue;
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
                if (!payResult.Confirmed) { ItemSearch.FocusSearch(); return; }
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
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,
                DiscountBeforeTax = true,
                Subtotal = newSub,
                TaxTotal = newTax,
                Total = newGrand,
                CashierId = cashierIdLocal,
                SalesmanId = salesmanIdLocal,
                CustomerKind = (WalkInCheck.IsChecked == true) ? CustomerKind.WalkIn : CustomerKind.Registered,
                CustomerId = null, // attach if you have customers table
                CustomerName = (WalkInCheck.IsChecked == true) ? null : (string.IsNullOrWhiteSpace(CustNameBox.Text) ? null : CustNameBox.Text.Trim()),
                CustomerPhone = (WalkInCheck.IsChecked == true) ? null : (string.IsNullOrWhiteSpace(CustPhoneBox.Text) ? null : CustPhoneBox.Text.Trim()),
                CashAmount = addCash,
                CardAmount = addCard,
                PaymentMethod = (deltaGrand > 0.005m) ? payMethod : _orig.PaymentMethod,
                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox.Text) ? null : FooterBox.Text
            };
            db.Sales.Add(newSale);
            db.SaveChanges();

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
            // Build per-item deltas (new vs original)
            var origQtyByItem = _origLines
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var newQtyByItem = _cart
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var allItemIds = origQtyByItem.Keys.Union(newQtyByItem.Keys).Distinct().ToList();

            // Collect OUT deltas for guard and all StockEntry rows to write
            var pendingOutDeltas = new List<(int itemId, int outletId, InventoryLocationType locType, int locId, decimal delta)>();
            var pendingEntries = new List<StockEntry>();

            foreach (var itemId in allItemIds)
            {
                var oldQty = origQtyByItem.TryGetValue(itemId, out var oq) ? oq : 0m;
                var newQty = newQtyByItem.TryGetValue(itemId, out var nq) ? nq : 0m;
                var deltaQty = newQty - oldQty; // +ve => extra sold (OUT); -ve => reduced (IN)

                if (deltaQty > 0m)
                {
                    // More being sold in this revision -> OUT (negative)
                    pendingOutDeltas.Add((
                        itemId: itemId,
                        outletId: newSale.OutletId,
                        locType: InventoryLocationType.Outlet,
                        locId: newSale.OutletId,
                        delta: -deltaQty // OUT
                    ));

                    pendingEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = newSale.OutletId,
                        OutletId = newSale.OutletId,
                        ItemId = itemId,
                        QtyChange = -deltaQty,           // OUT
                        RefType = "SaleRev",
                        RefId = newSale.Id,
                        Ts = DateTime.UtcNow
                    });
                }
                else if (deltaQty < 0m)
                {
                    // Less being sold vs original -> IN (positive)
                    var qtyIn = Math.Abs(deltaQty);
                    pendingEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = newSale.OutletId,
                        OutletId = newSale.OutletId,
                        ItemId = itemId,
                        QtyChange = qtyIn,               // IN
                        RefType = "SaleRev",
                        RefId = newSale.Id,
                        Ts = DateTime.UtcNow
                    });
                }
                // deltaQty == 0m -> nothing to write for this item
            }

            // Link original -> revised (make sure it's tracked in this context)
            var origTracked = db.Sales.First(s => s.Id == _orig.Id);
            origTracked.RevisedToSaleId = newSale.Id;

            // Guard once for all OUT deltas (will throw if any would go negative)
            if (pendingOutDeltas.Count > 0)
            {
                var guard = new StockGuard(db);
                await guard.EnsureNoNegativeAtLocationAsync(pendingOutDeltas.ToArray());
            }

            // Now it’s safe to write stock entries
            db.StockEntries.AddRange(pendingEntries);

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
            ItemSearch.FocusSearch();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        // Original qty of this item on the ORIGINAL invoice (already deducted from stock)
        private decimal GetOriginalQty(int itemId)
            => _origLines?.Where(l => l.ItemId == itemId).Sum(l => l.Qty) ?? 0m;

        /// <summary>
        /// In amendments, only the "extra OUT" beyond original needs guarding.
        /// proposedCartQty = qty for this item currently in the edit cart (after user's change).
        /// extraOut = proposedCartQty - originalQty.
        /// If extraOut <= 0, it's fine (we're not increasing OUT).
        /// If extraOut > 0, validate that extraOut is available at the outlet.
        /// </summary>
        private async Task<bool> GuardEditLineQtyAsync(int itemId, decimal proposedCartQty)
        {
            var originalQty = GetOriginalQty(itemId);
            var extraOut = proposedCartQty - originalQty;

            if (extraOut <= 0m) return true; // no additional OUT, always OK

            using var db = new PosClientDbContext(_dbOptions);
            var guard = new StockGuard(db);
            try
            {
                await guard.EnsureNoNegativeAtLocationAsync(new[]
{
    (itemId: itemId,
     outletId: (int)_orig.OutletId,
     locType: InventoryLocationType.Outlet,
     locId: (int)_orig.OutletId,
     delta: -extraOut)
});

                return true;
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Not enough stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }


    }
}
