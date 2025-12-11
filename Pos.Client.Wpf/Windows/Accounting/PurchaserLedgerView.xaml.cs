using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Printing;            // optional

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class PurchaserLedgerView : UserControl
    {
        private readonly PurchaserLedgerVm _vm;

        public PurchaserLedgerView(PurchaserLedgerVm vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;                  // <-- CRITICAL
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try { SupplierSearch?.Focus(); } catch { }

            if (DataContext is PurchaserLedgerVm vm)
                await vm.RefreshAsync();   // blank -> filled
        }

        // EXACT same pattern as PurchaseView: set SupplierId and refresh
        private async void SupplierSearch_CustomerPicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PurchaserLedgerVm vm) return;
            vm.SupplierId = SupplierSearch?.SelectedCustomerId;  // matches Purchases API
            await vm.RefreshAsync();
        }

        private async void ClearSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PurchaserLedgerVm vm) return;
            SupplierSearch.SelectedCustomer = null;
            SupplierSearch.SelectedCustomerId = null;
            SupplierSearch.Query = string.Empty;
            vm.SupplierId = null; // all suppliers
            await vm.RefreshAsync();
        }

        private void OnPrintClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PurchaserLedgerVm vm || vm.Rows is null) return;

            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;

            var doc = new FlowDocument { PagePadding = new Thickness(48) };
            doc.Blocks.Add(new Paragraph(new Run("Supplier Ledger"))
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold
            });

            var from = vm.From.ToString("yyyy-MM-dd");   // changed
            var to = vm.To.ToString("yyyy-MM-dd");     // changed
            var supplierName = "All Suppliers";
            if (vm.SupplierId is int)
                supplierName = SupplierSearch?.SelectedCustomer?.Name ?? "Selected Supplier";

            doc.Blocks.Add(new Paragraph(new Run($"From: {from}   To: {to}   Supplier: {supplierName}"))
            {
                FontSize = 12
            });

            var table = new Table();
            doc.Blocks.Add(table);
            string[] heads = { "Date/Time", "Doc No", "Supplier", "Total", "Paid", "Due" };
            for (int i = 0; i < heads.Length; i++) table.Columns.Add(new TableColumn());

            var header = new TableRowGroup(); table.RowGroups.Add(header);
            var hr = new TableRow(); header.Rows.Add(hr);
            foreach (var h in heads)
                hr.Cells.Add(new TableCell(new Paragraph(new Run(h))) { FontWeight = FontWeights.Bold });

            var body = new TableRowGroup(); table.RowGroups.Add(body);
            foreach (var r in vm.Rows)
            {
                var tr = new TableRow(); body.Rows.Add(tr);
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.TsUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.DocNo))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Supplier))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.GrandTotal.ToString("N2")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Paid.ToString("N2")))));
                tr.Cells.Add(new TableCell(new Paragraph(new Run(r.Due.ToString("N2")))));
            }

            doc.Blocks.Add(new Paragraph(new Run(
                $"Total: {vm.TotalGrand:N2}   Paid: {vm.TotalPaid:N2}   Due: {vm.TotalDue:N2}"))
            { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });

            pd.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Supplier Ledger");
        }

    }
}
