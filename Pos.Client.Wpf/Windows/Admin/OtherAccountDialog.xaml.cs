using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Infrastructure;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OtherAccountDialog : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private int? _id;
        public string DialogTitle => _id == null ? "New Other Account" : "Edit Other Account";

        public OtherAccountDialog(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            DataContext = this;
        }

        public async void Configure(int? id)
        {
            _id = id;
            DataContext = null; DataContext = this;

            if (_id is null)
            {
                CodeBox.Text = await GenerateNextOtherCodeAsync();
                return;
            }

            using var db = _dbf.CreateDbContext();
            var row = await db.OtherAccounts.AsNoTracking().FirstAsync(x => x.Id == _id.Value);
            CodeBox.Text = row.Code ?? "";
            NameBox.Text = row.Name;
            PhoneBox.Text = row.Phone ?? "";
            EmailBox.Text = row.Email ?? "";
        }

        private async Task<string> GenerateNextOtherCodeAsync()
        {
            using var db = _dbf.CreateDbContext();
            var codes = await db.OtherAccounts.AsNoTracking().Select(s => s.Code).ToListAsync();
            int max = 0;
            foreach (var c in codes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var last = c!.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            return $"OTH-{(max + 1).ToString("D3")}";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Name is required."); return;
            }

            using var db = _dbf.CreateDbContext();
            OtherAccount row;
            if (_id == null) { row = new OtherAccount(); db.OtherAccounts.Add(row); }
            else { row = await db.OtherAccounts.FirstAsync(x => x.Id == _id.Value); }

            // Map
            row.Code = string.IsNullOrWhiteSpace(CodeBox.Text) ? await GenerateNextOtherCodeAsync() : CodeBox.Text.Trim();
            row.Name = NameBox.Text.Trim();
            row.Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
            row.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
            row.IsActive = true;

            // Ensure CoA account under "64 Others" (link-aware like Party/Staff)
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == "64");
            if (parent == null)
            {
                MessageBox.Show("Chart of Accounts header 64 (Others) not found. Seed CoA first.");
                return;
            }

            Account? linked = null;
            if (row.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == row.AccountId.Value);
                if (linked == null) row.AccountId = null;
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, false);
                linked = new Account
                {
                    Code = code,
                    Name = row.Name,
                    Type = AccountType.Parties,   // same reporting bucket family
                    NormalSide = NormalSide.Debit,      // Others are typically debit-nature (adjust per case)
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                db.Accounts.Add(linked);
                await db.SaveChangesAsync();   // get Id
                row.AccountId = linked.Id;
            }
            else
            {
                linked.Name = row.Name;
                if (linked.ParentId != parent.Id) linked.ParentId = parent.Id;
                linked.NormalSide = NormalSide.Debit;
            }

            await db.SaveChangesAsync();
            AppEvents.RaiseAccountsChanged();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, bool forHeader)
        {
            var sibs = await db.Accounts.AsNoTracking()
                                        .Where(a => a.ParentId == parent.Id)
                                        .Select(a => a.Code)
                                        .ToListAsync();
            int max = 0;
            foreach (var code in sibs)
            {
                var last = code?.Split('-').LastOrDefault();
                if (int.TryParse(last, out var n) && n > max) max = n;
            }
            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }
    }
}
