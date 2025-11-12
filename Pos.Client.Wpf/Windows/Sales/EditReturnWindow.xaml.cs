//Pos.Client.Wpf/EditReturnWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Pricing;
using Pos.Persistence;
using Microsoft.Extensions.DependencyInjection;    // GetRequiredService
using Pos.Client.Wpf.Services;                     // IPaymentDialogService, PaymentResult
using Pos.Persistence.Sync;                 // IOutboxWriter
using Pos.Client.Wpf.Services.Sync;         // EnqueueAfterSaveAsync extension (if you created it)
using Pos.Persistence.Services;            // IGlPostingService
using Pos.Domain.Models.Sales;
using Pos.Domain.Services;
using System.Drawing;
using ZXing;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class EditReturnWindow : Window
    {
        private readonly int _returnSaleId;
        private IReturnsService _returns;
        private EditReturnLoadDto? _load;   // snapshot for UI
        public bool Confirmed { get; private set; }
        public int NewRevision { get; private set; }
        public class Row : INotifyPropertyChanged
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string Name { get; set; } = "";
            public int SoldQty { get; set; }
            public int AlreadyReturnedExcludingThis { get; set; }
            public int AvailableQty { get; set; }
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

            public int OldReturnQty { get; set; } // absolute of old (positive)
            public decimal UnitPrice { get; set; }
            public decimal? DiscountPct { get; set; }
            public decimal? DiscountAmt { get; set; }
            public decimal TaxRatePct { get; set; }
            public bool TaxInclusive { get; set; }
            private decimal _lineRefund;
            public decimal LineRefund
            {
                get => _lineRefund;
                set { if (_lineRefund == value) return; _lineRefund = value; OnPropertyChanged(); }
            }

            public int AlreadyReturned => AlreadyReturnedExcludingThis + OldReturnQty;

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly ObservableCollection<Row> _rows = new();

        public EditReturnWindow(int returnSaleId)
        {
            InitializeComponent();
            _returnSaleId = returnSaleId;
            _returns = App.Services.GetRequiredService<IReturnsService>();
            Grid.ItemsSource = _rows;
            Loaded += async (_, __) => await LoadReturnAsync();   // async load
            Grid.CellEditEnding += (s, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit) return;
                if (e.Row?.Item is Row r) r.ReturnQty = r.ReturnQty; // clamp trigger
                Dispatcher.BeginInvoke(new Action(RecalcTotals), DispatcherPriority.Background);
            };
        }

        private async Task LoadReturnAsync()
        {
            try
            {
                _load = await _returns.LoadReturnForAmendAsync(_returnSaleId);
                
                HeaderText.Text = $"Amend Return {_load.CounterId}-{_load.InvoiceNumber}  (Rev {_load.Revision})  Total: {_load.CurrentTotal:0.00}";
                _rows.Clear();
                foreach (var l in _load.Lines)
                {
                    _rows.Add(new Row
                    {
                        ItemId = l.ItemId,
                        Sku = l.Sku,
                        Name = l.Name,
                        SoldQty = l.SoldQty,
                        AlreadyReturnedExcludingThis = l.AlreadyReturnedExcludingThis,
                        AvailableQty = l.AvailableQty,
                        OldReturnQty = l.OldReturnQty,
                        ReturnQty = Math.Min(l.OldReturnQty, l.AvailableQty),
                        UnitPrice = l.UnitPrice,
                        DiscountPct = l.DiscountPct,
                        DiscountAmt = l.DiscountAmt,
                        TaxRatePct = l.TaxRatePct,
                        TaxInclusive = l.TaxInclusive,
                        LineRefund = 0m
                    });
                }
                RecalcTotals();
                }
                catch (Exception ex)
                {
                MessageBox.Show("Failed to load return for amend: " + ex.Message);
                Close();
                }
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
            if (_load == null) return;
            var reason = ReasonBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Enter a reason for the amendment."); return;
            }
            if (!_rows.Any(r => r.ReturnQty != r.OldReturnQty))
            {
                MessageBox.Show("No changes to save."); return;
            }
            try
            {
                // Compute the NEW totals locally to know the delta direction & amount
                var newCalcs = _rows.Select(r => Pos.Domain.Pricing.PricingMath.CalcLine(new Pos.Domain.Pricing.LineInput(
                Qty: r.ReturnQty,
                UnitPrice: r.UnitPrice,
                DiscountPct: r.DiscountPct,
                DiscountAmt: r.DiscountAmt,
                TaxRatePct: r.TaxRatePct,
                TaxInclusive: r.TaxInclusive
                        ))).ToList();
                var magSub = newCalcs.Sum(a => a.LineNet);
                var magTax = newCalcs.Sum(a => a.LineTax);
                var magGrand = magSub + magTax;
                var newTotalSigned = -magGrand; // return totals are negative
                var amountDelta = newTotalSigned - _load.CurrentTotal; // +collect / -refund
                decimal payCash = 0m, payCard = 0m;
                        if (amountDelta != 0m)
                            {
                    var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
                    var modeTitle = (amountDelta >= 0m) ? "Collect Difference" : "Refund Difference";
                    var result = await paySvc.ShowAsync(
                    subtotal: Math.Abs(-magSub),
                    discountValue: 0m,
                    tax: Math.Abs(-magTax),
                    grandTotal: Math.Abs(newTotalSigned),
                    items: _rows.Count,
                    qty: _rows.Sum(x => x.ReturnQty),
                    differenceMode: true,
                    amountDelta: amountDelta,
                    title: modeTitle
                                );
                                if (!result.Confirmed) { MessageBox.Show("Amendment cancelled."); return; }
                                // Sign by direction
                    payCash = (result.Cash > 0 ? (amountDelta >= 0 ? result.Cash : -result.Cash) : 0m);
                    payCard = (result.Card > 0 ? (amountDelta >= 0 ? result.Card : -result.Card) : 0m);
                        }
                
                var req = new EditReturnFinalizeRequest(
                ReturnSaleId: _returnSaleId,
                Reason: reason!,
                Lines: _rows.Select(r => new EditReturnFinalizeLine(
                ItemId: r.ItemId,
                ReturnQty: r.ReturnQty,
                UnitPrice: r.UnitPrice,
                DiscountPct: r.DiscountPct,
                DiscountAmt: r.DiscountAmt,
                TaxRatePct: r.TaxRatePct,
                TaxInclusive: r.TaxInclusive
                            )).ToList(),
                PayCash: payCash,
                PayCard: payCard
                        );
                
                var res = await _returns.FinalizeReturnAmendAsync(req);
                Confirmed = true;
                NewRevision = res.NewRevision;
                DialogResult = true;
                    }
                catch (Exception ex)
            {
                MessageBox.Show("Failed to save amended return: " + ex.Message);
                    }
            }
    }
}
