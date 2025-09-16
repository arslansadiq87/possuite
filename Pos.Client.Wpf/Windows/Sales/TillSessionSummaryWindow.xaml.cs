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
            using var db = new PosClientDbContext(_opts);

            var till = db.TillSessions.AsNoTracking().FirstOrDefault(t => t.Id == _tillId);
            if (till == null) { MessageBox.Show("Till session not found."); Close(); return; }

            OutletText.Text = _outletId.ToString();
            CounterText.Text = _counterId.ToString();
            TillIdText.Text = till.Id.ToString();
            OpenedText.Text = till.OpenTs.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");
            DurationText.Text = (DateTime.UtcNow - till.OpenTs).ToString(@"hh\:mm");

            // All FINAL documents for this till
            var all = db.Sales.AsNoTracking()
                .Where(s => s.TillSessionId == _tillId && s.Status == SaleStatus.Final)
                .ToList();

            var sales = all.Where(s => !s.IsReturn).ToList();
            var returns = all.Where(s => s.IsReturn).ToList();

            // Money
            var salesTotal = sales.Sum(s => s.Total);
            var returnsTotal = returns.Sum(s => s.Total);   // positive magnitudes (your return flow saved header totals positive)
            var netTotal = salesTotal - returnsTotal;

            var cashIn = sales.Sum(s => s.CashAmount);
            var cashOut = returns.Sum(s => s.CashAmount);     // cash refunds
            var openingFloat = till.OpeningFloat;
            var expectedCash = openingFloat + cashIn - cashOut;

            var cardIn = sales.Sum(s => s.CardAmount);
            var cardOut = returns.Sum(s => s.CardAmount);

            // Activity
            var salesCount = sales.Count;
            var returnsCount = returns.Count;
            var docsCount = all.Count;
            var lastTx = all.OrderByDescending(s => s.Ts).FirstOrDefault()?.Ts.ToLocalTime();

            // Items (qty): sale lines are positive, return lines are negative
            var saleIds = sales.Select(s => s.Id).ToHashSet();
            var returnIds = returns.Select(s => s.Id).ToHashSet();

            int itemsSoldQty = 0;
            int itemsRetQty = 0;

            if (saleIds.Count > 0)
                itemsSoldQty = db.SaleLines.AsNoTracking().Where(l => saleIds.Contains(l.SaleId))
                    .Select(l => (int?)l.Qty).Sum() ?? 0;

            if (returnIds.Count > 0)
            {
                var retNeg = db.SaleLines.AsNoTracking().Where(l => returnIds.Contains(l.SaleId))
                    .Select(l => (int?)l.Qty).Sum() ?? 0;
                itemsRetQty = Math.Abs(retNeg);
            }

            var itemsNetQty = itemsSoldQty - itemsRetQty;

            // Tax
            var taxCollected = sales.Sum(s => s.TaxTotal);
            var taxRefunded = returns.Sum(s => s.TaxTotal);

            // Fill UI
            SalesTotalText.Text = M(salesTotal);
            ReturnsTotalText.Text = M(returnsTotal);
            NetTotalText.Text = M(netTotal);

            OpeningFloatText.Text = M(openingFloat);
            CashInText.Text = M(cashIn);
            CashOutText.Text = M(cashOut);
            ExpectedCashText.Text = M(expectedCash);

            CardInText.Text = M(cardIn);
            CardOutText.Text = M(cardOut);

            SalesCountText.Text = salesCount.ToString("N0");
            ReturnsCountText.Text = returnsCount.ToString("N0");
            DocsCountText.Text = docsCount.ToString("N0");
            LastTxText.Text = lastTx?.ToString("dd-MMM-yyyy HH:mm") ?? "-";

            ItemsSoldQtyText.Text = itemsSoldQty.ToString("N0");
            ItemsReturnedQtyText.Text = itemsRetQty.ToString("N0");
            ItemsNetQtyText.Text = itemsNetQty.ToString("N0");

            TaxCollectedText.Text = M(taxCollected);
            TaxRefundedText.Text = M(taxRefunded);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadSummary();
    }
}
