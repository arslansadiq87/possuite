using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class PayDialog : UserControl
    {
        // State
        private decimal _subtotal, _discountValue, _tax, _grand;
        private int _items, _qty;
        private bool _differenceMode;
        private decimal _amountDelta;

        private bool _cashFirst = true;
        private bool _cardFirst = true;
        private TextBox? _active;
        private Action? _closeOverlay;

        private static readonly Regex _numRx = new(@"^[0-9.]$", RegexOptions.Compiled);
        

        // Result
        private TaskCompletionSource<PaymentResult>? _tcs;

        public PayDialog()
        {
            InitializeComponent();
        }

        public Task<PaymentResult> InitializeAndShowAsync(
            decimal subtotal, decimal discountValue, decimal tax, decimal grandTotal,
            int items, int qty, bool differenceMode, decimal amountDelta, string? title,
            Action closeOverlay)
        {
            _tcs = new TaskCompletionSource<PaymentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeOverlay = closeOverlay;  // <— store it for OK/Cancel

            _subtotal = subtotal;
            _discountValue = discountValue;
            _tax = tax;
            _grand = grandTotal;
            _items = items;
            _qty = qty;
            _differenceMode = differenceMode;
            _amountDelta = amountDelta;

            TitleBlock.Text = string.IsNullOrWhiteSpace(title) ? "Finalize Payment" : title!;
            ItemsText.Text = _items.ToString();
            QtyText.Text = _qty.ToString();

            SubtotalText.Text = _subtotal.ToString("0.00");
            DiscountText.Text = "-" + _discountValue.ToString("0.00");
            TaxText.Text = _tax.ToString("0.00");

            var target = TargetAmount;
            GrandText.Text = target.ToString("0.00");

            if (_differenceMode)
            {
                TitleBlock.Text = (_amountDelta >= 0m) ? $"Collect {target:0.00}" : $"Refund {target:0.00}";
            }

            // Prefill tender
            if (target == 0m)
            {
                _tcs.TrySetResult(new PaymentResult { Confirmed = true, Cash = 0m, Card = 0m });
                _closeOverlay?.Invoke();   // was: closeOverlay();
                //closeOverlay();
                return _tcs.Task;
            }

            CashBox.Text = target.ToString("0.00");
            CardBox.Text = "0";
            _cashFirst = _cardFirst = false;

            // focus
            Loaded += (_, __) =>
            {
                _active = CashBox;
                CashBox.Focus();
                CashBox.SelectAll();
                Recompute();
            };

            // disallow non-numeric paste
            DataObject.AddPastingHandler(CashBox, OnPasteNumericOnly);
            DataObject.AddPastingHandler(CardBox, OnPasteNumericOnly);

            return _tcs.Task;
        }

        private decimal TargetAmount => _differenceMode ? Math.Abs(_amountDelta) : _grand;

        private void Recompute()
        {
            decimal.TryParse(CashBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var c1);
            decimal.TryParse(CardBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var c2);

            var cash = c1 < 0 ? 0 : c1;
            var card = c2 < 0 ? 0 : c2;
            var target = TargetAmount;

            var paid = cash + card;
            var due = Math.Max(0m, target - paid);
            var change = Math.Max(0m, paid - target);

            DueText.Text = due.ToString("0.00");
            ChangeText.Text = change.ToString("0.00");
        }

        // Input helpers
        private void OnPasteNumericOnly(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }
            var text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? "";
            if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                e.CancelCommand();
        }

        private void NumericOnly(object sender, TextCompositionEventArgs e)
        {
            if (!_numRx.IsMatch(e.Text)) { e.Handled = true; return; }
            var tb = (TextBox)sender;
            if ((tb == CashBox && _cashFirst) || (tb == CardBox && _cardFirst))
            {
                tb.Text = e.Text == "." ? "0." : e.Text;
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

        private void Pad_Click(object sender, RoutedEventArgs e)
        {
            _active ??= CashBox;
            var ch = (string)((Button)sender).Content;

            if (ch == "⌫")
            {
                var t = _active.Text ?? "";
                _active.Text = t.Length > 0 ? t[..^1] : "0";
            }
            else
            {
                if ((_active == CashBox && _cashFirst) || (_active == CardBox && _cardFirst))
                {
                    _active.Text = ch == "." ? "0." : ch;
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

        private void ExactCash_Click(object sender, RoutedEventArgs e)
        {
            var t = TargetAmount;
            CashBox.Text = t.ToString("0.00");
            CardBox.Text = "0";
            _cashFirst = _cardFirst = false;
            _active = CashBox;
            Recompute();
        }
        private void ExactCard_Click(object sender, RoutedEventArgs e)
        {
            var t = TargetAmount;
            CardBox.Text = t.ToString("0.00");
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

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            decimal.TryParse(CashBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var cash);
            decimal.TryParse(CardBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var card);
            var target = Math.Round(TargetAmount, 2);
            var tendered = Math.Round(cash + card, 2);

            if (_differenceMode)
            {
                if (tendered != target)
                {
                    MessageBox.Show($"Please enter exactly {target:0.00} split across Cash/Card.");
                    return;
                }
            }
            else
            {
                if (tendered + 0.01m < target)
                {
                    MessageBox.Show("Payment is less than total.");
                    return;
                }
            }

            //_tcs?.TrySetResult(new PaymentResult { Confirmed = true, Cash = cash, Card = card });
            CompleteAndClose(true, cash, card);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            //_tcs?.TrySetResult(new PaymentResult { Confirmed = false, Cash = 0m, Card = 0m });
            CompleteAndClose(false, 0m, 0m);
        }

        private DateTime _lastEsc = DateTime.MinValue;
        private int _escCount = 0;
        private const int EscChordMs = 450;

        private void Root_KeyDown(object sender, KeyEventArgs e)
        {
            // Pay (Enter/F9)
            if (e.Key == Key.F9 || e.Key == Key.Enter)
            {
                Confirm_Click(sender, e);
                e.Handled = true;
                return;
            }

            // Cancel via double-Escape (press Esc twice quickly)
            if (e.Key == Key.Escape)
            {
                var now = DateTime.UtcNow;
                _escCount = (now - _lastEsc).TotalMilliseconds <= EscChordMs ? _escCount + 1 : 1;
                _lastEsc = now;

                e.Handled = true; // swallow both Esc presses

                if (_escCount >= 2)    // only cancel on the 2nd Esc
                {
                    _escCount = 0;     // reset for next chord
                    Cancel_Click(sender, e); // will CompleteAndClose(...)
                }
            }
        }


        // add anywhere inside the class
        private bool _done;

        private void CompleteAndClose(bool confirmed, decimal cash, decimal card)
        {
            if (_tcs == null || _tcs.Task.IsCompleted) return;
            if (_done || _tcs == null || _tcs.Task.IsCompleted) return;
            _done = true;
            _tcs.TrySetResult(new PaymentResult { Confirmed = confirmed, Cash = cash, Card = card });
            _closeOverlay?.Invoke();  // <— this actually hides the dialog
        }


    }
}
