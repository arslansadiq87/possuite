using System.Linq;
using System.Threading.Tasks;
using Pos.Domain.Entities;   // <-- for Account, AccountType, NormalSide
using Pos.Persistence;

namespace Pos.Persistence.Seeding
{
    public static class CoASeeder
    {
        public static async Task EnsureSeedAsync(PosClientDbContext db)
        {
            // If you already have any accounts, skip
            if (db.Accounts.Any()) return;

            var accts = new[]
            {
                // Assets (1xxx)
                new Account { Code="1000", Name="Cash in Hand",        Type=AccountType.Asset,    NormalSide=NormalSide.Debit },
                new Account { Code="1010", Name="Bank Account",        Type=AccountType.Asset,    NormalSide=NormalSide.Debit },
                new Account { Code="1100", Name="Inventory",           Type=AccountType.Asset,    NormalSide=NormalSide.Debit },
                new Account { Code="1200", Name="Accounts Receivable", Type=AccountType.Asset,    NormalSide=NormalSide.Debit },

                // Liabilities (2xxx)
                new Account { Code="2000", Name="Accounts Payable",    Type=AccountType.Liability,NormalSide=NormalSide.Credit },
                new Account { Code="2100", Name="Output Tax Payable",  Type=AccountType.Liability,NormalSide=NormalSide.Credit },
                new Account { Code="2200", Name="Salaries Payable",    Type=AccountType.Liability,NormalSide=NormalSide.Credit },

                // Equity (3xxx)
                new Account { Code="3000", Name="Retained Earnings",   Type=AccountType.Equity,   NormalSide=NormalSide.Credit },

                // Income (4xxx)
                new Account { Code="4000", Name="Sales Revenue",       Type=AccountType.Income,   NormalSide=NormalSide.Credit },

                // Expenses (5xxx)
                new Account { Code="5000", Name="Cost of Goods Sold",  Type=AccountType.Expense,  NormalSide=NormalSide.Debit },
                new Account { Code="5100", Name="Operating Expenses",  Type=AccountType.Expense,  NormalSide=NormalSide.Debit },
                new Account { Code="5110", Name="Rent Expense",        Type=AccountType.Expense,  NormalSide=NormalSide.Debit },
                new Account { Code="5120", Name="Utilities Expense",   Type=AccountType.Expense,  NormalSide=NormalSide.Debit },
                new Account { Code="5130", Name="Salaries Expense",    Type=AccountType.Expense,  NormalSide=NormalSide.Debit },
                new Account { Code="9000", Name="Parties", Type=AccountType.Parties, NormalSide=NormalSide.Debit, IsHeader=true, AllowPosting=false },

            };

            db.Accounts.AddRange(accts);
            await db.SaveChangesAsync();
        }
    }
}
