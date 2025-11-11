// Pos.Client.Wpf/Windows/Sales/EditSaleWindow.xaml.cs
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Models;
using Pos.Client.Wpf.Services;            // AppState, IPaymentDialogService
using Pos.Domain;
using Pos.Domain.Formatting;
using Pos.Domain.Pricing;
using Pos.Domain.Services;
using Pos.Domain.Models.Sales;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class EditSaleWindow : Window
    {
        // ----- services -----
        private readonly ISalesService _sales;
        // ----- identity / snapshot -----
        private readonly int _saleId;
        private EditSaleLoadDto _orig = null!;
        private decimal _origSubtotal, _origTax, _origGrand;
        // ----- UI state -----
        private readonly ObservableCollection<CartLine> _cart = new();
        public ObservableCollection<ItemIndexDto> DataContextItemIndex { get; } = new();
        private readonly Dictionary<string, ItemIndexDto> _barcodeIndex = new(StringComparer.OrdinalIgnoreCase);
        private int cashierId => AppState.Current?.CurrentUser?.Id ?? 1;
        private string cashierDisplay => AppState.Current?.CurrentUser?.DisplayName ?? "Cashier";
        private int? _selectedSalesmanId = null;
        private string? _selectedSalesmanName = null;
        private decimal _invDiscPct = 0m;
        private decimal _invDiscAmt = 0m;
        private string _invoiceFooter = "";
        public bool Confirmed { get; private set; } = false;
        public int NewRevision { get; private set; } = 0;

        public EditSaleWindow(int saleId)
        {
            InitializeComponent();
            _saleId = saleId;
            _sales = App.Services.GetRequiredService<ISalesService>();

            CartGrid.ItemsSource = _cart;
            CartGrid.CellEditEnding += CartGrid_CellEditEnding;

            CustNameBox.IsEnabled = CustPhoneBox.IsEnabled = false;

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F9) { SaveRevision_Click(s, e); e.Handled = true; }
                if (e.Key == Key.F5) { Revert_Click(s, e); e.Handled = true; }
            };

            Loaded += async (_, __) =>
            {
                await LoadSaleAsync();
                await LoadItemIndexAsync();
                CashierNameText.Text = cashierDisplay;
                LoadSalesmen(); // keep your local UI fill if you want; not touching EF here
                UpdateHeaderUi();
                UpdateTotal();
                ItemSearch.FocusSearch();
            };
        }

        // ----- Local UI DTO for the grid/search binding -----
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

        // ===== Loads =====
        private async Task LoadSaleAsync()
        {
            _orig = await _sales.GetSaleForEditAsync(_saleId);

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
            foreach (var l in _orig.Lines)
            {
                _cart.Add(new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = "",
                    DisplayName = "",
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
        }

        private async Task LoadItemIndexAsync()
        {
            var list = await _sales.GetItemIndexAsync(); // domain ItemIndexDto

            DataContextItemIndex.Clear();
            foreach (var it in list)
            {
                DataContextItemIndex.Add(new ItemIndexDto(
                    it.Id, it.Name, it.Sku, it.Barcode, it.Price,
                    it.TaxCode, it.DefaultTaxRatePct, it.TaxInclusive,
                    it.DefaultDiscountPct, it.DefaultDiscountAmt,
                    it.ProductName, it.Variant1Name, it.Variant1Value, it.Variant2Name, it.Variant2Value
                ));
            }

            _barcodeIndex.Clear();
            foreach (var dto in DataContextItemIndex)
                if (!string.IsNullOrWhiteSpace(dto.Barcode))
                    _barcodeIndex[dto.Barcode] = dto;

            var byId = DataContextItemIndex.ToDictionary(x => x.Id);
            foreach (var cl in _cart)
                if (byId.TryGetValue(cl.ItemId, out var meta))
                {
                    cl.Sku = meta.Sku;
                    cl.DisplayName = meta.DisplayName;
                }
        }

        private void UpdateHeaderUi()
        {
            // We don’t have InvoiceNumber in EditSaleLoadDto; keep a sensible display
            TitleText.Text = $"Amend Invoice  Counter {_orig.CounterId}";
            SubTitleText.Text = $"Original Rev {_orig.Revision}  •  {_orig.TsUtc.ToLocalTime():dd-MMM-yyyy HH:mm}";
            OrigTotalsText.Text = $"Original: Subtotal {_origSubtotal:N2}   Tax {_origTax:N2}   Total {_origGrand:N2}";
            InvoiceDateText.Text = DateTime.Now.ToString("dd-MMM-yyyy");
            InvoicePreviewText.Text = $"Counter {_orig.CounterId} (Rev {_orig.Revision + 1})";
        }

        private void LoadSalesmen()
        {
            // Keep your existing UI binding approach if you want (populate via another service if needed).
            // This stub intentionally does nothing to avoid EF in the UI layer.
        }

        // ===== Item pick / cart ops =====
        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var box = (Pos.Client.Wpf.Controls.ItemSearchBox)sender;
            object? selected = box.SelectedItem;
            if (selected is null) return;

            Pos.Domain.Models.Sales.ItemIndexDto? pick = selected as Pos.Domain.Models.Sales.ItemIndexDto;
            if (pick is null)
            {
                MessageBox.Show($"Unsupported SelectedItem: {selected.GetType().FullName}");
                return;
            }

            var itemId = pick.Id;
            var existing = _cart.FirstOrDefault(c => c.ItemId == itemId);
            var proposedCartQty = (existing?.Qty ?? 0m) + 1m;

            if (!await GuardEditLineQtyAsync(itemId, proposedCartQty))
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
                AddItemToCart(new ItemIndexDto(
                    pick.Id, pick.Name, pick.Sku, pick.Barcode, pick.Price, pick.TaxCode,
                    pick.DefaultTaxRatePct, pick.TaxInclusive, pick.DefaultDiscountPct, pick.DefaultDiscountAmt,
                    pick.ProductName, pick.Variant1Name, pick.Variant1Value, pick.Variant2Name, pick.Variant2Value
                ));
            }

            UpdateTotal();
            try { ItemSearch?.FocusSearch(); } catch { }
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

        // ===== Grid edits & qty buttons =====
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
                        if (l.Qty <= 0) l.Qty = 1;
                        var ok = await GuardEditLineQtyAsync(l.ItemId, l.Qty);
                        if (!ok)
                        {
                            l.Qty -= 1;
                            if (l.Qty < 1) l.Qty = 1;
                        }
                    }

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
                var proposedCartQty = l.Qty + 1;
                _ = GuardAndApplyAsync(l, proposedCartQty);
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

        private async Task GuardAndApplyAsync(CartLine l, int proposedCartQty)
        {
            if (!await GuardEditLineQtyAsync(l.ItemId, proposedCartQty)) return;
            l.Qty = proposedCartQty;
            RecalcLineShared(l);
            UpdateTotal();
        }

        // ===== Invoice-level inputs =====
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

        private void SalesmanBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = SalesmanBox.SelectedItem;
            var (id, name) = ExtractIdAndName(sel);

            if (id.HasValue && id.Value != 0)
            {
                _selectedSalesmanId = id.Value;
                _selectedSalesmanName = name;
            }
            else
            {
                _selectedSalesmanId = null;
                _selectedSalesmanName = null;
            }
        }

        private static (int? Id, string? Name) ExtractIdAndName(object? obj)
        {
            if (obj == null) return (null, null);

            var t = obj.GetType();

            // try common shapes
            var idProp = t.GetProperty("Id");
            var nameProp = t.GetProperty("DisplayName")
                       ?? t.GetProperty("FullName")
                       ?? t.GetProperty("Name");

            int? id = null;
            string? name = null;

            if (idProp != null)
            {
                var v = idProp.GetValue(obj);
                if (v is int i) id = i;
                else if (v is long l) id = checked((int)l);
                else if (v is short s) id = s;
                else if (v is byte b) id = b;
                else if (v is decimal d) id = (int)d;
                else if (v is string sId && int.TryParse(sId, out var i2)) id = i2;
            }

            if (nameProp != null)
            {
                name = nameProp.GetValue(obj)?.ToString();
            }

            return (id, name);
        }


        // ===== Totals =====
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

        // ===== Guard helpers =====
        private decimal GetOriginalQty(int itemId)
            => _orig?.Lines?.Where(l => l.ItemId == itemId).Sum(l => l.Qty) ?? 0m;

        private async Task<bool> GuardEditLineQtyAsync(int itemId, decimal proposedCartQty)
        {
            var originalQty = GetOriginalQty(itemId);
            var ok = await _sales.GuardEditExtraOutAsync(_orig.OutletId, itemId, originalQty, proposedCartQty);
            if (!ok)
                MessageBox.Show("Not enough stock at this outlet.", "Not enough stock",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            return ok;
        }

        // ===== Save / Revert / Cancel =====
        private async void SaveRevision_Click(object? sender, RoutedEventArgs e)
        {
            if (!_cart.Any()) { MessageBox.Show("Cart is empty."); return; }

            var open = await _sales.GetOpenTillAsync(_orig.OutletId, _orig.CounterId);
            if (open == null)
            {
                MessageBox.Show("Till is CLOSED. Open till before saving an amendment.", "Till Closed");
                return;
            }

            // refresh math
            foreach (var cl in _cart) RecalcLineShared(cl);

            var lines = _cart.ToList();
            var lineNetSum = lines.Sum(l => l.LineNet);

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

            foreach (var l in lines)
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
            if (deltaGrand < 0.005m)
            {
                if (MessageBox.Show("No net change vs original. Save as a new revision anyway?", "No Change",
                                    MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            }

            decimal addCash = 0m, addCard = 0m;
            var payMethod = PaymentMethod.Cash;

            if (deltaGrand > 0.005m)
            {
                var deltaDisc = invDiscValue - (_orig.InvoiceDiscountValue);
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

            var req = new EditSaleSaveRequest(
                OriginalSaleId: _orig.SaleId,
                OutletId: _orig.OutletId,
                CounterId: _orig.CounterId,
                TillSessionId: open.Id,
                NewRevisionNumber: _orig.Revision + 1,
                Subtotal: newSub,
                TaxTotal: newTax,
                Total: newGrand,
                InvoiceDiscountPct: (_invDiscAmt > 0m) ? null : _invDiscPct,
                InvoiceDiscountAmt: (_invDiscAmt > 0m) ? _invDiscAmt : null,
                InvoiceDiscountValue: invDiscValue,
                CashierId: cashierId,
                SalesmanId: _selectedSalesmanId,
                CustomerKind: (WalkInCheck.IsChecked == true) ? CustomerKind.WalkIn : CustomerKind.Registered,
                CustomerName: (WalkInCheck.IsChecked == true) ? null : string.IsNullOrWhiteSpace(CustNameBox.Text) ? null : CustNameBox.Text.Trim(),
                CustomerPhone: (WalkInCheck.IsChecked == true) ? null : string.IsNullOrWhiteSpace(CustPhoneBox.Text) ? null : CustPhoneBox.Text.Trim(),
                CollectedCash: addCash,
                CollectedCard: addCard,
                PaymentMethod: (deltaGrand > 0.005m) ? payMethod : _origCustomerPaymentMethodFallback(),
                InvoiceFooter: string.IsNullOrWhiteSpace(FooterBox.Text) ? null : FooterBox.Text,
                Lines: _cart.Select(l => new EditSaleSaveRequest.Line(
                    l.ItemId, l.Qty, l.UnitPrice, l.DiscountPct, l.DiscountAmt,
                    l.TaxCode, l.TaxRatePct, l.TaxInclusive, l.UnitNet, l.LineNet, l.LineTax, l.LineTotal
                )).ToList()
            );

            EditSaleSaveResult result;
            try
            {
                result = await _sales.SaveAmendmentAsync(req);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Amendment Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save amendment:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Optional: print here if desired (reuse your printer)
            // try { ReceiptPrinter.PrintSale(...); } catch { }

            Confirmed = true;
            NewRevision = result.NewRevision;

            var d = result.DeltaGrand;
            MessageBox.Show(
                $"Amendment saved.\nCounter {_orig.CounterId}\nRevision {result.NewRevision}\nDifference: {(d >= 0 ? "+" : "")}{d:N2}",
                "Success");

            DialogResult = true;
            Close();

            PaymentMethod _origCustomerPaymentMethodFallback()
                => PaymentMethod.Cash; // if you expose PaymentMethod in EditSaleLoadDto, return the original here
        }

        private void Revert_Click(object? sender, RoutedEventArgs e)
        {
            _cart.Clear();
            foreach (var l in _orig.Lines)
            {
                _cart.Add(new CartLine
                {
                    ItemId = l.ItemId,
                    Sku = DataContextItemIndex.FirstOrDefault(i => i.Id == l.ItemId)?.Sku ?? "",
                    DisplayName = DataContextItemIndex.FirstOrDefault(i => i.Id == l.ItemId)?.DisplayName ?? "",
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
    }
}
