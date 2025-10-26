using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Persistence;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Sales
{
    public sealed class TillSessionSummaryVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IDialogService _dialogs;

        public int TillId { get; }
        public int OutletId { get; }
        public int CounterId { get; }

        private string _summaryText = "Loading...";
        public string SummaryText { get => _summaryText; set => SetProperty(ref _summaryText, value); }

        public IAsyncRelayCommand LoadCmd { get; }
        public IRelayCommand CloseCmd { get; }

        public event Action<bool?>? RequestClose;

        public TillSessionSummaryVm(
            IDbContextFactory<PosClientDbContext> dbf,
            IDialogService dialogs,
            int tillId,
            int outletId,
            int counterId)
        {
            _dbf = dbf;
            _dialogs = dialogs;
            TillId = tillId;
            OutletId = outletId;
            CounterId = counterId;

            LoadCmd = new AsyncRelayCommand(LoadAsync);
            CloseCmd = new RelayCommand(() => RequestClose?.Invoke(true));
        }

        private async Task LoadAsync()
        {
            using var db = await _dbf.CreateDbContextAsync();
            // Placeholder summary — replace with your actual totals / details query
            SummaryText = $"Till #{TillId} (Outlet {OutletId}, Counter {CounterId}) is open.";
        }
    }
}
