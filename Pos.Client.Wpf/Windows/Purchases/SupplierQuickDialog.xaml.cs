//Pos.Client.Wpf/Purchases/SupplierQuickDialog.xaml.cs
using System.Windows;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class SupplierQuickDialog : Window
    {
        public string SupplierName => NameBox.Text.Trim();
        public string? SupplierPhone => string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
        public string? SupplierEmail => string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        public string? Address1 => string.IsNullOrWhiteSpace(Address1Box.Text) ? null : Address1Box.Text.Trim();
        public string? City => string.IsNullOrWhiteSpace(CityBox.Text) ? null : CityBox.Text.Trim();
        public string? Country => string.IsNullOrWhiteSpace(CountryBox.Text) ? null : CountryBox.Text.Trim();

        public SupplierQuickDialog() => InitializeComponent();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SupplierName))
            {
                MessageBox.Show("Supplier name is required.");
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
