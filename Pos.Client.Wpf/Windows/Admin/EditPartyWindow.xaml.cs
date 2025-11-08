using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Infrastructure;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditPartyWindow : Window
    {
        private readonly bool _design;
        private PartyService? _svc;
        private int? _partyId;

        public class OutletVM
        {
            public int OutletId { get; set; }
            public string OutletName { get; set; } = "";
            public bool IsActive { get; set; }
            public bool AllowCredit { get; set; }
            public decimal? CreditLimit { get; set; }
            public decimal Balance { get; set; }
            public string BalanceDisplay => Balance == 0 ? "-" : Balance.ToString("N2");
        }

        public EditPartyWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<PartyService>();
            Loaded += async (_, __) => await LoadFormAsync();
        }

        public void LoadParty(int partyId) => _partyId = partyId;

        private async Task LoadFormAsync()
        {
            if (_svc == null) return;
            try
            {
                var dbParty = _partyId == null ? null : await _svc.GetPartyAsync(_partyId.Value);
                NameText.Text = dbParty?.Name ?? "";
                PhoneText.Text = dbParty?.Phone ?? "";
                EmailText.Text = dbParty?.Email ?? "";
                TaxText.Text = dbParty?.TaxNumber ?? "";
                ActiveCheck.IsChecked = dbParty?.IsActive ?? true;
                SharedCheck.IsChecked = dbParty?.IsSharedAcrossOutlets ?? true;
                RoleCustomerCheck.IsChecked = dbParty?.Roles.Any(r => r.Role == RoleType.Customer) ?? true;
                RoleSupplierCheck.IsChecked = dbParty?.Roles.Any(r => r.Role == RoleType.Supplier) ?? false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error loading party");
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_svc == null) return;

            ErrorText.Text = "";
            var name = (NameText.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorText.Text = "Name is required.";
                NameText.Focus(); return;
            }

            var outlets = (OutletsGrid.ItemsSource as IEnumerable<OutletVM>)?
                .Select(o => (o.OutletId, o.IsActive, o.AllowCredit, o.CreditLimit))
                .ToList() ?? new List<(int, bool, bool, decimal?)>();

            try
            {
                await _svc.SavePartyAsync(
                    _partyId,
                    name,
                    PhoneText.Text,
                    EmailText.Text,
                    TaxText.Text,
                    ActiveCheck.IsChecked == true,
                    SharedCheck.IsChecked == true,
                    RoleCustomerCheck.IsChecked == true,
                    RoleSupplierCheck.IsChecked == true,
                    outlets);

                AppEvents.RaiseAccountsChanged();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Save_Click(sender, e);
        }

        private void SharedChanged(object sender, RoutedEventArgs e)
        {
            // No-op: kept only to satisfy existing XAML binding.
        }

    }
}
