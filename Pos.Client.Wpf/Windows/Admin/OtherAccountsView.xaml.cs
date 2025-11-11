using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Client.Wpf.Services;      // AppEvents, AuthZ
using Pos.Domain.Services;     // OtherAccountService
using Pos.Client.Wpf.Infrastructure;


namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OtherAccountsView : UserControl
    {
        private IOtherAccountService? _svc;
        private Func<OtherAccountDialog>? _dialogFactory;
        private readonly bool _design;

        public OtherAccountsView()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<IOtherAccountService>();
            _dialogFactory = () => App.Services.GetRequiredService<OtherAccountDialog>();

            Loaded += async (_, __) => await RefreshAsync();
        }

        private bool Ready => !_design && _svc != null;

        // ---------------- REFRESH ----------------
        private async Task RefreshAsync()
        {
            if (!Ready) return;
            try
            {
                Grid.ItemsSource = await _svc!.GetAllAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load accounts: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------------- BUTTONS ----------------
        private async void New_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthZ.IsManagerOrAbove())
            {
                MessageBox.Show("Only Manager or Admin can create new accounts.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = _dialogFactory!();
            dlg.Configure(null);
            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not OtherAccount row) return;

            if (!AuthZ.IsManagerOrAbove())
            {
                MessageBox.Show("Only Manager or Admin can edit accounts.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = _dialogFactory!();
            dlg.Configure(row.Id);
            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not OtherAccount row) return;

            if (!AuthZ.IsAdmin())
            {
                MessageBox.Show("Only Admin can delete accounts.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Delete account “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                var ok = await _svc!.DeleteAsync(row.Id);
                if (!ok)
                {
                    MessageBox.Show("Account could not be deleted. It may be linked or used in GL.",
                        "Other Accounts", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AppEvents.RaiseAccountsChanged();
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Delete failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    }
}
