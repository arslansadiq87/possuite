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
using System.Windows;

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

        [ObservableProperty] private DateTime voucherDate = DateTime.Today;
        [ObservableProperty] private VoucherLineVm? selectedLine;
        [ObservableProperty] private string memo = "";
        [ObservableProperty] private string refNo = "";
        [ObservableProperty] private string type = nameof(VoucherType.Journal);
        [ObservableProperty] private Outlet? selectedOutlet;

        public decimal TotalDebit => Lines.Sum(l => l.Debit);
        public decimal TotalCredit => Lines.Sum(l => l.Credit);

        // UI helpers
        public bool ShowDebitColumn => Type == nameof(VoucherType.Debit) || Type == nameof(VoucherType.Journal);
        public bool ShowCreditColumn => Type == nameof(VoucherType.Credit) || Type == nameof(VoucherType.Journal);
        public bool IsTypeChangeAllowed => Lines.Count == 0;
        public bool IsOutletSelectable => AuthZ.IsAdmin();

        // Journal must balance; Cash vouchers must be > 0 on their side
        public bool SaveEnabled
        {
            get
            {
                var vt = Enum.Parse<VoucherType>(Type);
                if (vt == VoucherType.Journal)
                    return Math.Abs(TotalDebit - TotalCredit) < 0.005m && TotalDebit > 0m;
                if (vt == VoucherType.Debit)
                    return TotalDebit > 0m && Lines.All(l => l.Debit > 0m && l.Credit == 0m && l.Account != null);
                if (vt == VoucherType.Credit)
                    return TotalCredit > 0m && Lines.All(l => l.Credit > 0m && l.Debit == 0m && l.Account != null);
                return false;
            }
        }

        private void RecalcUi()
        {
            OnPropertyChanged(nameof(TotalDebit));
            OnPropertyChanged(nameof(TotalCredit));
            OnPropertyChanged(nameof(ShowDebitColumn));
            OnPropertyChanged(nameof(ShowCreditColumn));
            OnPropertyChanged(nameof(IsTypeChangeAllowed));
            OnPropertyChanged(nameof(SaveEnabled));
        }

        public VoucherEditorVm(IDbContextFactory<PosClientDbContext> dbf, IGlPostingService gl)
        {
            _dbf = dbf; _gl = gl;

            Lines.CollectionChanged += (_, e) =>
            {
                if (e.OldItems != null)
                    foreach (VoucherLineVm vm in e.OldItems) vm.PropertyChanged -= Line_PropertyChanged;
                if (e.NewItems != null)
                    foreach (VoucherLineVm vm in e.NewItems) vm.PropertyChanged += Line_PropertyChanged;

                RecalcUi();
            };
        }

        private void Line_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VoucherLineVm.Debit)
                              or nameof(VoucherLineVm.Credit)
                              or nameof(VoucherLineVm.Account))
                RecalcUi();
        }

        [RelayCommand]
        public void DeleteLine(VoucherLineVm line)
        {
            Lines.Remove(line);
        }


        [RelayCommand]
        public async Task LoadAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();

            Accounts.Clear();
            foreach (var a in await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync())
                Accounts.Add(a);

            Outlets.Clear();
            var outlets = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            foreach (var o in outlets) Outlets.Add(o);

            // Preselect outlet
            if (!AuthZ.IsAdmin())
            {
                var oid = AppState.Current.CurrentOutletId;
                SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == oid) ?? outlets.FirstOrDefault();
            }
            else
            {
                SelectedOutlet = SelectedOutlet ?? outlets.FirstOrDefault();
            }

            RecalcUi();
        }


        [RelayCommand]
        public void AddLine()
        {
            var line = new VoucherLineVm();
            line.PropertyChanged += (_, __) => RecalcUi();  // <-- keep totals reactive
            Lines.Add(line);
            RecalcUi();
        }


        [RelayCommand]
        public void RemoveLine()
        {
            if (SelectedLine == null) return;
            Lines.Remove(SelectedLine);
            RecalcUi();
        }


        [RelayCommand]
        public async Task SaveAsync()
        {
            if (SelectedOutlet == null)
                throw new InvalidOperationException("Select an outlet.");

            var vt = Enum.Parse<VoucherType>(Type);

            // Validation per type
            if (vt == VoucherType.Journal)
            {
                if (Math.Abs(TotalDebit - TotalCredit) > 0.004m || TotalDebit <= 0m)
                    throw new InvalidOperationException("For Journal Voucher, Debits must equal Credits and be > 0.");
            }
            else if (vt == VoucherType.Debit)
            {
                if (!Lines.Any() || TotalDebit <= 0m || Lines.Any(l => l.Credit != 0m || l.Debit <= 0m || l.Account == null))
                    throw new InvalidOperationException("For Debit Voucher, enter Debit amounts only (and > 0) with accounts selected.");
            }
            else if (vt == VoucherType.Credit)
            {
                if (!Lines.Any() || TotalCredit <= 0m || Lines.Any(l => l.Debit != 0m || l.Credit <= 0m || l.Account == null))
                    throw new InvalidOperationException("For Credit Voucher, enter Credit amounts only (and > 0) with accounts selected.");
            }

            // Use chosen date as local, store UTC
            var local = DateTime.SpecifyKind(voucherDate.Date, DateTimeKind.Local);
            var tsUtc = local.ToUniversalTime();

            using var db = await _dbf.CreateDbContextAsync();
            var v = new Voucher
            {
                TsUtc = tsUtc,
                OutletId = SelectedOutlet.Id,
                RefNo = RefNo,
                Memo = Memo,
                Type = vt
            };
            db.Vouchers.Add(v);
            await db.SaveChangesAsync();

            foreach (var ln in Lines)
            {
                db.VoucherLines.Add(new VoucherLine
                {
                    VoucherId = v.Id,
                    AccountId = ln.Account!.Id,
                    Description = ln.Description,
                    Debit = ln.Debit,
                    Credit = ln.Credit
                });
            }
            await db.SaveChangesAsync();

            await _gl.PostVoucherAsync(v);

            // Done: close window if running as dialog, otherwise just clear
            try
            {
                if (Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this) is Window w)
                    w.DialogResult = true;
            }
            catch { /* ignore */ }
        }

    }
}
