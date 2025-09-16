//Pos.Client.Wpf/Purchases/ItemQuickDialog.xaml.cs
using System.Globalization;
using System.Windows;

namespace Pos.Client.Wpf.Windows.Purchases
{
    public partial class ItemQuickDialog : Window
    {
        // Required
        public string Sku => SkuBox.Text.Trim();
        public string NameVal => NameBox.Text.Trim();

        // Non-nullable in your entity: return "" if left blank
        public string BarcodeVal => string.IsNullOrWhiteSpace(BarcodeBox.Text) ? "" : BarcodeBox.Text.Trim();

        // Money/percent
        public decimal PriceVal => ParseDecOrZero(PriceBox.Text);
        public decimal TaxPctVal => ParseDecOrZero(TaxPctBox.Text);
        public string? TaxCodeVal => string.IsNullOrWhiteSpace(TaxCodeBox.Text) ? null : TaxCodeBox.Text.Trim();
        public bool TaxInclusiveVal => TaxInclusiveBox.IsChecked == true;

        // Optional discounts (map to decimal? in entity)
        public decimal? DiscountPctVal => ParseNullableDec(DiscPctBox.Text);
        public decimal? DiscountAmtVal => ParseNullableDec(DiscAmtBox.Text);

        // Variants (optional)
        public string? Variant1NameVal => string.IsNullOrWhiteSpace(Var1NameBox.Text) ? null : Var1NameBox.Text.Trim();
        public string? Variant1ValueVal => string.IsNullOrWhiteSpace(Var1ValueBox.Text) ? null : Var1ValueBox.Text.Trim();
        public string? Variant2NameVal => string.IsNullOrWhiteSpace(Var2NameBox.Text) ? null : Var2NameBox.Text.Trim();
        public string? Variant2ValueVal => string.IsNullOrWhiteSpace(Var2ValueBox.Text) ? null : Var2ValueBox.Text.Trim();

        public ItemQuickDialog()
        {
            InitializeComponent();
        }

        private static decimal ParseDecOrZero(string? s) =>
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static decimal? ParseNullableDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Sku))
            {
                MessageBox.Show("SKU is required.");
                return;
            }
            if (string.IsNullOrWhiteSpace(NameVal))
            {
                MessageBox.Show("Name is required.");
                return;
            }
            // Barcode is allowed to be empty string (your entity has non-nullable string with default "")
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
