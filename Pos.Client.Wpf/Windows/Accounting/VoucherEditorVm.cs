// Pos.Client.Wpf/Windows/Accounting/VoucherEditorVm.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;           // <-- for IRelayCommand & [RelayCommand]
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;
using Pos.Persistence;
using System.Windows;
using System.Diagnostics;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherLineVm : ObservableObject
    {
        [ObservableProperty] private Account? account;
        [ObservableProperty] private string? description;
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
        // NOTE: no command aliases here — commands live on VoucherEditorVm

    }

    public partial class VoucherEditorVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;
        // Editing state: null => creating new; value => editing this voucher
        private int? _editingVoucherId = null;
        // inside VoucherEditorVm

        public bool WasSaved { get; private set; } = false;
        public event Action<bool>? CloseRequested; // true = saved, false = cancel/close

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

        // UI helpers
        public bool ShowDebitColumn => Type == nameof(VoucherType.Debit) || Type == nameof(VoucherType.Journal);
        public bool ShowCreditColumn => Type == nameof(VoucherType.Credit) || Type == nameof(VoucherType.Journal);

        public event Action? AccountsReloadRequested;

        public async Task ReloadAccountsAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();
            var list = await db.Accounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync();

            Accounts.Clear();
            foreach (var a in list) Accounts.Add(a);
        }

        // Allow changing Type while there are no meaningful entries on any row
        public bool IsTypeChangeAllowed =>
            Lines.Count == 0 ||
            Lines.All(l =>
                l.Account == null &&
                l.Debit == 0m &&
                l.Credit == 0m &&
                string.IsNullOrWhiteSpace(l.Description));


        // Non-admins are locked to their outlet (combobox IsEnabled bound to this)
        public bool IsOutletSelectable => AuthZ.IsAdmin();

        // Enable Save according to voucher type and basic validations
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

                // Journal: either side > 0 on at least one row
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

        private static void ClearLine(VoucherLineVm l)
        {
            l.Account = null;
            l.Description = "";
            l.Debit = 0m;
            l.Credit = 0m;
        }


        // ---------- Commands ----------

        [RelayCommand]
        public void Clear()
        {
            _editingVoucherId = null;   // <--- reset edit mode

            VoucherDate = DateTime.Today;
            RefNo = "";
            Memo = "";

            Lines.Clear();
            AddLine(); // seed exactly one empty line

            // Re-select outlet according to role
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

        // Allow any parameter; ignore placeholder/invalids; fall back to SelectedLine
        [RelayCommand(CanExecute = nameof(CanDeleteLine))]
        public void DeleteLine(object? parameter)
        {
            var line = parameter as VoucherLineVm ?? SelectedLine;
            if (line == null) return;

            // If this is the only row, clear it instead of removing
            if (Lines.Count <= 1)
            {
                ClearLine(Lines[0]);
                SelectedLine = Lines[0];
                RecalcUi();
                return;
            }

            // Else remove normally
            Lines.Remove(line);
            // Keep at least one row at all times (defensive)
            if (Lines.Count == 0)
                AddLine();

            RecalcUi();
        }


        // Disable the delete button on placeholder rows
        private bool CanDeleteLine(object? parameter)
        {
            return parameter is VoucherLineVm || SelectedLine is VoucherLineVm;
        }

        public async Task LoadAsync(int voucherId)
        {
            // 1) Ensure lists are loaded (accounts, outlets, default outlet, one empty line, etc.)
            if (Accounts.Count == 0 || Outlets.Count == 0)
                await LoadAsync(); // your parameterless loader
            using var db = await _dbf.CreateDbContextAsync();
            var v = await db.Vouchers
                .AsNoTracking()
                .Include(x => x.Lines)
                .FirstAsync(x => x.Id == voucherId);
            // 2) Mark we are editing this voucher
            _editingVoucherId = v.Id;
            WasSaved = false;   // <--- add this

            // 3) Populate header fields
            VoucherDate = v.TsUtc.ToLocalTime().Date; // convert to local date for the DatePicker
            RefNo = v.RefNo ?? "";
            Memo = v.Memo ?? "";
            Type = v.Type.ToString(); // your UI binds to string enum name
            // 4) Outlet selection
            SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == v.OutletId) ?? Outlets.FirstOrDefault();
            // 5) Populate line items
            Lines.Clear();
            foreach (var ln in v.Lines.OrderBy(l => l.Id))
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
            // 6) Refresh UI calculated properties
            RecalcUi();
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

            // seed exactly one row if empty
            if (Lines.Count == 0) AddLine();

            RecalcUi();
        }

        [RelayCommand]
        public void AddLine()
        {
            var line = new VoucherLineVm();
            line.PropertyChanged += (_, __) => RecalcUi();  // keep totals reactive
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
            // Trim: only rows that actually matter
            var linesToSave = Lines
                .Where(l => l.Account != null && ((l.Debit > 0m) || (l.Credit > 0m)))
                .ToList();
            if (linesToSave.Count == 0)
                throw new InvalidOperationException("Enter at least one non-zero line.");

            // 🔎 INSERT near the top of SaveAsync(), after basic null/line checks
            if (!string.Equals(Type, "Journal", StringComparison.OrdinalIgnoreCase) && SelectedOutlet == null)
            {
                System.Windows.MessageBox.Show(
                    "Select an Outlet for Debit/Credit vouchers (cash side).",
                    "Outlet Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }



            // STRICT per-type validation (kept from your original intent)
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
            // Store date as UTC (date-only picked by user)
            var localDate = DateTime.SpecifyKind(VoucherDate.Date, DateTimeKind.Local);
            var tsUtc = localDate.ToUniversalTime();
            using var db = await _dbf.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            try
            {
                if (_editingVoucherId.HasValue)
                {
                    // -------- EDIT/UPDATE EXISTING --------
                    var v = await db.Vouchers
                        .Include(x => x.Lines)
                        .FirstAsync(x => x.Id == _editingVoucherId.Value);
                    // Update header
                    v.TsUtc = tsUtc;
                    v.OutletId = SelectedOutlet.Id;
                    v.RefNo = RefNo?.Trim();
                    v.Memo = Memo?.Trim();
                    v.Type = vt;
                    // Remove existing GL entries for this voucher's base posting
                    var baseDocTypes = new[] { GlDocType.JournalVoucher, GlDocType.CashPayment, GlDocType.CashReceipt };
                    var oldGl = await db.GlEntries
                        .Where(g => g.DocId == v.Id && baseDocTypes.Contains(g.DocType))
                        .ToListAsync();
                    if (oldGl.Count > 0)
                        db.GlEntries.RemoveRange(oldGl);
                    // Replace lines
                    if (v.Lines.Count > 0)
                        db.VoucherLines.RemoveRange(v.Lines);
                    await db.SaveChangesAsync();
                    foreach (var ln in linesToSave)
                    {
                        db.VoucherLines.Add(new VoucherLine
                        {
                            VoucherId = v.Id,
                            AccountId = ln.Account!.Id,
                            Description = string.IsNullOrWhiteSpace(ln.Description)
                                          ? (vt == VoucherType.Debit ? "Cash Payment Voucher"
                                             : vt == VoucherType.Credit ? "Cash Receiving Voucher"
                                             : "Journal Voucher")
                                          : ln.Description!.Trim(),
                            Debit = ln.Debit,
                            Credit = ln.Credit
                        });
                    }
                    await db.SaveChangesAsync();
                    // Re-post with the SAME DbContext/transaction
                    await _gl.PostVoucherAsync(db, v);
                    await tx.CommitAsync();
                    WasSaved = true;
                    CloseRequested?.Invoke(true);

                    // Reset edit mode after save
                    _editingVoucherId = null;
                    Clear();
                }
                else
                {
                    // -------- CREATE NEW (your original code) --------
                    var v = new Voucher
                    {
                        TsUtc = tsUtc,
                        OutletId = SelectedOutlet.Id,
                        RefNo = RefNo?.Trim(),
                        Memo = Memo?.Trim(),
                        Type = vt
                    };
                    db.Vouchers.Add(v);
                    await db.SaveChangesAsync();

                    foreach (var ln in linesToSave)
                    {
                        db.VoucherLines.Add(new VoucherLine
                        {
                            VoucherId = v.Id,
                            AccountId = ln.Account!.Id,
                            Description = string.IsNullOrWhiteSpace(ln.Description)
                                          ? (vt == VoucherType.Debit ? "Cash Payment Voucher"
                                             : vt == VoucherType.Credit ? "Cash Receiving Voucher"
                                             : "Journal Voucher")
                                          : ln.Description!.Trim(),
                            Debit = ln.Debit,
                            Credit = ln.Credit
                        });
                    }
                    await db.SaveChangesAsync();
                    // GL posting (handles Cash-in-Hand auto-line)
                    await _gl.PostVoucherAsync(db, v);
                    await tx.CommitAsync();
                    try { AccountsReloadRequested?.Invoke(); } catch { /* ignore */ }
                    // Optional: clear form for next entry
                    Clear();
                    CloseRequested?.Invoke(true);

                }
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

     
        // Your XAML uses DeleteLineCmd / SaveCmd / ClearCmd
        public IRelayCommand DeleteLineCmd => DeleteLineCommand;
        public IRelayCommand SaveCmd => SaveCommand;
        public IRelayCommand ClearCmd => ClearCommand;
    }
}
