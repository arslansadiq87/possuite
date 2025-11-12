using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Client.Wpf.Services;
using Pos.Domain.DTO;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Sales
{
    public sealed class TillSessionSummaryVm : ObservableObject
    {
        private readonly ITillReadService _tillRead;
        private readonly IDialogService _dialogs;

        public int TillId { get; }
        public int OutletId { get; }
        public int CounterId { get; }

        // Bind these to your TextBlocks in XAML
        public string HeaderText { get => _headerText; private set => SetProperty(ref _headerText, value); }
        private string _headerText = "Loading...";

        public string OpenedText { get => _openedText; private set => SetProperty(ref _openedText, value); }
        private string _openedText = "-";

        public string DurationText { get => _durationText; private set => SetProperty(ref _durationText, value); }
        private string _durationText = "-";

        public string SalesTotal { get => _salesTotal; private set => SetProperty(ref _salesTotal, value); }
        private string _salesTotal = "0.00";

        public string ReturnsTotal { get => _returnsTotal; private set => SetProperty(ref _returnsTotal, value); }
        private string _returnsTotal = "0.00";

        public string NetTotal { get => _netTotal; private set => SetProperty(ref _netTotal, value); }
        private string _netTotal = "0.00";

        public string OpeningFloat { get => _openingFloat; private set => SetProperty(ref _openingFloat, value); }
        private string _openingFloat = "0.00";

        public string CashIn { get => _cashIn; private set => SetProperty(ref _cashIn, value); }
        private string _cashIn = "0.00";

        public string CashOut { get => _cashOut; private set => SetProperty(ref _cashOut, value); }
        private string _cashOut = "0.00";

        public string ExpectedCash { get => _expectedCash; private set => SetProperty(ref _expectedCash, value); }
        private string _expectedCash = "0.00";

        public string CardIn { get => _cardIn; private set => SetProperty(ref _cardIn, value); }
        private string _cardIn = "0.00";

        public string CardOut { get => _cardOut; private set => SetProperty(ref _cardOut, value); }
        private string _cardOut = "0.00";

        public string SalesCount { get => _salesCount; private set => SetProperty(ref _salesCount, value); }
        private string _salesCount = "0";

        public string ReturnsCount { get => _returnsCount; private set => SetProperty(ref _returnsCount, value); }
        private string _returnsCount = "0";

        public string DocsCount { get => _docsCount; private set => SetProperty(ref _docsCount, value); }
        private string _docsCount = "0";

        public string LastTx { get => _lastTx; private set => SetProperty(ref _lastTx, value); }
        private string _lastTx = "-";

        public string ItemsSoldQty { get => _itemsSoldQty; private set => SetProperty(ref _itemsSoldQty, value); }
        private string _itemsSoldQty = "0";

        public string ItemsReturnedQty { get => _itemsReturnedQty; private set => SetProperty(ref _itemsReturnedQty, value); }
        private string _itemsReturnedQty = "0";

        public string ItemsNetQty { get => _itemsNetQty; private set => SetProperty(ref _itemsNetQty, value); }
        private string _itemsNetQty = "0";

        public string TaxCollected { get => _taxCollected; private set => SetProperty(ref _taxCollected, value); }
        private string _taxCollected = "0.00";

        public string TaxRefunded { get => _taxRefunded; private set => SetProperty(ref _taxRefunded, value); }
        private string _taxRefunded = "0.00";

        public string SalesAmendments { get => _salesAmendments; private set => SetProperty(ref _salesAmendments, value); }
        private string _salesAmendments = "0";

        public string ReturnAmendments { get => _returnAmendments; private set => SetProperty(ref _returnAmendments, value); }
        private string _returnAmendments = "0";

        public string VoidsCount { get => _voidsCount; private set => SetProperty(ref _voidsCount, value); }
        private string _voidsCount = "0";

        public IAsyncRelayCommand LoadCmd { get; }
        public IRelayCommand CloseCmd { get; }
        public event Action<bool?>? RequestClose;

        public TillSessionSummaryVm(
            ITillReadService tillRead,
            IDialogService dialogs,
            int tillId,
            int outletId,
            int counterId)
        {
            _tillRead = tillRead;
            _dialogs = dialogs;

            TillId = tillId;
            OutletId = outletId;
            CounterId = counterId;

            LoadCmd = new AsyncRelayCommand(LoadAsync);
            CloseCmd = new RelayCommand(() => RequestClose?.Invoke(true));
        }

        private static string M(decimal v) =>
            v.ToString("N2", CultureInfo.CurrentCulture);
        private static string N0(decimal v) =>
            v.ToString("N0", CultureInfo.CurrentCulture);
        private static string N0(int v) =>
            v.ToString("N0", CultureInfo.CurrentCulture);

        private async Task LoadAsync()
        {
            try
            {
                var dto = await _tillRead.GetSessionSummaryAsync(TillId);

                HeaderText = $"Till #{dto.TillId} (Outlet {dto.OutletId}, Counter {dto.CounterId})";

                OpenedText = dto.OpenedAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm");
                DurationText = (DateTime.UtcNow - dto.OpenedAtUtc).ToString(@"hh\:mm");

                SalesTotal = M(dto.SalesTotal);
                ReturnsTotal = M(dto.ReturnsTotalAbs);
                NetTotal = M(dto.NetTotal);

                OpeningFloat = M(dto.OpeningFloat);
                CashIn = M(dto.SalesCash);
                CashOut = M(dto.RefundsCashAbs);
                ExpectedCash = M(dto.ExpectedCash);

                CardIn = M(dto.SalesCard);
                CardOut = M(dto.RefundsCardAbs);

                SalesCount = N0(dto.SalesCount);
                ReturnsCount = N0(dto.ReturnsCount);
                DocsCount = N0(dto.DocsCount);
                LastTx = dto.LastTxUtc?.ToLocalTime().ToString("dd-MMM-yyyy HH:mm") ?? "-";

                ItemsSoldQty = N0(dto.ItemsSoldQty);
                ItemsReturnedQty = N0(dto.ItemsReturnedQty);
                ItemsNetQty = N0(dto.ItemsNetQty);

                TaxCollected = M(dto.TaxCollected);
                TaxRefunded = M(dto.TaxRefundedAbs);

                SalesAmendments = N0(dto.SalesAmendments);
                ReturnAmendments = N0(dto.ReturnAmendments);
                VoidsCount = N0(dto.VoidsCount);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Failed to load till summary:\n" + ex.Message,
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
