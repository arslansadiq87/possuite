// Pos.Client.Wpf/Windows/Purchases/PurchaseCenterWindow.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Formatting;         // for ProductNameComposer
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Services;       // AppState / AppCtx

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseCenterWindow : Window
    {
        public int? SelectedHeldPurchaseId { get; private set; }  // bubble selection back to caller
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;

        // ----- Grid row shapes -----
        public class UiPurchaseRow
        {
            public int PurchaseId { get; set; }
            public string DocNoOrId { get; set; } = "";
            public string Supplier { get; set; } = "";
            public string TsLocal { get; set; } = "";
            public string Status { get; set; } = "";
            public decimal GrandTotal { get; set; }
            public bool IsReturn { get; set; }     // ← IMPORTANT: set while loading
            public int Revision { get; set; }
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

            // Double-Esc to close (same feel as Sales center)
            this.PreviewKeyDown += PurchaseCenterWindow_PreviewKeyDown;

            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));

            PurchasesGrid.ItemsSource = _purchases;
            LinesGrid.ItemsSource = _lines;

            // defaults: last 30 days
            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;

            UpdateFilterSummary();
            LoadPurchases();
            UpdateHeldButtonVisibility();
        }

        private DateTime? _lastEscDown;
        private void PurchaseCenterWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var now = DateTime.UtcNow;
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
            // (Optional) maintain a summary label if you add one in XAML
        }

        private void Search_Click(object sender, RoutedEventArgs e) => LoadPurchases();
        private void Search_Executed(object sender, ExecutedRoutedEventArgs e) => LoadPurchases();

        // ===== Load & filter list (Purchases + Returns) =====
        private void LoadPurchases()
        {
            _purchases.Clear();

            DateTime? fromUtc = FromDate.SelectedDate?.Date.ToUniversalTime();
            DateTime? toUtc = ToDate.SelectedDate?.AddDays(1).Date.ToUniversalTime();

            using var db = new PosClientDbContext(_opts);

            // Base query includes Party for supplier display and BOTH purchases & returns
            var q = db.Purchases.AsNoTracking()
                    .Include(p => p.Party)  // ← ensures Supplier name is available
                    .Where(p =>
                        (!fromUtc.HasValue || (p.CreatedAtUtc >= fromUtc || p.ReceivedAtUtc >= fromUtc)) &&
                        (!toUtc.HasValue || (p.CreatedAtUtc < toUtc || p.ReceivedAtUtc < toUtc))
                    );

            // Search box: doc no / supplier / vendor invoice
            var term = (SearchBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                q = q.Where(p =>
                    (p.DocNo ?? "").Contains(term) ||
                    (p.VendorInvoiceNo ?? "").Contains(term) ||
                    (p.Party != null && p.Party.Name.Contains(term))
                );
            }

            // Pull minimal columns for speed and transform
            var list = q.OrderByDescending(p => p.ReceivedAtUtc ?? p.CreatedAtUtc)
                        .Select(p => new
                        {
                            p.Id,
                            p.DocNo,
                            Supplier = p.Party != null ? p.Party.Name : "",
                            Ts = p.ReceivedAtUtc ?? p.CreatedAtUtc,
                            p.Status,
                            p.GrandTotal,
                            p.Revision,
                            p.IsReturn
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
                    GrandTotal = r.GrandTotal,
                    Revision = r.Revision,
                    IsReturn = r.IsReturn               // ← KEY: now your action bar logic can differentiate
                });

            foreach (var r in rows)
                _purchases.Add(r);

            HeaderText.Text = "Select a document to view lines.";
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

            // Load purchase + lines
            var purchase = db.Purchases
                .Include(p => p.Lines)
                .First(p => p.Id == sel.PurchaseId);

            // Header (show PR / PO semantics via IsReturn)
            var kind = sel.IsReturn ? "Return" : "Purchase";
            HeaderText.Text =
                $"{kind} {sel.DocNoOrId}  Rev {purchase.Revision}  " +
                $"Status: {purchase.Status}  Grand: {purchase.GrandTotal:0.00}";

            // Compose friendly line display names (same as Sales)
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

        // ===== Bottom actions visibility =====
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

            // Final purchase: can Amend, Return With, Void (business rule: allow if you support it)
            if (!sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmend.Visibility = Visibility.Visible;
                BtnReturnWith.Visibility = Visibility.Visible;
                BtnVoidPurchase.Visibility = Visibility.Visible;
                return;
            }

            // Final return: can Amend Return, Void Return
            if (sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmendReturn.Visibility = Visibility.Visible;
                BtnVoidReturn.Visibility = Visibility.Visible;
                return;
            }
        }

        private UiPurchaseRow? Pick() => PurchasesGrid.SelectedItem as UiPurchaseRow;

        // ===== Bottom buttons (click) =====
        private void BtnHeld_Click(object sender, RoutedEventArgs e) => OpenHeldPicker();
        private void Receive_Click(object sender, RoutedEventArgs e) => Receive_Executed(sender, null!);
        private void Amend_Click(object sender, RoutedEventArgs e) => Amend_Executed(sender, null!);
        private void Return_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!);
        private void Void_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        // ===== Command handlers =====
        private void Receive_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.IsReturn) { MessageBox.Show("Returns cannot be received."); return; }
            if (sel.Status != nameof(PurchaseStatus.Draft)) { MessageBox.Show("Only DRAFT purchases can be received."); return; }

            // TODO: open your Receive/Finalize dialog (collect OnReceive payments if any)
            MessageBox.Show($"Receive {sel.DocNoOrId}", "Receive");
            LoadPurchases();
        }

        private void Amend_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.IsReturn) { MessageBox.Show("Use 'Amend Return' for returns."); return; }
            if (sel.Status != nameof(PurchaseStatus.Final)) { MessageBox.Show("Only FINAL purchases can be amended."); return; }

            var win = new EditPurchaseWindow(sel.PurchaseId) { Owner = this };
            if (win.ShowDialog() == true || win.Confirmed)
            {
                MessageBox.Show($"Amended to Revision {win.NewRevision}.");
                LoadPurchases();
            }
        }

        private void Return_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase/return first."); return; }

            if (!sel.IsReturn)
            {
                if (sel.Status != nameof(PurchaseStatus.Final))
                {
                    MessageBox.Show("Only FINAL purchases can have returns.");
                    return;
                }

                // “Return With” base purchase flow:
                // If you already have a PurchaseReturnWindow, open it here; otherwise keep this placeholder.
                // Example:
                var retWin = new PurchaseReturnWindow(refPurchaseId: sel.PurchaseId) { Owner = this };
                if (retWin.ShowDialog() == true) { LoadPurchases(); return; }

                //MessageBox.Show($"Return With base purchase {sel.DocNoOrId} — TODO: open return window.");
                LoadPurchases();
                return;
            }

            // If the selected row IS a return, route to amend-return
            AmendReturn_Click(sender!, new RoutedEventArgs());
        }

        private void Void_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a document."); return; }

            using var db = new PosClientDbContext(_opts);

            // ----- VOID RETURN -----
            if (sel.IsReturn)
            {
                if (sel.Status != nameof(PurchaseStatus.Final))
                {
                    MessageBox.Show("Only FINAL returns can be voided here.");
                    return;
                }

                var reason = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Void return {sel.DocNoOrId}\nEnter reason:", "Void Return", "Wrong return");
                if (string.IsNullOrWhiteSpace(reason)) return;

                try
                {
                    using var tx = db.Database.BeginTransaction();
                    var r = db.Purchases.Include(x => x.Lines).First(x => x.Id == sel.PurchaseId && x.IsReturn);
                    if (r.Status != PurchaseStatus.Final) { MessageBox.Show("Only FINAL returns can be voided."); return; }

                    // TODO: Reverse stock / supplier credit ledger if you post them.
                    r.Status = PurchaseStatus.Voided;
                    r.UpdatedAtUtc = DateTime.UtcNow;
                    r.UpdatedBy = AppState.Current?.CurrentUserName ?? "system";
                    db.SaveChanges();
                    tx.Commit();

                    MessageBox.Show("Return voided.");
                    LoadPurchases();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to void return: " + ex.Message);
                }
                return;
            }

            // ----- VOID PURCHASE (Draft/Final) -----
            if (sel.Status == nameof(PurchaseStatus.Draft))
            {
                var reason = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Void draft {sel.DocNoOrId}\nEnter reason:", "Void Purchase", "Wrong entry");
                if (string.IsNullOrWhiteSpace(reason)) return;

                var p = db.Purchases.First(x => x.Id == sel.PurchaseId && !x.IsReturn);
                if (p.Status != PurchaseStatus.Draft) { MessageBox.Show("Only DRAFT can be voided here."); return; }

                p.Status = PurchaseStatus.Voided;
                p.UpdatedAtUtc = DateTime.UtcNow;
                p.UpdatedBy = AppState.Current?.CurrentUserName ?? "system";
                db.SaveChanges();

                MessageBox.Show("Purchase voided.");
                LoadPurchases();
                return;
            }

            if (sel.Status == nameof(PurchaseStatus.Final))
            {
                var reason = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Void purchase {sel.DocNoOrId}\nEnter reason:", "Void Purchase", "Wrong purchase");
                if (string.IsNullOrWhiteSpace(reason)) return;

                try
                {
                    using var tx = db.Database.BeginTransaction();

                    var p = db.Purchases.Include(x => x.Lines).First(x => x.Id == sel.PurchaseId && !x.IsReturn);
                    if (p.Status != PurchaseStatus.Final) { MessageBox.Show("Only FINAL purchases can be voided here."); return; }

                    // NOTE: When you wire purchase stock posting, add symmetric reversal here.
                    p.Status = PurchaseStatus.Voided;
                    p.UpdatedAtUtc = DateTime.UtcNow;
                    p.UpdatedBy = AppState.Current?.CurrentUserName ?? "system";

                    db.SaveChanges();
                    tx.Commit();

                    MessageBox.Show("Purchase voided.");
                    LoadPurchases();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to void purchase: " + ex.Message);
                }
                return;
            }

            MessageBox.Show("Only DRAFT or FINAL documents can be voided.");
        }

        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private async void OpenHeldPicker()
        {
            var picker = new HeldPurchasesWindow(_opts) { Owner = this };

            // If user picked a draft, bubble the ID up to the caller (PurchaseWindow),
            // close Held picker AND close Invoice Center itself.
            if (picker.ShowDialog() == true && picker.SelectedPurchaseId.HasValue)
            {
                this.SelectedHeldPurchaseId = picker.SelectedPurchaseId;
                this.DialogResult = true;  // continue flow in caller
                this.Close();
                return;
            }

            // else: stay open
        }

        private void UpdateHeldButtonVisibility()
        {
            try
            {
                using var db = new PosClientDbContext(_opts);
                bool anyHeld = db.Purchases.AsNoTracking()
                                    .Any(p => p.Status == PurchaseStatus.Draft && !p.IsReturn);
                if (BtnHeld != null)
                    BtnHeld.Visibility = anyHeld ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                if (BtnHeld != null) BtnHeld.Visibility = Visibility.Collapsed;
            }
        }

        // ----- Action bar buttons (wired from XAML) -----
        private void AmendReturn_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a return."); return; }
            if (!sel.IsReturn) { MessageBox.Show("Pick a return to amend."); return; }
            // TODO: open your PurchaseReturnWindow in amend mode:
            var win = new PurchaseReturnWindow(returnId: sel.PurchaseId) { Owner = this };
            if (win.ShowDialog() == true) LoadPurchases();
            //MessageBox.Show($"Amend Return {sel.DocNoOrId} — TODO: open return window.");
        }

        private void VoidReturn_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private void VoidPurchase_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private void ReturnWith_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!); // “Return With…” selected purchase

        private void ReturnWithout_Click(object sender, RoutedEventArgs e)
        {
            // Free-form return (no base document)
            // TODO: open your free-form return window:
            var win = new PurchaseReturnWindow(); // without RefPurchaseId
            if (win.ShowDialog() == true) LoadPurchases();
            //MessageBox.Show("Return without base purchase — TODO: open return window.");
        }
    }
}
