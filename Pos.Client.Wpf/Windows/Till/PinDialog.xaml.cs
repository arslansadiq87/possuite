using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pos.Client.Wpf.Windows.Till
{
    public partial class PinDialog : Window
    {
        public string? EnteredPin { get; private set; }

        // Tweak these to your policy
        private const int MinLen = 4;
        private const int MaxLen = 6;

        public PinDialog()
        {
            InitializeComponent();
            // Focus a button so Enter triggers OK (IsDefault) and not the window
            this.Loaded += (_, __) =>
            {
                // No keyboard typing into PinBox (we use keypad), but allow physical digits:
                this.Focus();
            };
        }

        /* ---------- Keypad handlers ---------- */

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Content is string d && d.All(char.IsDigit))
                AppendDigit(d[0]);
        }

        private void Backspace_Click(object sender, RoutedEventArgs e) => Backspace();

        private void Clear_Click(object sender, RoutedEventArgs e) => SetPin("");

        /* ---------- OK / Cancel ---------- */

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var pin = PinBox.Password;
            if (pin.Length < MinLen)
            {
                MessageBox.Show($"PIN must be at least {MinLen} digits.", "PIN", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            EnteredPin = pin;
            DialogResult = true;
            Close();
        }

        /* ---------- Helpers ---------- */

        private void AppendDigit(char c)
        {
            if (!char.IsDigit(c)) return;
            if (PinBox.Password.Length >= MaxLen) return;
            PinBox.Password += c;
        }

        private void Backspace()
        {
            var p = PinBox.Password;
            if (p.Length == 0) return;
            SetPin(p.Substring(0, p.Length - 1));
        }

        private void SetPin(string v)
        {
            if (v.Length > MaxLen) v = v.Substring(0, MaxLen);
            PinBox.Password = v;
        }

        /* ---------- Allow physical keyboard digits (optional), block others ---------- */

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // We keep PinBox disabled for typing, but if you enable it later, this keeps digits only.
            e.Handled = !e.Text.All(char.IsDigit) || (PinBox.Password.Length >= MaxLen);
            if (!e.Handled)
            {
                // Manually append to keep consistent
                AppendDigit(e.Text[0]);
                e.Handled = true;
            }
        }

        private void PinBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Back)
            {
                Backspace();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Ok_Click(sender!, e);
                e.Handled = true;
            }
            else
            {
                // Block all typing into the box (we drive it ourselves)
                e.Handled = true;
            }
        }
    }
}
