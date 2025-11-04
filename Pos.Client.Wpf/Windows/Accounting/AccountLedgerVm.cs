using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Accounting;
using Pos.Persistence;

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
        //private readonly PosClientDbContext _db;
        private readonly ILedgerQueryService _ledger;

        public ObservableCollection<AccountLite> Accounts { get; } = new();

        [ObservableProperty] private AccountLite? selectedAccount;
        [ObservableProperty] private DateTime fromDate = DateTime.UtcNow.Date.AddDays(-30);
        [ObservableProperty] private DateTime toDate = DateTime.UtcNow.Date;

        [ObservableProperty] private decimal opening;
        [ObservableProperty] private decimal closing;

        public ObservableCollection<CashBookRowVm> Rows { get; } = new();

        public IAsyncRelayCommand RefreshCmd { get; }

        //public AccountLedgerVm(PosClientDbContext db, ILedgerQueryService ledger)
        //{
        //    _db = db;
        //    _ledger = ledger;
        //    RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        //}
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public AccountLedgerVm(IDbContextFactory<PosClientDbContext> dbf, ILedgerQueryService ledger)
        {
            _dbf = dbf;
            _ledger = ledger;
            RefreshCmd = new AsyncRelayCommand(RefreshAsync);
        }
        public async Task LoadAsync()
        {
            Accounts.Clear();
            using var db = await _dbf.CreateDbContextAsync();
            var list = await db.Accounts.AsNoTracking()
                .OrderBy(a => a.Code).ThenBy(a => a.Name)
                .Select(a => new AccountLite { Id = a.Id, Code = a.Code, Name = a.Name, OutletId = a.OutletId })
                .ToListAsync();

            foreach (var a in list) Accounts.Add(a);
            SelectedAccount ??= Accounts.FirstOrDefault();
            await RefreshAsync();
        }


        private async Task RefreshAsync()
        {
            Rows.Clear();
            Opening = Closing = 0m;
            if (SelectedAccount == null) return;

            var (op, rows, cl) = await _ledger.GetAccountLedgerAsync(
                SelectedAccount.Id, FromDate, ToDate.AddDays(1).AddTicks(-1));

            Opening = op; Closing = cl;

            foreach (var r in rows)
                Rows.Add(new CashBookRowVm { TsUtc = r.TsUtc, Memo = r.Memo, Debit = r.Debit, Credit = r.Credit, Running = r.Running });
        }
    }
}
