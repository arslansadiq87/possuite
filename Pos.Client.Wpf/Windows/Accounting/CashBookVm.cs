// Pos.Client.Wpf/Windows/Accounting/CashBookVm.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;                             // ← add
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Infrastructure;           // ← add (AuthZ, AppState)
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Domain.Models.Accounting;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class CashBookRowVm : ObservableObject
    {
        [ObservableProperty] private DateTime tsUtc;
        [ObservableProperty] private string? memo;
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
        [ObservableProperty] private decimal running;
        [ObservableProperty] private string? sourceRef;
        [ObservableProperty] private bool isVoided;
        [ObservableProperty] private int? tillId;
    }

    public partial class CashBookVm : ObservableObject
    {
        private readonly ILedgerQueryService _ledger;
        private readonly IOutletService _outlets;

        [ObservableProperty] private bool includeVoided;
        [ObservableProperty] private CashBookScope scope = CashBookScope.HandOnly;

        public ObservableCollection<Outlet> OutletChoices { get; } = new();
        [ObservableProperty] private Outlet? selectedOutlet;

        [ObservableProperty] private DateTime fromDate = DateTime.UtcNow.Date.AddDays(-7);
        [ObservableProperty] private DateTime toDate = DateTime.UtcNow.Date;

        [ObservableProperty] private decimal opening;
        [ObservableProperty] private decimal closing;

        public ObservableCollection<CashBookRowVm> Rows { get; } = new();

        public IAsyncRelayCommand RefreshCmd { get; }

        public CashBookVm(ILedgerQueryService ledger, IOutletService outlets)
        {
            _ledger = ledger;
            _outlets = outlets;
            RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        }

        public async Task LoadAsync()
        {
            OutletChoices.Clear();

            var all = await _outlets.GetAllAsync();

            if (AuthZ.IsAdmin())
            {
                foreach (var o in all) OutletChoices.Add(o);
                SelectedOutlet ??= OutletChoices.FirstOrDefault();
            }
            else
            {
                var myOutletId = AppState.Current.CurrentOutletId;
                var mine = all.Where(o => o.Id == myOutletId).ToList();

                foreach (var o in mine) OutletChoices.Add(o);
                SelectedOutlet = OutletChoices.FirstOrDefault();
            }

            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Rows.Clear();
            Opening = Closing = 0m;
            if (SelectedOutlet == null) return;

            try
            {
                // Use a temp variable instead of tuple deconstruction to avoid CS8130
                var result = await _ledger.GetCashBookAsync(
                    SelectedOutlet.Id,
                    FromDate,
                    ToDate.AddDays(1).AddTicks(-1),
                    IncludeVoided,
                    Scope);

                Opening = result.opening;
                Closing = result.closing;

                foreach (var r in result.rows)
                {
                    Rows.Add(new CashBookRowVm
                    {
                        TsUtc = r.TsUtc,
                        Memo = r.Memo,
                        Debit = r.Debit,
                        Credit = r.Credit,
                        Running = r.Running,
                        SourceRef = r.SourceRef,
                        IsVoided = r.IsVoided,
                        TillId = r.TillId
                    });
                }
                return;
            }
            catch (NotImplementedException)
            {
                // fall through to legacy path
            }
            catch
            {
                // fall through to legacy path if the new method is not yet wired
            }

            // LEGACY PATH (Cash-in-Hand only)
            var cashId = await _ledger.GetOutletCashAccountIdAsync(SelectedOutlet.Id);
            var legacy = await _ledger.GetAccountLedgerAsync(
                cashId, FromDate, ToDate.AddDays(1).AddTicks(-1));

            Opening = legacy.opening;
            Closing = legacy.closing;

            foreach (var r in legacy.rows)
            {
                Rows.Add(new CashBookRowVm
                {
                    TsUtc = r.TsUtc,
                    Memo = r.Memo,
                    Debit = r.Debit,
                    Credit = r.Credit,
                    Running = r.Running
                });
            }
        }
    }
}
