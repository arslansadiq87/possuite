using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherLineVm : ObservableObject
    {
        [ObservableProperty] private Account? account;
        [ObservableProperty] private string? description;
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
    }

    public partial class VoucherEditorVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;

        public ObservableCollection<Account> Accounts { get; } = new();
        public ObservableCollection<Outlet> Outlets { get; } = new();
        public ObservableCollection<VoucherLineVm> Lines { get; } = new();

        public string[] VoucherTypes { get; } = Enum.GetNames(typeof(VoucherType));

        [ObservableProperty] private VoucherLineVm? selectedLine;
        [ObservableProperty] private string memo = "";
        [ObservableProperty] private string refNo = "";
        [ObservableProperty] private string type = nameof(VoucherType.Journal);
        [ObservableProperty] private Outlet? selectedOutlet;

        public decimal TotalDebit => Lines.Sum(l => l.Debit);
        public decimal TotalCredit => Lines.Sum(l => l.Credit);

        public VoucherEditorVm(IDbContextFactory<PosClientDbContext> dbf, IGlPostingService gl)
        {
            _dbf = dbf; _gl = gl;
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();
            Accounts.Clear();
            foreach (var a in await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync()) Accounts.Add(a);
            Outlets.Clear();
            foreach (var o in await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync()) Outlets.Add(o);
        }

        [RelayCommand]
        public void AddLine()
        {
            Lines.Add(new VoucherLineVm());
            OnPropertyChanged(nameof(TotalDebit));
            OnPropertyChanged(nameof(TotalCredit));
        }

        [RelayCommand]
        public void RemoveLine()
        {
            if (SelectedLine == null) return;
            Lines.Remove(SelectedLine);
            OnPropertyChanged(nameof(TotalDebit));
            OnPropertyChanged(nameof(TotalCredit));
        }

        [RelayCommand]
        public async Task SaveAsync()
        {
            if (Math.Round(TotalDebit - TotalCredit, 2) != 0m)
                throw new InvalidOperationException("Debits must equal credits.");

            using var db = await _dbf.CreateDbContextAsync();
            var v = new Voucher
            {
                TsUtc = DateTime.UtcNow,
                OutletId = SelectedOutlet?.Id,
                RefNo = RefNo,
                Memo = Memo,
                Type = Enum.Parse<VoucherType>(Type)
            };
            db.Vouchers.Add(v);
            await db.SaveChangesAsync();

            foreach (var ln in Lines)
            {
                if (ln.Account == null) continue;
                db.VoucherLines.Add(new VoucherLine
                {
                    VoucherId = v.Id,
                    AccountId = ln.Account.Id,
                    Description = ln.Description,
                    Debit = ln.Debit,
                    Credit = ln.Credit
                });
            }
            await db.SaveChangesAsync();

            await _gl.PostVoucherAsync(v);
        }
    }
}
