using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Accounting;
using Pos.Persistence.Services;
using System.Windows;
using Pos.Domain.Services;

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

    public partial class VoucherCenterVm : ObservableObject
    {
        private readonly IVoucherCenterService _svc;
        private readonly IServiceProvider _sp;

        [ObservableProperty] private string? searchText;
        public List<VoucherType> TypeMulti { get; set; } = new();
        public List<VoucherStatus> StatusMulti { get; set; } = new();
        public ObservableCollection<VoucherLineRow> Lines { get; } = new();
        public ObservableCollection<VoucherRow> Rows { get; } = new();
        public VoucherCenterVm(IVoucherCenterService svc, IServiceProvider sp)
        {
            _svc = svc;
            _sp = sp;

            StartDate = DateTime.Today.AddDays(-30);
            EndDate = DateTime.Today.AddDays(1).AddSeconds(-1);
            StatusMulti = new List<VoucherStatus> { VoucherStatus.Posted, VoucherStatus.Draft };

            RefreshCommand = new AsyncRelayCommand(LoadAsync);
            AmendCommand = new AsyncRelayCommand(AmendAsync, () => Selected != null && Selected.Status != VoucherStatus.Voided);
            VoidCommand = new AsyncRelayCommand(VoidAsync, () => Selected != null && Selected.Status == VoucherStatus.Posted);
        }

        [ObservableProperty] private DateTime startDate;
        [ObservableProperty] private DateTime endDate;
        [ObservableProperty] private int? outletFilter;
        [ObservableProperty] private VoucherStatus? statusFilter;
        [ObservableProperty] private VoucherType? typeFilter;

        [ObservableProperty] private VoucherRow? selected;

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand AmendCommand { get; }
        public IAsyncRelayCommand VoidCommand { get; }

        private async Task LoadAsync()
        {
            Rows.Clear();

            var types = (TypeMulti?.Count ?? 0) > 0 ? TypeMulti : null;
            var statuses = (StatusMulti?.Count ?? 0) > 0 ? StatusMulti : null;

            var list = await _svc.SearchAsync(
                StartDate, EndDate, SearchText, OutletFilter,
                types, statuses);

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
                    HasRevisions = x.HasRevisions
                });
            }
        }

        public async Task LoadLinesAsync(int voucherId)
        {
            Lines.Clear();
            var lines = await _svc.GetLinesAsync(voucherId);
            foreach (var x in lines)
            {
                Lines.Add(new VoucherLineRow
                {
                    AccountId = x.AccountId,
                    AccountName = x.AccountName,
                    Description = x.Description,
                    Debit = x.Debit,
                    Credit = x.Credit
                });
            }
        }

        private async Task AmendAsync()
        {
            if (Selected == null) return;
            var oldId = Selected.Id;

            int newVoucherId;
            try
            {
                newVoucherId = await _svc.CreateRevisionDraftAsync(oldId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Amend Voucher");
                return;
            }
            var vm = _sp.GetRequiredService<VoucherEditorVm>();
            await vm.LoadAsync(newVoucherId);
            var win = new VoucherEditorDialog(vm)
            {
                Owner = Application.Current.MainWindow
            };
            win.ShowDialog();
            if (!vm.WasSaved)
            {
                await _svc.DeleteDraftAsync(newVoucherId);
                return;
            }
            try
            {
                await _svc.FinalizeRevisionAsync(newVoucherId, oldId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Finalize Revision");
            }

            await LoadAsync();
        }

        private async Task VoidAsync()
        {
            if (Selected == null) return;

            var reason = "User void"; // TODO: hook to input dialog
            try
            {
                await _svc.VoidAsync(Selected.Id, reason);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Void Voucher");
            }
        }

        partial void OnSelectedChanged(VoucherRow? value)
        {
            AmendCommand.NotifyCanExecuteChanged();
            VoidCommand.NotifyCanExecuteChanged();
        }
    }
}
