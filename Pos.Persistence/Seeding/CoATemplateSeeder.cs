using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;

namespace Pos.Persistence.Seeding
{
    public static class CoATemplateSeeder
    {
        private record Row(
            string Code,
            string Name,
            AccountType Type,
            string? ParentCode,
            bool IsHeader,
            bool AllowPosting,
            NormalSide Normal // expected natural balance
        );

        public static async Task SeedFromTemplateAsync(PosClientDbContext db)
        {
            // ---- Template ----
            var T = new List<Row>
            {
                // 1 Assets
                new("1",    "Assets",                 AccountType.Asset,  null,   true,  false, NormalSide.Debit),
                new("11",   "Current Assets",        AccountType.Asset,  "1",    true,  false, NormalSide.Debit),
                new("111",  "Cash In Hand",          AccountType.Asset,  "11",   true, false,  NormalSide.Debit),
                new("113",  "Bank Accounts",         AccountType.Asset,  "11",   true, false,  NormalSide.Debit),
                new("114",  "Inventory",             AccountType.Asset,  "11",   true,  false, NormalSide.Debit),
                new("1140", "Inventory on hand",     AccountType.Asset,     "114", false, false,  NormalSide.Debit),
                new("1141", "Stock openings",        AccountType.Asset,  "114",  true,  false, NormalSide.Debit),
                new("1142", "Stock purchased",             AccountType.Asset, "114",  true,  false, NormalSide.Debit),
                new("11421","Purchase stock value",        AccountType.Asset, "1142", false, false,  NormalSide.Debit),
                new("11422","Purchase return stock",       AccountType.Asset, "1142", false, false,  NormalSide.Debit),
                new("1143", "Stock sold",                  AccountType.Asset, "114",  true,  false, NormalSide.Debit),
                new("11431","Sold stock cost",             AccountType.Asset, "1143", false, false,  NormalSide.Debit),
                new("11432","Sold return cost",            AccountType.Asset, "1143", false, false,  NormalSide.Debit),
                new("1144","Expired & Damaged stock",      AccountType.Asset, "114",  false, false,  NormalSide.Debit),

                new("12",   "Fixed Assets",          AccountType.Asset,  "1",    true,  false, NormalSide.Debit),
                new("121",  "Auto vehicles",         AccountType.Asset,  "12",   true,  false, NormalSide.Debit),
                new("1211", "Auto vehicles: Cost",   AccountType.Asset,  "121",  false, true,  NormalSide.Debit),
                new("1212", "Auto vehicles: Depreciation", AccountType.Asset,"121", false, true, NormalSide.Credit),
                new("2120", "Accounts payable (trade)",   AccountType.Liability, "21", false, true, NormalSide.Credit),

                new("122",  "Furniture & fixture",   AccountType.Asset,  "12",   true,  false, NormalSide.Debit),
                new("1221", "F&F: Cost",             AccountType.Asset,  "122",  false, true,  NormalSide.Debit),
                new("1222", "F&F: Depreciation",     AccountType.Asset,  "122",  false, true,  NormalSide.Credit),

                new("123",  "Building & grounds",    AccountType.Asset,  "12",   true,  false, NormalSide.Debit),
                new("1231", "B&G: Cost",             AccountType.Asset,  "123",  false, true,  NormalSide.Debit),
                new("1232", "B&G: Depreciation",     AccountType.Asset,  "123",  false, true,  NormalSide.Credit),

                new("124",  "Office equipment",      AccountType.Asset,  "12",   true,  false, NormalSide.Debit),
                new("1241", "Office equip: Cost",    AccountType.Asset,  "124",  false, true,  NormalSide.Debit),
                new("1242", "Office equip: Depreciation", AccountType.Asset,"124", false,true, NormalSide.Credit),

                new("125",  "Computer & software",   AccountType.Asset,  "12",   true,  false, NormalSide.Debit),
                new("1251", "Comp & soft: Cost",     AccountType.Asset,  "125",  false, true,  NormalSide.Debit),
                new("1252", "Comp & soft: Depreciation", AccountType.Asset,"125", false,true, NormalSide.Credit),

                new("13",   "Other assets",          AccountType.Asset,  "1",    true,  false, NormalSide.Debit),
                new("132",  "Prepaid expenses",      AccountType.Asset,  "13",   false, true,  NormalSide.Debit),
                new("133",  "Deposits & securities", AccountType.Asset,  "13",   false, true,  NormalSide.Debit),
                new("134",  "Long term investment",  AccountType.Asset,  "13",   false, true,  NormalSide.Debit),
                new("135",  "Land & buildings",      AccountType.Asset,  "13",   false, true,  NormalSide.Debit),

                // 2 Liabilities
                new("2",    "Liabilities",           AccountType.Liability, null, true,  false, NormalSide.Credit),
                new("21",   "Short term liabilities",AccountType.Liability, "2",  true,  false, NormalSide.Credit),
                new("2110", "Sales tax payable (output)", AccountType.Liability, "21", false, true, NormalSide.Credit),
                new("2111", "Salaries payable",           AccountType.Liability, "21", false, true, NormalSide.Credit),
                new("22",   "Long term liabilities", AccountType.Liability, "2",  true,  false, NormalSide.Credit),

                // 3 Equity
                new("3",    "Equity",                AccountType.Equity, null,   true,  false, NormalSide.Credit),
                new("31",   "Capital investment",    AccountType.Equity, "3",    false, true,  NormalSide.Credit),
                new("32",   "Withdrawals",           AccountType.Equity, "3",    false, true,  NormalSide.Debit),
                new("33",   "Last year earnings",    AccountType.Equity, "3",    false, true,  NormalSide.Credit),
                new("34",   "Current year earnings", AccountType.Equity, "3",    false, true,  NormalSide.Credit),

                // 4 Revenue (Income)
                new("4",    "Revenue",               AccountType.Income,  null,  true,  false, NormalSide.Credit),
                new("41",   "Net Sales",             AccountType.Income,  "4",   true,  false, NormalSide.Credit),
                new("411",  "Gross sales value",     AccountType.Income,  "41",  false, false,  NormalSide.Credit),
                new("412",  "Sales returns",         AccountType.Income,  "41",  false, false,  NormalSide.Debit),
                new("43",   "Invoice Offer Discount On Sales", AccountType.Income, "4", false,false, NormalSide.Debit),
                new("49",   "Other incomes",         AccountType.Income,  "4",   true,  false, NormalSide.Credit),
                new("491", "Cash over",              AccountType.Income, "49", false, true, NormalSide.Credit),


                // 5 Expense
                new("5",    "Expense",               AccountType.Expense, null,  true,  false, NormalSide.Debit),
                new("51",   "Sales expenses",        AccountType.Expense, "5",   true,  false, NormalSide.Debit),
                new("511",  "Cost of goods sold",    AccountType.Expense, "51",  true,  false, NormalSide.Debit),
                new("5111", "Actual cost of sold stock",    AccountType.Expense, "511", false,false, NormalSide.Debit),
                new("5112", "Actual cost of returned stock",AccountType.Expense, "511", false,false, NormalSide.Debit),
                new("5119", "Cost/Price diff of PRet stock",AccountType.Expense, "511", false,false, NormalSide.Debit),

                new("512",  "Discounts",             AccountType.Expense, "51",  true,  false, NormalSide.Debit),
                new("5121", "Special discounts",      AccountType.Expense, "512", false,false, NormalSide.Debit),
                new("5122", "Special discount returned",AccountType.Expense,"512",false,false, NormalSide.Credit),
                new("5124", "Inv Offer Discount On Purchase",AccountType.Expense,"512",false,false, NormalSide.Credit),

                new("514",  "Expired stock actual value", AccountType.Expense, "51", false, false, NormalSide.Debit),

                new("52",   "General & admin expenses", AccountType.Expense, "5", true,  false, NormalSide.Debit),
                new("5201", "Payroll",               AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("52011","Wages",                 AccountType.Expense, "5201",false, true,  NormalSide.Debit),
                new("52012","Benefits",              AccountType.Expense, "5201",false, true,  NormalSide.Debit),
                new("52013","Payroll taxes",         AccountType.Expense, "5201",false, true,  NormalSide.Debit),

                new("5202", "Maintenance",           AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("52021","Auto vehicles",         AccountType.Expense, "5202",false, true,  NormalSide.Debit),
                new("52022","Furniture & fixture",   AccountType.Expense, "5202",false, true,  NormalSide.Debit),
                new("52023","Building & ground",     AccountType.Expense, "5202",false, true,  NormalSide.Debit),
                new("52024","Office equipment",      AccountType.Expense, "5202",false, true,  NormalSide.Debit),
                new("52025","Computer & software",   AccountType.Expense, "5202",false, true,  NormalSide.Debit),

                new("5203", "Depreciation",          AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("52031","Depr: Auto vehicles",   AccountType.Expense, "5203",false, true,  NormalSide.Debit),
                new("52032","Depr: Furniture & fixture", AccountType.Expense, "5203", false, true, NormalSide.Debit),
                new("52033","Depr: Building & ground",   AccountType.Expense, "5203", false, true, NormalSide.Debit),
                new("52034","Depr: Office equipment",    AccountType.Expense, "5203", false, true, NormalSide.Debit),
                new("52035","Depr: Computer & software", AccountType.Expense, "5203", false, true, NormalSide.Debit),

                new("5204", "Rents & leases",        AccountType.Expense, "52",  false, true,  NormalSide.Debit),

                new("5205", "Travel & entertainment",AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("52051","Lodging",               AccountType.Expense, "5205",false, true,  NormalSide.Debit),
                new("52052","Transportation",        AccountType.Expense, "5205",false, true,  NormalSide.Debit),
                new("52053","Meals",                 AccountType.Expense, "5205",false, true,  NormalSide.Debit),
                new("52054","Entertainment",         AccountType.Expense, "5205",false, true,  NormalSide.Debit),
                new("52055","Gasoline charges",      AccountType.Expense, "5205",false, true,  NormalSide.Debit),

                new("5207", "Taxes & Zakat",         AccountType.Expense, "52",  false, true,  NormalSide.Debit),
                new("5208", "Consultancy fees",      AccountType.Expense, "52",  false, true,  NormalSide.Debit),

                new("5209", "Overhead expenses",     AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("520901","Telephone & faxes",    AccountType.Expense, "5209",false, true,  NormalSide.Debit),
                new("520902","Internet expenses",    AccountType.Expense, "5209",false, true,  NormalSide.Debit),
                new("520903","Mail & postage",       AccountType.Expense, "5209",false, true,  NormalSide.Debit),
                new("520904","Utility expenses",     AccountType.Expense, "5209",false, true,  NormalSide.Debit),
                new("5209041","Electricity bills",   AccountType.Expense, "520904",false,true, NormalSide.Debit),
                new("5209042","Gas bills",           AccountType.Expense, "520904",false,true, NormalSide.Debit),
                new("520905","Advertising",          AccountType.Expense, "5209",false, true,  NormalSide.Debit),
                new("520906","Contributions & donations", AccountType.Expense,"5209",false,true,NormalSide.Debit),
                new("520907","License or permit fees",    AccountType.Expense,"5209",false,true,NormalSide.Debit),
                new("520908","Membership dues",           AccountType.Expense,"5209",false,true,NormalSide.Debit),
                new("520909","Newspapers & journals",     AccountType.Expense,"5209",false,true,NormalSide.Debit),
                new("520910","Promotion & PR",            AccountType.Expense,"5209",false,true,NormalSide.Debit),

                new("5210", "Financial expenses",   AccountType.Expense, "52",  true,  false, NormalSide.Debit),
                new("52101","Interest",             AccountType.Expense, "5210",false, true,  NormalSide.Debit),
                new("52102","Bank charges",         AccountType.Expense, "5210",false, true,  NormalSide.Debit),

                new("53",   "Income tax",           AccountType.Expense, "5",   false, true,  NormalSide.Debit),
                new("54",   "Other expenses",       AccountType.Expense, "5",   true,  false, NormalSide.Debit),
                new("541",  "Cash short",           AccountType.Expense, "54",  false, true,  NormalSide.Debit),
                new("542",  "Daily expense",        AccountType.Expense, "54",  false, true,  NormalSide.Debit),
                new("543",  "Home expense",         AccountType.Expense, "54",  false, true,  NormalSide.Debit),

                // 6 Parties
                new("6",    "Parties",              AccountType.Parties, null,  true,  false, NormalSide.Debit),
                new("61",   "Suppliers",              AccountType.Parties, "6",   true,  false, NormalSide.Credit),
                new("62",   "Customers",            AccountType.Parties, "6",   true,  false, NormalSide.Debit),
                new("63",   "Staff",               AccountType.Parties, "6",   true,  false, NormalSide.Debit),
                new("64",   "Others",               AccountType.Parties, "6",   true,  false, NormalSide.Debit),
                

            };

            // ---- upsert all rows by Code, then patch ParentId based on ParentCode ----
            var existing = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a);

            // Insert missing
            foreach (var r in T)
            {
                if (existing.ContainsKey(r.Code)) continue;

                db.Accounts.Add(new Account
                {
                    Code = r.Code,
                    Name = r.Name,
                    Type = r.Type,
                    NormalSide = r.Normal,
                    IsHeader = r.IsHeader,
                    AllowPosting = r.AllowPosting,
                    IsActive = true,
                    IsSystem = true    // <-- protect seeded structure

                });
            }
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync();

            // Reload dictionary (includes new ids)
            existing = await db.Accounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a);

            // Set ParentId for all that have ParentCode
            var toUpdate = new List<Account>();
            foreach (var r in T.Where(x => !string.IsNullOrWhiteSpace(x.ParentCode)))
            {
                if (!existing.TryGetValue(r.Code, out var me)) continue;
                if (!existing.TryGetValue(r.ParentCode!, out var parent)) continue;

                // Only patch if different
                if (me.ParentId != parent.Id || me.IsHeader != r.IsHeader || me.AllowPosting != r.AllowPosting || me.NormalSide != r.Normal)
                {
                    var tracked = await db.Accounts.FirstAsync(a => a.Id == me.Id);
                    tracked.ParentId = parent.Id;
                    tracked.IsHeader = r.IsHeader;
                    tracked.AllowPosting = r.AllowPosting;
                    tracked.NormalSide = r.Normal;
                    tracked.IsSystem = true;
                    toUpdate.Add(tracked);
                }
            }
            if (toUpdate.Count > 0)
                await db.SaveChangesAsync();

            // Find existing header "111 Cash In Hand"
            var cashHeader = db.Accounts.Single(a => a.Code == "111");

            // Tag it with a SystemKey (idempotent)
            if (cashHeader.SystemKey != SystemAccountKey.CashInHandHeader)
            {
                cashHeader.IsSystem = true;
                cashHeader.SystemKey = SystemAccountKey.CashInHandHeader;
                db.SaveChanges();
            }

            // For every outlet, ensure child accounts exist and are tagged
            var outlets = db.Outlets.AsNoTracking().ToList();
            foreach (var o in outlets)
            {
                // e.g., 11101-OUTCODE and 11102-OUTCODE (or any code pattern you prefer)
                EnsureAccount(
                    code: $"11101-{o.Code}",
                    name: $"Cash in Hand — {o.Name}",
                    type: AccountType.Asset,
                    parentId: cashHeader.Id,
                    systemKey: SystemAccountKey.CashInHandOutlet,
                    outletId: o.Id);

                EnsureAccount(
                    code: $"11102-{o.Code}",
                    name: $"Cash in Till — {o.Name}",
                    type: AccountType.Asset,
                    parentId: cashHeader.Id,
                    systemKey: SystemAccountKey.CashInTillOutlet,
                    outletId: o.Id);
            }

            Account EnsureAccount(string code, string name, AccountType type, int parentId,
                                  SystemAccountKey systemKey, int outletId)
            {
                var a = db.Accounts.FirstOrDefault(x => x.Code == code);
                if (a == null)
                {
                    a = new Account
                    {
                        Code = code,
                        Name = name,
                        Type = type,
                        ParentId = parentId,
                        IsHeader = false,
                        AllowPosting = false,              // 👈 not postable by user
                        IsSystem = true,
                        SystemKey = systemKey,
                        OutletId = outletId,
                        OpeningDebit = 0m,                 // 👈 no OB
                        OpeningCredit = 0m                 // 👈 no OB
                    };
                    db.Accounts.Add(a);
                    db.SaveChanges();
                }
                else
                {
                    // make sure the tags are correct if row was created earlier
                    if (!a.IsSystem || a.SystemKey != systemKey || a.OutletId != outletId)
                    {
                        a.AllowPosting = false;           // 👈 enforce non-postable
                        a.OpeningDebit = 0m;
                        a.OpeningCredit = 0m;
                        a.IsSystem = true;
                        a.SystemKey = systemKey;
                        a.OutletId = outletId;
                        db.SaveChanges();
                    }
                }
                return a;
            }

        }
    }
}
