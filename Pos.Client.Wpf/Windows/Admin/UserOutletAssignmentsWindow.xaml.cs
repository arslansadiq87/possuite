using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Common;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class UserOutletAssignmentsWindow : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly int _userId;

        public UserOutletAssignmentsWindow(IDbContextFactory<PosClientDbContext> dbf, int userId)
        {
            InitializeComponent();
            _dbf = dbf; _userId = userId;
            Loaded += (_, __) => LoadRows();
        }

        private void LoadRows()
        {
            using var db = _dbf.CreateDbContext();
            var rows = db.UserOutlets
                .Include(uo => uo.Outlet)
                .Where(uo => uo.UserId == _userId)
                .AsNoTracking()
                .OrderBy(uo => uo.Outlet.Name)
                .ToList();
            Grid.ItemsSource = rows;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            using var db = _dbf.CreateDbContext();
            var outlets = db.Outlets.AsNoTracking().OrderBy(o => o.Name).ToList();
            var dlg = new SimplePromptWindow(
                "Assign Outlet",
                ("OutletId (existing)", outlets.FirstOrDefault()?.Id.ToString() ?? "1"),
                ("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)", UserRole.Cashier.ToString())
            );
            if (dlg.ShowDialog() != true) return;

            if (!int.TryParse(dlg.GetText("OutletId (existing)"), out var outletId)) { MessageBox.Show("Invalid OutletId"); return; }
            if (!System.Enum.TryParse<UserRole>(dlg.GetText("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)"), true, out var role)) role = UserRole.Cashier;

            // guard: duplicate
            var exists = db.UserOutlets.Any(uo => uo.UserId == _userId && uo.OutletId == outletId);
            if (exists) { MessageBox.Show("Already assigned."); return; }

            db.UserOutlets.Add(new UserOutlet { UserId = _userId, OutletId = outletId, Role = role });
            db.SaveChanges();
            LoadRows();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not UserOutlet row) { MessageBox.Show("Select a row."); return; }
            var dlg = new SimplePromptWindow("Edit Role", ("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)", row.Role.ToString()));
            if (dlg.ShowDialog() != true) return;
            if (!System.Enum.TryParse<UserRole>(dlg.GetText("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)"), true, out var role)) role = row.Role;

            using var db = _dbf.CreateDbContext();
            var uo = db.UserOutlets.Find(row.UserId, row.OutletId)!;
            uo.Role = role;
            db.SaveChanges();
            LoadRows();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not UserOutlet row) { MessageBox.Show("Select a row."); return; }
            if (MessageBox.Show($"Remove outlet '{row.Outlet.Name}' from user?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            using var db = _dbf.CreateDbContext();
            var uo = db.UserOutlets.Find(row.UserId, row.OutletId)!;
            db.UserOutlets.Remove(uo);
            db.SaveChanges();
            LoadRows();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
