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
using Pos.Client.Wpf.Services;
using System.Windows.Controls;       // AppState / AppCtx

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseCenterView : UserControl
    {
        public int? SelectedHeldPurchaseId { get; private set; }  // bubble selection back to caller
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        public Style? CurrentButtonStyle { get; set; }

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
            public bool HasRevisions => Revision > 1;
            public bool IsReturnWithInvoice { get; set; }

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

        public PurchaseCenterView()
        {
            InitializeComponent();

            // Double-Esc to close (same feel as Sales center)
            //this.PreviewKeyDown += PurchaseCenterWindow_PreviewKeyDown;

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
        //private void PurchaseCenterWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        //{
        //    if (e.Key == Key.Escape)
        //    {
        //        var now = DateTime.UtcNow;
        //        if (_lastEscDown.HasValue && (now - _lastEscDown.Value).TotalMilliseconds <= 600)
        //        {
        //            Close();
        //            return;
        //        }
        //        _lastEscDown = now;
        //        e.Handled = true;
        //    }
        //}

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
                    p.IsReturn,
                    p.RefPurchaseId            // << add this
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
                    GrandTotal = r.IsReturn ? -Math.Abs(r.GrandTotal) : Math.Abs(r.GrandTotal),
                    Revision = r.Revision,
                    IsReturn = r.IsReturn,               // ← KEY: now your action bar logic can differentiate
                    IsReturnWithInvoice = r.IsReturn && r.RefPurchaseId.HasValue
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

            // Load the purchase (base immutable lines)
            var purchase = db.Purchases
                .Include(p => p.Lines)
                .AsNoTracking()
                .First(p => p.Id == sel.PurchaseId);

            // Is this a return?
            var isReturn = purchase.IsReturn || sel.IsReturn;

            // Header
            var kind = isReturn ? "Return" : "Purchase";
            var revPart = purchase.Revision > 1 ? $"  Rev {purchase.Revision}  " : "  ";
            HeaderText.Text = $"{kind} {sel.DocNoOrId}{revPart}Status: {purchase.Status}  Grand: {purchase.GrandTotal:0.00}";

            // -------- fold in prior amendment deltas (qty only) --------
            var refTypeAmend = isReturn ? "PurchaseReturnAmend" : "PurchaseAmend";

            var amendQtyByItem = db.StockEntries
                .AsNoTracking()
                .Where(se => se.RefType == refTypeAmend && se.RefId == purchase.Id)
                .GroupBy(se => se.ItemId)
                .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QtyChange) })
                .ToDictionary(x => x.ItemId, x => x.Qty);

            // Base (original immutable) grouped per item
            var baseByItem = purchase.Lines
                .GroupBy(l => l.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => new
                    {
                        Qty = g.Sum(x => x.Qty),                              // may be negative for returns
                        AvgUnitCost = g.Any() ? Math.Round(g.Average(x => x.UnitCost), 2) : 0m,
                        LineTotal = g.Sum(x => x.LineTotal)
                    });

            // Get meta for all items present in base or amendments
            var itemIds = baseByItem.Keys.Union(amendQtyByItem.Keys).Distinct().ToList();

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
                    i.Sku,
                    i.Price
                }
            ).ToList().ToDictionary(x => x.Id);

            // Build EFFECTIVE preview rows
            foreach (var itemId in itemIds)
            {
                baseByItem.TryGetValue(itemId, out var b);
                amendQtyByItem.TryGetValue(itemId, out var aQty);

                var effectiveQty = (b?.Qty ?? 0m) + aQty;         // purchases: positive; returns: likely negative
                var displayQty = isReturn ? Math.Abs(effectiveQty) : effectiveQty;

                if (displayQty == 0m) continue;                  // only skip when nothing left to show

                // Display cost: prefer base avg; if item exists only via amendments, fallback to default price
                var unitCost = b?.AvgUnitCost ?? (meta.TryGetValue(itemId, out var m0) ? (m0?.Price ?? 0m) : 0m);

                // Friendly name + SKU
                string display = $"Item #{itemId}";
                string sku = "";
                if (meta.TryGetValue(itemId, out var m))
                {
                    display = ProductNameComposer.Compose(
                        m.ProductName, m.ItemName,
                        m.Variant1Name, m.Variant1Value,
                        m.Variant2Name, m.Variant2Value);
                    sku = m.Sku ?? "";
                }

                _lines.Add(new UiLineRow
                {
                    ItemId = itemId,
                    Sku = sku,
                    DisplayName = display,
                    Qty = displayQty,                                 // ← returns now show positive qty
                    UnitCost = unitCost,
                    LineTotal = Math.Round(displayQty * unitCost, 2)        // preview only
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
        private async void Receive_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.IsReturn) { MessageBox.Show("Returns cannot be received."); return; }
            if (sel.Status != nameof(PurchaseStatus.Draft)) { MessageBox.Show("Only DRAFT purchases can be received."); return; }

            try
            {
                using var db = new PosClientDbContext(_opts);
                var svc = new PurchasesService(db);

                // Load draft header + lines
                var draft = await db.Purchases
                    .Include(p => p.Lines)
                    .FirstOrDefaultAsync(p => p.Id == sel.PurchaseId);

                if (draft == null) { MessageBox.Show("Draft not found."); return; }
                if (draft.Status != PurchaseStatus.Draft) { MessageBox.Show("This document is not in Draft anymore."); return; }

                // Build a minimal header model in FINAL status
                var header = new Purchase
                {
                    Id = draft.Id,
                    PartyId = draft.PartyId,
                    TargetType = draft.TargetType,
                    OutletId = draft.OutletId,
                    WarehouseId = draft.WarehouseId,
                    VendorInvoiceNo = draft.VendorInvoiceNo,
                    PurchaseDate = draft.PurchaseDate,
                    Status = PurchaseStatus.Final
                };

                // Effective lines (for drafts this is normally same as base lines)
                // You already have a helper used in the editor:
                var eff = await svc.GetEffectiveLinesAsync(draft.Id); // (ItemId, Qty, UnitCost, Discount, TaxRate, …)

                var lines = eff.Select(x => new PurchaseLine
                {
                    ItemId = x.ItemId,
                    Qty = x.Qty,
                    UnitCost = x.UnitCost,
                    Discount = x.Discount,
                    TaxRate = x.TaxRate
                }).ToList();

                var user = AppState.Current?.CurrentUserName ?? "system";

                // Finalize + write stock via the same service the editor uses
                await svc.ReceiveAsync(header, lines, user);

                MessageBox.Show("Purchase received and posted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadPurchases();         // refresh list
                _lines.Clear();          // clear preview
                UpdateActions(null);     // reset action bar
            }
            catch (InvalidOperationException ex)
            {
                // business rules (e.g., negative stock guard on amendments etc.)
                MessageBox.Show(ex.Message, "Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (DbUpdateException ex)
            {
                MessageBox.Show("Database error: " + ex.GetBaseException().Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unexpected error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Amend_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.IsReturn) { MessageBox.Show("Use 'Amend Return' for returns."); return; }
            if (sel.Status != nameof(PurchaseStatus.Final)) { MessageBox.Show("Only FINAL purchases can be amended."); return; }

            var win = new EditPurchaseWindow(sel.PurchaseId) { Owner = Window.GetWindow(this) };
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
                var retWin = new PurchaseReturnWindow(refPurchaseId: sel.PurchaseId) { Owner = Window.GetWindow(this) };
                if (retWin.ShowDialog() == true) { LoadPurchases(); return; }

                //MessageBox.Show($"Return With base purchase {sel.DocNoOrId} — TODO: open return window.");
                LoadPurchases();
                return;
            }

            // If the selected row IS a return, route to amend-return
            AmendReturn_Click(sender!, new RoutedEventArgs());
        }

        // At top of the class you already construct _svc; reuse it here.

        private async void Void_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a document."); return; }

            var user = AppState.Current?.CurrentUserName ?? "system";

            // VOID RETURN
            if (sel.IsReturn)
            {
                var reason = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Void return {sel.DocNoOrId}\nEnter reason:", "Void Return", "Wrong return");
                if (string.IsNullOrWhiteSpace(reason)) return;

                try
                {
                    await _svc.VoidReturnAsync(sel.PurchaseId, reason, user);
                    MessageBox.Show("Return voided.");
                    LoadPurchases();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to void return: " + ex.Message);
                }
                return;
            }

            // VOID PURCHASE (Draft or Final)
            var reason2 = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void purchase {sel.DocNoOrId}\nEnter reason:", "Void Purchase", "Wrong purchase");
            if (string.IsNullOrWhiteSpace(reason2)) return;

            try
            {
                await _svc.VoidPurchaseAsync(sel.PurchaseId, reason2, user);
                MessageBox.Show("Purchase voided.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadPurchases();           // refresh the list
                LinesGrid.ItemsSource = null; // optional: clear lines after void
            }
            catch (InvalidOperationException ex)
            {
                // will show: "Cannot void purchase — it would make stock negative: ..."
                MessageBox.Show(ex.Message, "Void blocked", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to void purchase: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private async void OpenHeldPicker()
        {
            var picker = new HeldPurchasesWindow(_opts) { Owner = Window.GetWindow(this) };

            // If user picked a draft, bubble the ID up to the caller (PurchaseWindow),
            // close Held picker AND close Invoice Center itself.
            if (picker.ShowDialog() == true && picker.SelectedPurchaseId.HasValue)
            {
                this.SelectedHeldPurchaseId = picker.SelectedPurchaseId;
                var owner = Window.GetWindow(this);   // the Window hosting this view
                if (owner != null)
                {
                    owner.DialogResult = true;        // signal OK to the caller
                    owner.Close();                    // close the dialog window
                }
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
            var win = new PurchaseReturnWindow(returnId: sel.PurchaseId) { Owner = Window.GetWindow(this) };
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
