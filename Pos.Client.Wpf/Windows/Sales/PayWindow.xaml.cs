//Pos.Client.Wpf/PayWindow.xaml.cs
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class PayWindow : Window
    {
        private readonly decimal _subtotal;
        private readonly decimal _discountValue;
        private readonly decimal _tax;
        private readonly decimal _grandTotal;
        private readonly int _items;
        private readonly int _qty;

        // “difference mode” (amendments/returns) — settle a delta instead of the full grand total
        private bool _differenceMode = false;   // true = settle delta only
        private decimal _amountDelta = 0m;      // +collect, -refund

        // First-input-replaces flags
        private bool _cashFirst = true;
        private bool _cardFirst = true;

        // Active target for keypad
        private TextBox? _active;

        public decimal Cash { get; private set; }
        public decimal Card { get; private set; }
        public bool Confirmed { get; private set; }

        // If in difference mode, target is |delta|; otherwise full grand total
        private decimal TargetAmount => _differenceMode ? Math.Abs(_amountDelta) : _grandTotal;

        // ===== Constructors =====

        /// <summary>
        /// Normal (full-invoice) mode.
        /// </summary>
        public PayWindow(decimal subtotal, decimal discountValue, decimal tax,
                         decimal grandTotal, int items, int qty)
        {
            InitializeComponent();

            _subtotal = subtotal;
            _discountValue = discountValue;
            _tax = tax;
            _grandTotal = grandTotal;
            _items = items;
            _qty = qty;

            // Bind summary (normal mode shows full invoice figures)
            SubtotalText.Text = _subtotal.ToString("0.00");
            DiscountText.Text = "-" + _discountValue.ToString("0.00");
            TaxText.Text = _tax.ToString("0.00");
            GrandText.Text = _grandTotal.ToString("0.00");
            ItemsText.Text = _items.ToString();
            QtyText.Text = _qty.ToString();

            // Prefill cash with invoice amount
            CashBox.Text = _grandTotal.ToString("0.00");
            CardBox.Text = "0";

            Loaded += (_, __) =>
            {
                _active = CashBox;
                CashBox.Focus();
                CashBox.SelectAll();
                Recompute();
            };

            // Optional: prevent paste of non-numeric (keeps things tidy)
            DataObject.AddPastingHandler(CashBox, OnPasteNumericOnly);
            DataObject.AddPastingHandler(CardBox, OnPasteNumericOnly);
        }

        /// <summary>
        /// Difference mode (amend/return) — settle only the delta (amountDelta).
        /// </summary>
        public PayWindow(decimal subtotal, decimal discountValue, decimal tax,
                         decimal grandTotal, int items, int qty,
                         bool differenceMode, decimal amountDelta)
            : this(subtotal, discountValue, tax, grandTotal, items, qty)
        {
            _differenceMode = differenceMode;
            _amountDelta = amountDelta;

            var target = TargetAmount;
            Title = (_amountDelta >= 0m) ? $"Collect {target:0.00}" : $"Refund {target:0.00}";
            GrandText.Text = target.ToString("0.00");
            if (target == 0m)
            {
                // No settlement needed
                CashBox.Text = "0";
                CardBox.Text = "0";
                Confirmed = true;
                DialogResult = true;
                Close();
                return;
            }
            // Prefill tender to the exact delta; user can split if needed
            CashBox.Text = target.ToString("0.00");
            CardBox.Text = "0";
            _cashFirst = _cardFirst = false;
            Recompute(); // recompute due/change based on TargetAmount
        }

        // ===== Helpers =====
        private static readonly Regex _numRx = new(@"^[0-9.]$", RegexOptions.Compiled);

        private void Recompute()
        {
            decimal.TryParse(CashBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var c1);
            decimal.TryParse(CardBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var c2);

            Cash = c1 < 0 ? 0 : c1;
            Card = c2 < 0 ? 0 : c2;

            var target = TargetAmount;
            var paid = Cash + Card;
            var due = Math.Max(0m, target - paid);
            var change = Math.Max(0m, paid - target);

            DueText.Text = due.ToString("0.00");
            ChangeText.Text = change.ToString("0.00");
        }

        private void OnPasteNumericOnly(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }
            var text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? "";
            if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                e.CancelCommand();
        }

        // ===== TextBox events =====
        private void NumericOnly(object sender, TextCompositionEventArgs e)
        {
            if (!_numRx.IsMatch(e.Text)) { e.Handled = true; return; }

            var tb = (TextBox)sender;
            // On first key after focus, replace the prefilled text
            if ((tb == CashBox && _cashFirst) || (tb == CardBox && _cardFirst))
            {
                tb.Text = e.Text == "." ? "0." : e.Text; // sensible start
                tb.CaretIndex = tb.Text.Length;
                e.Handled = true;
                if (tb == CashBox) _cashFirst = false;
                if (tb == CardBox) _cardFirst = false;
                Recompute();
            }
        }

        private void CashBox_TextChanged(object sender, TextChangedEventArgs e) => Recompute();
        private void CardBox_TextChanged(object sender, TextChangedEventArgs e) => Recompute();

        private void CashBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _active = CashBox;
            CashBox.SelectAll();
        }

        private void CardBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _active = CardBox;
            CardBox.SelectAll();
        }

        // ===== Keypad =====
        private void Pad_Click(object sender, RoutedEventArgs e)
        {
            _active ??= CashBox;
            var btn = (Button)sender;
            var ch = btn.Content?.ToString();

            if (ch == "⌫")
            {
                var t = _active.Text ?? "";
                _active.Text = t.Length > 0 ? t[..^1] : "0";
            }
            else
            {
                // First input after focus replaces prefill
                if ((_active == CashBox && _cashFirst) || (_active == CardBox && _cardFirst))
                {
                    _active.Text = ch == "." ? "0." : ch!;
                    if (_active == CashBox) _cashFirst = false;
                    if (_active == CardBox) _cardFirst = false;
                }
                else
                {
                    if (ch == ".")
                    {
                        if (!_active.Text.Contains(".")) _active.Text += ".";
                    }
                    else
                    {
                        _active.Text += ch;
                    }
                }
            }

            _active.CaretIndex = _active.Text.Length;
            Recompute();
        }

        // ===== Convenience buttons =====
        private void ExactCash_Click(object sender, RoutedEventArgs e)
        {
            var target = TargetAmount;
            CashBox.Text = target.ToString("0.00");
            CardBox.Text = "0";
            _cashFirst = _cardFirst = false;
            _active = CashBox;
            Recompute();
        }

        private void ExactCard_Click(object sender, RoutedEventArgs e)
        {
            var target = TargetAmount;
            CardBox.Text = target.ToString("0.00");
            CashBox.Text = "0";
            _cashFirst = _cardFirst = false;
            _active = CardBox;
            Recompute();
        }

        private void ClearCash_Click(object sender, RoutedEventArgs e)
        {
            CashBox.Text = "0";
            _cashFirst = false;
            _active = CashBox;
            CashBox.Focus();
            CashBox.SelectAll();
            Recompute();
        }

        private void ClearCard_Click(object sender, RoutedEventArgs e)
        {
            CardBox.Text = "0";
            _cardFirst = false;
            _active = CardBox;
            CardBox.Focus();
            CardBox.SelectAll();
            Recompute();
        }

        private void ClearBoth_Click(object sender, RoutedEventArgs e)
        {
            CashBox.Text = "0";
            CardBox.Text = "0";
            _cashFirst = _cardFirst = false;
            _active = CashBox;
            CashBox.Focus();
            CashBox.SelectAll();
            Recompute();
        }

        // ===== Confirm / Cancel & shortcuts =====
        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_differenceMode)
            {
                // Must settle the delta exactly (no under/over)
                var required = Math.Round(TargetAmount, 2);
                var tendered = Math.Round(Cash + Card, 2);

                if (tendered != required)
                {
                    MessageBox.Show($"Please enter exactly {required:0.00} split across Cash/Card.");
                    return;
                }

                Confirmed = true;
                DialogResult = true;
                Close();
                return;
            }

            // Normal (full invoice) mode: allow tiny 0.01 under due to rounding
            if (Cash + Card + 0.01m < _grandTotal)
            {
                MessageBox.Show("Payment is less than total.");
                return;
            }
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private DateTime _lastEsc = DateTime.MinValue;
        private int _escCount = 0;
        private const int EscChordMs = 450;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F9 || e.Key == Key.Enter)
            {
                Confirm_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastEsc).TotalMilliseconds <= EscChordMs) _escCount++;
                else _escCount = 1;
                _lastEsc = now;

                // In this window, single Esc cancels; MainWindow handles double-Esc refocus.
                Cancel_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
