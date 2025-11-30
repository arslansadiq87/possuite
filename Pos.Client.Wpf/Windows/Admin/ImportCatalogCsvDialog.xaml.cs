using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Models.Catalog;
using Pos.Persistence.Features.Catalog;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ImportCatalogCsvDialog : UserControl
    {
        private readonly ICsvCatalogImportService _import;
        private readonly ObservableCollection<CsvImportRow> _rows = new();

        public ImportCatalogCsvDialog()
        {
            InitializeComponent();
            Resources["StatusToBrushConverter"] = new Converters.StatusToBrushConverter();
            _import = App.Services.GetRequiredService<ICsvCatalogImportService>();
            GridRows.ItemsSource = _rows;
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                TxtPath.Text = dlg.FileName;
                _rows.Clear();
                TxtSummary.Text = "";
                BtnSave.IsEnabled = false;
            }
        }

        private async void Validate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtPath.Text) || !File.Exists(TxtPath.Text))
                {
                    MessageBox.Show("Please select a CSV file first.");
                    return;
                }
                var res = await _import.ParseAndValidateAsync(TxtPath.Text, CancellationToken.None);
                _rows.Clear();
                foreach (var r in res.Rows) _rows.Add(r);
                TxtSummary.Text = $"Rows: {_rows.Count} • Valid: {res.ValidCount} • Errors: {res.ErrorCount}";
                BtnSave.IsEnabled = res.ErrorCount == 0 && res.ValidCount > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Validate failed: " + ex.Message);
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSave.IsEnabled = false;
                var ok = await _import.SaveAsync(_rows.Where(r => r.Status == "Valid"),
                                                 createMissingBrandCategory: ChkCreateMissing.IsChecked == true,
                                                 ct: CancellationToken.None);
                TxtSummary.Text += $" • Saved: {ok}";
                foreach (var r in _rows.Where(r => r.Status == "Valid")) r.Status = "Saved";
                MessageBox.Show($"Imported {ok} rows successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var wnd = Window.GetWindow(this);
            wnd?.Close();
        }
    }
}
