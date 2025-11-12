//Pos.Client.Wpf/InvoiceCenterWindow.xaml.cs
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Pos.Domain;
using Pos.Domain.Services;
using System.Windows.Controls;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class InvoiceCenterView : UserControl, IRefreshOnActivate
    {
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private readonly int _outletId;
        private readonly int _counterId;
        private readonly IInvoiceService _inv;
        public static readonly RoutedUICommand CmdAmendReturn = new("Amend Return", "CmdAmendReturn", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdVoidReturn = new("Void Return", "CmdVoidReturn", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdAmend = new("Amend", "CmdAmend", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdReturnWith = new("Return With", "CmdReturnWith", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdReturnWithout = new("Return Without", "CmdReturnWithout", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdVoidSale = new("Void Sale", "CmdVoidSale", typeof(InvoiceCenterView));
    
        public int? SelectedHeldSaleId { get; private set; } = null;

        public class UiInvoiceRow
        {
            public int SaleId { get; set; }
            public int CounterId { get; set; }
            public int InvoiceNumber { get; set; }
            public int Revision { get; set; }
            public SaleStatus Status { get; set; }
            public bool IsReturn { get; set; }
            public string TsLocal { get; set; } = "";
            public string Customer { get; set; } = "";
            public decimal Total { get; set; }
            public bool HasRevisions => Revision > 0;
            public bool IsReturnWithInvoice { get; set; }   // NEW
        }
        public class UiLineRow
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public int Qty { get; set; }
            public decimal Price { get; set; }
            public decimal LineTotal { get; set; }
        }

        private readonly ObservableCollection<UiInvoiceRow> _invoices = new();
        private readonly ObservableCollection<UiLineRow> _lines = new();

        public InvoiceCenterView(int outletId, int counterId)
        {
            InitializeComponent();
            _outletId = outletId; _counterId = counterId;
            _inv = App.Services.GetRequiredService<IInvoiceService>();
            InvoicesGrid.ItemsSource = _invoices;
            LinesGrid.ItemsSource = _lines;
            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;
            ChkSales.IsChecked = true;
            ChkReturns.IsChecked = true;
            ChkFinal.IsChecked = true;
            ChkDraft.IsChecked = true;
            ChkVoided.IsChecked = true;   // show voided by default
            ChkOnlyWithInvNo.IsChecked = false;  // include invoices with no number
            ChkSingleTypeMode.IsChecked = false;
            UpdateFilterSummary();
            LoadInvoices();
        }

        public InvoiceCenterView()
       : this(AppState.Current.CurrentOutletId, AppState.Current.CurrentCounterId)
        {
        }

        private void UpdateHeldButtonVisibility()
        {
            _ = Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    bool anyHeld = await _inv.AnyHeldAsync(_outletId, _counterId);
                    if (BtnHeld != null)
                        BtnHeld.Visibility = anyHeld ? Visibility.Visible : Visibility.Collapsed;
                }
                catch { if (BtnHeld != null) BtnHeld.Visibility = Visibility.Collapsed; }
            });
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadInvoices();
        private void BtnHeld_Click(object sender, RoutedEventArgs e) => OpenHeldPicker();
        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private void OpenHeldPicker()
        {
            var picker = new HeldPickerWindow(_outletId, _counterId)
            { Owner = System.Windows.Window.GetWindow(this) };
            if (picker.ShowDialog() == true && picker.SelectedSaleId.HasValue)
            {
                this.SelectedHeldSaleId = picker.SelectedSaleId;
                var owner = Window.GetWindow(this);   // the Window hosting this view
                if (owner != null)
                {
                    owner.DialogResult = true;        // signal OK to the caller
                    owner.Close();                    // close the dialog window
                }
                return;
            }
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            PopupFilter.IsOpen = false;
            UpdateFilterSummary();
            LoadInvoices();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            ChkSales.IsChecked = true;
            ChkReturns.IsChecked = true;
            ChkFinal.IsChecked = true;
            ChkDraft.IsChecked = true;
            ChkVoided.IsChecked = true;   // << change
            ChkOnlyWithInvNo.IsChecked = false;  // << change
            ChkSingleTypeMode.IsChecked = false;
            UpdateFilterSummary();
        }

        private void UpdateFilterSummary()
        {
            string type = (ChkSales.IsChecked == true && ChkReturns.IsChecked == true) ? "Sales+Returns"
                       : (ChkSales.IsChecked == true) ? "Sales"
                       : (ChkReturns.IsChecked == true) ? "Returns" : "None";
            var statuses = new System.Collections.Generic.List<string>();
            if (ChkFinal.IsChecked == true) statuses.Add("Final");
            if (ChkDraft.IsChecked == true) statuses.Add("Draft");
            if (ChkVoided.IsChecked == true) statuses.Add("Voided");
            string statusPart = statuses.Count == 0 ? "No status" : string.Join(", ", statuses);
            string invPart = (ChkOnlyWithInvNo.IsChecked == true) ? "with #" : "all #";
        }

        private void ChkSingleTypeMode_Checked(object sender, RoutedEventArgs e)
        {
            ChkSales.Checked += TypeBox_Checked_ToggleOther;
            ChkReturns.Checked += TypeBox_Checked_ToggleOther;
        }
        private void ChkSingleTypeMode_Unchecked(object sender, RoutedEventArgs e)
        {
            ChkSales.Checked -= TypeBox_Checked_ToggleOther;
            ChkReturns.Checked -= TypeBox_Checked_ToggleOther;
        }
        private void TypeBox_Checked_ToggleOther(object sender, RoutedEventArgs e)
        {
            if (ChkSingleTypeMode.IsChecked == true)
            {
                if (sender == ChkSales) ChkReturns.IsChecked = false;
                if (sender == ChkReturns) ChkSales.IsChecked = false;
            }
        }

        private void LoadInvoices()
        {
            _invoices.Clear();

            DateTime? fromUtc = FromDate.SelectedDate?.Date.ToUniversalTime();
            DateTime? toUtc = ToDate.SelectedDate?.AddDays(1).Date.ToUniversalTime();
            var list = _inv.SearchLatestInvoicesAsync(_outletId, _counterId, fromUtc, toUtc, SearchBox.Text).GetAwaiter().GetResult();
            var returnIds = list.Where(r => r.IsReturn).Select(r => r.SaleId).ToList();
            var hasBaseById = (returnIds.Count == 0)
                ? new Dictionary<int, bool>()
                : _inv.GetReturnHasBaseMapAsync(returnIds).GetAwaiter().GetResult();
            var rows = list.Select(r => new UiInvoiceRow
            {
                SaleId = r.SaleId,
                CounterId = r.CounterId,
                InvoiceNumber = r.InvoiceNumber,
                Revision = r.Revision,
                Status = r.Status,
                IsReturn = r.IsReturn,
                IsReturnWithInvoice = r.IsReturn && hasBaseById.TryGetValue(r.SaleId, out var hb) && hb,
                TsLocal = r.TsUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                Customer = r.Customer,
                Total = r.IsReturn ? -Math.Abs(r.Total) : Math.Abs(r.Total),
            });
            bool wantSales = ChkSales.IsChecked == true;
            bool wantReturns = ChkReturns.IsChecked == true;
            bool wantFinal = ChkFinal.IsChecked == true;
            bool wantDraft = ChkDraft.IsChecked == true;
            bool wantVoided = ChkVoided.IsChecked == true;
            bool onlyWithInv = ChkOnlyWithInvNo.IsChecked == true;
            rows = rows.Where(r =>
                ((r.IsReturn && wantReturns) || (!r.IsReturn && wantSales))
                && ((r.Status == SaleStatus.Final && wantFinal)
                 || (r.Status == SaleStatus.Draft && wantDraft)
                 || (r.Status == SaleStatus.Voided && wantVoided))
                && (!onlyWithInv || r.InvoiceNumber > 0)
            );
            foreach (var r in rows) _invoices.Add(r);
            HeaderText.Text = "Select an invoice to view details.";
            _lines.Clear();
            UpdateActions(null);
            UpdateHeldButtonVisibility();
        }

        private void Search_Click(object sender, RoutedEventArgs e) => LoadInvoices();
        private void Search_Executed(object sender, ExecutedRoutedEventArgs e) => LoadInvoices();
        private void InvoicesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _lines.Clear();
            if (InvoicesGrid.SelectedItem is not UiInvoiceRow sel)
            {
                HeaderText.Text = "";
                UpdateActions(null);
                return;
            }
            var(header, lines) = _inv.LoadSaleWithLinesAsync(sel.SaleId).GetAwaiter().GetResult();
            var displayTotal = header.IsReturn ? -Math.Abs(header.Total) : Math.Abs(header.Total);
            var revPart = header.Revision > 0 ? $"  Rev {header.Revision}  " : "  ";
            HeaderText.Text =
            $"Invoice {header.CounterId}-{header.InvoiceNumber}{revPart}" +
            $"Status: {header.Status}  {(header.IsReturn ? "[RETURN]" : "")}  Total: {displayTotal:0.00}";
            
            foreach (var l in lines)
            {
                _lines.Add(new UiLineRow
                {
                    ItemId = l.ItemId,
                    Sku = l.Sku,
                    DisplayName = l.DisplayName,
                    Qty = l.Qty,
                    Price = l.UnitPrice,
                    LineTotal = l.LineTotal
                });
            }
            UpdateActions(sel);
        }

        private void UpdateActions(UiInvoiceRow? sel)
        {
            BtnAmend.Visibility = Visibility.Collapsed;
            BtnReturnWith.Visibility = Visibility.Collapsed;
            BtnAmendReturn.Visibility = Visibility.Collapsed;
            BtnVoidReturn.Visibility = Visibility.Collapsed;
            BtnVoidSale.Visibility = Visibility.Collapsed; // << add this
            BtnReturnWithout.Visibility = Visibility.Visible;
            if (sel is null) return;
            if (sel.IsReturn)
            {
                if (sel.Status == SaleStatus.Final)
                {
                    BtnAmendReturn.Visibility = Visibility.Visible;
                    BtnVoidReturn.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (sel.Status == SaleStatus.Final)
                {
                    BtnAmend.Visibility = Visibility.Visible;
                    BtnReturnWith.Visibility = Visibility.Visible;
                    bool hasReturn = _inv.HasNonVoidedReturnAgainstAsync(sel.SaleId).GetAwaiter().GetResult();
                    BtnVoidSale.Visibility = hasReturn ? Visibility.Collapsed : Visibility.Visible;
                    BtnAmend.Visibility = hasReturn ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }
        private UiInvoiceRow? Pick() => InvoicesGrid.SelectedItem as UiInvoiceRow;

        private void Amend_Click(object sender, RoutedEventArgs e)
        {
            if (InvoicesGrid.SelectedItem is not UiInvoiceRow sel)
            {
                MessageBox.Show("Select an invoice."); return;
            }
            if (sel.IsReturn)
            {
                MessageBox.Show("Returns cannot be amended."); return;
            }
            if (sel.Status != SaleStatus.Final)
            {
                MessageBox.Show("Only FINAL invoices can be amended."); return;
            }

            bool hasReturn = _inv.HasNonVoidedReturnAgainstAsync(sel.SaleId).GetAwaiter().GetResult();
            if (hasReturn)
            {
                MessageBox.Show("This sale has a return against it and cannot be amended.", "Blocked");
                return;
            }

            var win = new Pos.Client.Wpf.Windows.Sales.EditSaleWindow(sel.SaleId)
            {
                Owner = Window.GetWindow(this)
            };
            if (win.ShowDialog() == true && win.Confirmed)
            {
                MessageBox.Show($"Amended to Revision {win.NewRevision}.");
                LoadInvoices();
            }
        }

        private void ReturnWithInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (InvoicesGrid.SelectedItem is not UiInvoiceRow sel) { MessageBox.Show("Select an invoice."); return; }
            if (sel.IsReturn) { MessageBox.Show("This is already a return."); return; }
            if (sel.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be returned."); return; }
            var win = new ReturnFromInvoiceWindow(sel.SaleId) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && win.Confirmed)
            {
                MessageBox.Show($"Return saved. Credit: {win.RefundMagnitude:0.00}");
                LoadInvoices();
            }
        }

        private void ReturnWithoutInvoice_Click(object sender, RoutedEventArgs e)
        {
            OpenReturnWithoutInvoiceDialog();
        }

        private void OpenReturnWithoutInvoiceDialog()
        {
            var w = new ReturnWithoutInvoiceWindow(_outletId, _counterId)
            { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true)
                LoadInvoices();
        }
        private void RefreshGrid()
        {
        }

        private void AmendReturn_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select an invoice."); return; }
            if (!sel.IsReturn) { MessageBox.Show("This action is for return invoices only."); return; }
            if (sel.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL documents can be amended."); return; }
            var win = new Pos.Client.Wpf.Windows.Sales.EditReturnWindow(sel.SaleId) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && win.Confirmed)
            {
                MessageBox.Show($"Return amended (Rev {win.NewRevision}).");
                LoadInvoices();
            }
        }

        private async void VoidReturn_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select an invoice."); return; }
            if (!sel.IsReturn) { MessageBox.Show("This action is for return invoices only."); return; }
            if (sel.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL returns can be voided."); return; }
            var reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void return {sel.CounterId}-{sel.InvoiceNumber} (Rev {sel.Revision})\nEnter reason:",
                "Void Return", "Wrong return");
            if (string.IsNullOrWhiteSpace(reason)) return;
            try
            {
                await _inv.VoidReturnAsync(sel.SaleId, reason);
                MessageBox.Show("Return voided.");
                LoadInvoices();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to void return: " + ex.Message);
            }
        }

        private async void VoidSale_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select an invoice."); return; }
            if (sel.IsReturn) { MessageBox.Show("This action is for normal sale invoices, not returns."); return; }
            if (sel.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be voided."); return; }
            bool hasReturn = _inv.HasNonVoidedReturnAgainstAsync(sel.SaleId).GetAwaiter().GetResult();
            if (hasReturn)
            {
                MessageBox.Show("This sale has a return against it and cannot be voided.", "Blocked");
                return;
            }

            var reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void sale {sel.CounterId}-{sel.InvoiceNumber} (Rev {sel.Revision})\nEnter reason:",
                "Void Sale", "Wrong sale");
            if (string.IsNullOrWhiteSpace(reason)) return;
            try
            {
                await _inv.VoidSaleAsync(sel.SaleId, reason);
                MessageBox.Show("Sale voided.");
                LoadInvoices();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to void sale: " + ex.Message);
            }
        }

        public void OnActivated()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefreshUtc < TimeSpan.FromMilliseconds(250)) return;
            _lastRefreshUtc = now;
            if (!IsLoaded) Dispatcher.BeginInvoke(new Action(LoadInvoices));
            else LoadInvoices();
        }
        private void AmendReturn_Executed(object sender, ExecutedRoutedEventArgs e) => AmendReturn_Click(sender, e);
        private void VoidReturn_Executed(object sender, ExecutedRoutedEventArgs e) => VoidReturn_Click(sender, e);
        private void Amend_Executed(object sender, ExecutedRoutedEventArgs e) => Amend_Click(sender, e);
        private void ReturnWith_Executed(object sender, ExecutedRoutedEventArgs e) => ReturnWithInvoice_Click(sender, e);
        private void ReturnWithout_Executed(object sender, ExecutedRoutedEventArgs e) => ReturnWithoutInvoice_Click(sender, e);
        private void VoidSale_Executed(object sender, ExecutedRoutedEventArgs e) => VoidSale_Click(sender, e);
    }
}
