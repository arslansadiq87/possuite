//Pos.Client.Wpf/ReturnFromInvoiceWindow.xaml.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Domain.Pricing;           // PricingMath
using Pos.Persistence;
using Microsoft.Extensions.DependencyInjection;   // GetRequiredService
using Pos.Client.Wpf.Services;                    // IPaymentDialogService, PaymentResult


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class ReturnFromInvoiceWindow : Window
    {
        private readonly int _origSaleId;
        private readonly DbContextOptions<PosClientDbContext> _opts;

        public bool Confirmed { get; private set; }
        public decimal RefundMagnitude { get; private set; } // absolute amount

        public class Row : INotifyPropertyChanged
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string Name { get; set; } = "";

            public int SoldQty { get; set; }              // total sold on original invoice
            public int AlreadyReturned { get; set; }      // cumulative returned so far
            public int AvailableQty { get; set; }         // Sold - AlreadyReturned (never < 0)

            private int _returnQty;
            public int ReturnQty
            {
                get => _returnQty;
                set
                {
                    // clamp to availability
                    var clamped = Math.Max(0, Math.Min(value, AvailableQty));
                    if (_returnQty == clamped) return;
                    _returnQty = clamped;
                    OnPropertyChanged();
                }
            }

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

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly ObservableCollection<Row> _rows = new();

        public ReturnFromInvoiceWindow(int saleId)
        {
            InitializeComponent();
            _origSaleId = saleId;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString)
                .Options;

            Grid.ItemsSource = _rows;
            LoadOriginal();

            // Recalc safely AFTER the grid has committed the edit.
            Grid.CellEditEnding += (s, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit) return;

                if (e.Row?.Item is Row r)
                {
                    // Reapply to trigger clamp & notify if needed
                    r.ReturnQty = r.ReturnQty;
                }

                Dispatcher.BeginInvoke(new Action(RecalcTotals), DispatcherPriority.Background);
            };
        }

        private void LoadOriginal()
        {
            using var db = new PosClientDbContext(_opts);

            var sale = db.Sales.AsNoTracking().First(s => s.Id == _origSaleId);
            var origLines = (from l in db.SaleLines.AsNoTracking().Where(x => x.SaleId == _origSaleId)
                             join i in db.Items.AsNoTracking() on l.ItemId equals i.Id
                             select new { l.ItemId, l.Qty, l.UnitPrice, l.DiscountPct, l.DiscountAmt, l.TaxRatePct, l.TaxInclusive, i.Sku, i.Name })
                             .ToList();

            // Sum previous returns linked to this original sale (cumulative)
            var priorReturnedByItem = (
                from s in db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == _origSaleId && s.Status != SaleStatus.Voided
                join l in db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) } // returns have negative qty; use absolute
            ).ToDictionary(x => x.ItemId, x => x.Qty);

            HeaderText.Text = $"Return from Invoice {sale.CounterId}-{sale.InvoiceNumber}  Rev {sale.Revision}  Total: {sale.Total:0.00}";

            _rows.Clear();
            foreach (var x in origLines)
            {
                var already = priorReturnedByItem.TryGetValue(x.ItemId, out var q) ? q : 0;
                var avail = Math.Max(0, x.Qty - already);

                _rows.Add(new Row
                {
                    ItemId = x.ItemId,
                    Sku = x.Sku ?? "",
                    Name = x.Name ?? "",
                    SoldQty = x.Qty,
                    AlreadyReturned = already,
                    AvailableQty = avail,
                    ReturnQty = 0,

                    UnitPrice = x.UnitPrice,
                    DiscountPct = x.DiscountPct,
                    DiscountAmt = x.DiscountAmt,
                    TaxRatePct = x.TaxRatePct,
                    TaxInclusive = x.TaxInclusive,

                    LineRefund = 0m
                });
            }

            // Initial totals
            RecalcTotals();
        }

        private void RecalcTotals()
        {
            foreach (var r in _rows)
            {
                // ReturnQty is already clamped to AvailableQty
                var qty = r.ReturnQty;

                var a = PricingMath.CalcLine(new LineInput(
                    Qty: qty,
                    UnitPrice: r.UnitPrice,
                    DiscountPct: r.DiscountPct,
                    DiscountAmt: r.DiscountAmt,
                    TaxRatePct: r.TaxRatePct,
                    TaxInclusive: r.TaxInclusive));

                // Positive magnitude
                r.LineRefund = a.LineNet + a.LineTax;
            }
        }

        
        private async void Save_Click(object sender, RoutedEventArgs e)
            {
                var reason = ReasonBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(reason))
                {
                    MessageBox.Show("Enter a reason."); return;
                }

                var any = _rows.Any(r => r.ReturnQty > 0);
                if (!any) { MessageBox.Show("No quantities entered to return."); return; }

                using var db = new PosClientDbContext(_opts);

                // ---- READ-ONLY: original sale + sold quantities (no transaction) ----
                var orig = db.Sales.AsNoTracking().First(s => s.Id == _origSaleId);

                var origByItem = db.SaleLines.AsNoTracking()
                    .Where(l => l.SaleId == _origSaleId)
                    .GroupBy(l => l.ItemId)
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty)); // sold qty is positive

                // Optional: quick pre-check outside tx (can be skipped; final check happens later in tx)
                foreach (var r in _rows.Where(x => x.ReturnQty > 0))
                {
                    var sold = origByItem.TryGetValue(r.ItemId, out var sQty) ? sQty : 0;
                    if (r.ReturnQty > sold)
                    {
                        MessageBox.Show($"Item {r.Sku} — trying to return {r.ReturnQty}, but only {sold} were sold.", "Invalid return");
                        return;
                    }
                }

                // ---- Compute intended refund magnitude (no transaction) ----
                RecalcTotals();
                var amounts = _rows.Select(r =>
                    PricingMath.CalcLine(new LineInput(
                        Qty: r.ReturnQty,
                        UnitPrice: r.UnitPrice,
                        DiscountPct: r.DiscountPct,
                        DiscountAmt: r.DiscountAmt,
                        TaxRatePct: r.TaxRatePct,
                        TaxInclusive: r.TaxInclusive)))
                    .ToList();

                var magSubtotal = amounts.Sum(a => a.LineNet);
                var magTax = amounts.Sum(a => a.LineTax);
                var magGrand = magSubtotal + magTax;

                if (magGrand <= 0m) { MessageBox.Show("Nothing to return."); return; }

                // ---- Ask cashier for refund split (overlay dialog) ----
                var paySvc = App.Services.GetRequiredService<IPaymentDialogService>();
                var payResult = await paySvc.ShowAsync(
                    subtotal: magSubtotal,
                    discountValue: 0m,
                    tax: magTax,
                    grandTotal: magGrand,
                    items: _rows.Count(r => r.ReturnQty > 0),
                    qty: _rows.Sum(r => r.ReturnQty),
                    differenceMode: true,
                    amountDelta: -magGrand,      // refund
                    title: "Refund Payment"
                );

                if (!payResult.Confirmed) return; // user cancelled; nothing to do

                // =====================================================================
                // START TRANSACTION *after* user confirmed, to minimize lock time
                // =====================================================================
                using var tx = db.Database.BeginTransaction();

                // ---- RE-READ "already returned so far" INSIDE TX to prevent races ----
                var priorNow = (
                    from s in db.Sales.AsNoTracking()
                    where s.IsReturn && s.OriginalSaleId == _origSaleId && s.Status != SaleStatus.Voided
                    join l in db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                    group l by l.ItemId into g
                    select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
                ).ToDictionary(x => x.ItemId, x => x.Qty);

                // ---- FINAL VALIDATION INSIDE TX ----
                foreach (var r in _rows.Where(x => x.ReturnQty > 0))
                {
                    var sold = origByItem.TryGetValue(r.ItemId, out var sQty) ? sQty : 0;
                    var already = priorNow.TryGetValue(r.ItemId, out var pQty) ? pQty : 0;
                    var availNow = Math.Max(0, sold - already);

                    if (r.ReturnQty > availNow)
                    {
                        MessageBox.Show(
                            $"Item {r.Sku} — trying to return {r.ReturnQty}, but only {availNow} is available to return now.",
                            "Return exceeds available");
                        tx.Rollback();
                        return;
                    }
                }

                // ---- Allocate invoice number INSIDE TX ----
                var seq = db.CounterSequences.SingleOrDefault(x => x.CounterId == orig.CounterId)
                    ?? db.CounterSequences.Add(new CounterSequence { CounterId = orig.CounterId, NextInvoiceNumber = 1 }).Entity;
                db.SaveChanges();
                var allocatedInvoiceNo = seq.NextInvoiceNumber;
                seq.NextInvoiceNumber++;
                db.SaveChanges();

                // ---- Create the return sale (negative totals), linked to original ----
                var ret = new Sale
                {
                    Ts = DateTime.UtcNow,
                    OutletId = orig.OutletId,
                    CounterId = orig.CounterId,
                    TillSessionId = db.TillSessions
                        .OrderByDescending(t => t.Id)
                        .Where(t => t.OutletId == orig.OutletId && t.CounterId == orig.CounterId && t.CloseTs == null)
                        .Select(t => (int?)t.Id)
                        .FirstOrDefault() ?? orig.TillSessionId,


                    IsReturn = true,
                    OriginalSaleId = _origSaleId,
                    Status = SaleStatus.Final,
                    Revision = 0,
                    InvoiceNumber = allocatedInvoiceNo,

                    Subtotal = magSubtotal,
                    TaxTotal = magTax,
                    Total = magGrand,

                    CashierId = orig.CashierId,
                    SalesmanId = orig.SalesmanId,

                    CustomerKind = orig.CustomerKind,
                    CustomerId = orig.CustomerId,
                    CustomerName = orig.CustomerName,
                    CustomerPhone = orig.CustomerPhone,

                    CashAmount = payResult.Cash,   // positive = cash paid out (refund)
                    CardAmount = payResult.Card,
                    PaymentMethod =
                        (payResult.Cash > 0 && payResult.Card > 0) ? PaymentMethod.Mixed :
                        (payResult.Cash > 0) ? PaymentMethod.Cash : PaymentMethod.Card,

                    Note = reason
                };
                db.Sales.Add(ret);
                db.SaveChanges();

                // ---- Lines (negative qty) + stock add-back ----
                foreach (var r in _rows.Where(x => x.ReturnQty > 0))
                {
                    var a = PricingMath.CalcLine(new LineInput(
                        Qty: r.ReturnQty,
                        UnitPrice: r.UnitPrice,
                        DiscountPct: r.DiscountPct,
                        DiscountAmt: r.DiscountAmt,
                        TaxRatePct: r.TaxRatePct,
                        TaxInclusive: r.TaxInclusive));

                    db.SaleLines.Add(new SaleLine
                    {
                        SaleId = ret.Id,
                        ItemId = r.ItemId,
                        Qty = -r.ReturnQty,                // negative on return
                        UnitPrice = r.UnitPrice,
                        DiscountPct = r.DiscountPct,
                        DiscountAmt = r.DiscountAmt,
                        TaxCode = null,
                        TaxRatePct = r.TaxRatePct,
                        TaxInclusive = r.TaxInclusive,
                        UnitNet = -(a.UnitNet),
                        LineNet = -(a.LineNet),
                        LineTax = -(a.LineTax),
                        LineTotal = -(a.LineTotal)
                    });

                    db.StockEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet, // ensure this enum exists in your domain
                        LocationId = ret.OutletId,
                        OutletId = ret.OutletId,
                        ItemId = r.ItemId,
                        QtyChange = +r.ReturnQty,         // add back to stock
                        RefType = "SaleReturn",
                        RefId = ret.Id,
                        Ts = DateTime.UtcNow
                    });
                }

                db.SaveChanges();
                tx.Commit();
            // === GL POST: Sale Return (same DbContext) ===
            try
            {
                var gl = App.Services.GetRequiredService<IGlPostingService>();

                var already = db.GlEntries.AsNoTracking().Any(g =>
                    g.DocType == Pos.Domain.Accounting.GlDocType.SaleReturn &&
                    g.DocId == ret.Id);

                if (!already)
                {
                    await gl.PostSaleReturnAsync(ret);  // one-arg overload
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GL post (return-from-invoice) failed: " + ex);
            }


            Confirmed = true;
                RefundMagnitude = magGrand;

                try
                {
                    // Optional: print your return receipt
                    // ReceiptPrinter.PrintSaleAmended(ret, Array.Empty<CartLine>(), "RETURN");
                }
                catch { /* ignore print errors */ }

                DialogResult = true;
        }

}
}
