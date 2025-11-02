using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Hr;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class StaffDialog : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private int? _id;
        public string DialogTitle => _id == null ? "New Staff" : "Edit Staff";

        public StaffDialog(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            DataContext = this;
        }

        public async void Configure(int? id)
        {
            _id = id;
            DataContext = null; DataContext = this;

            if (_id != null)
            {
                using var db = _dbf.CreateDbContext();
                var s = await db.Staff.AsNoTracking().FirstAsync(x => x.Id == _id.Value);

                CodeBox.Text = s.Code ?? "";
                NameBox.Text = s.FullName ?? "";

                // JoinedOnUtc -> local date for DatePicker
                var localJoined = DateTime.SpecifyKind(s.JoinedOnUtc, DateTimeKind.Utc).ToLocalTime().Date;
                JoinDatePicker.SelectedDate = localJoined;

                // BasicSalary is non-nullable decimal -> no null-conditional operator
                SalaryBox.Text = s.BasicSalary.ToString(CultureInfo.InvariantCulture);

                ActsAsSalesmanBox.IsChecked = s.ActsAsSalesman;
            }
            else
            {
                CodeBox.Text = await GenerateNextStaffCodeAsync();
                JoinDatePicker.SelectedDate = DateTime.Today;
                SalaryBox.Text = "0";
                ActsAsSalesmanBox.IsChecked = false;
            }
        }

        private async Task<string> GenerateNextStaffCodeAsync()
        {
            using var db = _dbf.CreateDbContext();
            var codes = await db.Staff.AsNoTracking().Select(s => s.Code).ToListAsync();
            int max = 0;
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var last = c!.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"STF-{(max + 1).ToString("D3")}";
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

            using var db = _dbf.CreateDbContext();
            Staff s;
            if (_id == null) { s = new Staff(); db.Staff.Add(s); }
            else { s = await db.Staff.FirstAsync(x => x.Id == _id.Value); }

            // Map fields
            s.Code = string.IsNullOrWhiteSpace(CodeBox.Text) ? await GenerateNextStaffCodeAsync() : CodeBox.Text.Trim();
            s.FullName = NameBox.Text.Trim();

            var jd = (JoinDatePicker.SelectedDate ?? DateTime.Today);
            // store as UTC in entity
            s.JoinedOnUtc = DateTime.SpecifyKind(jd, DateTimeKind.Local).ToUniversalTime();

            s.BasicSalary = basic;                         // non-nullable decimal
            s.ActsAsSalesman = ActsAsSalesmanBox.IsChecked == true;

            // Ensure CoA account under "63 Staff"
            // Ensure CoA account under "63 Staff" (link-aware, like Party)
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "63");
            if (parent == null)
            {
                MessageBox.Show("Chart of Accounts header 63 (Staff) not found. Seed CoA first.");
                return;
            }

            Account? linked = null;
            if (s.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == s.AccountId.Value);
                if (linked == null) s.AccountId = null; // dangling link, treat as missing
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent);
                linked = new Account
                {
                    Code = code,
                    Name = s.FullName,             // keep 1:1 label with staff
                    Type = AccountType.Parties,
                    NormalSide = NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                db.Accounts.Add(linked);
                await db.SaveChangesAsync();   // get Id for link
                s.AccountId = linked.Id;
            }
            else
            {
                // Keep tidy on edit/rename; ensure parent is 63 (in case header changed in template)
                linked.Name = s.FullName;
                if (linked.ParentId != parent.Id)
                    linked.ParentId = parent.Id;
                linked.NormalSide = NormalSide.Debit;
            }

            await db.SaveChangesAsync();
            Pos.Client.Wpf.Infrastructure.AppEvents.RaiseAccountsChanged();

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent)
        {
            var sibs = await db.Accounts
                .AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync();

            int max = 0;
            foreach (var code in sibs)
            {
                var last = code?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"{parent.Code}-{(max + 1).ToString("D3")}";
        }
    }
}
