using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Purchases;
using Pos.Client.Wpf.Windows.Sales;
using Pos.Persistence;
using Pos.Client.Wpf.Services;
using Pos.Client.Wpf.Windows.Admin;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class DashboardWindow : Window
    {
        private readonly DashboardVm _vm;

        public DashboardWindow()
        {
            InitializeComponent();

            // Build a VM from DI and set DataContext
            var dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            var st = App.Services.GetRequiredService<AppState>();
            _vm = new DashboardVm(dbf, st);
            DataContext = _vm;

            // Refresh when the window loads
            Loaded += async (_, __) => await _vm.RefreshAsync();
        }

        private void NewSale_Click(object s, RoutedEventArgs e)
        {
            var win = App.Services.GetRequiredService<SaleInvoiceWindow>();
            win.Owner = this;
            win.Show();
        }

        private void NewPurchase_Click(object sender, RoutedEventArgs e)
        {
            // Use DI — your manual 'new PosClientDbContext(new DbContextOptions<...>())' was wrong
            var w = App.Services.GetRequiredService<PurchaseWindow>();
            w.Owner = this;
            w.ShowDialog();
        }

        private void OpenProductsItems_Click(object s, RoutedEventArgs e)
        {
            var w = new Pos.Client.Wpf.Windows.Admin.ProductsItemsWindow();
            w.ShowDialog();
        }
        
        private void OpenOutletsCounters_Click(object s, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>();
            w.Owner = this;
            w.ShowDialog();

            // After assigning a counter, refresh the banner
            _ = _vm.RefreshAsync();
        }
        private void OpenUsers_Click(object s, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.UsersWindow>();
            w.Owner = this;
            w.ShowDialog();
        }
        private void OpenParties_Click(object s, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.PartiesWindow>();
            w.Owner = this;
            w.ShowDialog();
        }

        private void OpenWarehouses_Click(object sender, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.WarehousesWindow>();
            w.Owner = this;
            w.ShowDialog();
        }

        private void OpenSamples_Click(object sender, RoutedEventArgs e)
        {
            var w = new Sample();
            w.Owner = this;
            w.ShowDialog();
        }
        

    }
}
