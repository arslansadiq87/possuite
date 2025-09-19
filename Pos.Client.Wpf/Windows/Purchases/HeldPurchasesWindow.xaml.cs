using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class HeldPurchasesWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        public int? SelectedPurchaseId { get; private set; }

        public class UiDraftRow
        {
            public int PurchaseId { get; set; }
            public string DocNoOrId { get; set; } = "";
            public string Supplier { get; set; } = "";
            public string TsLocal { get; set; } = "";
            public int Lines { get; set; }
            public decimal GrandTotal { get; set; }
        }

        private readonly ObservableCollection<UiDraftRow> _rows = new();
        private List<UiDraftRow> _all = new();

        public HeldPurchasesWindow(DbContextOptions<PosClientDbContext> opts)
        {
            InitializeComponent();
            _opts = opts;
            _svc = new PurchasesService(new PosClientDbContext(_opts));
            DraftsGrid.ItemsSource = _rows;
            Loaded += HeldPurchasesWindow_Loaded;
        }

        private async void HeldPurchasesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            using var db = new PosClientDbContext(_opts);
            var results = await db.Purchases
                .AsNoTracking()
                .Include(p => p.Supplier)   // bring supplier names
                .Include(p => p.Lines)      // bring line counts
                .Where(p => p.Status == PurchaseStatus.Draft)
                .OrderByDescending(p => p.CreatedAtUtc)
                .Select(p => new UiDraftRow
                {
                    PurchaseId = p.Id,
                    DocNoOrId = string.IsNullOrWhiteSpace(p.DocNo) ? $"#{p.Id}" : p.DocNo!,
                    Supplier = string.IsNullOrWhiteSpace(p.Supplier!.Name) ? "—" : p.Supplier!.Name,
                    TsLocal = (p.UpdatedAtUtc ?? p.CreatedAtUtc).ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                    Lines = p.Lines.Count,
                    GrandTotal = p.GrandTotal
                })
                .ToListAsync();

            _all = results;
            ApplyFilter();
        }


        private void ApplyFilter()
        {
            var term = (SearchBox.Text ?? "").Trim();
            IEnumerable<UiDraftRow> src = _all;

            if (!string.IsNullOrWhiteSpace(term))
            {
                src = src.Where(r =>
                    r.Supplier.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    r.DocNoOrId.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            _rows.Clear();
            foreach (var r in src) _rows.Add(r);
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void DraftsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
        private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

        private void OpenSelected()
        {
            if (DraftsGrid.SelectedItem is not UiDraftRow row) { MessageBox.Show("Select a draft."); return; }
            SelectedPurchaseId = row.PurchaseId;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
            if (e.Key == Key.Escape) { Cancel_Click(sender, e); }
        }
    }
}
