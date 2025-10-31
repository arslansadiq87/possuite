using System.Globalization;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class TillSessionSummaryWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly int _tillId;
        private readonly int _outletId;
        private readonly int _counterId;

        public TillSessionSummaryWindow(DbContextOptions<PosClientDbContext> opts, int tillSessionId, int outletId, int counterId)
        {
            InitializeComponent();
            _opts = opts;
            _tillId = tillSessionId;
            _outletId = outletId;
            _counterId = counterId;

            this.Loaded += (_, __) => { LoadSummary(); };
            this.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.F5) { LoadSummary(); e.Handled = true; } };
        }

        private string M(decimal v) => v.ToString("N2", CultureInfo.CurrentCulture);

        private void LoadSummary()
        {
            try
            {
                using var db = new PosClientDbContext(_opts);

                var till = db.TillSessions.AsNoTracking().FirstOrDefault(t => t.Id == _tillId);
                if (till == null) { MessageBox.Show("Till session not found."); Close(); return; }

                // Header
                OutletText.Text = _outletId.ToString();
                CounterText.Text = _counterId.ToString();
                TillIdText.Text = till.Id.ToString();
                OpenedText.Text = till.OpenTs.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");
                DurationText.Text = (DateTime.UtcNow - till.OpenTs).ToString(@"hh\:mm");

                // === A) LATEST STATE for business totals ===
                var latest = db.Sales.AsNoTracking()
                    .Where(s => s.TillSessionId == _tillId
                             && s.Status == SaleStatus.Final
                             && s.VoidedAtUtc == null
                             && s.RevisedToSaleId == null) // only surviving revision
                    .ToList();

                var latestSales = latest.Where(s => !s.IsReturn).ToList();
                var latestReturns = latest.Where(s => s.IsReturn).ToList();

                // Sales total as stored (positive)
                var salesTotal = latestSales.Sum(s => s.Total);

                // Returns total shown as POSITIVE amount regardless of storage sign
                var returnsTotalAbs = latestReturns.Sum(s => Math.Abs(s.Total));

                // Net = Sales − Returns (clean, unambiguous)
                var netTotal = salesTotal - returnsTotalAbs;

                // === B) MOVEMENTS (cash/card) — include ALL final non-voided docs (each revision is a delta) ===
                var moves = db.Sales.AsNoTracking()
                    .Where(s => s.TillSessionId == _tillId
                             && s.Status == SaleStatus.Final
                             && s.VoidedAtUtc == null)
                    .ToList();

                var openingFloat = till.OpeningFloat;

                // CASH (sign-preserving for sales; absolute for refunds)
                var salesCash = moves.Where(s => !s.IsReturn).Sum(s => s.CashAmount);
                var refundsCashAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CashAmount));

                // Optional: if you also show card figures, mirror the same pattern
                var salesCard = moves.Where(s => !s.IsReturn).Sum(s => s.CardAmount);
                var refundsCardAbs = Math.Abs(moves.Where(s => s.IsReturn).Sum(s => s.CardAmount));


                // Cash UI (absolute inflow/outflow by type)
                var cashInAbs = moves.Where(s => !s.IsReturn).Sum(s => Math.Max(s.CashAmount, 0m));
                var cashOutAbs = moves.Where(s => s.IsReturn).Sum(s => Math.Abs(s.CashAmount));

                // Expected drawer cash = opening + signed net cash movement across ALL revisions
                var cashDelta = moves.Sum(s => (s.IsReturn ? -1m : 1m) * s.CashAmount);
                var expectedCash = openingFloat + salesCash - refundsCashAbs;

                // Card UI (same approach)
                var cardInAbs = moves.Where(s => !s.IsReturn).Sum(s => Math.Max(s.CardAmount, 0m));
                var cardOutAbs = moves.Where(s => s.IsReturn).Sum(s => Math.Abs(s.CardAmount));

                // Activity (latest docs)
                var salesCount = latestSales.Count;
                var returnsCount = latestReturns.Count;
                var docsCount = latest.Count;
                var lastTx = moves.OrderByDescending(s => s.Ts).FirstOrDefault()?.Ts.ToLocalTime();

                // Items (latest docs) – materialize before sum to avoid EF cast issues
                decimal itemsSoldQtyDec = 0m, itemsRetQtyDec = 0m;

                var saleIds = latestSales.Select(s => s.Id).ToHashSet();
                var returnIds = latestReturns.Select(s => s.Id).ToHashSet();

                if (saleIds.Count > 0)
                {
                    var soldList = db.SaleLines.AsNoTracking()
                        .Where(l => saleIds.Contains(l.SaleId))
                        .Select(l => l.Qty).ToList();
                    itemsSoldQtyDec = soldList.Sum(q => (decimal)q);
                }

                if (returnIds.Count > 0)
                {
                    var retList = db.SaleLines.AsNoTracking()
                        .Where(l => returnIds.Contains(l.SaleId))
                        .Select(l => l.Qty).ToList();
                    itemsRetQtyDec = Math.Abs(retList.Sum(q => (decimal)q));
                }

                var itemsNetQtyDec = itemsSoldQtyDec - itemsRetQtyDec;

                // Tax (latest docs); show refunded as positive magnitude
                var taxCollected = latestSales.Sum(s => s.TaxTotal);
                var taxRefunded = latestReturns.Sum(s => Math.Abs(s.TaxTotal));

                // === Fill UI ===
                SalesTotalText.Text = M(salesTotal);        // e.g., 1,770.00
                ReturnsTotalText.Text = M(returnsTotalAbs);   // e.g., 440.00 (positive)
                NetTotalText.Text = M(netTotal);          // e.g., 1,330.00

                OpeningFloatText.Text = M(openingFloat);
                CashInText.Text = M(salesCash);
                CashOutText.Text = M(refundsCashAbs);
                ExpectedCashText.Text = M(expectedCash);      // e.g., 1,330.00 for all-cash

                CardInText.Text = M(salesCard);
                CardOutText.Text = M(refundsCardAbs);

                SalesCountText.Text = salesCount.ToString("N0");
                ReturnsCountText.Text = returnsCount.ToString("N0");
                DocsCountText.Text = docsCount.ToString("N0");
                LastTxText.Text = lastTx?.ToString("dd-MMM-yyyy HH:mm") ?? "-";

                ItemsSoldQtyText.Text = itemsSoldQtyDec.ToString("N0");
                ItemsReturnedQtyText.Text = itemsRetQtyDec.ToString("N0");
                ItemsNetQtyText.Text = itemsNetQtyDec.ToString("N0");

                TaxCollectedText.Text = M(taxCollected);
                TaxRefundedText.Text = M(taxRefunded);
                // === Amendments & Voids (session-scoped) ===
                // An “amendment” = any doc that was superseded by a newer revision in this till-session.
                // We count superseded docs via RevisedToSaleId != null.
                // We exclude voided ones here (amend ≠ void).
                var salesAmendments = db.Sales.AsNoTracking()
                    .Count(s => s.TillSessionId == _tillId
                             && s.RevisedToSaleId != null
                             && s.Status == SaleStatus.Final
                             && s.VoidedAtUtc == null
                             && !s.IsReturn);

                var returnAmendments = db.Sales.AsNoTracking()
                    .Count(s => s.TillSessionId == _tillId
                             && s.RevisedToSaleId != null
                             && s.Status == SaleStatus.Final
                             && s.VoidedAtUtc == null
                             && s.IsReturn);

                // “Voids during session” = any invoice voided in this till-session (sales or returns)
                var voidsCount = db.Sales.AsNoTracking()
                    .Count(s => s.TillSessionId == _tillId
                             && s.VoidedAtUtc != null);

                // === Fill UI (add these three) ===
                SalesAmendmentsText.Text = salesAmendments.ToString("N0");
                ReturnAmendmentsText.Text = returnAmendments.ToString("N0");
                VoidsCountText.Text = voidsCount.ToString("N0");


            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load till summary:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }






        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadSummary();
    }
}
