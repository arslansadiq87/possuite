using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    // Row shape for the grid
    public sealed class UserRow
    {
        public int Id { get; init; }
        public string Username { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public UserRole Role { get; init; }
        public bool IsActive { get; init; }
        public bool IsGlobalAdmin { get; init; }
    }

    // Row model for outlet assignment editor
      public partial class UsersView : UserControl
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ObservableCollection<OutletAssignRow> _outletRows = new();
        private readonly bool _currentIsGlobalAdmin;
        private readonly UserRole _currentRole;
        public bool CanSetGlobalAdmin => _currentIsGlobalAdmin;
        private bool _isNew;
        private int? _editingUserId;
        private double? _originalWidth;
        private sealed class OutletAssignRow
        {
            public int OutletId { get; init; }
            public string OutletName { get; init; } = "";
            public bool IsAssigned { get; set; }
            public string Role { get; set; } = "Cashier"; // default UI role text
        }

        // Map enum <-> UI text
        private static string RoleToText(UserRole r) => r switch
        {
            UserRole.Salesman => "Salesman",
            UserRole.Cashier => "Cashier",
            UserRole.Supervisor => "Supervisor",
            UserRole.Manager => "Manager",
            UserRole.Admin => "Admin",
            _ => "Cashier"
        };
        
        public UsersView(IDbContextFactory<PosClientDbContext> dbf, AppState state)
        {
            InitializeComponent();
            _dbf = dbf;

            var currentUser = state.CurrentUser;   // <<-- your already-saved login user
            _currentIsGlobalAdmin = currentUser?.IsGlobalAdmin == true;
            _currentRole = currentUser?.Role ?? UserRole.Cashier;

            DataContext = this;
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

        // --- Toolbar actions ---
        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // Only GA or Admin can open Add; others cannot
            if (!_currentIsGlobalAdmin && _currentRole != UserRole.Admin)
            {
                MessageBox.Show("You do not have permission to create users.", "Users",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenEditor(new User
            {
                Username = "",
                DisplayName = "",
                Role = UserRole.Cashier,
                IsActive = true,
                IsGlobalAdmin = false
            }, isNew: true);

            // If current user is NOT Global Admin, prevent selecting Admin role
            if (!_currentIsGlobalAdmin)
            {
                // Disable "Admin" option (index 4)
                if (EdRole.Items.Count >= 5 && EdRole.Items[4] is ComboBoxItem adminItem)
                    adminItem.IsEnabled = false;
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

                OpenEditor(new User
                {
                    Id = dbU.Id,
                    Username = dbU.Username,
                    DisplayName = dbU.DisplayName,
                    Role = dbU.Role,
                    IsActive = dbU.IsActive,
                    IsGlobalAdmin = dbU.IsGlobalAdmin,
                    PasswordHash = ""
                }, isNew: false);

                // Non-GA cannot assign Admin role or Global Admin
                if (!_currentIsGlobalAdmin)
                {
                    if (EdRole.Items.Count >= 5 && EdRole.Items[4] is ComboBoxItem adminItem)
                        adminItem.IsEnabled = false;

                    EdGlobalAdmin.IsEnabled = false;
                }
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

            // keep your existing assignments dialog
            var dlg = new UserOutletAssignmentsWindow(_dbf, sel.Id) { };
            dlg.ShowDialog();
        }

        // --- Editor open/close helpers ---
        private void OpenEditor(User snapshot, bool isNew)
        {
            _isNew = isNew;
            _editingUserId = isNew ? null : snapshot.Id;

            EditorTitle.Text = isNew ? "Add User" : "Edit User";
            EdUsername.Text = snapshot.Username ?? "";
            EdDisplayName.Text = snapshot.DisplayName ?? "";

            EdRole.SelectedIndex = RoleToIndex(snapshot.Role);
            EdActive.IsChecked = snapshot.IsActive;
            EdGlobalAdmin.IsChecked = snapshot.IsGlobalAdmin;   // now sticks

            EdPassword.Password = ""; // never prefill actual hash
                                      // Load all outlets and current assignments
            _outletRows.Clear();
            using (var db = _dbf.CreateDbContext())
            {
                var outlets = db.Outlets
                                .AsNoTracking()
                                .OrderBy(o => o.Name)
                                .Select(o => new { o.Id, o.Name })
                                .ToList();

                // Existing assignments if editing
                Dictionary<int, UserRole> assigned = new();
                if (!isNew)
                {
                    assigned = db.UserOutlets
                                 .AsNoTracking()
                                 .Where(x => x.UserId == snapshot.Id)
                                 .ToDictionary(x => x.OutletId, x => x.Role);
                }

                foreach (var o in outlets)
                {
                    var has = assigned.TryGetValue(o.Id, out var r);
                    _outletRows.Add(new OutletAssignRow
                    {
                        OutletId = o.Id,
                        OutletName = o.Name,
                        IsAssigned = has,
                        Role = RoleToText(has ? r : UserRole.Cashier)
                    });
                }
            }
            EdOutletGrid.ItemsSource = _outletRows;



            ShowEditor(true);   // open sidebar
        }

        private void ShowEditor(bool show)
        {
            if (show)
            {
                // optional window auto-grow
                _originalWidth ??= this.Width;
                if (this.Width < 980) this.Width = 980;

                EditorPanel.Visibility = Visibility.Visible;
                EditorCol.Width = new GridLength(390);   // sidebar width when open
            }
            else
            {
                // optional revert to original width
                if (_originalWidth.HasValue) this.Width = _originalWidth.Value;

                EditorPanel.Visibility = Visibility.Collapsed;
                EditorCol.Width = new GridLength(0);     // fully collapse column
            }
        }

        // --- Editor buttons ---
        private void CancelEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowEditor(false);
            _editingUserId = null;
        }

        private void SaveEditor_Click(object sender, RoutedEventArgs e)
        {
            var username = EdUsername.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Username is required.");
                return;
            }

            var selectedRole = IndexToRole(EdRole.SelectedIndex);
            var wantsGlobalAdmin = EdGlobalAdmin.IsChecked == true;

            // Permission checks
            if (!_currentIsGlobalAdmin)
            {
                // Admin can create below-Admin only, and cannot set Global Admin
                if (_currentRole == UserRole.Admin)
                {
                    if (selectedRole == UserRole.Admin || wantsGlobalAdmin)
                    {
                        MessageBox.Show("Only a Global Admin can create Admin users or grant Global Admin.", "Users",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    // Non-admin cannot create or change users
                    if (_isNew)
                    {
                        MessageBox.Show("You do not have permission to create users.", "Users",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // For edits, also block elevation to Admin / GA
                    if (selectedRole == UserRole.Admin || wantsGlobalAdmin)
                    {
                        MessageBox.Show("You do not have permission to assign Admin or Global Admin.", "Users",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            var entity = new User
            {
                Id = _editingUserId ?? 0,
                Username = username,
                DisplayName = EdDisplayName.Text?.Trim() ?? "",
                Role = selectedRole,
                IsActive = EdActive.IsChecked == true,
                IsGlobalAdmin = wantsGlobalAdmin,
            };

            var newPwd = EdPassword.Password ?? "";

            try
            {
                using var db = _dbf.CreateDbContext();
                using var tx = db.Database.BeginTransaction();

                if (_isNew)
                {
                    entity.PasswordHash = string.IsNullOrWhiteSpace(newPwd)
                        ? BCrypt.Net.BCrypt.HashPassword("1234")
                        : BCrypt.Net.BCrypt.HashPassword(newPwd);

                    db.Users.Add(entity);
                    db.SaveChanges();
                }
                else
                {
                    var dbU = db.Users.First(u => u.Id == entity.Id);

                    // Prevent self-demotion/elevation abuses as needed (optional extra rules)

                    dbU.Username = entity.Username;
                    dbU.DisplayName = entity.DisplayName;
                    dbU.Role = entity.Role;
                    dbU.IsActive = entity.IsActive;
                    dbU.IsGlobalAdmin = entity.IsGlobalAdmin;

                    if (!string.IsNullOrWhiteSpace(newPwd))
                        dbU.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPwd);

                    db.SaveChanges();
                }

                // (keep your existing outlet-assignment save block here if present)

                tx.Commit();

                ShowEditor(false);
                _editingUserId = null;
                LoadUsers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed:\n\n" + ex.Message, "Users",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // --- Role helpers ---
        private int RoleToIndex(UserRole role) => role switch
        {
            UserRole.Salesman => 0,
            UserRole.Cashier => 1,
            UserRole.Supervisor => 2,
            UserRole.Manager => 3,
            UserRole.Admin => 4,
            _ => 1
        };

        private UserRole IndexToRole(int idx) => idx switch
        {
            0 => UserRole.Salesman,
            1 => UserRole.Cashier,
            2 => UserRole.Supervisor,
            3 => UserRole.Manager,
            4 => UserRole.Admin,
            _ => UserRole.Cashier
        };
    }
}
