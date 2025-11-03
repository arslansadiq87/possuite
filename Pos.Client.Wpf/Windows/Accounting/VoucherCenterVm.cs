using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Accounting;
using Pos.Persistence;
using System.Collections.Generic;
using System.Windows;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherRow : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private DateTime tsUtc;
        [ObservableProperty] private string type = "";
        [ObservableProperty] private string? memo;
        [ObservableProperty] private int? outletId;
        [ObservableProperty] private VoucherStatus status;
        [ObservableProperty] private int revisionNo;
        [ObservableProperty] private decimal totalDebit;
        [ObservableProperty] private decimal totalCredit;
        // NEW: for XAML visibility
        [ObservableProperty] private bool hasRevisions;
    }

    public partial class VoucherLineRow : ObservableObject
    {
        [ObservableProperty] private int accountId;
        [ObservableProperty] private string accountName = "";
        [ObservableProperty] private string? description;
        [ObservableProperty] private decimal debit;
        [ObservableProperty] private decimal credit;
    }


    // Make VM public so XAML and DI can see it
    public partial class VoucherCenterVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IGlPostingService _gl;
        private readonly ICoaService _coa;
        private readonly IServiceProvider _sp;
        [ObservableProperty] private string? searchText;
        // Multi filters (checkbox style)
        public List<VoucherType> TypeMulti { get; set; } = new();
        public List<VoucherStatus> StatusMulti { get; set; } = new();

        public ObservableCollection<VoucherLineRow> Lines { get; } = new();

        public VoucherCenterVm(
            IDbContextFactory<PosClientDbContext> dbf,
            IGlPostingService gl,
            ICoaService coa,
            IServiceProvider sp)
        {
            _dbf = dbf;
            _gl = gl;
            _coa = coa;
            _sp = sp;

            StartDate = DateTime.Today.AddDays(-30);
            EndDate = DateTime.Today.AddDays(1).AddSeconds(-1);
            
            StatusMulti = new List<VoucherStatus> { VoucherStatus.Posted, VoucherStatus.Draft };

            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            //EditCommand = new AsyncRelayCommand(EditAsync, () => Selected != null && Selected.Status != VoucherStatus.Voided);
            AmendCommand = new AsyncRelayCommand(AmendAsync, () => Selected != null && Selected.Status != VoucherStatus.Voided);
            VoidCommand = new AsyncRelayCommand(VoidAsync, () => Selected != null && Selected.Status == VoucherStatus.Posted);
        }

        // ---- Filters & selection ----
        [ObservableProperty] private DateTime startDate;
        [ObservableProperty] private DateTime endDate;
        [ObservableProperty] private int? outletFilter;
        [ObservableProperty] private VoucherStatus? statusFilter;
        [ObservableProperty] private VoucherType? typeFilter;

        public ObservableCollection<VoucherRow> Rows { get; } = new();

        [ObservableProperty] private VoucherRow? selected;

        // ---- Commands ----
        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand EditCommand { get; }
        public IAsyncRelayCommand AmendCommand { get; }
        public IAsyncRelayCommand VoidCommand { get; }

        // ---- Data load ----
        
        private async Task LoadAsync()
        {
            
            using var db = await _dbf.CreateDbContextAsync();
            var q = db.Vouchers
            .AsNoTracking()
            .Where(v => v.TsUtc >= StartDate && v.TsUtc <= EndDate);
            // Search by id or memo
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.Trim();
                if (int.TryParse(s, out var idVal))
                    q = q.Where(v => v.Id == idVal || (v.Memo != null && EF.Functions.Like(v.Memo, $"%{s}%")));
                else
                    q = q.Where(v => v.Memo != null && EF.Functions.Like(v.Memo, $"%{s}%"));
            }
            if (outletFilter.HasValue) q = q.Where(v => v.OutletId == outletFilter.Value);
            if (TypeMulti?.Count > 0 && TypeMulti.Count < 3)
                q = q.Where(v => TypeMulti.Contains(v.Type));
            if (StatusMulti?.Count > 0 && StatusMulti.Count < 4)
                q = q.Where(v => StatusMulti.Contains(v.Status));
            var list = await q
             .OrderByDescending(v => v.TsUtc)
             .Select(v => new
             {
                 v.Id,
                 v.TsUtc,
                 v.Memo,
                 v.OutletId,
                 v.Status,
                 v.RevisionNo,
                 v.Type,
                 TotalDebit = v.Lines.Sum(l => l.Debit),
                 TotalCredit = v.Lines.Sum(l => l.Credit),
                 HasRevisions = v.RevisionNo > 1
             })
             .ToListAsync();
            Rows.Clear();
            foreach (var x in list)
            {
                Rows.Add(new VoucherRow
                {
                    Id = x.Id,
                    TsUtc = x.TsUtc,
                    Memo = x.Memo,
                    OutletId = x.OutletId,
                    Status = x.Status,
                    RevisionNo = x.RevisionNo,
                    Type = x.Type.ToString(),
                    TotalDebit = x.TotalDebit,
                    TotalCredit = x.TotalCredit,
                    HasRevisions = x.HasRevisions   // <-- add this

                });
            }
        }

        public async Task LoadLinesAsync(int voucherId)
        {
           
            using var db = await _dbf.CreateDbContextAsync();
            var data = await db.VoucherLines
                .Where(l => l.VoucherId == voucherId)
                .Select(l => new
                {
                    l.AccountId,
                    AccountName = db.Accounts.Where(a => a.Id == l.AccountId).Select(a => a.Name).FirstOrDefault(),
                    l.Description,
                    l.Debit,
                    l.Credit
                })
                .ToListAsync();
            Lines.Clear();
            foreach (var x in data)
            {
                Lines.Add(new VoucherLineRow
                {
                    AccountId = x.AccountId,
                    AccountName = x.AccountName ?? "",
                    Description = x.Description,
                    Debit = x.Debit,
                    Credit = x.Credit
                });
            }
        }

        // ---- Edit ----
        //private async Task EditAsync()
        //{
        //    if (Selected == null) return;
        //    var vm = _sp.GetRequiredService<VoucherEditorVm>();
        //    await vm.LoadAsync(Selected.Id); // implement LoadAsync(id) in VoucherEditorVm if not present
        //    var win = new VoucherEditorWindow(vm);
        //    win.Owner = Application.Current.MainWindow;
        //    win.ShowDialog();
        //    await LoadAsync();
        //}

        // ---- Amend / Revision ----
        private async Task AmendAsync()
        {
            if (Selected == null) return;
            var selectedId = Selected.Id;
            int newVoucherId;
            // PHASE 1: Create the revision copy and COMMIT, then dispose the context.
            using (var db = await _dbf.CreateDbContextAsync())
            {
                var old = await db.Vouchers
                    .Include(v => v.Lines)
                    .FirstAsync(v => v.Id == selectedId);
                if (old.Status == VoucherStatus.Voided) return;
                var newV = new Voucher
                {
                    TsUtc = DateTime.UtcNow,
                    OutletId = old.OutletId,
                    Type = old.Type,
                    Memo = $"Revision of #{old.Id}: {old.Memo}",
                    // Keep your original flow; if you have Draft enum, you can set Draft here instead.
                    Status = VoucherStatus.Draft,
                    RevisionNo = old.RevisionNo + 1,
                    AmendedFromId = old.Id,
                    AmendedAtUtc = DateTime.UtcNow
                };

                db.Vouchers.Add(newV);
                await db.SaveChangesAsync();
                foreach (var ln in old.Lines)
                {
                    db.VoucherLines.Add(new VoucherLine
                    {
                        VoucherId = newV.Id,
                        AccountId = ln.AccountId,
                        Debit = ln.Debit,
                        Credit = ln.Credit,
                        Description = ln.Description
                    });
                }
                await db.SaveChangesAsync();
                newVoucherId = newV.Id;
            } // <-- context disposed, no locks held
            {
                var vm = _sp.GetRequiredService<VoucherEditorVm>();
                await vm.LoadAsync(newVoucherId);
                var win = new VoucherEditorDialog(vm)
                {
                    Owner = Application.Current.MainWindow
                };
                win.ShowDialog();
                // After dialog closes: keep the revision ONLY if user saved.
                if (!vm.WasSaved)
                {
                    using (var db = await _dbf.CreateDbContextAsync())
                    {
                        var draft = await db.Vouchers
                            .Include(v => v.Lines)
                            .FirstOrDefaultAsync(v => v.Id == newVoucherId);

                        if (draft != null)
                        {
                            if (draft.Lines.Count > 0) db.VoucherLines.RemoveRange(draft.Lines);
                            db.Vouchers.Remove(draft);
                            await db.SaveChangesAsync();
                        }
                    }
                    // Do not mark old voucher amended; do not post any GL delta
                    return;
                }

            }

            // PHASE 3: After user closes the editor, post GL delta and mark old as amended in a NEW context.
            using (var db = await _dbf.CreateDbContextAsync())
            {
                var newV = await db.Vouchers
                    .Include(v => v.Lines)
                    .FirstAsync(v => v.Id == newVoucherId);
                var old = await db.Vouchers
                    .Include(v => v.Lines)
                    .FirstAsync(v => v.Id == selectedId);

                await _gl.PostVoucherRevisionAsync(db, newV, old.Lines.ToList());
                old.Status = VoucherStatus.Amended;
                old.AmendedAtUtc = DateTime.UtcNow;

                // Optionally flip the new voucher to Posted if your SaveAsync didn't already do it
                if (newV.Status == VoucherStatus.Draft)
                    newV.Status = VoucherStatus.Posted;

                await db.SaveChangesAsync();
            }
            await LoadAsync();

        }


        // ---- Void ----
        private async Task VoidAsync()
        {
            if (Selected == null) return;

            var reason = "User void"; // hook into your input dialog if you have one

            using var db = await _dbf.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            var v = await db.Vouchers.FirstAsync(x => x.Id == Selected.Id);
            if (v.Status != VoucherStatus.Posted)
                return;

            await _gl.PostVoucherVoidAsync(db, v); // reversal GL
            v.Status = VoucherStatus.Voided;
            v.VoidReason = reason;
            v.VoidedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await tx.CommitAsync();
            await LoadAsync();
        }

        partial void OnSelectedChanged(VoucherRow? value)
        {
            //EditCommand.NotifyCanExecuteChanged();
            AmendCommand.NotifyCanExecuteChanged();
            VoidCommand.NotifyCanExecuteChanged();
        }

    }
}
