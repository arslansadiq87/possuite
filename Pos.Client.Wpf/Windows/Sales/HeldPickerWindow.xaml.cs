using System.Globalization;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class HeldPickerWindow : Window
    {
        private readonly int _outletId, _counterId;
        private readonly IInvoiceService _inv;

        public sealed class HeldRow
        {
            public int Id { get; init; }
            public DateTime TsUtc { get; init; }
            public string TsLocal => TsUtc.ToLocalTime().ToString("dd-MMM HH:mm");
            public string? HoldTag { get; init; }
            public string? CustomerName { get; init; }
            public decimal Total { get; init; }
            public string TotalFormatted => Total.ToString("N2", CultureInfo.CurrentCulture);
        }

        public int? SelectedSaleId { get; private set; }

        // Service-layer constructor (no DbContextOptions)
        public HeldPickerWindow(int outletId, int counterId)
        {
            InitializeComponent();
            _outletId = outletId;
            _counterId = counterId;
            _inv = App.Services.GetRequiredService<IInvoiceService>();
            Loaded += async (_, __) => await LoadRowsAsync();
        }

        private async Task LoadRowsAsync()
        {
            var rows = await _inv.GetHeldAsync(_outletId, _counterId);
            var uiRows = rows.Select(r => new HeldRow
            {
                Id = r.Id,
                TsUtc = r.TsUtc,
                HoldTag = r.HoldTag,
                CustomerName = r.CustomerName,
                Total = r.Total
            }).ToList();
            List.ItemsSource = uiRows;
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem is HeldRow r) { SelectedSaleId = r.Id; DialogResult = true; }
        }

        private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Resume_Click(sender, e);

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem is not HeldRow r) return;
            var ok = MessageBox.Show($"Delete held invoice {r.Id}?", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;

            try
            {
                await _inv.DeleteHeldAsync(r.Id);
                await LoadRowsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete held invoice: " + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
