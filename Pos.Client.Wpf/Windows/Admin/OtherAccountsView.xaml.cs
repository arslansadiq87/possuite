using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Infrastructure;
using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OtherAccountsView : UserControl
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public OtherAccountsView()
        {
            InitializeComponent();
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            Loaded += async (_, __) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            using var db = _dbf.CreateDbContext();
            Grid.ItemsSource = await db.OtherAccounts.AsNoTracking()
                                 .OrderBy(x => x.Name)
                                 .ToListAsync();
        }

        private async void New_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OtherAccountDialog(_dbf);
            dlg.Configure(null);
            if (dlg.ShowDialog() == true) await RefreshAsync();
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is OtherAccount row)
            {
                var dlg = new OtherAccountDialog(_dbf);
                dlg.Configure(row.Id);
                if (dlg.ShowDialog() == true) await RefreshAsync();
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not OtherAccount row) return;

            using var db = _dbf.CreateDbContext();
            var entity = await db.OtherAccounts.FirstAsync(x => x.Id == row.Id);

            // Safety: prevent delete if GL or usage exists
            if (entity.AccountId.HasValue)
            {
                var usedInGl = await db.JournalLines.AnyAsync(l => l.AccountId == entity.AccountId.Value);
                if (usedInGl)
                {
                    MessageBox.Show("This account is used in GL and cannot be deleted.", "Other Accounts");
                    return;
                }
                // Also block if the Account is system (rare for Others, but check)
                var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == entity.AccountId.Value);
                if (acc?.IsSystem == true)
                {
                    MessageBox.Show("Cannot delete a system-linked account.", "Other Accounts");
                    return;
                }
                if (acc != null) db.Accounts.Remove(acc);
            }

            db.OtherAccounts.Remove(entity);
            await db.SaveChangesAsync();
            AppEvents.RaiseAccountsChanged();
            await RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    }
}
