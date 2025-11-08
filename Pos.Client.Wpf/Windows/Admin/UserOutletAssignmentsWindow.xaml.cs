using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Common;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class UserOutletAssignmentsWindow : Window
    {
        private readonly OutletCounterService _svc;
        private readonly int _userId;

        public UserOutletAssignmentsWindow(int userId)
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<OutletCounterService>();
            _userId = userId;
            Loaded += async (_, __) => await LoadRowsAsync();
        }

        private async Task LoadRowsAsync()
        {
            try
            {
                var rows = await _svc.GetUserOutletsAsync(_userId);
                Grid.ItemsSource = rows;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load assignments:\n\n" + ex.Message, "Assignments",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Simple prompt — can replace later with a nicer dialog
                var allOutlets = (await _svc.GetOutletsAsync()).OrderBy(o => o.Name).ToList();
                var dlg = new SimplePromptWindow(
                    "Assign Outlet",
                    ("OutletId (existing)", allOutlets.FirstOrDefault()?.Id.ToString() ?? "1"),
                    ("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)", UserRole.Cashier.ToString())
                );
                if (dlg.ShowDialog() != true) return;

                if (!int.TryParse(dlg.GetText("OutletId (existing)"), out var outletId))
                {
                    MessageBox.Show("Invalid OutletId"); return;
                }

                if (!Enum.TryParse<UserRole>(
                        dlg.GetText("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)"),
                        true,
                        out var role))
                    role = UserRole.Cashier;

                await _svc.AssignOutletAsync(_userId, outletId, role);
                await LoadRowsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to assign outlet:\n\n" + ex.Message,
                    "Assignments", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is not UserOutlet row)
                {
                    MessageBox.Show("Select a row."); return;
                }

                var dlg = new SimplePromptWindow(
                    "Edit Role",
                    ("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)", row.Role.ToString())
                );
                if (dlg.ShowDialog() != true) return;

                if (!Enum.TryParse<UserRole>(
                        dlg.GetText("Role(enum:Salesman,Cashier,Supervisor,Manager,Admin)"),
                        true,
                        out var newRole))
                    newRole = row.Role;

                await _svc.UpdateUserOutletRoleAsync(_userId, row.OutletId, newRole);
                await LoadRowsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit role:\n\n" + ex.Message,
                    "Assignments", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Grid.SelectedItem is not UserOutlet row)
                {
                    MessageBox.Show("Select a row."); return;
                }

                if (MessageBox.Show(
                        $"Remove outlet '{row.Outlet?.Name ?? row.OutletId.ToString()}' from user?",
                        "Confirm",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    ) != MessageBoxResult.Yes)
                    return;

                await _svc.RemoveUserOutletAsync(_userId, row.OutletId);
                await LoadRowsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to remove assignment:\n\n" + ex.Message,
                    "Assignments", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
