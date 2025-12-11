using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class StockValuationView : UserControl
    {
        private readonly StockValuationVm _vm;

        public StockValuationView(StockValuationVm vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            Loaded += OnLoaded;                 // NEW: auto-refresh
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is StockValuationVm vm)
                await vm.RefreshAsync();
        }

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;

            var vm = DataContext as StockValuationVm;

            var doc = new FlowDocument { PagePadding = new Thickness(48), FontSize = 11 };
            var title = vm?.Mode == StockValuationMode.Cost ? "Stock Valuation (Cost)" : "Stock Valuation (Sale)";
            doc.Blocks.Add(new Paragraph(new Run(title))
            { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });

            // NEW: add As-of line
            if (vm is not null)
            {
                var asOfLocal = vm.AsOf.ToString("yyyy-MM-dd");
                doc.Blocks.Add(new Paragraph(new Run($"As of: {asOfLocal}")));
            }

            var table = new Table();
            doc.Blocks.Add(table);
            string[] heads = { "SKU", "Name", "Brand", "Category", "OnHand", "UnitCost", "UnitPrice", "TotalCost", "TotalPrice" };
            for (int i = 0; i < heads.Length; i++) table.Columns.Add(new TableColumn());

            var header = new TableRowGroup(); table.RowGroups.Add(header);
            var hr = new TableRow(); header.Rows.Add(hr);
            foreach (var h in heads) hr.Cells.Add(new TableCell(new Paragraph(new Run(h))) { FontWeight = FontWeights.Bold });

            var body = new TableRowGroup(); table.RowGroups.Add(body);
            foreach (var r in vm?.Rows ?? [])
            {
                var tr = new TableRow(); body.Rows.Add(tr);
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Sku))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.DisplayName))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Brand))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Category))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.OnHand.ToString("N3")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.UnitCost.ToString("N4")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.UnitPrice.ToString("N2")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.TotalCost.ToString("N2")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.TotalPrice.ToString("N2")))));
            }

            // (Optional) Print totals at bottom
            if (vm is not null)
            {
                doc.Blocks.Add(new Paragraph(new Run(
                    $"Totals — Qty: {vm.SumQty:N3}   Cost: {vm.SumCost:N2}   Price: {vm.SumPrice:N2}"))
                { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            }

            pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, title);
        }
    }

}
