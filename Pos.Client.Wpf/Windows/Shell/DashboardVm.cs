using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Shell
{
    public sealed class DashboardVm : INotifyPropertyChanged
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly AppState _st;

        public DashboardVm(IDbContextFactory<PosClientDbContext> dbf, AppState st)
        {
            _dbf = dbf; _st = st;
        }

        // Top strip bindings
        private string _outletName = "-";
        public string OutletName { get => _outletName; set { _outletName = value; OnPropertyChanged(); } }

        private string _counterName = "-";
        public string CounterName { get => _counterName; set { _counterName = value; OnPropertyChanged(); } }

        public string TillStatus { get; private set; } = "Till: —";
        public string OnlineText => "Offline";     // wire later with sync
        public string LastSyncText => "Last sync: —";

        public string CurrentUserName => _st.CurrentUserName;
        public int CurrentOutletId => _st.CurrentOutletId;
        public int CurrentCounterId => _st.CurrentCounterId;

        // KPIs (keep simple for now)
        public string TodaySalesFormatted => "₨ 0";
        public string TodaySalesInvoicesText => "0 invoices";
        public string TodayReturnsFormatted => "₨ 0";
        public string TodayReturnsCountText => "0 returns";
        public string ShiftCashFormatted => "₨ 0";
        public string ShiftHint => "—";

        // Tables (bind later if you want)
        public object RecentInvoices { get; } = null;
        public object LowStockItems { get; } = null;

        public async Task RefreshAsync()
        {
            // load names from DB using the IDs in AppState
            using var db = await _dbf.CreateDbContextAsync();

            if (_st.CurrentOutletId > 0)
            {
                var outlet = await db.Outlets.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == _st.CurrentOutletId);
                OutletName = outlet?.Name ?? $"Outlet #{_st.CurrentOutletId}";
            }
            else
            {
                OutletName = "-";
            }

            if (_st.CurrentCounterId > 0)
            {
                var counter = await db.Counters.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == _st.CurrentCounterId);
                CounterName = counter?.Name ?? $"Counter #{_st.CurrentCounterId}";
            }
            else
            {
                CounterName = "-";
            }

            TillStatus = "Till: —"; // TODO: set to "Till: Open"/"Till: Closed" from TillSessions
            OnPropertyChanged(nameof(TillStatus));

            OnPropertyChanged(nameof(CurrentUserName));

            // (You can extend here to fill KPIs and lists.)
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
