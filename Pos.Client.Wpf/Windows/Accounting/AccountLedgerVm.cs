using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Persistence.Services;   // ILedgerQueryService, ILookupService
using Pos.Domain.Services;
using Pos.Client.Wpf.Infrastructure; // AppState for current outlet
using Pos.Client.Wpf.Services;   // <-- add this so AppState resolves


namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AccountLite : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private string code = "";
        [ObservableProperty] private string name = "";
        [ObservableProperty] private int? outletId;
    }

    public partial class AccountLedgerVm : ObservableObject
    {
        private readonly ILedgerQueryService _ledger;
        private readonly ILookupService _lookup;

        public ObservableCollection<AccountLite> Accounts { get; } = new();

        [ObservableProperty] private AccountLite? selectedAccount;
        [ObservableProperty] private DateTime fromDate = DateTime.UtcNow.Date.AddDays(-30);
        [ObservableProperty] private DateTime toDate = DateTime.UtcNow.Date;
        [ObservableProperty] private decimal opening;
        [ObservableProperty] private decimal closing;

        public ObservableCollection<CashBookRowVm> Rows { get; } = new();

        public IAsyncRelayCommand RefreshCmd { get; }

        public AccountLedgerVm(ILedgerQueryService ledger, ILookupService lookup)
        {
            _ledger = ledger;
            _lookup = lookup;
            RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        }

        public async Task LoadAsync()
        {
            Accounts.Clear();

            // Determine which outlet to scope to
            var (outletId, _) = AppCtx.GetOutletCounterOrThrow();
            // Load accounts for this outlet (or global null)
            var list = await _lookup.GetAccountsAsync(outletId);

            foreach (var a in list)
                Accounts.Add(new AccountLite
                {
                    Id = a.Id,
                    Code = a.Code,
                    Name = a.Name,
                    OutletId = a.OutletId
                });

            SelectedAccount ??= Accounts.FirstOrDefault();
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Rows.Clear();
            Opening = Closing = 0m;
            if (SelectedAccount == null) return;

            var (op, rows, cl) = await _ledger.GetAccountLedgerAsync(
                SelectedAccount.Id,
                FromDate,
                ToDate.AddDays(1).AddTicks(-1)
            );

            Opening = op;
            Closing = cl;

            foreach (var r in rows)
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
