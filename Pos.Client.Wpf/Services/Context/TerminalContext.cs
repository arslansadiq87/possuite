using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface ITerminalContext
    {
        int OutletId { get; }
        int CounterId { get; }

        string OutletName { get; }
        string CounterName { get; }
    }


    public sealed class TerminalContext : ITerminalContext
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public TerminalContext(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        public int OutletId => AppState.Current?.CurrentOutletId ?? 1;
        public int CounterId => AppState.Current?.CurrentCounterId ?? 1;

        private string? _outletName;
        private string? _counterName;

        public string OutletName
        {
            get
            {
                if (_outletName != null) return _outletName;

                using var db = _dbf.CreateDbContext();
                _outletName = db.Outlets.AsNoTracking()
                    .Where(o => o.Id == OutletId)
                    .Select(o => o.Name)
                    .FirstOrDefault() ?? "(Unknown Outlet)";
                return _outletName;
            }
        }

        public string CounterName
        {
            get
            {
                if (_counterName != null) return _counterName;

                using var db = _dbf.CreateDbContext();
                _counterName = db.Counters.AsNoTracking()
                    .Where(c => c.Id == CounterId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "(Counter)";
                return _counterName;
            }
        }
    }

}
