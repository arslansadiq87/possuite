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

            Loaded += (_, __) =>
            {
                // Focus the window so Enter triggers OK (IsDefault)
                this.Focus();
            };
        }

        /* ---------- Keypad handlers ---------- */

        private void Digit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Content is string d && d.All(char.IsDigit))
            {
                AppendDigit(d[0]);
            }
        }

        private void Backspace_Click(object sender, RoutedEventArgs e) => Backspace();

        private void Clear_Click(object sender, RoutedEventArgs e) => SetPin(string.Empty);

        /* ---------- OK / Cancel ---------- */

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var pin = PinBox.Password;
            if (pin.Length < MinLen || pin.Length > MaxLen)
            {
                MessageBox.Show(
                    $"PIN must be {MinLen}-{MaxLen} digits.",
                    "Invalid PIN",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EnteredPin = pin;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /* ---------- Internal helpers ---------- */

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
            SetPin(p[..^1]); // everything except last char
        }

        private void SetPin(string v)
        {
            if (v.Length > MaxLen)
                v = v.Substring(0, MaxLen);

            PinBox.Password = v;
        }

        /* ---------- Keyboard guards ---------- */

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // We drive the box via keypad only; block manual typing.
            e.Handled = true;
        }

        private void PinBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel_Click(sender!, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Ok_Click(sender!, e);
                e.Handled = true;
            }
            else
            {
                // Block all direct keyboard typing into the PasswordBox
                e.Handled = true;
            }
        }
    }
}
