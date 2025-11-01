//Pos.Client.Wpf/InvoiceCenterWindow.xaml.cs
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Domain.Formatting; // <-- add this
using Pos.Domain.Services;
using System.Windows.Controls;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Infrastructure;


namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class InvoiceCenterView : UserControl, IRefreshOnActivate
    {
        private DateTime _lastRefreshUtc = DateTime.MinValue;

        private readonly int _outletId;
        private readonly int _counterId;
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly InvoiceService _svc;
        // Global commands for shortcuts
        public static readonly RoutedUICommand CmdAmendReturn = new("Amend Return", "CmdAmendReturn", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdVoidReturn = new("Void Return", "CmdVoidReturn", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdAmend = new("Amend", "CmdAmend", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdReturnWith = new("Return With", "CmdReturnWith", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdReturnWithout = new("Return Without", "CmdReturnWithout", typeof(InvoiceCenterView));
        public static readonly RoutedUICommand CmdVoidSale = new("Void Sale", "CmdVoidSale", typeof(InvoiceCenterView));
        private DateTime? _lastEscDown;
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
            // Double-Esc to close
            //this.PreviewKeyDown += InvoiceCenterWindow_PreviewKeyDown;
            _outletId = outletId; _counterId = counterId;
            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;
            _svc = new InvoiceService(_opts);
            InvoicesGrid.ItemsSource = _invoices;
            LinesGrid.ItemsSource = _lines;
            // defaults: last 30 days
            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;
            // ✅ Show everything by default (including Voided and invoices without #)
            ChkSales.IsChecked = true;
            ChkReturns.IsChecked = true;
            ChkFinal.IsChecked = true;
            ChkDraft.IsChecked = true;
            ChkVoided.IsChecked = true;   // show voided by default
            ChkOnlyWithInvNo.IsChecked = false;  // include invoices with no number
            ChkSingleTypeMode.IsChecked = false;

            UpdateFilterSummary();
            LoadInvoices();
            //UpdateHeldButtonVisibility();
        }

        public InvoiceCenterView()
       : this(AppState.Current.CurrentOutletId, AppState.Current.CurrentCounterId)
        {
        }


        private void UpdateHeldButtonVisibility()
        {
            try
            {
                using var db = new PosClientDbContext(_opts);
                bool anyHeld = db.Sales.AsNoTracking()
                    .Any(s => s.OutletId == _outletId
                           && s.CounterId == _counterId
                           && s.Status == SaleStatus.Draft);
                if (BtnHeld != null)
                    BtnHeld.Visibility = anyHeld ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                if (BtnHeld != null) BtnHeld.Visibility = Visibility.Collapsed;
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadInvoices();

        private void BtnHeld_Click(object sender, RoutedEventArgs e) => OpenHeldPicker();

        private void Held_Executed(object sender, ExecutedRoutedEventArgs e) => OpenHeldPicker();

        private void OpenHeldPicker()
        {
            var picker = new HeldPickerWindow(_opts, _outletId, _counterId) { Owner = Window.GetWindow(this) };
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



        //private void InvoiceCenterWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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
        //        e.Handled = true; // swallow single Esc
        //    }
        //}

        // Filter popup buttons
        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            PopupFilter.IsOpen = false;
            UpdateFilterSummary();
            LoadInvoices();
            //UpdateHeldButtonVisibility();
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
            //FilterSummary.Text = $"{type} • {statusPart} • {invPart}";
        }

        // Single-type mode (Sales XOR Returns)
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

            // 1) Get the list and materialize it (we’ll need the ids twice)
            var list = _svc.SearchLatestInvoices(_outletId, _counterId, fromUtc, toUtc, SearchBox.Text)
                           .ToList();

            // 2) Build a map for returns: SaleId -> has base (RefSaleId OR OriginalSaleId)
            var returnIds = list.Where(r => r.IsReturn).Select(r => r.SaleId).ToList();
            var hasBaseById = new Dictionary<int, bool>();
            if (returnIds.Count > 0)
            {
                using var db = new PosClientDbContext(_opts);
                hasBaseById = db.Sales.AsNoTracking()
                                      .Where(s => returnIds.Contains(s.Id))
                                      .Select(s => new
                                      {
                                          s.Id,
                                          HasBase = (s.RefSaleId != null) || (s.OriginalSaleId != null)  // ← key change
                                      })
                                      .ToDictionary(x => x.Id, x => x.HasBase);
            }


            // 3) Project UI rows (arrow always for IsReturn; icon only when IsReturnWithInvoice)
            var rows = list.Select(r => new UiInvoiceRow
            {
                SaleId = r.SaleId,
                CounterId = r.CounterId,
                InvoiceNumber = r.InvoiceNumber,
                Revision = r.Revision,
                Status = r.Status,
                IsReturn = r.IsReturn,
                IsReturnWithInvoice = r.IsReturn && hasBaseById.TryGetValue(r.SaleId, out var hb) && hb, // NEW
                TsLocal = r.TsUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                Customer = r.Customer,
                Total = r.IsReturn ? -Math.Abs(r.Total) : Math.Abs(r.Total),
            });

            // 4) Apply your existing dropdown filters (unchanged)
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

            var (sale, lines) = _svc.LoadSaleWithLines(sel.SaleId);
            var displayTotal = sale.IsReturn ? -Math.Abs(sale.Total) : Math.Abs(sale.Total);
            var revPart = sale.Revision > 0 ? $"  Rev {sale.Revision}  " : "  ";
            HeaderText.Text = $"Invoice {sale.CounterId}-{sale.InvoiceNumber}{revPart}" +
                              $"Status: {sale.Status}  {(sale.IsReturn ? "[RETURN]" : "")}  Total: {displayTotal:0.00}";


            // 👇 add this block INSIDE the method, before using meta
            using var db = new PosClientDbContext(_opts);
            var itemIds = lines.Select(t => t.line.ItemId).Distinct().ToList();
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
                    i.Variant2Value
                }
            ).ToList().ToDictionary(x => x.Id);
            foreach (var (line, fallbackName, sku) in lines)
            {
                string display = fallbackName;
                if (meta.TryGetValue(line.ItemId, out var m))
                {
                    display = ProductNameComposer.Compose(
                        m.ProductName, m.ItemName,
                        m.Variant1Name, m.Variant1Value,
                        m.Variant2Name, m.Variant2Value
                    );
                }
                _lines.Add(new UiLineRow
                {
                    ItemId = line.ItemId,
                    Sku = sku,
                    DisplayName = display, // changed from Name to ItemName
                    Qty = line.Qty,
                    Price = line.UnitPrice,
                    LineTotal = line.LineTotal
                });
            }
            UpdateActions(sel);
        }


        // Show/hide bottom buttons depending on selection
        private void UpdateActions(UiInvoiceRow? sel)
        {
            BtnAmend.Visibility = Visibility.Collapsed;
            BtnReturnWith.Visibility = Visibility.Collapsed;
            BtnAmendReturn.Visibility = Visibility.Collapsed;
            BtnVoidReturn.Visibility = Visibility.Collapsed;
            BtnVoidSale.Visibility = Visibility.Collapsed; // << add this
            // independent button
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

                    // 🔒 Hide Void if there is any non-voided return against this sale
                    bool hasReturn;
                    using (var db = new PosClientDbContext(_opts))
                    {
                        hasReturn = db.Sales.AsNoTracking()
                            .Any(s => s.IsReturn
                                   && (s.OriginalSaleId == sel.SaleId || s.RefSaleId == sel.SaleId)
                                   && s.Status != SaleStatus.Voided);
                    }

                    BtnVoidSale.Visibility = hasReturn ? Visibility.Collapsed : Visibility.Visible;
                    BtnAmend.Visibility = hasReturn ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        // Button click handlers (your existing logic kept)
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

            // 🔒 Block if any non-voided return exists for this sale
            using (var db = new PosClientDbContext(_opts))
            {
                bool hasReturn = db.Sales.AsNoTracking()
                    .Any(s => s.IsReturn
                           && (s.OriginalSaleId == sel.SaleId || s.RefSaleId == sel.SaleId)
                           && s.Status != SaleStatus.Voided);

                if (hasReturn)
                {
                    MessageBox.Show("This sale has a return against it and cannot be amended.", "Blocked");
                    return;
                }
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
            // Build DbContext options (replace with your project’s actual setup)
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PosClientDbContext>()
                // .UseSqlServer("YOUR-CONNECTION-STRING")
                .UseSqlite("Data Source=pos.db") // <-- replace with your real config
                .Options;
            using var db = new PosClientDbContext(options);
            IReturnsService returnsSvc = new ReturnsService(db);
            // Replace these with your real getters if you have them
            var w = new ReturnWithoutInvoiceWindow(
            _unused: null,              // signature keeps compatibility; not used inside
            outletId: _outletId,
            counterId: _counterId,
            _unusedTill: null,
            _unusedUser: 0)
            { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true)
                LoadInvoices();
        }

        // Stub to satisfy the call if you don't already have it
        private void RefreshGrid()
        {
            // TODO: re-run your search and rebind InvoicesGrid here
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


        private void VoidReturn_Click(object sender, RoutedEventArgs e)
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
                using var db = new PosClientDbContext(_opts);
                using var tx = db.Database.BeginTransaction();
                var sale = db.Sales.First(s => s.Id == sel.SaleId);
                if (!sale.IsReturn) { MessageBox.Show("Not a return."); return; }
                if (sale.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL documents can be voided."); return; }
                // Get all stock entries that were created for THIS return
                
                var prior = db.StockEntries
                    .Where(e => e.RefId == sale.Id && e.RefType == "SaleReturn")  // adjust if your RefType differs
                    .ToList();

                foreach (var p in prior)
                {
                    db.StockEntries.Add(new StockEntry
                    {
                        LocationType = p.LocationType,
                        LocationId = p.LocationId,
                        OutletId = p.OutletId,
                        ItemId = p.ItemId,
                        QtyChange = -p.QtyChange,  // exact negation of what was posted
                        RefType = "Void",
                        RefId = sale.Id,
                        Ts = DateTime.UtcNow
                    });
                }

                //var lines = db.SaleLines.Where(l => l.SaleId == sale.Id).ToList();
                //foreach (var l in lines)
                //{
                //    // Reverse stock effect from the return
                //    db.StockEntries.Add(new StockEntry
                //    {
                //        LocationType = InventoryLocationType.Outlet,
                //        LocationId = sale.OutletId,
                //        OutletId = sale.OutletId,
                //        ItemId = l.ItemId,
                //        //QtyChange = l.Qty,   // l.Qty is positive, so this becomes negative (stock OUT)
                //        QtyChange = -l.Qty,    // correct: reverse the return (IN → OUT)
                //        RefType = "Void",
                //        RefId = sale.Id,
                //        Ts = DateTime.UtcNow
                //    });
                //}
                sale.Status = SaleStatus.Voided;
                sale.VoidReason = reason;
                sale.VoidedAtUtc = DateTime.UtcNow;
                db.SaveChanges();
                tx.Commit();
                MessageBox.Show("Return voided.");
                LoadInvoices();
                //UpdateHeldButtonVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to void return: " + ex.Message);
            }
        }

        private void VoidSale_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select an invoice."); return; }
            if (sel.IsReturn) { MessageBox.Show("This action is for normal sale invoices, not returns."); return; }
            if (sel.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be voided."); return; }

            // 🔒 Block if any non-voided return exists for this sale
            using (var db = new PosClientDbContext(_opts))
            {
                bool hasReturn = db.Sales.AsNoTracking()
                    .Any(s => s.IsReturn
                           && (s.OriginalSaleId == sel.SaleId || s.RefSaleId == sel.SaleId)
                           && s.Status != SaleStatus.Voided);

                if (hasReturn)
                {
                    MessageBox.Show("This sale has a return against it and cannot be voided.", "Blocked");
                    return;
                }
            }

            var reason = Microsoft.VisualBasic.Interaction.InputBox(
                $"Void sale {sel.CounterId}-{sel.InvoiceNumber} (Rev {sel.Revision})\nEnter reason:",
                "Void Sale", "Wrong sale");
            if (string.IsNullOrWhiteSpace(reason)) return;

            try
            {
                using var db = new PosClientDbContext(_opts);
                using var tx = db.Database.BeginTransaction();

                // 🔁 Recheck inside the transaction to prevent race conditions
                bool hasReturnNow = db.Sales.AsNoTracking()
                    .Any(s => s.IsReturn
                           && (s.OriginalSaleId == sel.SaleId || s.RefSaleId == sel.SaleId)
                           && s.Status != SaleStatus.Voided);

                if (hasReturnNow)
                {
                    MessageBox.Show("This sale now has a return against it and cannot be voided.", "Blocked");
                    return;
                }

                var sale = db.Sales.First(s => s.Id == sel.SaleId);
                if (sale.IsReturn) { MessageBox.Show("Selected document is a return."); return; }
                if (sale.Status != SaleStatus.Final) { MessageBox.Show("Only FINAL invoices can be voided."); return; }

                var lines = db.SaleLines.Where(l => l.SaleId == sale.Id).ToList();
                foreach (var l in lines)
                {
                    // Reverse stock effect of a sale (sale wrote QtyChange = -Qty)
                    db.StockEntries.Add(new StockEntry
                    {
                        LocationType = InventoryLocationType.Outlet,
                        LocationId = sale.OutletId,
                        OutletId = sale.OutletId,
                        ItemId = l.ItemId,
                        QtyChange = +l.Qty,   // add back to stock
                        RefType = "Void",
                        RefId = sale.Id,
                        Ts = DateTime.UtcNow
                    });
                }

                sale.Status = SaleStatus.Voided;
                sale.VoidReason = reason;
                sale.VoidedAtUtc = DateTime.UtcNow;

                db.SaveChanges();
                tx.Commit();

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
            // tiny throttle so activation + selection doesn't double-call
            var now = DateTime.UtcNow;
            if (now - _lastRefreshUtc < TimeSpan.FromMilliseconds(250)) return;
            _lastRefreshUtc = now;

            if (!IsLoaded) Dispatcher.BeginInvoke(new Action(LoadInvoices));
            else LoadInvoices();
        }

        // Shortcut command wrappers (call existing click handlers)
        private void AmendReturn_Executed(object sender, ExecutedRoutedEventArgs e) => AmendReturn_Click(sender, e);
        private void VoidReturn_Executed(object sender, ExecutedRoutedEventArgs e) => VoidReturn_Click(sender, e);
        private void Amend_Executed(object sender, ExecutedRoutedEventArgs e) => Amend_Click(sender, e);
        private void ReturnWith_Executed(object sender, ExecutedRoutedEventArgs e) => ReturnWithInvoice_Click(sender, e);
        private void ReturnWithout_Executed(object sender, ExecutedRoutedEventArgs e) => ReturnWithoutInvoice_Click(sender, e);
        private void VoidSale_Executed(object sender, ExecutedRoutedEventArgs e) => VoidSale_Click(sender, e);

    }
}
