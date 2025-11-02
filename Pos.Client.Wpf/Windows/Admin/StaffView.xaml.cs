using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class StaffView : UserControl
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public StaffView(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            Loaded += async (_, __) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            using var db = _dbf.CreateDbContext();
            Grid.ItemsSource = await db.Staff
                .AsNoTracking()
                .OrderBy(s => s.FullName)
                .ToListAsync();
        }


        private async void New_Click(object sender, RoutedEventArgs e)
        {
            var dlg = App.Services.GetRequiredService<StaffDialog>();
            dlg.Configure(null); // New mode

            // find the host window for this UserControl
            var owner = Window.GetWindow(this)
                       ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;

            if (owner != null) dlg.Owner = owner;

            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not Pos.Domain.Hr.Staff s)
            {
                MessageBox.Show("Select a staff row first.");
                return;
            }

            var dlg = App.Services.GetRequiredService<StaffDialog>();
            dlg.Configure(s.Id); // Edit mode

            var owner = Window.GetWindow(this)
                       ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;

            if (owner != null) dlg.Owner = owner;

            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }


        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not Staff s)
            {
                MessageBox.Show("Select a staff row first."); return;
            }

            if (MessageBox.Show($"Delete {s.FullName}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            using var db = _dbf.CreateDbContext();
            var ent = await db.Staff.FirstAsync(x => x.Id == s.Id);
            db.Remove(ent);
            await db.SaveChangesAsync();
            await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    }
}
