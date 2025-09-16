using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class VariantBatchDialog : Window
    {
        private Product? _product;

        public string Axis1Name => Axis1NameBox.Text.Trim();
        public string Axis2Name => Axis2NameBox.Text.Trim();
        public IEnumerable<string> Axis1Values => (Axis1ValuesBox.Text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        public IEnumerable<string> Axis2Values => (Axis2ValuesBox.Text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

        public decimal Price => ParseDec(PriceBox.Text);
        public decimal TaxPct => ParseDec(TaxPctBox.Text);
        public string? TaxCode => string.IsNullOrWhiteSpace(TaxCodeBox.Text) ? null : TaxCodeBox.Text.Trim();
        public bool TaxInclusive => TaxInclusiveBox.IsChecked == true;
        public decimal? DefaultDiscPct => ParseNullableDec(DiscPctBox.Text);
        public decimal? DefaultDiscAmt => ParseNullableDec(DiscAmtBox.Text);

        public VariantBatchDialog() => InitializeComponent();

        public void PrefillProduct(Product p)
        {
            _product = p;
            ProductText.Text = $"Product: {p.Name}" + (p.Brand != null ? $"  ({p.Brand.Name})" : "");
        }


        private static decimal ParseDec(string? s) =>
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static decimal? ParseNullableDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_product == null) { DialogResult = false; return; }
            if (!Axis1Values.Any() || !Axis2Values.Any())
            {
                MessageBox.Show("Enter at least one value for each axis (comma-separated).");
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
