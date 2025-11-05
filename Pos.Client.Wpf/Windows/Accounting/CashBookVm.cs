using System;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Client.Wpf.Services;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class CashBookRowVm : ObservableObject
    {
        [ObservableProperty] private DateTime tsUtc;
        [ObservableProperty] private string? memo;
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
        [ObservableProperty] private decimal running;
        // NEW:
        [ObservableProperty] private string? sourceRef;   // e.g., "PO #1023 · Till T-03" or "Voucher PMT-45"
        [ObservableProperty] private bool isVoided;
        [ObservableProperty] private int? tillId;
    }

    public partial class CashBookVm : ObservableObject
    {
        private readonly ILedgerQueryService _ledger;
        private readonly IOutletService _outlets;
        [ObservableProperty] private bool includeVoided;  // bound to checkbox
        [ObservableProperty] private CashBookScope scope = CashBookScope.HandOnly; // NEW

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

                OutletChoices.Clear();
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

            // Prefer a richer API for cash book rows if available
            try
            {
                // NEW: use outlet directly so we can attach till/source/void filters
                //var (op2, rows2, cl2) = await _ledger.GetCashBookAsync(
                //    SelectedOutlet.Id,
                //    FromDate,
                //    ToDate.AddDays(1).AddTicks(-1),
                //    IncludeVoided);

                var (op2, rows2, cl2) = await _ledger.GetCashBookAsync(
                    SelectedOutlet.Id,
                    FromDate,
                    ToDate.AddDays(1).AddTicks(-1),
                    IncludeVoided,
                    Scope); // NEW


                Opening = op2; Closing = cl2;

                foreach (var r in rows2)
                {
                    Rows.Add(new CashBookRowVm
                    {
                        TsUtc = r.TsUtc,
                        Memo = r.Memo,
                        Debit = r.Debit,
                        Credit = r.Credit,
                        Running = r.Running,
                        SourceRef = r.SourceRef,  // "PO #x", "Sale #x", "Voucher PMT-xx", optionally " · Till T-yy"
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

            // LEGACY PATH (keeps your current behavior; no void/source info)
            var cashId = await _ledger.GetOutletCashAccountIdAsync(SelectedOutlet.Id);
            var (op, rows, cl) = await _ledger.GetAccountLedgerAsync(
                cashId, FromDate, ToDate.AddDays(1).AddTicks(-1));

            Opening = op; Closing = cl;

            foreach (var r in rows)
                Rows.Add(new CashBookRowVm
                {
                    TsUtc = r.TsUtc,
                    Memo = r.Memo,
                    Debit = r.Debit,
                    Credit = r.Credit,
                    Running = r.Running,
                    // SourceRef/IsVoided/TillId unavailable in legacy path
                });
        }

    }
}
