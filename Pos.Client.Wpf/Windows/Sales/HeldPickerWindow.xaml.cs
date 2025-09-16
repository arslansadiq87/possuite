//Pos.Client.Wpf/HeldPickerWindow.xaml.cs
using System.Globalization;
using System.Windows;
using Pos.Persistence;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;

namespace Pos.Client.Wpf.Windows.Sales
{
    public partial class HeldPickerWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;
        private readonly int _outletId, _counterId;

        public class HeldRow
        {
            public int Id { get; set; }
            public DateTime Ts { get; set; }
            public string TsLocal => Ts.ToLocalTime().ToString("dd-MMM HH:mm");
            public string? HoldTag { get; set; }
            public string? CustomerName { get; set; }
            public decimal Total { get; set; }
            public string TotalFormatted => Total.ToString("N2", CultureInfo.CurrentCulture);
        }

        public int? SelectedSaleId { get; private set; }

        public HeldPickerWindow(DbContextOptions<PosClientDbContext> opts, int outletId, int counterId)
        {
            InitializeComponent();
            _opts = opts; _outletId = outletId; _counterId = counterId;
            LoadRows();
        }

        private void LoadRows()
        {
            using var db = new PosClientDbContext(_opts);
            var rows = db.Sales.AsNoTracking()
                .Where(s => s.OutletId == _outletId && s.CounterId == _counterId
                            && s.Status == SaleStatus.Draft)
                .OrderByDescending(s => s.Ts)
                .Select(s => new HeldRow
                {
                    Id = s.Id,
                    Ts = s.Ts,
                    HoldTag = s.HoldTag,
                    CustomerName = s.CustomerName,
                    Total = s.Total
                })
                .ToList();
            List.ItemsSource = rows;
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem is HeldRow r) { SelectedSaleId = r.Id; DialogResult = true; }
        }

        private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Resume_Click(sender, e);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (List.SelectedItem is not HeldRow r) return;
            var ok = MessageBox.Show($"Delete held invoice {r.Id}?", "Confirm", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;

            using var db = new PosClientDbContext(_opts);
            var s = db.Sales.FirstOrDefault(x => x.Id == r.Id && x.Status == SaleStatus.Draft);
            if (s != null)
            {
                // hard delete draft + lines (safe since it never affected stock)
                var lines = db.SaleLines.Where(x => x.SaleId == s.Id);
                db.SaleLines.RemoveRange(lines);
                db.Sales.Remove(s);
                db.SaveChanges();
            }
            LoadRows();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
