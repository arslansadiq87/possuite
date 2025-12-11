using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Domain.Services;
using Pos.Domain.Models.Reports;
using Pos.Client.Wpf.Services; // <-- correct namespace

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class PurchaserLedgerRowVm : ObservableObject
    {
        public int PurchaseId { get; set; }
        public string DocNo { get; set; } = "";
        public string Supplier { get; set; } = "";
        public DateTime TsUtc { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal Paid { get; set; }
        public decimal Due { get; set; }
    }

    public partial class PurchaserLedgerVm : ObservableObject
    {
        private readonly IPurchaseLedgerReadService _svc;

        [ObservableProperty] private DateTime _from = DateTime.UtcNow.Date.AddDays(-30);
        [ObservableProperty] private DateTime _to = DateTime.UtcNow.Date.AddDays(1);
        [ObservableProperty] private int? _supplierId = null;

        public ObservableCollection<PurchaserLedgerRowVm> Rows { get; } = new();

        [ObservableProperty] private decimal _totalGrand;
        [ObservableProperty] private decimal _totalPaid;
        [ObservableProperty] private decimal _totalDue;

        public PurchaserLedgerVm(IPurchaseLedgerReadService svc)
        {
            _svc = svc;
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            try
            {
                Rows.Clear();

                // Convert LOCAL [From .. To] to UTC [fromUtc .. toUtcExclusive)
                var fromLocal = From.Date;
                var toLocalExclusive = To.Date.AddDays(1);

                var fromUtc = DateTime.SpecifyKind(fromLocal, DateTimeKind.Local).ToUniversalTime();
                var toUtcExclusive = DateTime.SpecifyKind(toLocalExclusive, DateTimeKind.Local).ToUniversalTime();

                int? outletId = AppState.Current.CurrentOutletId > 0 ? AppState.Current.CurrentOutletId : (int?)null;
                int? supplierId = SupplierId; // null = all

                var data = await _svc.GetSupplierLedgerAsync(
                    fromUtc: fromUtc,
                    toUtcExclusive: toUtcExclusive,
                    supplierId: supplierId,
                    outletId: outletId);

                foreach (var r in data)
                {
                    Rows.Add(new PurchaserLedgerRowVm
                    {
                        PurchaseId = r.PurchaseId,
                        DocNo = r.DocNo,
                        Supplier = r.Supplier,
                        TsUtc = r.TsUtc,        // service maps CreatedAtUtc -> TsUtc
                        GrandTotal = r.GrandTotal,
                        Paid = r.Paid,
                        Due = r.Due
                    });
                    if (Rows.Count > 0)
                        System.Diagnostics.Debug.WriteLine($"[Ledger] First supplier: '{Rows[0].Supplier}'");

                }

                TotalGrand = Rows.Sum(x => x.GrandTotal);
                TotalPaid = Rows.Sum(x => x.Paid);
                TotalDue = Rows.Sum(x => x.Due);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.ToString(), "Supplier Ledger Error");
                throw;
            }
        }


    }
}
