using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using Microsoft.VisualBasic;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class EditPurchaseWindow : Window
    {
        private readonly int _purchaseId;
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly PurchasesService _svc;
        private Purchase _model = null!;
        private readonly ObservableCollection<LineVM> _lines = new();
        private readonly ObservableCollection<PurchasePayment> _payments = new();

        public int NewRevision { get; private set; }  // NEW

        public bool Confirmed { get; private set; } = false;
        public string? NewDocNo { get; private set; }

        public EditPurchaseWindow(int purchaseId)
        {
            InitializeComponent();

            _purchaseId = purchaseId;
            _opts = new DbContextOptionsBuilder<PosClientDbContext>()
                .UseSqlite(DbPath.ConnectionString).Options;

            _svc = new PurchasesService(new PosClientDbContext(_opts));

            LinesGrid.ItemsSource = _lines;

            Loaded += async (_, __) =>
            {
                await LoadAsync();
                await RefreshPaymentsAsync();     // ← load payments into the grid
                try { IsDraftBox.Visibility = Visibility.Collapsed; } catch { }
            };

        }

        private async void AddPayment_Click(object sender, RoutedEventArgs e)
        {
            if (_model == null || _model.Id <= 0) { MessageBox.Show("Load a purchase first."); return; }

            if (!decimal.TryParse(PayAmountBox.Text, out var amt) || amt <= 0m)
            { MessageBox.Show("Enter a valid amount > 0."); return; }

            var method = ParseMethodFromCombo(PayMethodBox);
            var kind = AskPaymentKind();
            var note = Interaction.InputBox("Note (optional):", "Add Payment Note", "").Trim();
            if (note.Length == 0) note = null;

            int outletId = (_model.TargetType == StockTargetType.Outlet && _model.OutletId is int po && po > 0)
                ? po
                : (AppState.Current?.CurrentOutletId ?? 0);
            if (outletId <= 0) { MessageBox.Show("An outlet is required to record the payment."); return; }

            var user = AppState.Current?.CurrentUserName ?? "system";
            int supplierId = _model.PartyId;
            int? tillSessionId = null;
            int? counterId = AppState.Current?.CurrentCounterId;

            try
            {
                await _svc.AddPaymentAsync(_model.Id, kind, method, amt, note, outletId, supplierId, tillSessionId, counterId, user);
                PayAmountBox.Clear();
                await RefreshPaymentsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add payment: " + ex.Message);
            }
        }

        private void PaymentsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => EditPayment_Click(sender, e);

        private async void EditPayment_Click(object sender, RoutedEventArgs e)
        {
            if (PaymentsGrid.SelectedItem is not PurchasePayment pay) { MessageBox.Show("Select a payment first."); return; }

            // Amount
            var amtStr = Interaction.InputBox("Enter new amount:", "Edit Payment", pay.Amount.ToString("0.00"));
            if (string.IsNullOrWhiteSpace(amtStr)) return;
            if (!decimal.TryParse(amtStr, out var newAmt) || newAmt <= 0m)
            { MessageBox.Show("Invalid amount."); return; }

            // Method
            var methodStr = Interaction.InputBox("Method (Cash, Card, Bank, MobileWallet, etc.):",
                                                 "Edit Payment Method", pay.Method.ToString());
            if (!Enum.TryParse<TenderMethod>(methodStr, true, out var newMethod))
            { MessageBox.Show("Unknown method."); return; }

            // Note
            var newNote = Interaction.InputBox("Note (optional):", "Edit Payment Note", pay.Note ?? "");

            try
            {
                await UpdatePaymentLocalAsync(pay.Id, newAmt, newMethod, newNote);
                await RefreshPaymentsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to update payment: " + ex.Message);
            }
        }

        private async void DeletePayment_Click(object sender, RoutedEventArgs e)
        {
            if (PaymentsGrid.SelectedItem is not PurchasePayment pay) { MessageBox.Show("Select a payment first."); return; }

            var ok = MessageBox.Show($"Delete payment #{pay.Id} ({pay.Method}, {pay.Amount:N2})?",
                                     "Confirm Delete", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;

            try
            {
                await RemovePaymentLocalAsync(pay.Id);
                await RefreshPaymentsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete payment: " + ex.Message);
            }
        }


        private async System.Threading.Tasks.Task RefreshPaymentsAsync()
        {
            if (_model == null || _model.Id <= 0) { PaymentsGrid.ItemsSource = null; return; }

            var tuple = await _svc.GetWithPaymentsAsync(_model.Id);
            _payments.Clear();
            foreach (var p in tuple.payments) _payments.Add(p);
            PaymentsGrid.ItemsSource = _payments;

            // If you later add PaidText/DueText labels to XAML, this will update them.
            var paid = _payments.Sum(p => p.Amount);
            var due = Math.Max(0m, (_model.GrandTotal) - paid);
            //try { PaidText.Text = paid.ToString("N2"); DueText.Text = due.ToString("N2"); } catch { }
        }

        private static TenderMethod ParseMethodFromCombo(ComboBox box)
        {
            var s = (box.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (Enum.TryParse<TenderMethod>(s ?? "", true, out var m)) return m;
            if (string.Equals(s, "Card", StringComparison.OrdinalIgnoreCase)) return TenderMethod.Card;
            if (string.Equals(s, "Bank", StringComparison.OrdinalIgnoreCase)) return TenderMethod.Bank;
            return TenderMethod.Cash;
        }

        private static PurchasePaymentKind AskPaymentKind()
        {
            var s = Interaction.InputBox("Kind (OnReceive or Adjustment):", "Add Payment", "OnReceive");
            return Enum.TryParse<PurchasePaymentKind>(s, true, out var k) ? k : PurchasePaymentKind.OnReceive;
        }

        private async System.Threading.Tasks.Task RecomputePaymentSnapshotsAsync()
        {
            using var db = new PosClientDbContext(_opts);
            var p = await db.Purchases.Include(x => x.Payments).FirstAsync(x => x.Id == _model.Id);
            var sum = p.Payments.Sum(x => x.Amount);
            p.CashPaid = Math.Min(sum, p.GrandTotal);
            p.CreditDue = Math.Max(0, p.GrandTotal - p.CashPaid);
            p.UpdatedAtUtc = DateTime.UtcNow;
            p.UpdatedBy = AppState.Current?.CurrentUserName ?? "system";
            await db.SaveChangesAsync();
        }

        private async System.Threading.Tasks.Task UpdatePaymentLocalAsync(int paymentId, decimal newAmount, TenderMethod newMethod, string? newNote)
        {
            using var db = new PosClientDbContext(_opts);
            var pay = await db.PurchasePayments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (pay is null) throw new InvalidOperationException($"Payment #{paymentId} not found.");

            // prevent overpayment
            var others = await db.PurchasePayments
                .Where(p => p.PurchaseId == pay.PurchaseId && p.Id != pay.Id)
                .SumAsync(p => p.Amount);
            if (others + newAmount > _model.GrandTotal)
                throw new InvalidOperationException("Payment exceeds total.");

            // sync cash ledger if you have one
            var cash = await db.CashLedgers.FirstOrDefaultAsync(c => c.RefType == "PurchasePayment" && c.RefId == pay.Id);

            pay.Amount = Math.Round(newAmount, 2);
            pay.Method = newMethod;
            pay.Note = string.IsNullOrWhiteSpace(newNote) ? null : newNote.Trim();
            pay.UpdatedAtUtc = DateTime.UtcNow;
            pay.UpdatedBy = AppState.Current?.CurrentUserName ?? "system";
            await db.SaveChangesAsync();

            if (newMethod == TenderMethod.Cash)
            {
                if (cash == null)
                {
                    // create matching cash movement
                    cash = new CashLedger
                    {
                        OutletId = pay.OutletId,
                        CounterId = AppState.Current?.CurrentCounterId,
                        TillSessionId = null,
                        TsUtc = DateTime.UtcNow,
                        Delta = -pay.Amount,
                        RefType = "PurchasePayment",
                        RefId = pay.Id,
                        Note = pay.Note,
                        CreatedAtUtc = DateTime.UtcNow,
                        CreatedBy = AppState.Current?.CurrentUserName ?? "system"
                    };
                    db.CashLedgers.Add(cash);
                }
                else
                {
                    cash.Delta = -pay.Amount; // keep negative (cash out)
                    cash.Note = pay.Note;
                    cash.TsUtc = DateTime.UtcNow;
                }
            }
            else
            {
                if (cash != null) db.CashLedgers.Remove(cash);
            }
            await db.SaveChangesAsync();

            await RecomputePaymentSnapshotsAsync();
        }

        private async System.Threading.Tasks.Task RemovePaymentLocalAsync(int paymentId)
        {
            using var db = new PosClientDbContext(_opts);
            var pay = await db.PurchasePayments.FirstOrDefaultAsync(p => p.Id == paymentId);
            if (pay is null) return;

            var cash = await db.CashLedgers.FirstOrDefaultAsync(c => c.RefType == "PurchasePayment" && c.RefId == pay.Id);

            db.PurchasePayments.Remove(pay);
            if (cash != null) db.CashLedgers.Remove(cash);
            await db.SaveChangesAsync();

            await RecomputePaymentSnapshotsAsync();
        }


        // Lightweight line VM (mirrors PurchaseWindow math)
        public class LineVM : INotifyPropertyChanged
        {
            public int ItemId { get; set; }
            private string _sku = ""; public string Sku { get => _sku; set { _sku = value; OnChanged(nameof(Sku)); } }
            private string _name = ""; public string Name { get => _name; set { _name = value; OnChanged(nameof(Name)); } }

            private decimal _qty; public decimal Qty { get => _qty; set { _qty = value; Recalc(); OnChanged(nameof(Qty)); } }
            private decimal _unit; public decimal UnitCost { get => _unit; set { _unit = value; Recalc(); OnChanged(nameof(UnitCost)); } }
            private decimal _disc; public decimal Discount { get => _disc; set { _disc = value; Recalc(); OnChanged(nameof(Discount)); } }
            private decimal _tax; public decimal TaxRate { get => _tax; set { _tax = value; Recalc(); OnChanged(nameof(TaxRate)); } }

            private decimal _total; public decimal LineTotal { get => _total; private set { _total = value; OnChanged(nameof(LineTotal)); } }
            public string? Notes { get; set; }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

            public void Recalc()
            {
                var baseAmt = Qty * UnitCost;
                var taxable = Math.Max(0m, baseAmt - Discount);
                var tax = Math.Round(taxable * (TaxRate / 100m), 2);
                LineTotal = Math.Round(taxable + tax, 2);
            }
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            // Load header + lines
            var purchase = await _svc.LoadWithLinesAsync(_purchaseId);
            if (purchase.Status != PurchaseStatus.Final)
            {
                MessageBox.Show("Only FINAL purchases can be amended.");
                Close(); return;
            }

            _model = purchase;

            // Header
            SupplierNameText.Text = purchase.Party?.Name ?? $"Supplier #{purchase.PartyId}";
            VendorInvBox.Text = purchase.VendorInvoiceNo ?? "";
            DatePicker.SelectedDate = purchase.PurchaseDate;
            OtherChargesBox.Text = purchase.OtherCharges.ToString("0.00");
            DocNoText.Text = string.IsNullOrWhiteSpace(purchase.DocNo) ? $"#{purchase.Id}" : purchase.DocNo;

            // Lines → _lines with SKU/Name lookup
            _lines.Clear();

            using var db = new PosClientDbContext(_opts);
            var itemIds = purchase.Lines.Select(l => l.ItemId).Distinct().ToList();
            var meta = await db.Items.AsNoTracking()
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Sku, i.Name })
                .ToDictionaryAsync(x => x.Id);

            foreach (var l in purchase.Lines)
            {
                meta.TryGetValue(l.ItemId, out var m);
                var vm = new LineVM
                {
                    ItemId = l.ItemId,
                    Sku = m?.Sku ?? "",
                    Name = m?.Name ?? $"Item #{l.ItemId}",
                    Qty = l.Qty,
                    UnitCost = l.UnitCost,
                    Discount = l.Discount,
                    TaxRate = l.TaxRate,
                    Notes = l.Notes
                };
                vm.Recalc();
                _lines.Add(vm);
            }

            RecomputeTotals();
            LinesGrid.Focus();
        }

        // Recompute header totals from line VMs + OtherCharges
        private void RecomputeTotals()
        {
            var subtotal = Math.Round(_lines.Sum(x => x.Qty * x.UnitCost), 2);
            var discount = Math.Round(_lines.Sum(x => x.Discount), 2);
            var taxSum = Math.Round(_lines.Sum(x => Math.Max(0m, x.Qty * x.UnitCost - x.Discount) * (x.TaxRate / 100m)), 2);
            if (!decimal.TryParse(OtherChargesBox.Text, out var other)) other = 0m;
            var grand = Math.Round(subtotal - discount + taxSum + other, 2);

            SubtotalText.Text = subtotal.ToString("N2");
            DiscountText.Text = discount.ToString("N2");
            TaxText.Text = taxSum.ToString("N2");
            GrandTotalText.Text = grand.ToString("N2");
        }

        private void LinesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
            => Dispatcher.BeginInvoke(RecomputeTotals);

        private void LinesGrid_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
            => Dispatcher.BeginInvoke(RecomputeTotals);

        private void OtherChargesBox_TextChanged(object sender, TextChangedEventArgs e)
            => RecomputeTotals();

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F9) { Save_Click(sender, e); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.Escape) { Cancel_Click(sender, e); e.Handled = true; }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            // Basic validations (same style as PurchaseWindow)
            if (_lines.Count == 0) { MessageBox.Show("Add at least one item."); return; }
            if (_lines.Any(l => l.Qty <= 0 || l.UnitCost < 0 || l.Discount < 0))
            { MessageBox.Show("Please ensure Qty > 0 and Price/Discount are not negative."); return; }
            foreach (var l in _lines)
            {
                var baseAmt = l.Qty * l.UnitCost;
                if (l.Discount > baseAmt)
                { MessageBox.Show($"Discount exceeds base for '{l.Name}'."); return; }
            }

            // Reflect editable header fields
            _model.VendorInvoiceNo = string.IsNullOrWhiteSpace(VendorInvBox.Text) ? null : VendorInvBox.Text.Trim();
            _model.PurchaseDate = DatePicker.SelectedDate ?? DateTime.Now;
            if (!decimal.TryParse(OtherChargesBox.Text, out var other)) other = 0m;
            _model.OtherCharges = other;

            // Build lines
            var lines = _lines.Select(l => new PurchaseLine
            {
                ItemId = l.ItemId,
                Qty = l.Qty,
                UnitCost = l.UnitCost,
                Discount = l.Discount,
                TaxRate = l.TaxRate,
                Notes = l.Notes
            });

            // Keep destination & supplier unchanged; service recomputes totals and preserves Id
            var user = AppState.Current?.CurrentUserName ?? "admin";
            var saved = await _svc.ReceiveAsync(_model, lines, user);

            Confirmed = true;
            NewDocNo = saved.DocNo;
            NewRevision = saved.Revision;                              // NEW
            MessageBox.Show($"Purchase amended.\nDoc #: {saved.DocNo ?? $"#{saved.Id}"}\n" +
                  $"Revision: {saved.Revision}\nTotal: {saved.GrandTotal:N2}", "Saved");
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            Close();
        }
    }
}
