using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Formatting;         // for ProductNameComposer (same as Sales)
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Services;   // AppState / AppCtx


namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseCenterWindow : Window
    {
        public int? SelectedHeldPurchaseId { get; private set; }  // << expose held ID
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        

        // Row shape mirrors Sales UI row
        public class UiPurchaseRow
        {
            public int PurchaseId { get; set; }
            public string DocNoOrId { get; set; } = "";
            public string Supplier { get; set; } = "";
            public string TsLocal { get; set; } = "";
            public string Status { get; set; } = "";
            public decimal GrandTotal { get; set; }
            public bool IsReturn { get; set; } // ← add this (set it when you load rows)

        }

        public class UiLineRow
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public decimal Qty { get; set; }
            public decimal UnitCost { get; set; }
            public decimal LineTotal { get; set; }
        }

        private readonly ObservableCollection<UiPurchaseRow> _purchases = new();
        private readonly ObservableCollection<UiLineRow> _lines = new();

        public PurchaseCenterWindow()
        {
            InitializeComponent();

            // Double-Esc to close (same feel as Sales)
            this.PreviewKeyDown += PurchaseCenterWindow_PreviewKeyDown;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));

            PurchasesGrid.ItemsSource = _purchases;
            LinesGrid.ItemsSource = _lines;

            // defaults: last 30 days
            FromDate.SelectedDate = System.DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = System.DateTime.Today;

            UpdateFilterSummary();
            LoadPurchases();
            UpdateHeldButtonVisibility();
        }

        private System.DateTime? _lastEscDown;
        private void PurchaseCenterWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var now = System.DateTime.UtcNow;
                if (_lastEscDown.HasValue && (now - _lastEscDown.Value).TotalMilliseconds <= 600)
                {
                    Close();
                    return;
                }
                _lastEscDown = now;
                e.Handled = true;
            }
        }

        // ===== Top bar: filter summary / actions =====
        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            PopupFilter.IsOpen = false;
            UpdateFilterSummary();
            LoadPurchases();
            UpdateHeldButtonVisibility();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            ChkFinal.IsChecked = true;
            ChkDraft.IsChecked = true;
            ChkVoided.IsChecked = false;
            ChkOnlyWithDocNo.IsChecked = true;
            UpdateFilterSummary();
        }

        private void UpdateFilterSummary()
        {
            // Place to update any summary label if you add one (kept minimal here)
        }

        private void Search_Click(object sender, RoutedEventArgs e) => LoadPurchases();
        private void Search_Executed(object sender, ExecutedRoutedEventArgs e) => LoadPurchases();

        // ===== Load & filter list =====
        private void LoadPurchases()
        {
            _purchases.Clear();

            System.DateTime? fromUtc = FromDate.SelectedDate?.Date.ToUniversalTime();
            System.DateTime? toUtc = ToDate.SelectedDate?.AddDays(1).Date.ToUniversalTime();

            using var db = new PosClientDbContext(_opts);

            // Base query: join Supplier; filter date range on CreatedAt or ReceivedAt
            var q = db.Purchases.AsNoTracking()
                    .Include(p => p.Supplier)
                    .Where(p =>
                        (!fromUtc.HasValue || (p.CreatedAtUtc >= fromUtc || p.ReceivedAtUtc >= fromUtc)) &&
                        (!toUtc.HasValue || (p.CreatedAtUtc < toUtc || p.ReceivedAtUtc < toUtc))
                    );

            // Search box: doc no / supplier / vendor inv
            var term = (SearchBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(p =>
                    (p.DocNo ?? "").Contains(term) ||
                    (p.VendorInvoiceNo ?? "").Contains(term) ||
                    (p.Supplier != null && p.Supplier.Name.Contains(term))
                );
            }

            // Materialize to apply UI checkboxes easily
            var list = q.OrderByDescending(p => p.ReceivedAtUtc ?? p.CreatedAtUtc)
                        .Select(p => new
                        {
                            p.Id,
                            p.DocNo,
                            Supplier = p.Supplier != null ? p.Supplier.Name : "",
                            Ts = p.ReceivedAtUtc ?? p.CreatedAtUtc,
                            p.Status,
                            p.GrandTotal
                        })
                        .ToList();

            bool wantFinal = ChkFinal.IsChecked == true;
            bool wantDraft = ChkDraft.IsChecked == true;
            bool wantVoided = ChkVoided.IsChecked == true;
            bool onlyWithDoc = ChkOnlyWithDocNo.IsChecked == true;

            var rows = list
                .Where(r =>
                    ((r.Status == PurchaseStatus.Final && wantFinal) ||
                     (r.Status == PurchaseStatus.Draft && wantDraft) ||
                     (r.Status == PurchaseStatus.Voided && wantVoided))
                    && (!onlyWithDoc || !string.IsNullOrWhiteSpace(r.DocNo))
                )
                .Select(r => new UiPurchaseRow
                {
                    PurchaseId = r.Id,
                    DocNoOrId = string.IsNullOrWhiteSpace(r.DocNo) ? $"#{r.Id}" : r.DocNo!,
                    Supplier = string.IsNullOrWhiteSpace(r.Supplier) ? "—" : r.Supplier.Trim(),
                    TsLocal = r.Ts.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                    Status = r.Status.ToString(),
                    GrandTotal = r.GrandTotal
                });

            foreach (var r in rows)
                _purchases.Add(r);

            HeaderText.Text = "Select a purchase to view lines.";
            _lines.Clear();

            UpdateActions(null);
        }

        // ===== Selection → show lines =====
        private void PurchasesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _lines.Clear();
            if (PurchasesGrid.SelectedItem is not UiPurchaseRow sel)
            {
                HeaderText.Text = "";
                UpdateActions(null);
                return;
            }

            using var db = new PosClientDbContext(_opts);

            // Load purchase + lines via service helper you added
            var purchase = db.Purchases
                .Include(p => p.Lines)
                .First(p => p.Id == sel.PurchaseId);

            HeaderText.Text =
                $"Purchase {sel.DocNoOrId}  Status: {purchase.Status}  Grand: {purchase.GrandTotal:0.00}";

            // Load item/product meta to compose display name (like Sales)
            var itemIds = purchase.Lines.Select(l => l.ItemId).Distinct().ToList();

            var meta = (
                from i in db.Items.AsNoTracking()
                join p in db.Products.AsNoTracking() on i.ProductId equals p.Id into gp
                from p in gp.DefaultIfEmpty()
                where itemIds.Contains(i.Id)
                select new
                {
                    i.Id,
                    ItemName = i.Name,
                    ProductName = p != null ? p.Name : null,
                    i.Variant1Name,
                    i.Variant1Value,
                    i.Variant2Name,
                    i.Variant2Value,
                    i.Sku
                }
            ).ToList().ToDictionary(x => x.Id);

            foreach (var line in purchase.Lines)
            {
                string display = $"Item #{line.ItemId}";
                string sku = "";
                if (meta.TryGetValue(line.ItemId, out var m))
                {
                    display = ProductNameComposer.Compose(
                        m.ProductName, m.ItemName,
                        m.Variant1Name, m.Variant1Value,
                        m.Variant2Name, m.Variant2Value);
                    sku = m.Sku ?? "";
                }

                _lines.Add(new UiLineRow
                {
                    ItemId = line.ItemId,
                    Sku = sku,
                    DisplayName = display,
                    Qty = line.Qty,
                    UnitCost = line.UnitCost,
                    LineTotal = line.LineTotal
                });
            }

            UpdateActions(sel);
        }

        // ===== Actions visibility (bottom bar) =====
        private void UpdateActions(UiPurchaseRow? sel)
        {
            // hide all by default
            BtnAmend.Visibility = Visibility.Collapsed;
            BtnVoidPurchase.Visibility = Visibility.Collapsed;
            BtnReturnWith.Visibility = Visibility.Collapsed;
            BtnReturnWithout.Visibility = Visibility.Visible; // independent like Sales
            BtnReceive.Visibility = Visibility.Collapsed;

            BtnAmendReturn.Visibility = Visibility.Collapsed;
            BtnVoidReturn.Visibility = Visibility.Collapsed;

            if (sel is null) return;

            // Draft purchase: can Receive, Amend, Void
            if (!sel.IsReturn && sel.Status == nameof(PurchaseStatus.Draft))
            {
                BtnReceive.Visibility = Visibility.Visible;
                BtnAmend.Visibility = Visibility.Visible;
                BtnVoidPurchase.Visibility = Visibility.Visible;
                return;
            }

            // Final purchase: can Amend, Return With
            if (!sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmend.Visibility = Visibility.Visible;        // if you support revisions
                BtnReturnWith.Visibility = Visibility.Visible;   // return to supplier referencing this purchase
                return;
            }

            // Final return: can Amend Return, Void Return
            if (sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmendReturn.Visibility = Visibility.Visible;
                BtnVoidReturn.Visibility = Visibility.Visible;
                return;
            }

            // Voided docs: nothing (ReturnWithout stays visible globally)
        }


        private UiPurchaseRow? Pick() => PurchasesGrid.SelectedItem as UiPurchaseRow;

        // ===== Bottom buttons (click) =====
        private void BtnHeld_Click(object sender, RoutedEventArgs e) => OpenHeldPicker();

        private void Receive_Click(object sender, RoutedEventArgs e) => Receive_Executed(sender, null!);
        private void Amend_Click(object sender, RoutedEventArgs e) => Amend_Executed(sender, null!);
        private void Return_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!);
        private void Void_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        // ===== Command handlers =====
        private void Receive_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.Status != nameof(PurchaseStatus.Draft)) { MessageBox.Show("Only DRAFT purchases can be received."); return; }

            // TODO: open your Receive/Finalize dialog (collect OnReceive payments if any)
            MessageBox.Show($"Receive {sel.DocNoOrId}", "Receive");

            LoadPurchases();
        }

        private void Amend_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }

            // TODO: open your editor window with PurchaseId = sel.PurchaseId
            MessageBox.Show($"Amend {sel.DocNoOrId}", "Amend");

            LoadPurchases();
        }

        private void Return_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.Status != nameof(PurchaseStatus.Final)) { MessageBox.Show("Only FINAL purchases can have returns."); return; }

            // TODO: open Purchase Return flow
            MessageBox.Show($"Return for {sel.DocNoOrId}", "Return");

            LoadPurchases();
        }

        private void Void_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.Status != nameof(PurchaseStatus.Draft)) { MessageBox.Show("Only DRAFT purchases can be voided."); return; }

            var reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void draft {sel.DocNoOrId}\nEnter reason:", "Void Purchase", "Wrong entry");
            if (string.IsNullOrWhiteSpace(reason)) return;

            using var db = new PosClientDbContext(_opts);
            var p = db.Purchases.First(x => x.Id == sel.PurchaseId);
            if (p.Status != PurchaseStatus.Draft) { MessageBox.Show("Only DRAFT can be voided."); return; }

            p.Status = PurchaseStatus.Voided;
            p.UpdatedAtUtc = System.DateTime.UtcNow;
            p.UpdatedBy = "system"; // replace with current user
            db.SaveChanges();

            MessageBox.Show("Purchase voided.");
            LoadPurchases();
        }

        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private async void OpenHeldPicker()
        {
            var picker = new HeldPurchasesWindow(_opts) { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedPurchaseId.HasValue)
            {
                // keep this context alive while the editor window is open
                var db = new PosClientDbContext(_opts);
                try
                {
                    var svc = new PurchasesService(db);
                    var draft = await svc.LoadWithLinesAsync(picker.SelectedPurchaseId.Value);

                    var win = new PurchaseWindow(db) { Owner = this };   // ⬅️ pass db
                    win.LoadDraft(draft);                                 // your loader
                    win.ShowDialog();
                }
                finally
                {
                    db.Dispose();
                }

                LoadPurchases(); // refresh list after editor closes
            }
        }



        private void UpdateHeldButtonVisibility()
        {
            try
            {
                using var db = new PosClientDbContext(_opts);
                bool anyHeld = db.Purchases.AsNoTracking()
                                    .Any(p => p.Status == PurchaseStatus.Draft);
                if (BtnHeld != null)
                    BtnHeld.Visibility = anyHeld ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                if (BtnHeld != null) BtnHeld.Visibility = Visibility.Collapsed;
            }
        }

        private void AmendReturn_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!); // or dedicated flow
        private void VoidReturn_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);    // or dedicated flow

        private void VoidPurchase_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private void ReturnWith_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!);  // open “Return With” dialog (select base purchase)
        private void ReturnWithout_Click(object sender, RoutedEventArgs e)
        {
            // open “Return Without” dialog (free-form return to supplier)
            MessageBox.Show("Return without base purchase — TODO wire dialog");
        }

    }
}
