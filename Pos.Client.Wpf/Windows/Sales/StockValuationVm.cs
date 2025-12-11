// Pos.Client.Wpf/Windows/Sales/StockValuationVm.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Client.Wpf.Services;           // AppState.Current
using Pos.Domain.Services;               // IStockValuationReadService
using Pos.Domain.Models.Reports;         // (service DTOs)

namespace Pos.Client.Wpf.Windows.Sales
{
    public enum StockValuationMode   // <-- must be public
    {
        Cost = 0,
        Sale = 1
    }

    public partial class StockValuationRowVm : ObservableObject
    {
        public string Sku { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal OnHand { get; set; }
        public decimal UnitCost { get; set; }     // service provides average cost (Cost mode)
        public decimal UnitPrice { get; set; }    // service provides sale price  (Sale mode)
        public decimal TotalCost { get; set; }    // OnHand * UnitCost
        public decimal TotalPrice { get; set; }   // OnHand * UnitPrice
    }

    public partial class StockValuationVm : ObservableObject
    {
        private readonly IStockValuationReadService _svc;

        public ObservableCollection<StockValuationRowVm> Rows { get; } = new();

        [ObservableProperty] private StockValuationMode _mode = StockValuationMode.Cost;
        [ObservableProperty] private DateTime _asOf = DateTime.Today;

        [ObservableProperty] private decimal _sumQty;
        [ObservableProperty] private decimal _sumCost;
        [ObservableProperty] private decimal _sumPrice;

        private CancellationTokenSource? _refreshCts;

        public StockValuationVm(IStockValuationReadService svc)
        {
            _svc = svc;
        }

        // Auto-refresh on Mode change
        partial void OnModeChanged(StockValuationMode oldValue, StockValuationMode newValue)
            => _ = RefreshAsync();

        // Auto-refresh on AsOf change
        partial void OnAsOfChanged(DateTime oldValue, DateTime newValue)
            => _ = RefreshAsync();

        [RelayCommand]
        public async Task RefreshAsync()
        {
            _refreshCts?.Cancel();
            _refreshCts = new CancellationTokenSource();
            var ct = _refreshCts.Token;

            try
            {
                Rows.Clear();
                ResetTotals();

                // Inclusive end-of-day in LOCAL -> UTC (service should compare <= cutoffUtc)
                var localEnd = AsOf.Date.AddDays(1).AddTicks(-1);
                var cutoffUtc = DateTime.SpecifyKind(localEnd, DateTimeKind.Local).ToUniversalTime();

                // If you want “all outlets”, set outletId = null; otherwise use current outlet
                int outletId = AppState.Current.CurrentOutletId;

                var data = _mode == StockValuationMode.Cost
                    ? await _svc.GetCostViewAsync(outletId, cutoffUtc, ct)
                    : await _svc.GetSaleViewAsync(outletId, cutoffUtc, ct);

                foreach (var r in data)
                {
                    // r.UnitCost and r.UnitPrice come from the service; when in Cost mode
                    // UnitPrice may be informational only (still show totals).
                    var row = new StockValuationRowVm
                    {
                        Sku = r.Sku,
                        DisplayName = r.DisplayName,
                        Brand = r.Brand,
                        Category = r.Category,
                        OnHand = r.OnHand,
                        UnitCost = r.UnitCost,
                        UnitPrice = r.UnitPrice,
                        TotalCost = Math.Round(r.OnHand * r.UnitCost, 2, MidpointRounding.AwayFromZero),
                        TotalPrice = Math.Round(r.OnHand * r.UnitPrice, 2, MidpointRounding.AwayFromZero)
                    };
                    Rows.Add(row);
                }

                SumQty = Rows.Sum(x => x.OnHand);
                SumCost = Rows.Sum(x => x.TotalCost);
                SumPrice = Rows.Sum(x => x.TotalPrice);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                // Keep it visible during integration; you can route to a logger later
                System.Windows.MessageBox.Show(ex.ToString(), "Stock Valuation Error");
                throw;
            }
        }

        private void ResetTotals()
        {
            SumQty = 0m;
            SumCost = 0m;
            SumPrice = 0m;
        }
    }
}
