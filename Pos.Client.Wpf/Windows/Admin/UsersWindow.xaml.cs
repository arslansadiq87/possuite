// Pos.Client.Wpf/Windows/Admin/UsersWindow.cs
using System;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Common;

namespace Pos.Client.Wpf.Windows.Admin
{
    // What the DataGrid shows (typed, stable shape for WPF bindings)
    public sealed class UserRow
    {
        public int Id { get; init; }
        public string Username { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public UserRole Role { get; init; }          // keep as enum; DataGrid will show the enum name
        public bool IsActive { get; init; }
        public bool IsGlobalAdmin { get; init; }
    }

    public partial class UsersWindow : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public UsersWindow(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            Loaded += (_, __) => LoadUsers();
        }

        private void LoadUsers()
        {
            try
            {
                using var db = _dbf.CreateDbContext();

                var users = db.Users.AsNoTracking()
                    .OrderBy(u => u.Username)
                    .Select(u => new UserRow
                    {
                        Id = u.Id,
                        Username = u.Username,
                        DisplayName = u.DisplayName,
                        Role = u.Role,
                        IsActive = u.IsActive,
                        IsGlobalAdmin = u.IsGlobalAdmin
                    })
                    .ToList();

                UsersGrid.ItemsSource = users;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load users:\n\n" + ex.Message,
                                "Users", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadUsers();

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // Work with a real User entity for edits/creation
            var u = new User
            {
                Username = "newuser",
                DisplayName = "New User",
                Role = UserRole.Cashier,
                IsActive = true
            };

            if (EditUserDialog(ref u, isNew: true))
            {
                try
                {
                    using var db = _dbf.CreateDbContext();

                    // If the dialog provided a "{PLAIN}:" password, hash or store accordingly
                    if (!string.IsNullOrWhiteSpace(u.PasswordHash) && u.PasswordHash.StartsWith("{PLAIN}:"))
                    {
                        var plain = u.PasswordHash.Substring("{PLAIN}:".Length);
                        // TODO: replace with real hasher from your AuthService
                        u.PasswordHash = plain;
                    }

                    db.Users.Add(u);
                    db.SaveChanges();
                    LoadUsers();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Add user failed:\n\n" + ex.Message, "Users",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow sel)
            {
                MessageBox.Show("Select a user.");
                return;
            }

            try
            {
                using var db = _dbf.CreateDbContext();
                var dbU = db.Users.First(u => u.Id == sel.Id);

                // Edit a copy to avoid mutating tracked entity before confirmation
                var copy = new User
                {
                    Id = dbU.Id,
                    Username = dbU.Username,
                    DisplayName = dbU.DisplayName,
                    Role = dbU.Role,
                    IsActive = dbU.IsActive,
                    IsGlobalAdmin = dbU.IsGlobalAdmin,
                    PasswordHash = "" // do not expose stored hash; leave blank
                };

                if (!EditUserDialog(ref copy, isNew: false)) return;

                // Apply changes back to tracked entity
                dbU.Username = copy.Username;
                dbU.DisplayName = copy.DisplayName;
                dbU.Role = copy.Role;
                dbU.IsActive = copy.IsActive;
                dbU.IsGlobalAdmin = copy.IsGlobalAdmin;

                if (!string.IsNullOrWhiteSpace(copy.PasswordHash) &&
                    copy.PasswordHash.StartsWith("{PLAIN}:"))
                {
                    var plain = copy.PasswordHash.Substring("{PLAIN}:".Length);
                    // TODO: replace with real hasher from your AuthService
                    dbU.PasswordHash = plain;
                }

                db.SaveChanges();
                LoadUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Edit user failed:\n\n" + ex.Message, "Users",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow sel)
            {
                MessageBox.Show("Select a user.");
                return;
            }

            if (MessageBox.Show($"Delete user '{sel.Username}'?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                using var db = _dbf.CreateDbContext();
                var dbU = db.Users.Include(u => u.UserOutlets).First(u => u.Id == sel.Id);

                if (dbU.UserOutlets.Any())
                {
                    MessageBox.Show("User has outlet assignments. Remove them first.");
                    return;
                }

                db.Users.Remove(dbU);
                db.SaveChanges();
                LoadUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete user failed:\n\n" + ex.Message, "Users",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Assignments_Click(object s, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow sel)
            {
                MessageBox.Show("Select a user.");
                return;
            }

            var dlg = new UserOutletAssignmentsWindow(_dbf, sel.Id) { Owner = this };
            dlg.ShowDialog();
        }

        // Simple inline editor using your existing SimplePromptWindow
        private bool EditUserDialog(ref User u, bool isNew)
        {
            var dlg = new SimplePromptWindow(
                isNew ? "Add User" : "Edit User",
                ("Username", u.Username),
                ("DisplayName", u.DisplayName),
                ("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)", u.Role.ToString()),
                ("Active", u.IsActive),
                ("GlobalAdmin", u.IsGlobalAdmin),
                ("Password ({PLAIN}:yourpassword to change)", "")
            );

            if (dlg.ShowDialog() == true)
            {
                u.Username = dlg.GetText("Username");
                u.DisplayName = dlg.GetText("DisplayName");

                var roleStr = dlg.GetText("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)");
                if (!Enum.TryParse<UserRole>(roleStr, true, out var parsed))
                    parsed = UserRole.Cashier;
                u.Role = parsed;

                u.IsActive = dlg.GetBool("Active");
                u.IsGlobalAdmin = dlg.GetBool("GlobalAdmin");

                var pwd = dlg.GetText("Password ({PLAIN}:yourpassword to change)");
                if (!string.IsNullOrWhiteSpace(pwd))
                    u.PasswordHash = pwd; // "{PLAIN}:..." convention handled by callers

                return true;
            }
            return false;
        }
    }
}
