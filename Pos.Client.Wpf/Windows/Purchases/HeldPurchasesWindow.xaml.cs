// Pos.Client.Wpf/Windows/Purchases/HeldPurchasesWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Pos.Client.Wpf.Services;   // IPurchaseCenterReadService, AppState

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class HeldPurchasesWindow : Window
    {
        private readonly IPurchaseCenterReadService _read;
        public int? SelectedPurchaseId { get; private set; }
        public sealed class UiDraftRow
        {
            public int PurchaseId { get; init; }
            public string DocNoOrId { get; init; } = "";
            public string Supplier { get; init; } = "";
            public string TsLocal { get; init; } = "";
            public int Lines { get; init; }
            public decimal GrandTotal { get; init; }
        }

        private readonly ObservableCollection<UiDraftRow> _rows = new();
        private List<UiDraftRow> _all = new();

        public HeldPurchasesWindow(IPurchaseCenterReadService read)
        {
            InitializeComponent();
            _read = read;
            DraftsGrid.ItemsSource = _rows;
            Loaded += OnLoadedAsync;
        }

        private async void OnLoadedAsync(object? sender, RoutedEventArgs e)
        {
            var outletId = AppState.Current?.CurrentOutletId ?? 0;
            var counterId = AppState.Current?.CurrentCounterId ?? 0;
            if (outletId <= 0 || counterId <= 0)
            {
                MessageBox.Show(
                    "Please select outlet and counter (till) before opening Held Purchases.",
                    "Outlet/Counter Required",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                DialogResult = false;
                Close();
                return;
            }
            try
            {
                await LoadDraftsAsync();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load held purchases: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private async Task LoadDraftsAsync()
        {
            var list = await _read.SearchAsync(
                fromUtc: null,
                toUtc: null,
                term: null,
                wantFinal: false,
                wantDraft: true,
                wantVoided: false,
                onlyWithDoc: false);
            var drafts = list.Where(r => !r.IsReturn).ToList();
            var rows = new List<UiDraftRow>(drafts.Count);
            foreach (var d in drafts)
            {
                int lines = 0;
                try
                {
                    var preview = await _read.GetPreviewLinesAsync(d.PurchaseId);
                    lines = preview?.Count ?? 0;
                }
                catch
                {
                }
                rows.Add(new UiDraftRow
                {
                    PurchaseId = d.PurchaseId,
                    DocNoOrId = d.DocNoOrId,
                    Supplier = d.Supplier,
                    TsLocal = d.TsLocal,
                    Lines = lines,
                    GrandTotal = d.GrandTotal
                });
            }
            _all = rows;
        }

        private void ApplyFilter()
        {
            var term = (SearchBox.Text ?? "").Trim();
            IEnumerable<UiDraftRow> src = _all;
            if (!string.IsNullOrWhiteSpace(term))
            {
                src = src.Where(r =>
                    (!string.IsNullOrEmpty(r.Supplier) &&
                        r.Supplier.Contains(term, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(r.DocNoOrId) &&
                        r.DocNoOrId.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }
            _rows.Clear();
            foreach (var r in src) _rows.Add(r);
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ApplyFilter();

        private void DraftsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
        private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void OpenSelected()
        {
            if (DraftsGrid.SelectedItem is not UiDraftRow row)
            {
                MessageBox.Show("Select a draft.");
                return;
            }
            SelectedPurchaseId = row.PurchaseId;
            DialogResult = true;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        }
    }
}
