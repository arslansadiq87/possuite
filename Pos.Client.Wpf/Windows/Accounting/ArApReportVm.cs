// Pos.Client.Wpf/Windows/Accounting/ArApReportVm.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
//using Pos.Client.Wpf.Services;
using Pos.Domain.Entities;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class ArApRowVm : ObservableObject
    {
        [ObservableProperty] private int partyId;
        [ObservableProperty] private string partyName = "";
        [ObservableProperty] private int? outletId;
        [ObservableProperty] private string? outletName;
        [ObservableProperty] private decimal balance;
    }

    public partial class ArApReportVm : ObservableObject
    {
        private readonly IArApQueryService _svc;
        private readonly IOutletService _outletSvc;
        //private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public ObservableCollection<Outlet> Outlets { get; } = new();
        public ObservableCollection<ArApRowVm> ArRows { get; } = new();
        public ObservableCollection<ArApRowVm> ApRows { get; } = new();

        [ObservableProperty] private Outlet? selectedOutlet;   // null => All outlets
        [ObservableProperty] private bool includeZero;         // default false
        [ObservableProperty] private decimal arTotal;
        [ObservableProperty] private decimal apTotal;

        public IAsyncRelayCommand RefreshCmd { get; }

        public ArApReportVm(IArApQueryService svc, IOutletService outletSvc)
        {
            _svc = svc;
            RefreshCmd = new AsyncRelayCommand(LoadAsync);
            _outletSvc = outletSvc;
        }

        public async Task InitAsync()
        {
            //using var db = await _dbf.CreateDbContextAsync();
            var outlets = await _outletSvc.GetAllAsync();
            //await db.Outlets.AsNoTracking().OrderBy(o => o.Name).ToListAsync();
            Outlets.Clear();
            foreach (var o in outlets) Outlets.Add(o);
            SelectedOutlet = null; // All
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            ArRows.Clear();
            ApRows.Clear();
            var outletId = SelectedOutlet?.Id;

            var ar = await _svc.GetAccountsReceivableAsync(outletId, IncludeZero);
            var ap = await _svc.GetAccountsPayableAsync(outletId, IncludeZero);

            foreach (var r in ar)
                ArRows.Add(new ArApRowVm { PartyId = r.PartyId, PartyName = r.PartyName, OutletId = r.OutletId, OutletName = r.OutletName, Balance = r.Balance });
            foreach (var r in ap)
                ApRows.Add(new ArApRowVm { PartyId = r.PartyId, PartyName = r.PartyName, OutletId = r.OutletId, OutletName = r.OutletName, Balance = r.Balance });

            ArTotal = ar.Sum(x => x.Balance);
            ApTotal = ap.Sum(x => x.Balance);
        }
    }
}
