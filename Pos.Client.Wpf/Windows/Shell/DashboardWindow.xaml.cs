//Pos.Client.Wpf/Windows/Shell/DashboardWindow.cs
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Purchases;
using Pos.Client.Wpf.Windows.Sales;
using Pos.Persistence;
using Pos.Client.Wpf.Services;


namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class DashboardWindow : Window
    {
        private readonly DbContextOptions<PosClientDbContext> _opts;

        public DashboardWindow()
        {
            InitializeComponent();
            _opts = Db.ClientOptions; // see Db helper below

        }


        private void NewSale_Click(object s, RoutedEventArgs e)
        {
            var win = App.Services.GetRequiredService<SaleInvoiceWindow>();
            win.Owner = this;
            win.Show();
            // later: open Sales/SaleInvoiceWindow
            //MessageBox.Show("Sale window to be wired in Step 2", "Info");
        }

        private void NewPurchase_Click(object sender, RoutedEventArgs e)
        {
            // however you currently build your DbContext (DI or manual)
            var db = /* resolve or create */ new PosClientDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptions<PosClientDbContext>());
            new PurchaseWindow(db) { Owner = this }.ShowDialog();
        }

        private void OpenProductsItems_Click(object s, RoutedEventArgs e)
        {
            var w = new Pos.Client.Wpf.Windows.Admin.ProductsItemsWindow();
            w.ShowDialog();
        }
        private void OpenSuppliers_Click(object s, RoutedEventArgs e)
        {
            var w = new Pos.Client.Wpf.Windows.Admin.SuppliersWindow();
            w.ShowDialog();
        }
        private void OpenOutletsCounters_Click(object s, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.OutletsCountersWindow>();
            w.Owner = this;
            w.ShowDialog();
        }
        private void OpenUsers_Click(object s, RoutedEventArgs e)
        {
            var w = App.Services.GetRequiredService<Pos.Client.Wpf.Windows.Admin.UsersWindow>();
            w.Owner = this;
            w.ShowDialog();
        }
        private void OpenCustomers_Click(object s, RoutedEventArgs e)
        {
            var w = new Pos.Client.Wpf.Windows.Admin.CustomersWindow();
            w.ShowDialog();
        }

      
    }
}
