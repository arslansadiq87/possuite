using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Accounting;     // keep this if OpeningBalance* classes are here
using Pos.Domain.Entities;       // <-- add this for Account
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public sealed partial class ObLineVm : ObservableObject
    {
        [ObservableProperty] private int accountId;
        [ObservableProperty] private string accountCode = "";
        [ObservableProperty] private string accountName = "";
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
    }

    public partial class OpeningBalanceVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly OpeningBalanceService _svc;

        private int _docId; // 0 for new (draft not yet saved)

        public ObservableCollection<ObLineVm> Lines { get; } = new();
        public ObservableCollection<Account> Accounts { get; } = new();

        [ObservableProperty] private DateTime asOfDate = DateTime.Today;
        [ObservableProperty] private int? outletId = null; // keep null for company-wide unless you scope by outlet
        [ObservableProperty] private string? memo = "Opening Balance";
        [ObservableProperty] private bool isPosted;

        public decimal TotalDebit => Lines.Sum(x => x.Debit);
        public decimal TotalCredit => Lines.Sum(x => x.Credit);
        public decimal Difference => TotalDebit - TotalCredit;

        public OpeningBalanceVm(IDbContextFactory<PosClientDbContext> dbf, OpeningBalanceService svc, int existingDocId = 0)
        {
            _dbf = dbf;
            _svc = svc;
            _docId = existingDocId;

            Lines.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(TotalDebit));
                OnPropertyChanged(nameof(TotalCredit));
                OnPropertyChanged(nameof(Difference));
                SaveCommand.NotifyCanExecuteChanged();
                PostCommand.NotifyCanExecuteChanged();
            };


        }

        // --------- Commands ----------

        [RelayCommand]
        public async Task LoadAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();

            Accounts.Clear();
            var accts = await db.Accounts
                .AsNoTracking()
                .Where(a => a.IsActive)
                .OrderBy(a => a.Code)
                .ToListAsync();
            foreach (var a in accts) Accounts.Add(a);

            if (_docId == 0)
            {
                // new draft
                Lines.Clear();
                AddLineInternal(); // start with one empty row
                IsPosted = false;
                return;
            }

            // load existing
            var ob = await db.OpeningBalances
                             .Include(d => d.Lines)
                             .AsNoTracking()
                             .FirstOrDefaultAsync(d => d.Id == _docId);
            if (ob == null)
            {
                MessageBox.Show($"Opening Balance #{_docId} not found.", "OB");
                _docId = 0;
                Lines.Clear();
                AddLineInternal();
                return;
            }

            AsOfDate = ob.AsOfDate;
            OutletId = ob.OutletId;
            Memo = ob.Memo;
            IsPosted = ob.IsPosted;

            Lines.Clear();
            foreach (var ln in ob.Lines.OrderBy(l => l.Id))
            {
                var a = Accounts.FirstOrDefault(x => x.Id == ln.AccountId);
                Lines.Add(new ObLineVm
                {
                    AccountId = ln.AccountId,
                    AccountCode = a?.Code ?? "",
                    AccountName = a?.Name ?? "",
                    Debit = ln.Debit,
                    Credit = ln.Credit
                });
            }
            if (Lines.Count == 0) AddLineInternal();
        }

        [RelayCommand]
        public void AddLine() => AddLineInternal();

        private void AddLineInternal() => Lines.Add(new ObLineVm());

        [RelayCommand(CanExecute = nameof(CanRemoveLine))]
        public void RemoveLine(ObLineVm? line)
        {
            if (line is null) return;
            Lines.Remove(line);
        }

        private bool CanRemoveLine(ObLineVm? line) => line != null;

        [RelayCommand(CanExecute = nameof(CanSave))]
        public async Task SaveAsync()
        {
            if (!ValidateLines()) return;

            using var db = await _dbf.CreateDbContextAsync();
            if (_docId == 0)
            {
                var doc = new OpeningBalance
                {
                    AsOfDate = AsOfDate,
                    OutletId = OutletId,
                    Memo = Memo,
                    IsPosted = false
                };
                db.OpeningBalances.Add(doc);
                await db.SaveChangesAsync();
                _docId = doc.Id;

                foreach (var vm in Lines.Where(LnHasAmount))
                {
                    db.OpeningBalanceLines.Add(new OpeningBalanceLine
                    {
                        OpeningBalanceId = _docId,
                        AccountId = vm.AccountId,
                        Debit = vm.Debit,
                        Credit = vm.Credit
                    });
                }
                await db.SaveChangesAsync();
            }
            else
            {
                var doc = await db.OpeningBalances
                                  .Include(d => d.Lines)
                                  .FirstAsync(d => d.Id == _docId);
                if (doc.IsPosted)
                {
                    MessageBox.Show("Already posted. You cannot edit a posted opening balance.", "OB");
                    return;
                }

                doc.AsOfDate = AsOfDate;
                doc.OutletId = OutletId;
                doc.Memo = Memo;

                // Replace lines
                db.OpeningBalanceLines.RemoveRange(doc.Lines);
                foreach (var vm in Lines.Where(LnHasAmount))
                {
                    db.OpeningBalanceLines.Add(new OpeningBalanceLine
                    {
                        OpeningBalanceId = _docId,
                        AccountId = vm.AccountId,
                        Debit = vm.Debit,
                        Credit = vm.Credit
                    });
                }
                await db.SaveChangesAsync();
            }

            MessageBox.Show("Opening Balance saved.", "OB");
        }

        

        private bool CanSave() => !IsPosted && Lines.Any(LnHasAmount);

        [RelayCommand(CanExecute = nameof(CanPost))]
        public async Task PostAsync()
        {
            if (!ValidateLines()) return;

            // Save first to get persisted lines
            await SaveAsync();

            await _svc.PostOpeningAsync(_docId);
            IsPosted = true;
            MessageBox.Show("Opening Balance posted to GL.", "OB");
            SaveCommand.NotifyCanExecuteChanged();
            PostCommand.NotifyCanExecuteChanged();
        }

        private bool CanPost() =>
            !IsPosted &&
            Lines.Any(LnHasAmount) &&
            AsOfDate != default;

        // ----- Helpers -----

        private static bool LnHasAmount(ObLineVm vm) => (vm.Debit > 0m) ^ (vm.Credit > 0m); // only one side

        private bool ValidateLines()
        {
            // Basic validations
            if (AsOfDate == default)
            {
                MessageBox.Show("Please set the 'As of' date.", "OB");
                return false;
            }
            foreach (var vm in Lines)
            {
                if (vm.AccountId <= 0 && (vm.Debit != 0m || vm.Credit != 0m))
                {
                    MessageBox.Show("Every line with an amount must have an Account selected.", "OB");
                    return false;
                }
                if (vm.Debit < 0 || vm.Credit < 0)
                {
                    MessageBox.Show("Debit/Credit cannot be negative.", "OB");
                    return false;
                }
                if (vm.Debit > 0m && vm.Credit > 0m)
                {
                    MessageBox.Show("A line cannot have both Debit and Credit.", "OB");
                    return false;
                }
            }
            return true;
        }

        // Called by ComboBox selection in XAML to set Account fields on the line
        public void AssignAccountToLine(ObLineVm line, Pos.Domain.Entities.Account? account)
        {
            if (line is null) return;
            if (account is null)
            {
                line.AccountId = 0;
                line.AccountCode = "";
                line.AccountName = "";
                return;
            }

            line.AccountId = account.Id;
            line.AccountCode = account.Code;
            line.AccountName = account.Name;
        }
    }
}
