using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence.Services;

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

    public partial class UsersView : UserControl
    {
        private readonly UserAdminService _svc;
        private readonly OutletCounterService _outletSvc; // for the Assignments dialog, if used

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

        private static string RoleToText(UserRole r) => r switch
        {
            UserRole.Salesman => "Salesman",
            UserRole.Cashier => "Cashier",
            UserRole.Supervisor => "Supervisor",
            UserRole.Manager => "Manager",
            UserRole.Admin => "Admin",
            _ => "Cashier"
        };

        private static UserRole TextToRole(string s) => (s ?? "").Trim().ToLowerInvariant() switch
        {
            "salesman" => UserRole.Salesman,
            "cashier" => UserRole.Cashier,
            "supervisor" => UserRole.Supervisor,
            "manager" => UserRole.Manager,
            "admin" => UserRole.Admin,
            _ => UserRole.Cashier
        };

        public UsersView(AppState state)
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<UserAdminService>();
            _outletSvc = App.Services.GetRequiredService<OutletCounterService>();

            var currentUser = state.CurrentUser;
            _currentIsGlobalAdmin = currentUser?.IsGlobalAdmin == true;
            _currentRole = currentUser?.Role ?? UserRole.Cashier;

            DataContext = this;
            Loaded += async (_, __) => await LoadUsersAsync();
        }

        // ─────────── Data loading ───────────
        private async System.Threading.Tasks.Task LoadUsersAsync()
        {
            try
            {
                var users = (await _svc.GetAllAsync())
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

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

        // ─────────── Toolbar actions ───────────
        private void Add_Click(object sender, RoutedEventArgs e)
        {
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

            if (!_currentIsGlobalAdmin)
            {
                if (EdRole.Items.Count >= 5 && EdRole.Items[4] is ComboBoxItem adminItem)
                    adminItem.IsEnabled = false;
            }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow sel)
            {
                MessageBox.Show("Select a user.");
                return;
            }

            try
            {
                var dbU = await _svc.GetAsync(sel.Id) ?? throw new InvalidOperationException("User not found.");
                OpenEditor(new User
                {
                    Id = dbU.Id,
                    Username = dbU.Username,
                    DisplayName = dbU.DisplayName,
                    Role = dbU.Role,
                    IsActive = dbU.IsActive,
                    IsGlobalAdmin = dbU.IsGlobalAdmin,
                    PasswordHash = "" // never show
                }, isNew: false);

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

        private async void Delete_Click(object sender, RoutedEventArgs e)
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
                await _svc.DeleteAsync(sel.Id);
                await LoadUsersAsync();
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

            // Use the service-driven assignments window we already refactored
            var dlg = new UserOutletAssignmentsWindow(sel.Id) { };
            dlg.ShowDialog();
        }

        // ─────────── Editor open/close helpers ───────────
        private async void OpenEditor(User snapshot, bool isNew)
        {
            _isNew = isNew;
            _editingUserId = isNew ? null : snapshot.Id;

            EditorTitle.Text = isNew ? "Add User" : "Edit User";
            EdUsername.Text = snapshot.Username ?? "";
            EdDisplayName.Text = snapshot.DisplayName ?? "";

            EdRole.SelectedIndex = RoleToIndex(snapshot.Role);
            EdActive.IsChecked = snapshot.IsActive;
            EdGlobalAdmin.IsChecked = snapshot.IsGlobalAdmin;

            EdPassword.Password = "";

            // Load all outlets + current assignments via service
            _outletRows.Clear();
            var outlets = await _svc.GetOutletsAsync();
            var assigned = isNew
                ? new System.Collections.Generic.Dictionary<int, UserRole>()
                : await _svc.GetUserAssignmentsAsync(snapshot.Id);

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

            EdOutletGrid.ItemsSource = _outletRows;
            ShowEditor(true);
        }

        private void ShowEditor(bool show)
        {
            if (show)
            {
                _originalWidth ??= this.Width;
                if (this.Width < 980) this.Width = 980;

                EditorPanel.Visibility = Visibility.Visible;
                EditorCol.Width = new GridLength(390);
            }
            else
            {
                if (_originalWidth.HasValue) this.Width = _originalWidth.Value;

                EditorPanel.Visibility = Visibility.Collapsed;
                EditorCol.Width = new GridLength(0);
            }
        }

        // ─────────── Editor buttons ───────────
        private void CancelEditor_Click(object sender, RoutedEventArgs e)
        {
            ShowEditor(false);
            _editingUserId = null;
        }

        private async void SaveEditor_Click(object sender, RoutedEventArgs e)
        {
            var username = EdUsername.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Username is required.");
                return;
            }

            var selectedRole = IndexToRole(EdRole.SelectedIndex);
            var wantsGlobalAdmin = EdGlobalAdmin.IsChecked == true;
            var newPwd = EdPassword.Password ?? "";

            // Permission checks
            if (!_currentIsGlobalAdmin)
            {
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
                    if (_isNew)
                    {
                        MessageBox.Show("You do not have permission to create users.", "Users",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    if (selectedRole == UserRole.Admin || wantsGlobalAdmin)
                    {
                        MessageBox.Show("You do not have permission to assign Admin or Global Admin.", "Users",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            try
            {
                // Uniqueness check
                var taken = await _svc.IsUsernameTakenAsync(username, excludingId: _editingUserId);
                if (taken)
                {
                    MessageBox.Show("Another user already uses this username.",
                        "Duplicate Username", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
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

                var savedId = await _svc.CreateOrUpdateAsync(entity, string.IsNullOrWhiteSpace(newPwd) ? null : newPwd);

                // Save outlet assignments from sidebar grid
                var desired = _outletRows.Select(r => (r.OutletId, r.IsAssigned, TextToRole(r.Role)));
                await _svc.SaveAssignmentsAsync(savedId, desired);

                ShowEditor(false);
                _editingUserId = null;
                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed:\n\n" + ex.Message, "Users",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─────────── Role helpers ───────────
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
