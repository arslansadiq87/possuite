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
using Pos.Client.Wpf.Infrastructure;
using Pos.Persistence.Sync; // for IOutboxWriter
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Intrinsics.Arm;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class PurchaseCenterView : UserControl, IRefreshOnActivate
    {
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        public int? SelectedHeldPurchaseId { get; private set; }  // bubble selection back to caller
        private readonly IPurchaseCenterReadService _read;
        private readonly IPurchasesServiceFactory _svcFactory;

        public Style? CurrentButtonStyle { get; set; }
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

            public bool HasActiveReturn { get; set; }       // for ORIGINAL purchases

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
            _read = App.Services.GetRequiredService<IPurchaseCenterReadService>();
            _svcFactory = App.Services.GetRequiredService<IPurchasesServiceFactory>();
            PurchasesGrid.ItemsSource = _purchases;
            LinesGrid.ItemsSource = _lines;
            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;
            UpdateFilterSummary();
            LoadPurchases();
            _ = UpdateHeldButtonVisibilityAsync();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadPurchases();

        //private DateTime? _lastEscDown;
        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            PopupFilter.IsOpen = false;
            UpdateFilterSummary();
            LoadPurchases();
            _ = UpdateHeldButtonVisibilityAsync();
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
        }

        private void Search_Click(object sender, RoutedEventArgs e) => LoadPurchases();
        private void Search_Executed(object sender, ExecutedRoutedEventArgs e) => LoadPurchases();

        private async void LoadPurchases()
        {
            _purchases.Clear();
            DateTime? fromUtc = FromDate.SelectedDate?.Date.ToUniversalTime();
            DateTime? toUtc = ToDate.SelectedDate?.AddDays(1).Date.ToUniversalTime();
            bool wantFinal = ChkFinal.IsChecked == true;
            bool wantDraft = ChkDraft.IsChecked == true;
            bool wantVoided = ChkVoided.IsChecked == true;
            bool onlyWithDoc = ChkOnlyWithDocNo.IsChecked == true;
            var term = (SearchBox.Text ?? "").Trim();
            var rows = await _read.SearchAsync(fromUtc, toUtc, term, wantFinal, wantDraft, wantVoided, onlyWithDoc);
            foreach (var r in rows)
            {
                _purchases.Add(new UiPurchaseRow
                {
                    PurchaseId = r.PurchaseId,
                    DocNoOrId = r.DocNoOrId,
                    Supplier = r.Supplier,
                    TsLocal = r.TsLocal,
                    Status = r.Status,
                    GrandTotal = r.GrandTotal,
                    Revision = r.Revision,
                    IsReturn = r.IsReturn,
                    IsReturnWithInvoice = r.IsReturnWithInvoice
                });
            }

            HeaderText.Text = "Select a document to view lines.";
            _lines.Clear();
            UpdateActions(null);
        }

        private async void PurchasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _lines.Clear();
            if (PurchasesGrid.SelectedItem is not UiPurchaseRow sel)
            {
                HeaderText.Text = "";
                UpdateActions(null);
                return;
            }

            var kind = sel.IsReturn ? "Return" : "Purchase";
            var revPart = sel.HasRevisions ? $"  Rev {sel.Revision}  " : "  ";
            HeaderText.Text = $"{kind} {sel.DocNoOrId}{revPart}Status: {sel.Status}  Grand: {sel.GrandTotal:0.00}";

            var rows = await _read.GetPreviewLinesAsync(sel.PurchaseId);
            foreach (var r in rows)
                _lines.Add(new UiLineRow
                {
                    ItemId = r.ItemId,
                    Sku = r.Sku,
                    DisplayName = r.DisplayName,
                    Qty = r.Qty,
                    UnitCost = r.UnitCost,
                    LineTotal = r.LineTotal
                });

            // NEW: fetch and store guard flags on the selected row
            var (hasActive, isReturnWith) = await _read.GetPreviewActionGuardsAsync(sel.PurchaseId);
            sel.HasActiveReturn = hasActive;
            sel.IsReturnWithInvoice = isReturnWith;

            UpdateActions(sel); // keep your existing signature
        }

        private void UpdateActions(UiPurchaseRow? sel)
        {
            BtnAmend.Visibility = Visibility.Collapsed;
            BtnVoidPurchase.Visibility = Visibility.Collapsed;
            BtnReturnWith.Visibility = Visibility.Collapsed;
            BtnReturnWithout.Visibility = Visibility.Visible; // independent like Sales
            BtnReceive.Visibility = Visibility.Collapsed;
            BtnAmendReturn.Visibility = Visibility.Collapsed;
            BtnVoidReturn.Visibility = Visibility.Collapsed;
            if (sel is null) return;
            if (!sel.IsReturn && sel.Status == nameof(PurchaseStatus.Draft))
            {
                BtnReceive.Visibility = Visibility.Visible;
                BtnAmend.Visibility = Visibility.Visible;
                BtnVoidPurchase.Visibility = Visibility.Visible;
                return;
            }
            if (!sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmend.Visibility = sel.HasActiveReturn ? Visibility.Collapsed : Visibility.Visible;
                BtnReturnWith.Visibility = sel.HasActiveReturn ? Visibility.Collapsed : Visibility.Visible;
                BtnVoidPurchase.Visibility = sel.HasActiveReturn ? Visibility.Collapsed : Visibility.Visible;
                BtnVoidPurchase.ToolTip = sel.HasActiveReturn
           ? "Void blocked: this purchase has non-voided returns. Void those returns first."
           : null;
                BtnReturnWith.ToolTip = sel.HasActiveReturn
           ? "Return with Invoice blocked: this purchase has non-voided returns. You cannot add more returns against this invoice."
           : null;

                return;
            }
            if (sel.IsReturn && sel.Status == nameof(PurchaseStatus.Final))
            {
                BtnAmendReturn.Visibility = Visibility.Visible;
                BtnVoidReturn.Visibility = Visibility.Visible;
                return;
            }
        }

        private UiPurchaseRow? Pick() => PurchasesGrid.SelectedItem as UiPurchaseRow;

        private void BtnHeld_Click(object sender, RoutedEventArgs e) => OpenHeldPicker();
        private void Receive_Click(object sender, RoutedEventArgs e) => Receive_Executed(sender, null!);
        private void Amend_Click(object sender, RoutedEventArgs e) => Amend_Executed(sender, null!);
        private void Return_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!);
        private void Void_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private async void Receive_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a purchase."); return; }
            if (sel.IsReturn) { MessageBox.Show("Returns cannot be received."); return; }
            if (sel.Status != nameof(PurchaseStatus.Draft)) { MessageBox.Show("Only DRAFT purchases can be received."); return; }
            var svc = _svcFactory.Create();
            try
            {
                var draftEff = await svc.GetEffectiveLinesAsync(sel.PurchaseId);
                var header = new Purchase
                {
                    Id = sel.PurchaseId,
                    Status = PurchaseStatus.Final
                };
                var lines = draftEff.Select(x => new PurchaseLine
                {
                    ItemId = x.ItemId,
                    Qty = x.Qty,
                    UnitCost = x.UnitCost,
                    Discount = x.Discount,
                    TaxRate = x.TaxRate
                }).ToList();
                var user = AppState.Current?.CurrentUserName ?? "system";
                await svc.ReceiveAsync(header, lines, user);
                MessageBox.Show("Purchase received and posted.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadPurchases();
                _lines.Clear();
                UpdateActions(null);
            }
            catch (InvalidOperationException ex)
            {
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
                var retWin = new PurchaseReturnWindow(refPurchaseId: sel.PurchaseId) { Owner = Window.GetWindow(this) };
                if (retWin.ShowDialog() == true) { LoadPurchases(); return; }
                LoadPurchases();
                return;
            }

            AmendReturn_Click(sender!, new RoutedEventArgs());
        }

        private async void Void_Executed(object? sender, ExecutedRoutedEventArgs? e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a document."); return; }
            var user = AppState.Current?.CurrentUserName ?? "system";
            if (sel.IsReturn)
            {
                var reason = Microsoft.VisualBasic.Interaction.InputBox(
                    $"Void return {sel.DocNoOrId}\nEnter reason:", "Void Return", "Wrong return");
                if (string.IsNullOrWhiteSpace(reason)) return;

                try
                {
                    var retSvc = App.Services.GetRequiredService<IPurchaseReturnsService>();
                    await retSvc.VoidReturnAsync(sel.PurchaseId, reason, user);
                    MessageBox.Show("Return voided.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadPurchases();
                    _lines.Clear();
                    UpdateActions(null);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Void blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to void return: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            var reason2 = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void purchase {sel.DocNoOrId}\nEnter reason:", "Void Purchase", "Wrong purchase");
            if (string.IsNullOrWhiteSpace(reason2)) return;
            try
            {
                var svc = _svcFactory.Create();
                await svc.VoidPurchaseAsync(sel.PurchaseId, reason2, user);
                MessageBox.Show("Purchase voided.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadPurchases();           // refresh the list
                LinesGrid.ItemsSource = null; // optional: clear lines after void
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(ex.Message, "Void blocked", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to void purchase: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private void OpenHeldPicker()
        {
            var read = App.Services.GetRequiredService<IPurchaseCenterReadService>();
            var picker = new HeldPurchasesWindow(read) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true && picker.SelectedPurchaseId is int id)
            {
                SelectedHeldPurchaseId = id;
                if (Window.GetWindow(this) is Window host)
                {
                    host.DialogResult = true;
                    host.Close();
                }
            }
        }

        private async Task UpdateHeldButtonVisibilityAsync()
        {
            try
            {
                bool anyHeld = await _read.AnyHeldDraftAsync();
                if (BtnHeld != null)
                    BtnHeld.Visibility = anyHeld ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                if (BtnHeld != null) BtnHeld.Visibility = Visibility.Collapsed;
            }
        }

        private void AmendReturn_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a return."); return; }
            if (!sel.IsReturn) { MessageBox.Show("Pick a return to amend."); return; }
            var win = new PurchaseReturnWindow(returnId: sel.PurchaseId) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true) LoadPurchases();
        }

        public void OnActivated()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefreshUtc < TimeSpan.FromMilliseconds(250)) return;
            _lastRefreshUtc = now;
            if (!IsLoaded) Dispatcher.BeginInvoke(new Action(LoadPurchases));
            else LoadPurchases();
        }

        private void VoidReturn_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private void VoidPurchase_Click(object sender, RoutedEventArgs e) => Void_Executed(sender, null!);

        private void ReturnWith_Click(object sender, RoutedEventArgs e) => Return_Executed(sender, null!); // “Return With…” selected purchase

        private void ReturnWithout_Click(object sender, RoutedEventArgs e)
        {
            var win = new PurchaseReturnWindow(); // without RefPurchaseId
            if (win.ShowDialog() == true) LoadPurchases();
        }
    }
}