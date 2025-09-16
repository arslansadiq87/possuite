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

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class EditReturnWindow : Window
    {
        private readonly int _returnSaleId;
        private readonly DbContextOptions<PosClientDbContext> _opts;

        private Sale? _old;
        private int _originalSaleId;

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

            // UI convenience (not displayed)
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

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            Grid.ItemsSource = _rows;
            LoadReturn();

            Grid.CellEditEnding += (s, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit) return;
                if (e.Row?.Item is Row r) r.ReturnQty = r.ReturnQty; // clamp trigger
                Dispatcher.BeginInvoke(new Action(RecalcTotals), DispatcherPriority.Background);
            };
        }

        private void LoadReturn()
        {
            using var db = new PosClientDbContext(_opts);

            _old = db.Sales.AsNoTracking().First(s => s.Id == _returnSaleId);
            if (!_old.IsReturn) { MessageBox.Show("Selected document is not a return."); Close(); return; }
            if (_old.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL returns can be amended."); Close(); return; }

            _originalSaleId = _old.OriginalSaleId ?? 0;
            var oldLines = db.SaleLines.AsNoTracking().Where(l => l.SaleId == _returnSaleId).ToList();

            // Original sold per item (positive)
            var soldByItem = db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == _originalSaleId)
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            // Already returned by OTHER returns (exclude this return)
            var returnedByOthers = (
                from s in db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == _originalSaleId && s.Status != SaleStatus.Voided && s.Id != _returnSaleId
                join l in db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
            ).ToDictionary(x => x.ItemId, x => x.Qty);

            // Items metadata
            var items = db.Items.AsNoTracking().ToDictionary(i => i.Id, i => new { i.Sku, i.Name });

            HeaderText.Text = $"Amend Return {_old.CounterId}-{_old.InvoiceNumber}  (Rev {_old.Revision})  Total: {_old.Total:0.00}";

            _rows.Clear();
            foreach (var l in oldLines)
            {
                var sold = soldByItem.TryGetValue(l.ItemId, out var sQty) ? sQty : 0;
                var oldRetAbs = Math.Abs(l.Qty);
                var otherReturned = returnedByOthers.TryGetValue(l.ItemId, out var rQty) ? rQty : 0;
                var availableNow = Math.Max(0, sold - otherReturned); // capacity available for THIS amended doc
                var meta = items.TryGetValue(l.ItemId, out var m) ? m : new { Sku = "", Name = "" };

                _rows.Add(new Row
                {
                    ItemId = l.ItemId,
                    Sku = meta.Sku ?? "",
                    Name = meta.Name ?? "",

                    SoldQty = sold,
                    AlreadyReturnedExcludingThis = otherReturned,
                    AvailableQty = availableNow,

                    // initial value = old absolute qty, clamped to available
                    OldReturnQty = oldRetAbs,
                    ReturnQty = Math.Min(oldRetAbs, availableNow),

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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_old == null) return;
            var reason = ReasonBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("Enter a reason for the amendment."); return;
            }

            if (!_rows.Any(r => r.ReturnQty != r.OldReturnQty))
            {
                MessageBox.Show("No changes to save."); return;
            }

            using var db = new PosClientDbContext(_opts);
            using var tx = db.Database.BeginTransaction();

            // Reload latest version of this return (to avoid amending an outdated revision)
            var latest = db.Sales
                .Where(s => s.CounterId == _old.CounterId
                         && s.InvoiceNumber == _old.InvoiceNumber
                         && s.IsReturn
                         && s.Status != SaleStatus.Voided)
                .OrderByDescending(s => s.Revision)
                .First();

            if (latest.Status != SaleStatus.Final) { MessageBox.Show("Current return is not FINAL."); return; }

            var latestLines = db.SaleLines.Where(l => l.SaleId == latest.Id).ToList();

            // Hard re-check availability inside tx (exclude this doc)
            var soldByItem = db.SaleLines.AsNoTracking()
                .Where(l => l.SaleId == _originalSaleId)
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            var returnedByOthersNow = (
                from s in db.Sales.AsNoTracking()
                where s.IsReturn && s.OriginalSaleId == _originalSaleId && s.Status != SaleStatus.Voided && s.Id != latest.Id
                join l in db.SaleLines.AsNoTracking() on s.Id equals l.SaleId
                group l by l.ItemId into g
                select new { ItemId = g.Key, Qty = g.Sum(x => Math.Abs(x.Qty)) }
            ).ToDictionary(x => x.ItemId, x => x.Qty);

            foreach (var r in _rows)
            {
                var sold = soldByItem.TryGetValue(r.ItemId, out var sQty) ? sQty : 0;
                var others = returnedByOthersNow.TryGetValue(r.ItemId, out var oQty) ? oQty : 0;
                var availNow = Math.Max(0, sold - others);

                if (r.ReturnQty > availNow)
                {
                    MessageBox.Show($"Item {r.Sku}: requested {r.ReturnQty}, available {availNow}.");
                    tx.Rollback(); return;
                }
            }

            // Compute NEW totals (signed: negative)
            var newAmounts = _rows.Select(r => PricingMath.CalcLine(new LineInput(
                Qty: r.ReturnQty, UnitPrice: r.UnitPrice, DiscountPct: r.DiscountPct, DiscountAmt: r.DiscountAmt,
                TaxRatePct: r.TaxRatePct, TaxInclusive: r.TaxInclusive))).ToList();

            var magSubtotal = newAmounts.Sum(a => a.LineNet);
            var magTax = newAmounts.Sum(a => a.LineTax);
            var magGrand = magSubtotal + magTax;

            // Build amended return
            var amended = new Sale
            {
                Ts = DateTime.UtcNow,
                OutletId = latest.OutletId,
                CounterId = latest.CounterId,
                TillSessionId = latest.TillSessionId,

                IsReturn = true,
                OriginalSaleId = _originalSaleId,

                Status = SaleStatus.Final,
                Revision = latest.Revision + 1,
                RevisedFromSaleId = latest.Id,
                InvoiceNumber = latest.InvoiceNumber,     // SAME invoice number

                Subtotal = -magSubtotal,                  // signed totals (return = negative)
                TaxTotal = -magTax,
                Total = -magGrand,

                CashierId = latest.CashierId,
                SalesmanId = latest.SalesmanId,

                CustomerKind = latest.CustomerKind,
                CustomerId = latest.CustomerId,
                CustomerName = latest.CustomerName,
                CustomerPhone = latest.CustomerPhone,

                Note = reason,
                EReceiptToken = Guid.NewGuid().ToString("N"),
                EReceiptUrl = null,
                InvoiceFooter = latest.InvoiceFooter
            };
            db.Sales.Add(amended);
            db.SaveChanges();

            // New lines (negative qty for returns)
            var newByItem = _rows.ToDictionary(r => r.ItemId, r => r);
            foreach (var r in _rows)
            {
                var a = PricingMath.CalcLine(new LineInput(
                    Qty: r.ReturnQty, UnitPrice: r.UnitPrice, DiscountPct: r.DiscountPct, DiscountAmt: r.DiscountAmt,
                    TaxRatePct: r.TaxRatePct, TaxInclusive: r.TaxInclusive));

                db.SaleLines.Add(new SaleLine
                {
                    SaleId = amended.Id,
                    ItemId = r.ItemId,
                    Qty = -r.ReturnQty,
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
            }
            db.SaveChanges();

            // Link previous as Revised
            latest.Status = SaleStatus.Revised;
            latest.RevisedToSaleId = amended.Id;
            db.SaveChanges();

            // STOCK DELTA (universal rule): deltaQty = newQty - oldQty (signed), QtyChange = -deltaQty
            var oldByItem = latestLines.ToDictionary(x => x.ItemId, x => x);
            var allItems = oldByItem.Keys.Union(newByItem.Keys).Distinct();

            foreach (var itemId in allItems)
            {
                var oldQty = oldByItem.TryGetValue(itemId, out var o) ? o.Qty : 0;           // old return line (negative)
                var newQty = newByItem.TryGetValue(itemId, out var r) ? -r.ReturnQty : 0;    // new line qty is negative
                var deltaQty = newQty - oldQty;
                if (deltaQty != 0)
                {
                    db.StockEntries.Add(new StockEntry
                    {
                        OutletId = amended.OutletId,
                        ItemId = itemId,
                        QtyChange = -deltaQty, // sign-aware
                        RefType = "Amend",
                        RefId = amended.Id,
                        Ts = DateTime.UtcNow
                    });
                }
            }
            db.SaveChanges();

            // PAYMENT DELTA (signed totals)
            var amountDelta = amended.Total - latest.Total; // signed
            if (amountDelta != 0m)
            {
                var pay = new PayWindow(
                    subtotal: Math.Abs(amended.Subtotal),
                    discountValue: 0m,
                    tax: Math.Abs(amended.TaxTotal),
                    grandTotal: Math.Abs(amended.Total),
                    items: _rows.Count,
                    qty: _rows.Sum(x => x.ReturnQty),
                    differenceMode: true,
                    amountDelta: amountDelta
                )
                { Owner = this };

                var ok = pay.ShowDialog() == true && pay.Confirmed;
                if (!ok) { tx.Rollback(); MessageBox.Show("Amendment cancelled."); return; }

                amended.CashAmount =
                    (pay.Cash > 0 ? (amountDelta >= 0 ? pay.Cash : -pay.Cash) : 0m);
                amended.CardAmount =
                    (pay.Card > 0 ? (amountDelta >= 0 ? pay.Card : -pay.Card) : 0m);

                amended.PaymentMethod =
                    (pay.Cash > 0 && pay.Card > 0) ? PaymentMethod.Mixed :
                    (pay.Cash > 0) ? PaymentMethod.Cash : PaymentMethod.Card;

                db.SaveChanges();
            }

            tx.Commit();
            Confirmed = true;
            NewRevision = amended.Revision;

            // Optional: print
            // try { ReceiptPrinter.PrintSaleAmended(amended, Array.Empty<CartLine>(), $"RETURN Rev {amended.Revision}"); } catch {}

            DialogResult = true;
        }
    }
}
