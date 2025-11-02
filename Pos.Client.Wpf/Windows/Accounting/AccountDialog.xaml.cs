using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Pos.Domain.Accounting;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class AccountDialog : Window
    {
        public Account Value { get; private set; }

        public List<AccountType> Types { get; } = new()
        {
            AccountType.Asset, AccountType.Liability, AccountType.Equity,
            AccountType.Revenue, AccountType.Expense, AccountType.ContraAsset, AccountType.ContraRevenue
        };
        public List<NormalBalance> Normals { get; } = new() { NormalBalance.Debit, NormalBalance.Credit };

        public List<Account> ParentChoices { get; private set; } = new();
        public Account? SelectedParent { get; set; }

        public AccountDialog(Account? existing = null, IEnumerable<Account>? allAccounts = null)
        {
            InitializeComponent();

            Value = existing != null
                ? new Account
                {
                    Id = existing.Id,
                    Code = existing.Code,
                    Name = existing.Name,
                    Type = existing.Type,
                    Normal = existing.Normal,
                    IsActive = existing.IsActive,
                    IsOutletScoped = existing.IsOutletScoped,
                    ParentId = existing.ParentId
                }
                : new Account
                {
                    IsActive = true,
                    Normal = NormalBalance.Debit,
                    Type = AccountType.Asset
                };

            if (allAccounts != null)
            {
                ParentChoices = allAccounts
                    .Where(a => existing == null || a.Id != existing.Id)
                    .OrderBy(a => a.Code)
                    .ToList();

                SelectedParent = ParentChoices.FirstOrDefault(a => a.Id == Value.ParentId);
            }

            DataContext = this;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Value.Code))
            {
                MessageBox.Show("Code is required.", "Account");
                return;
            }
            if (string.IsNullOrWhiteSpace(Value.Name))
            {
                MessageBox.Show("Name is required.", "Account");
                return;
            }

            // ✅ --- Add this normalization logic HERE ---
            Value.Normal = Value.Type switch
            {
                AccountType.Asset or AccountType.Expense or AccountType.ContraRevenue => NormalBalance.Debit,
                _ => NormalBalance.Credit
            };
            // ✅ -----------------------------------------

            if (SelectedParent != null)
            {
                Value.ParentId = SelectedParent.Id;
                // Optional: enforce same Type/Normal as parent
                Value.Type = SelectedParent.Type;
                Value.Normal = SelectedParent.Normal;
            }
            else
            {
                Value.ParentId = null;
            }

            DialogResult = true; // <-- Keep this at the end
        }
    }
}
