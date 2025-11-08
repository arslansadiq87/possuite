using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Infrastructure;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OtherAccountDialog : Window
    {
        private readonly OtherAccountService _svc;
        private int? _id;

        public string DialogTitle => _id == null ? "New Other Account" : "Edit Other Account";

        public OtherAccountDialog(OtherAccountService svc)
        {
            InitializeComponent();
            _svc = svc;
            DataContext = this;
        }

        public async void Configure(int? id)
        {
            _id = id;
            DataContext = null; DataContext = this;

            if (_id is null)
            {
                CodeBox.Text = await _svc.GenerateNextOtherCodeAsync();
                return;
            }

            var row = await _svc.GetAsync(_id.Value);
            if (row == null) { DialogResult = false; Close(); return; }

            CodeBox.Text = row.Code ?? "";
            NameBox.Text = row.Name;
            PhoneBox.Text = row.Phone ?? "";
            EmailBox.Text = row.Email ?? "";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Name is required.");
                return;
            }

            var dto = new OtherAccountService.OtherAccountDto
            {
                Id = _id,
                Code = CodeBox.Text,
                Name = NameBox.Text,
                Phone = PhoneBox.Text,
                Email = EmailBox.Text
            };

            try
            {
                await _svc.UpsertAsync(dto);
                AppEvents.RaiseAccountsChanged();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
