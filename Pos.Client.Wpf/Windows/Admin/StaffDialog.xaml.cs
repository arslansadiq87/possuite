using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Hr;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class StaffDialog : Window
    {
        private readonly IStaffService _svc;

        private int? _id;
        public string DialogTitle => _id == null ? "New Staff" : "Edit Staff";
        public StaffDialog()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<IStaffService>();
            DataContext = this;
        }

        public async void Configure(int? id)
        {
            _id = id;
            DataContext = null; DataContext = this;

            if (_id != null)
            {
                var s = await _svc.GetAsync(_id.Value);
                if (s == null)
                {
                    MessageBox.Show("Staff not found.", "Staff",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }
                CodeBox.Text = s.Code ?? "";
                NameBox.Text = s.FullName ?? "";
                var localJoined = DateTime.SpecifyKind(s.JoinedOnUtc, DateTimeKind.Utc).ToLocalTime().Date;
                JoinDatePicker.SelectedDate = localJoined;
                SalaryBox.Text = s.BasicSalary.ToString(CultureInfo.InvariantCulture);
                ActsAsSalesmanBox.IsChecked = s.ActsAsSalesman;
            }
            else
            {
                CodeBox.Text = await _svc.GenerateNextStaffCodeAsync();
                JoinDatePicker.SelectedDate = DateTime.Today;
                SalaryBox.Text = "0";
                ActsAsSalesmanBox.IsChecked = false;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Full Name is required."); return;
            }
            if (!decimal.TryParse(SalaryBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var basic))
            {
                MessageBox.Show("Salary must be a valid number."); return;
            }
            try
            {
                var name = NameBox.Text.Trim();
                var taken = await _svc.IsNameTakenAsync(name, excludingId: _id);
                if (taken)
                {
                    MessageBox.Show("Another staff member already uses this name.",
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var jdLocal = (JoinDatePicker.SelectedDate ?? DateTime.Today);
                var jdUtc = DateTime.SpecifyKind(jdLocal, DateTimeKind.Local).ToUniversalTime();
                var model = new Staff
                {
                    Id = _id ?? 0,
                    Code = string.IsNullOrWhiteSpace(CodeBox.Text)
                        ? null
                        : CodeBox.Text.Trim(),
                    FullName = name,
                    JoinedOnUtc = jdUtc,
                    BasicSalary = basic,
                    ActsAsSalesman = ActsAsSalesmanBox.IsChecked == true
                };
                var savedId = await _svc.CreateOrUpdateAsync(model);
                try { Pos.Client.Wpf.Infrastructure.AppEvents.RaiseAccountsChanged(); } catch { /* ignore */ }
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save staff:\n\n" + ex.Message, "Staff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
