// Pos.Client.Wpf/Windows/Sales/ReturnFromInvoiceWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;            // IPaymentDialogService
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Models.Sales;
using Pos.Domain.Pricing;                 // PricingMath
using Pos.Domain.Services;                // ISalesService

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class ReturnFromInvoiceWindow : Window
    {
        private readonly int _origSaleId;
        private readonly ISalesService _sales;
        private readonly AppState _state;
        private readonly IInvoiceSettingsLocalService _invSettings; // NEW
        private bool _useTill; // NEW

        public bool Confirmed { get; private set; }
        public decimal RefundMagnitude { get; private set; } // absolute amount

        public sealed class Row : INotifyPropertyChanged
        {
            public int ItemId { get; init; }
            public string Sku { get; init; } = "";
            public string Name { get; init; } = "";
            public int SoldQty { get; init; }
            public int AlreadyReturned { get; init; }
            public int AvailableQty { get; init; }

            private int _returnQty;
            public int ReturnQty
            {
                get => _returnQty;
                set
                {
                    var clamped = Math.Max(0, Math.Min(value, AvailableQty));
                    if (_returnQty == clamped) return;
                    _returnQty = clamped;
                    OnPropertyChanged();
                }
            }

            public decimal UnitPrice { get; init; }
            public decimal? DiscountPct { get; init; }
            public decimal? DiscountAmt { get; init; }
            public decimal TaxRatePct { get; init; }
            public bool TaxInclusive { get; init; }

            private decimal _lineRefund;
            public decimal LineRefund
            {
                get => _lineRefund;
                set { if (_lineRefund == value) return; _lineRefund = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly ObservableCollection<Row> _rows = new();

        public ReturnFromInvoiceWindow(int saleId)
        {
            InitializeComponent();
            _origSaleId = saleId;
            _invSettings = App.Services.GetRequiredService<IInvoiceSettingsLocalService>(); // NEW

            _sales = App.Services.GetRequiredService<ISalesService>();
            _state = App.Services.GetRequiredService<AppState>();

            Grid.ItemsSource = _rows;

            Loaded += async (_, __) =>
            {
                var dto = await _sales.GetReturnFromInvoiceAsync(_origSaleId);
                var counterId = AppState.Current.CurrentCounterId;
                var settings = await _invSettings.GetForCounterWithFallbackAsync(counterId, default);

                _useTill = settings.UseTill;

                HeaderText.Text = dto.HeaderHuman;
                _rows.Clear();
                foreach (var x in dto.Lines)
                {
                    _rows.Add(new Row
                    {
                        ItemId = x.ItemId,
                        Sku = x.Sku,
                        Name = x.Name,
                        SoldQty = x.SoldQty,
                        AlreadyReturned = x.AlreadyReturned,
                        AvailableQty = x.AvailableQty,
                        ReturnQty = 0,
                        UnitPrice = x.UnitPrice,
                        DiscountPct = x.DiscountPct,
                        DiscountAmt = x.DiscountAmt,
                        TaxRatePct = x.TaxRatePct,
                        TaxInclusive = x.TaxInclusive,
                        LineRefund = 0m
                    });
                }

                RecalcTotals();
            };

            Grid.CellEditEnding += (_, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit) return;
                if (e.Row?.Item is Row r) r.ReturnQty = r.ReturnQty; // reapply clamp
                Dispatcher.BeginInvoke(new Action(RecalcTotals), DispatcherPriority.Background);
            };
        }

        private void RecalcTotals()
        {
            foreach (var r in _rows)
            {
                var a = PricingMath.CalcLine(new LineInput(
                    Qty: r.ReturnQty,
                    UnitPrice: r.UnitPrice,
                    DiscountPct: r.DiscountPct,
                    DiscountAmt: r.DiscountAmt,
                    TaxRatePct: r.TaxRatePct,
                    TaxInclusive: r.TaxInclusive));
                r.LineRefund = a.LineNet + a.LineTax;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {

            TillSession? openTill = null;
            if (_useTill)
            {
                openTill = await _sales.GetOpenTillAsync(_state.CurrentOutletId, _state.CurrentCounterId);
                if (openTill == null) { MessageBox.Show("Till is closed."); return; }
            }

            var reason = ReasonBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Enter a reason."); return;
            }

            if (!_rows.Any(r => r.ReturnQty > 0))
            {
                MessageBox.Show("No quantities entered to return."); return;
            }



            // Totals
            RecalcTotals();
            var amounts = _rows.Select(r => PricingMath.CalcLine(new LineInput(
                Qty: r.ReturnQty,
                UnitPrice: r.UnitPrice,
                DiscountPct: r.DiscountPct,
                DiscountAmt: r.DiscountAmt,
                TaxRatePct: r.TaxRatePct,
                TaxInclusive: r.TaxInclusive))).ToList();

            var magSubtotal = amounts.Sum(a => a.LineNet);
            var magTax = amounts.Sum(a => a.LineTax);
            var magGrand = magSubtotal + magTax;
            if (magGrand <= 0m) { MessageBox.Show("Nothing to return."); return; }

            // Ask cashier for refund split (overlay dialog)
            var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
            var payResult = await paySvc.ShowAsync(
                subtotal: magSubtotal,
                discountValue: 0m,
                tax: magTax,
                grandTotal: magGrand,
                items: _rows.Count(r => r.ReturnQty > 0),
                qty: _rows.Sum(r => r.ReturnQty),
                differenceMode: true,
                amountDelta: -magGrand,
                title: "Refund Payment"
            );
            if (!payResult.Confirmed) return;

            // Get open till (defensive)
            // Use current till if any; service will enforce UseTill preference
            //var tillId = _state.CurrentTillSessionId ?? 0;


            // Build finalize request for a RETURN
            var req = new SaleFinalizeRequest
            {
                OutletId = _state.CurrentOutletId,
                CounterId = _state.CurrentCounterId,
                TillSessionId = _useTill ? openTill!.Id : (int?)null,

                IsReturn = true,
                OriginalSaleId = _origSaleId,
                CashierId = _state.CurrentUserId,
                SalesmanId = null,
                CustomerKind = CustomerKind.WalkIn,
                CustomerId = null,
                CustomerName = null,
                CustomerPhone = null,
                InvoiceDiscountPct = null,
                InvoiceDiscountAmt = null,
                InvoiceDiscountValue = 0m,
                Subtotal = magSubtotal,
                TaxTotal = magTax,
                Total = magGrand,
                CashAmount = payResult.Cash,
                CardAmount = payResult.Card,
                PaymentMethod =
                    (payResult.Cash > 0 && payResult.Card > 0) ? PaymentMethod.Mixed :
                    (payResult.Cash > 0) ? PaymentMethod.Cash : PaymentMethod.Card,
                InvoiceFooter = null,
                EReceiptToken = string.Empty,
                HeldSaleId = null,
                Note = reason,
                Lines = _rows.Where(r => r.ReturnQty > 0).Select(r =>
                {
                    var a = PricingMath.CalcLine(new LineInput(
                        Qty: r.ReturnQty,
                        UnitPrice: r.UnitPrice,
                        DiscountPct: r.DiscountPct,
                        DiscountAmt: r.DiscountAmt,
                        TaxRatePct: r.TaxRatePct,
                        TaxInclusive: r.TaxInclusive));
                    return new SaleFinalizeRequest.SaleLineInput(
                        ItemId: r.ItemId,
                        Qty: r.ReturnQty,           // positive here; service will treat as IN for IsReturn
                        UnitPrice: r.UnitPrice,
                        DiscountPct: r.DiscountPct,
                        DiscountAmt: r.DiscountAmt,
                        TaxCode: null,
                        TaxRatePct: r.TaxRatePct,
                        TaxInclusive: r.TaxInclusive,
                        UnitNet: a.UnitNet,
                        LineNet: a.LineNet,
                        LineTax: a.LineTax,
                        LineTotal: a.LineTotal
                    );
                }).ToList()
            };

            try
            {
                var sale = await _sales.FinalizeAsync(req);    // creates a posted return, allocates invoice, posts stock/GL
                Confirmed = true;
                RefundMagnitude = magGrand;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not finalize the return: " + ex.Message, "Error");
            }
        }
    }
}
