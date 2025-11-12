// Pos.Client.Wpf/Windows/Sales/ReturnWithoutInvoiceWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;        // AppState, IPaymentDialogService
using Pos.Domain;
using Pos.Domain.Formatting;
using Pos.Domain.Pricing;
using Pos.Domain.Models.Sales;        // SaleFinalizeRequest, InvoicePreviewDto
using Pos.Domain.Services;            // ISalesService

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class ReturnWithoutInvoiceWindow : Window
    {
        private readonly ObservableCollection<ReturnLine> _cart = new();
        private readonly ISalesService _sales;
        private readonly IInventoryReadService _invRead;
        private readonly IItemsReadService _items;

        private readonly AppState _state;
        private readonly IPaymentDialogService _pay;
        // Scope (constructor params)
        private readonly int _outletId;
        private readonly int _counterId;
        // Selected salesman
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;
        // Invoice-level discount
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;

        // Footer
        private string _footer = "Return processed — thank you!";

        public ReturnWithoutInvoiceWindow() : this(1, 1) { }
        
        public ReturnWithoutInvoiceWindow(int outletId, int counterId)
        {
            InitializeComponent();

            _outletId = outletId;
            _counterId = counterId;

            _sales = App.Services.GetRequiredService<ISalesService>();
            _state = App.Services.GetRequiredService<AppState>();
            _pay = App.Services.GetRequiredService<IPaymentDialogService>();
            _invRead = App.Services.GetRequiredService<IInventoryReadService>();
            _items = App.Services.GetRequiredService<IItemsReadService>();
            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F9) { RefundButton_Click(s, e); e.Handled = true; return; }
                if (e.Key == Key.F5) { ClearButton_Click(s, e); e.Handled = true; return; }
            };

            CartGrid.ItemsSource = _cart;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;

            FooterBox.Text = _footer;
            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;

            Loaded += async (_, __) =>
            {
                await UpdateTillStatusUiAsync();
                await UpdateInvoicePreviewAsync();
                UpdateInvoiceDateNow();
                await LoadSalesmenAsync();
                ItemSearch?.FocusSearch();
                UpdateTotal();
            };
        }

        // ---------- Helpers (no EF) ----------

        private async System.Threading.Tasks.Task UpdateTillStatusUiAsync()
        {
            var open = await _sales.GetOpenTillAsync(_outletId, _counterId);
            TillStatusText.Text = open == null
                ? "Closed"
                : $"OPEN (Id={open.Id}, Opened {open.OpenTs:HH:mm})";
        }

        private async System.Threading.Tasks.Task UpdateInvoicePreviewAsync()
        {
            var prev = await _sales.GetInvoicePreviewAsync(_counterId);
            // Format however you prefer
            InvoicePreviewText.Text = $"{_counterId}-{prev.NextInvoiceNumber}";
        }

        private void UpdateInvoiceDateNow()
            => InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");

        private async System.Threading.Tasks.Task LoadSalesmenAsync()
        {
            // StaffLiteDto: Id, DisplayName
            var list = await _sales.GetSalesmenAsync();

            var withNone = list.ToList();
            withNone.Insert(0, new StaffLiteDto(0, "-- None --"));

            SalesmanBox.ItemsSource = withNone;
            SalesmanBox.DisplayMemberPath = nameof(StaffLiteDto.FullName);
            SalesmanBox.SelectedValuePath = nameof(StaffLiteDto.Id);
            SalesmanBox.SelectedIndex = 0;

            CashierNameText.Text = _state.CurrentUser?.DisplayName ?? "Cashier";
        }

        private void SalesmanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SalesmanBox.SelectedItem is StaffLiteDto sel)
            {
                if (sel.Id == 0) { _selectedSalesmanId = null; _selectedSalesmanName = null; }
                else { _selectedSalesmanId = sel.Id; _selectedSalesmanName = sel.FullName; }
            }
            else
            {
                _selectedSalesmanId = null;
                _selectedSalesmanName = null;
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

        // BEFORE
        // private void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)

        // AFTER
        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
            var pick = box.SelectedItem; // Pos.Domain.DTO.ItemIndexDto
            if (pick is null) return;

            // Read display/sku/lastCost from the single source of truth
            var meta = await _items.GetItemMetaForReturnAsync(pick.Id);

            // Fallbacks if meta is null or partially missing
            var display = meta?.display
                ?? ProductNameComposer.Compose(pick.ProductName, pick.Name ?? pick.DisplayName ?? "",
                                               pick.Variant1Name, pick.Variant1Value, pick.Variant2Name, pick.Variant2Value);
            var sku = meta?.sku ?? (pick.Sku ?? "");

            // Optional: if you want to show current on-hand for this outlet (purely informational)
            // var onHand = await _invRead.GetOnHandAsync(pick.Id,
            //     InventoryLocationType.Outlet, _outletId, DateTime.UtcNow);

            AddItemToCart(new ItemIndexDto(
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
            ));

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
                DisplayName = item.DisplayName, // <-- use the prebuilt display (from meta fallback chain)
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
            if (!(_cart.Count == 0 && string.IsNullOrWhiteSpace(InvDiscPctBox.Text) && string.IsNullOrWhiteSpace(InvDiscAmtBox.Text)))
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

            var open = await _sales.GetOpenTillAsync(_outletId, _counterId);
            if (open == null) { MessageBox.Show("Till is CLOSED. Please open till before refund.", "Till Closed"); return; }

            foreach (var l in _cart) RecalcLineShared(l);

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

            // Customer snapshot
            bool isWalkIn = (WalkInCheck?.IsChecked == true);
            var customerKind = isWalkIn ? CustomerKind.WalkIn : CustomerKind.Registered;
            int? customerId = null; // if you support registered customers, set here
            string? customerName = isWalkIn ? null : (CustNameBox?.Text?.Trim());
            string? customerPhone = isWalkIn ? null : (CustPhoneBox?.Text?.Trim());

            // Cashier from AppState
            var cashierIdLocal = _state.CurrentUser?.Id ?? 1;

            // Salesman (from combo)
            var salesmanIdLocal = _selectedSalesmanId;

            // Ask cashier for refund split (overlay dialog)
            var payResult = await _pay.ShowAsync(
                subtotal: subtotal,
                discountValue: invDiscValue,
                tax: tax,
                grandTotal: grand,
                items: itemsCount,
                qty: qtySum,
                differenceMode: false,    // full refund for this flow
                amountDelta: 0m,
                title: "Refund"
            );
            if (!payResult.Confirmed) { ItemSearch?.FocusSearch(); return; }

            var refundCash = payResult.Cash;
            var refundCard = payResult.Card;
            if (refundCash + refundCard + 0.01m < grand)
            {
                MessageBox.Show("Refund split is less than total."); return;
            }

            var paymentMethod =
                (refundCash > 0 && refundCard > 0) ? PaymentMethod.Mixed :
                (refundCash > 0) ? PaymentMethod.Cash : PaymentMethod.Card;

            // Build finalize request — RETURN without original invoice
            var req = new SaleFinalizeRequest
            {
                OutletId = _outletId,
                CounterId = _counterId,
                TillSessionId = open.Id,
                IsReturn = true,
                OriginalSaleId = null,

                CashierId = cashierIdLocal,
                SalesmanId = salesmanIdLocal,

                CustomerKind = customerKind,
                CustomerId = customerId,
                CustomerName = customerName,
                CustomerPhone = customerPhone,

                // Invoice-level discounts (Pct vs Amt are mutually exclusive)
                InvoiceDiscountPct = (_invDiscAmt > 0m) ? (decimal?)null : _invDiscPct,
                InvoiceDiscountAmt = (_invDiscAmt > 0m) ? _invDiscAmt : (decimal?)null,
                InvoiceDiscountValue = invDiscValue,

                Subtotal = subtotal,
                TaxTotal = tax,
                Total = grand,

                CashAmount = refundCash,
                CardAmount = refundCard,
                PaymentMethod = paymentMethod,

                InvoiceFooter = string.IsNullOrWhiteSpace(FooterBox?.Text) ? null : FooterBox!.Text,
                EReceiptToken = string.Empty,
                HeldSaleId = null,

                Note = string.IsNullOrWhiteSpace(ReasonBox?.Text) ? null : ReasonBox!.Text,

                Lines = _cart.Select(l =>
                {
                    var a = PricingMath.CalcLine(new LineInput(
                        Qty: l.Qty,
                        UnitPrice: l.UnitPrice,
                        DiscountPct: l.DiscountPct,
                        DiscountAmt: l.DiscountAmt,
                        TaxRatePct: l.TaxRatePct,
                        TaxInclusive: l.TaxInclusive));

                    // IMPORTANT: qty stays positive; service treats IsReturn as stock IN
                    return new SaleFinalizeRequest.SaleLineInput(
                        ItemId: l.ItemId,
                        Qty: l.Qty,
                        UnitPrice: l.UnitPrice,
                        DiscountPct: l.DiscountPct,
                        DiscountAmt: l.DiscountAmt,
                        TaxCode: l.TaxCode,
                        TaxRatePct: l.TaxRatePct,
                        TaxInclusive: l.TaxInclusive,
                        UnitNet: a.UnitNet,
                        LineNet: a.LineNet,
                        LineTax: a.LineTax,
                        LineTotal: a.LineTotal
                    );
                }).ToList()
            };

            try
            {
                var sale = await _sales.FinalizeAsync(req);

                MessageBox.Show(
                    $"Return saved.\nInvoice: {sale.CounterId}-{sale.InvoiceNumber}\nRefund: {sale.Total:0.00}",
                    "Success");

                // Reset UI
                _cart.Clear();
                _invDiscPct = 0m; _invDiscAmt = 0m;
                InvDiscPctBox.Text = ""; InvDiscAmtBox.Text = "";
                if (WalkInCheck != null) WalkInCheck.IsChecked = true;
                if (CustNameBox != null) CustNameBox.Text = "";
                if (CustPhoneBox != null) CustPhoneBox.Text = "";
                ReasonBox.Text = "";
                UpdateTotal();
                await UpdateInvoicePreviewAsync();
                UpdateInvoiceDateNow();
                ItemSearch?.FocusSearch();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not finalize the return: " + ex.Message, "Error");
            }
        }

        // ---------- View model for lines ----------
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
