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
using Microsoft.Extensions.DependencyInjection; // for GetRequiredService
using Pos.Client.Wpf.Services;                 // for IPaymentDialogService, PaymentResult


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class ReturnWithoutInvoiceWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _dbOptions;
        private readonly ObservableCollection<ReturnLine> _cart = new();
        private readonly int _outletId;
        private readonly int _counterId;
        private int cashierId => AppState.Current?.CurrentUser?.Id ?? 1;
        private string cashierDisplay => AppState.Current?.CurrentUser?.DisplayName ?? "Cashier";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;
        // Invoice-level discount
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;
        // Footer
        private string _footer = "Return processed — thank you!";
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

        public ReturnWithoutInvoiceWindow(IReturnsService _unused, int outletId, int counterId, int? _unusedTill, int _unusedUser)
            : this(outletId, counterId) { }

        private void Init()
        {
            InitializeComponent();
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F9) { RefundButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.F5) { ClearButton_Click(s, e); e.Handled = true; return; }
            };
            CartGrid.ItemsSource = _cart;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;
            FooterBox.Text = _footer;
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;
            Loaded += (_, __) =>
            {
                UpdateTillStatusUi();
                UpdateInvoicePreview();
                UpdateInvoiceDateNow();
                LoadSalesmen();
                ItemSearch?.FocusSearch();
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

        // Map Pos.Domain.DTO.ItemIndexDto (from ItemSearchBox) to this window's local ItemIndexDto
        private ItemIndexDto AdaptItem(Pos.Domain.DTO.ItemIndexDto src)
        {
            return new ItemIndexDto(
                Id: src.Id,
                Name: src.Name ?? src.DisplayName ?? "",
                Sku: src.Sku ?? "",
                Barcode: src.Barcode ?? "",
                Price: src.Price,
                TaxCode: src.TaxCode,
                DefaultTaxRatePct: src.DefaultTaxRatePct,
                TaxInclusive: src.TaxInclusive,
                DefaultDiscountPct: src.DefaultDiscountPct,
                DefaultDiscountAmt: src.DefaultDiscountAmt,
                ProductName: src.ProductName,
                Variant1Name: src.Variant1Name,
                Variant1Value: src.Variant1Value,
                Variant2Name: src.Variant2Name,
                Variant2Value: src.Variant2Value
            );
        }

        private void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
            var pick = box.SelectedItem; // Pos.Domain.DTO.ItemIndexDto
            if (pick is null) return;
            var adapted = AdaptItem(pick);
            AddItemToCart(adapted);
            try { ItemSearch?.FocusSearch(); } catch { }
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

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cart.Count == 0 && string.IsNullOrWhiteSpace(InvDiscPctBox.Text) && string.IsNullOrWhiteSpace(InvDiscAmtBox.Text))
            {
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
            ItemSearch?.FocusSearch();
        }

        private async void RefundButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Nothing to return — cart is empty."); return; }
            using var db = new PosClientDbContext(_dbOptions);
            var open = GetOpenTill(db);
            if (open == null) { MessageBox.Show("Till is CLOSED. Please open till before refund.", "Till Closed"); return; }
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
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = null; // attach if you have a customers table/id
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());
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
            // Pay dialog (overlay, refund split)
            var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
            var payResult = await paySvc.ShowAsync(
                subtotal, invDiscValue, tax, grand, itemsCount, qtySum,
                differenceMode: false,      // refund the full amount; set true only for delta-settlement flows
                amountDelta: 0m,
                title: "Refund"
            );

            if (!payResult.Confirmed)
            {
                ItemSearch?.FocusSearch();
                return;
            }
            var refundCash = payResult.Cash;
            var refundCard = payResult.Card;
            if (refundCash + refundCard + 0.01m < grand)
            {
                MessageBox.Show("Refund split is less than total.");
                return;
            }

            // ===== Credit-remaining guard for return =====
            var refunded = refundCash + refundCard;
            var storeCredit = grand - refunded;             // amount left as credit to customer

            bool leavesStoreCredit = (storeCredit > 0.009m);
            if (leavesStoreCredit && (customerId == null))
            {
                MessageBox.Show(
                    "This return leaves a credit balance for the customer.\n\n" +
                    "Please select a registered customer to continue.",
                    "Customer required for store credit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
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
                    // ✅ Location-aware fields the report uses
                    LocationType = InventoryLocationType.Outlet, // use your enum
                    LocationId = _outletId,

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
            // === GL POST: Sale Return (full document) ===
            try
            {
                using (var chk = new PosClientDbContext(_dbOptions))
                {
                    var already = chk.GlEntries.AsNoTracking().Any(g =>
                        g.DocType == Pos.Domain.Accounting.GlDocType.SaleReturn &&
                        g.DocId == sale.Id);

                    if (!already)
                    {
                        var gl = App.Services.GetRequiredService<IGlPostingService>();
                        await gl.PostSaleReturnAsync(sale);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GL post (return) failed: " + ex);
            }


            MessageBox.Show($"Return saved. ID: {sale.Id}\nInvoice: {_counterId}-{sale.InvoiceNumber}\nRefund: {sale.Total:0.00}", "Success");
            // ===== Post customer store credit if not fully refunded =====
            try
            {
                var poster = App.Services.GetRequiredService<PartyPostingService>();

                var refundedNow = sale.CashAmount + sale.CardAmount; // both decimals
                var leftoverCredit = sale.Total - refundedNow;       // store credit / A/R decrease

                if (leftoverCredit > 0.009m && sale.CustomerId.HasValue)
                {
                    await poster.PostAsync(
                        partyId: sale.CustomerId.Value,
                        scope: BillingScope.Outlet,
                        outletId: sale.OutletId,
                        docType: PartyLedgerDocType.SaleReturn,
                        docId: sale.Id,
                        debit: 0m,
                        credit: leftoverCredit, // -A/R
                        memo: $"Return (store credit) #{sale.CounterId}-{sale.InvoiceNumber}"
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Return saved, but posting to customer ledger failed:\n" + ex.Message,
                    "Ledger warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

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
            ItemSearch?.FocusSearch();
        }

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
