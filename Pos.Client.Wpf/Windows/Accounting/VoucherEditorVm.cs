using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;            // AuthZ, AppState
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Persistence.Services;           // ILookupService, IVoucherEditorService
using System.Windows;
using Pos.Domain.Services;
using Pos.Domain.Models.Accounting;

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
        private readonly IVoucherCenterService _editor;
        private readonly ILookupService _lookup;
        private readonly IServiceProvider _sp;

        // null => creating new; value => editing this voucher
        private int? _editingVoucherId = null;

        public bool WasSaved { get; private set; } = false;
        public event Action<bool>? CloseRequested;

        public ObservableCollection<Account> Accounts { get; } = new();
        public ObservableCollection<Outlet> Outlets { get; } = new();
        public ObservableCollection<VoucherLineVm> Lines { get; } = new();

        public string[] VoucherTypes { get; } = Enum.GetNames(typeof(VoucherType));

        [ObservableProperty] private DateTime voucherDate = DateTime.Today;
        [ObservableProperty] private VoucherLineVm? selectedLine;
        [ObservableProperty] private string memo = "";
        [ObservableProperty] private string refNo = "";
        [ObservableProperty] private string type = nameof(VoucherType.Debit);
        [ObservableProperty] private Outlet? selectedOutlet;

        public decimal TotalDebit => Lines.Sum(l => l.Debit);
        public decimal TotalCredit => Lines.Sum(l => l.Credit);

        public bool ShowDebitColumn => Type == nameof(VoucherType.Debit) || Type == nameof(VoucherType.Journal);
        public bool ShowCreditColumn => Type == nameof(VoucherType.Credit) || Type == nameof(VoucherType.Journal);

        public event Action? AccountsReloadRequested;

        public VoucherEditorVm(IVoucherCenterService editor, ILookupService lookup, IServiceProvider sp)
        {
            _editor = editor;
            _lookup = lookup;
            _sp = sp;

            Lines.CollectionChanged += (_, e) =>
            {
                if (e.OldItems != null)
                    foreach (VoucherLineVm vm in e.OldItems) vm.PropertyChanged -= Line_PropertyChanged;
                if (e.NewItems != null)
                    foreach (VoucherLineVm vm in e.NewItems) vm.PropertyChanged += Line_PropertyChanged;
                RecalcUi();
            };
        }

        public async Task ReloadAccountsAsync()
        {
            Accounts.Clear();
            // pass null => all/global; or pass AppState.Current.CurrentOutletId if you want to scope
            var accs = await _lookup.GetAccountsAsync(null);
            foreach (var a in accs.OrderBy(a => a.Code))
                Accounts.Add(a);
        }

        public bool IsTypeChangeAllowed =>
            Lines.Count == 0 ||
            Lines.All(l =>
                l.Account == null &&
                l.Debit == 0m &&
                l.Credit == 0m &&
                string.IsNullOrWhiteSpace(l.Description));

        public bool IsOutletSelectable => AuthZ.IsAdmin();

        public bool SaveEnabled
        {
            get
            {
                if (SelectedOutlet == null) return false;
                var vt = Enum.Parse<VoucherType>(Type);

                if (vt == VoucherType.Debit)
                    return Lines.Any(l => l.Account != null && l.Debit > 0m);

                if (vt == VoucherType.Credit)
                    return Lines.Any(l => l.Account != null && l.Credit > 0m);

                return Lines.Any(l => l.Account != null && (l.Debit > 0m || l.Credit > 0m));
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

        partial void OnTypeChanged(string value) => RecalcUi();

        // ---------- Commands / Lifecycle ----------

        [RelayCommand]
        public void Clear()
        {
            _editingVoucherId = null;
            VoucherDate = DateTime.Today;
            RefNo = "";
            Memo = "";

            Lines.Clear();
            AddLine();

            // outlet selection based on role
            if (!AuthZ.IsAdmin())
            {
                var oid = AppState.Current.CurrentOutletId;
                SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == oid) ?? Outlets.FirstOrDefault();
            }
            else if (SelectedOutlet == null && Outlets.Count > 0)
            {
                SelectedOutlet = Outlets[0];
            }

            RecalcUi();
            AccountsReloadRequested?.Invoke();
        }

        [RelayCommand(CanExecute = nameof(CanDeleteLine))]
        public void DeleteLine(object? parameter)
        {
            var line = parameter as VoucherLineVm ?? SelectedLine;
            if (line == null) return;

            if (Lines.Count <= 1)
            {
                ClearLine(Lines[0]);
                SelectedLine = Lines[0];
                RecalcUi();
                return;
            }

            Lines.Remove(line);
            if (Lines.Count == 0) AddLine();
            RecalcUi();
        }

        private bool CanDeleteLine(object? parameter) => parameter is VoucherLineVm || SelectedLine is VoucherLineVm;

        private static void ClearLine(VoucherLineVm l)
        {
            l.Account = null;
            l.Description = "";
            l.Debit = 0m;
            l.Credit = 0m;
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            // lookups
            Accounts.Clear();
            foreach (var a in (await _lookup.GetAccountsAsync(null)).OrderBy(a => a.Code))
                Accounts.Add(a);

            Outlets.Clear();
            foreach (var o in (await _lookup.GetOutletsAsync()).OrderBy(o => o.Name))
                Outlets.Add(o);

            // preselect outlet
            if (!AuthZ.IsAdmin())
            {
                var oid = AppState.Current.CurrentOutletId;
                SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == oid) ?? Outlets.FirstOrDefault();
            }
            else
            {
                SelectedOutlet = SelectedOutlet ?? Outlets.FirstOrDefault();
            }

            if (Lines.Count == 0) AddLine();
            RecalcUi();
        }

        private void Line_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(VoucherLineVm.Debit)
                              or nameof(VoucherLineVm.Credit)
                              or nameof(VoucherLineVm.Account))
                RecalcUi();
        }


        public async Task LoadAsync(int voucherId)
        {
            if (Accounts.Count == 0 || Outlets.Count == 0)
                await LoadAsync();

            var dto = await _editor.LoadAsync(voucherId);

            _editingVoucherId = dto.Id;
            WasSaved = false;

            VoucherDate = dto.TsUtc.ToLocalTime().Date;
            RefNo = dto.RefNo ?? "";
            Memo = dto.Memo ?? "";
            Type = dto.Type.ToString();

            SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == dto.OutletId) ?? Outlets.FirstOrDefault();

            Lines.Clear();
            foreach (var ln in dto.Lines)
            {
                var acc = Accounts.FirstOrDefault(a => a.Id == ln.AccountId);
                Lines.Add(new VoucherLineVm
                {
                    Account = acc,
                    Description = ln.Description,
                    Debit = ln.Debit,
                    Credit = ln.Credit
                });
            }
            if (Lines.Count == 0) AddLine();
            RecalcUi();
        }

        [RelayCommand]
        public void AddLine()
        {
            var line = new VoucherLineVm();
            line.PropertyChanged += (_, __) => RecalcUi();
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

            var linesToSave = Lines
                .Where(l => l.Account != null && (l.Debit > 0m || l.Credit > 0m))
                .ToList();
            if (linesToSave.Count == 0)
                throw new InvalidOperationException("Enter at least one non-zero line.");

            if (!string.Equals(Type, "Journal", StringComparison.OrdinalIgnoreCase) && SelectedOutlet == null)
            {
                MessageBox.Show("Select an Outlet for Debit/Credit vouchers (cash side).",
                    "Outlet Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var totalDebit = linesToSave.Sum(l => l.Debit);
            var totalCredit = linesToSave.Sum(l => l.Credit);

            if (vt == VoucherType.Journal)
            {
                if (totalDebit <= 0m || Math.Abs(totalDebit - totalCredit) > 0.004m)
                    throw new InvalidOperationException("For Journal Voucher, Debits must equal Credits and be > 0.");
            }
            else if (vt == VoucherType.Debit)
            {
                if (linesToSave.Any(l => l.Credit != 0m || l.Debit <= 0m))
                    throw new InvalidOperationException("For Debit Voucher, only Debit amounts (> 0) are allowed.");
            }
            else // Credit
            {
                if (linesToSave.Any(l => l.Debit != 0m || l.Credit <= 0m))
                    throw new InvalidOperationException("For Credit Voucher, only Credit amounts (> 0) are allowed.");
            }

            // store date as UTC (date-only picked by user)
            var localDate = DateTime.SpecifyKind(VoucherDate.Date, DateTimeKind.Local);
            var tsUtc = localDate.ToUniversalTime();

            var dto = new VoucherEditLoadDto(
                _editingVoucherId ?? 0,
                tsUtc,
                SelectedOutlet?.Id,
                RefNo,
                Memo,
                vt,
                linesToSave.Select(l => new VoucherEditLineDto(l.Account!.Id, l.Description?.Trim(), l.Debit, l.Credit)).ToList()
            );

            var savedId = await _editor.SaveAsync(dto);

            WasSaved = true;
            CloseRequested?.Invoke(true);

            // reset edit state after save and clear form
            _editingVoucherId = null;
            Clear();
        }

        // XAML aliases
        public IRelayCommand DeleteLineCmd => DeleteLineCommand;
        public IRelayCommand SaveCmd => SaveCommand;
        public IRelayCommand ClearCmd => ClearCommand;
    }
}
