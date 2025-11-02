using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Infrastructure;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditPartyWindow : Window
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private readonly bool _design;
        private int? _partyId;

        public class OutletVM
        {
            public int OutletId { get; set; }
            public string OutletName { get; set; } = "";
            public bool IsActive { get; set; }               // party-outlet link active?
            public bool AllowCredit { get; set; }
            public decimal? CreditLimit { get; set; }
            // display-only
            public decimal Balance { get; set; }
            public string BalanceDisplay => Balance == 0 ? "-" : Balance.ToString("N2");
        }

        public class BalanceRowVM
        {
            public string OutletName { get; set; } = "";
            public decimal Balance { get; set; }
            public DateTime AsOfUtc { get; set; }
        }

        public EditPartyWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            Loaded += async (_, __) => await LoadFormAsync();
        }

        public void LoadParty(int partyId) => _partyId = partyId;

        private async Task LoadFormAsync()
        {
            using var db = await _dbf!.CreateDbContextAsync();

            // Load outlets up-front for the grid
            var outlets = await db.Outlets.AsNoTracking().OrderBy(o => o.Name)
                .Select(o => new { o.Id, o.Name }).ToListAsync();

            var outletVMs = outlets.Select(o => new OutletVM
            {
                OutletId = o.Id,
                OutletName = o.Name,
                IsActive = false,
                AllowCredit = false,
                CreditLimit = null,
                Balance = 0
            }).ToList();

            if (_partyId is null)
            {
                // New party defaults
                NameText.Text = "";
                ActiveCheck.IsChecked = true;
                SharedCheck.IsChecked = true;
                RoleCustomerCheck.IsChecked = true;   // default at least one
                RoleSupplierCheck.IsChecked = false;

                OutletsGrid.ItemsSource = outletVMs;
                BalancesGrid.ItemsSource = Array.Empty<BalanceRowVM>();
                return;
            }

            // Existing party
            var party = await db.Parties
                .Include(p => p.Roles)
                .Include(p => p.Outlets)
                .AsNoTracking()
                .FirstAsync(p => p.Id == _partyId.Value);

            NameText.Text = party.Name;
            PhoneText.Text = party.Phone;
            EmailText.Text = party.Email;
            TaxText.Text = party.TaxNumber;
            ActiveCheck.IsChecked = party.IsActive;
            SharedCheck.IsChecked = party.IsSharedAcrossOutlets;

            RoleCustomerCheck.IsChecked = party.Roles.Any(r => r.Role == RoleType.Customer);
            RoleSupplierCheck.IsChecked = party.Roles.Any(r => r.Role == RoleType.Supplier);

            // Map per-outlet settings
            foreach (var link in party.Outlets)
            {
                var vm = outletVMs.FirstOrDefault(x => x.OutletId == link.OutletId);
                if (vm != null)
                {
                    vm.IsActive = link.IsActive;
                    vm.AllowCredit = link.AllowCredit;
                    vm.CreditLimit = link.CreditLimit;
                }
            }

            // --- Balances (read-only) ---
            var balancesRaw = await db.PartyBalances
                .AsNoTracking()
                .Where(b => b.PartyId == party.Id)
                .ToListAsync();

            // Map outletId -> outletName for display
            var outletNameById = outlets.ToDictionary(o => o.Id, o => o.Name);

            // Build display rows WITHOUT using b.Outlet (no nav in your model)
            var balances = balancesRaw
                .Select(b => new BalanceRowVM
                {
                    OutletName = b.OutletId == null
                        ? "(Company)"
                        : (outletNameById.TryGetValue(b.OutletId.Value, out var nm) ? nm : "(Unknown)"),
                    Balance = b.Balance,
                    AsOfUtc = b.AsOfUtc
                })
                .OrderBy(x => x.OutletName)
                .ToList();

            // Also show balances inside the outlet VM grid
            // We need a quick lookup by OutletId (int)
            var balByOutletId = balancesRaw
                .Where(b => b.OutletId != null)
                .ToDictionary(b => b.OutletId!.Value, b => b.Balance);

            foreach (var vm in outletVMs)
            {
                if (balByOutletId.TryGetValue(vm.OutletId, out var bal))
                    vm.Balance = bal;
                else
                    vm.Balance = 0;
            }

            OutletsGrid.ItemsSource = outletVMs;
            BalancesGrid.ItemsSource = balances;

            ApplySharedStateToGrid(); // disable per-outlet toggles if shared off/on as needed
        }

        private void SharedChanged(object sender, RoutedEventArgs e) => ApplySharedStateToGrid();

        private void ApplySharedStateToGrid()
        {
            var shared = SharedCheck.IsChecked == true;
            // Behavior:
            // - If Shared = true: party exists across all outlets. We still allow per-outlet credit overrides.
            // - If Shared = false: you decide where it's active; balances still display.
            // No UI lock required; keeping it simple & flexible.
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            var name = (NameText.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorText.Text = "Name is required.";
                NameText.Focus(); return;
            }
            var roleCustomer = RoleCustomerCheck.IsChecked == true;
            var roleSupplier = RoleSupplierCheck.IsChecked == true;
            if (!roleCustomer && !roleSupplier)
            {
                ErrorText.Text = "Select at least one role (Customer/Supplier).";
                return;
            }

            using var db = await _dbf!.CreateDbContextAsync();
            Party party;

            if (_partyId is null)
            {
                party = new Party();
                db.Parties.Add(party);
            }
            else
            {
                party = await db.Parties
                    .Include(p => p.Roles)
                    .Include(p => p.Outlets)
                    .FirstAsync(p => p.Id == _partyId.Value);
            }

            // Map basics
            party.Name = name;
            party.Phone = string.IsNullOrWhiteSpace(PhoneText.Text) ? null : PhoneText.Text.Trim();
            party.Email = string.IsNullOrWhiteSpace(EmailText.Text) ? null : EmailText.Text.Trim();
            party.TaxNumber = string.IsNullOrWhiteSpace(TaxText.Text) ? null : TaxText.Text.Trim();
            party.IsActive = ActiveCheck.IsChecked == true;
            party.IsSharedAcrossOutlets = SharedCheck.IsChecked == true;

            // Roles upsert
            var want = new HashSet<RoleType>(new[] { ifTrue(roleCustomer, RoleType.Customer), ifTrue(roleSupplier, RoleType.Supplier) }.Where(x => x.HasValue)!.Select(x => x!.Value));
            var have = party.Roles.Select(r => r.Role).ToHashSet();
            // add missing
            foreach (var r in want.Except(have)) party.Roles.Add(new PartyRole { Role = r });
            // remove extra
            foreach (var r in have.Except(want))
            {
                var del = party.Roles.First(x => x.Role == r);
                db.Remove(del);
            }

            // Outlets upsert
            var gridRows = (OutletsGrid.ItemsSource as IEnumerable<OutletVM>)?.ToList() ?? new();
            var byId = party.Outlets.ToDictionary(x => x.OutletId, x => x);
            foreach (var vm in gridRows)
            {
                if (!byId.TryGetValue(vm.OutletId, out var link))
                {
                    // create when marked active or credit set
                    if (vm.IsActive || vm.AllowCredit || vm.CreditLimit.HasValue)
                    {
                        link = new PartyOutlet
                        {
                            OutletId = vm.OutletId,
                            IsActive = vm.IsActive,
                            AllowCredit = vm.AllowCredit,
                            CreditLimit = vm.CreditLimit
                        };
                        party.Outlets.Add(link);
                    }
                }
                else
                {
                    if (vm.IsActive || vm.AllowCredit || vm.CreditLimit.HasValue)
                    {
                        link.IsActive = vm.IsActive;
                        link.AllowCredit = vm.AllowCredit;
                        link.CreditLimit = vm.CreditLimit;
                    }
                    else
                    {
                        // no longer needed: remove the link entirely
                        db.Remove(link);
                    }
                }
            }

            // -------- Ensure Party GL account under 61/62 --------
            var isCustomer = party.Roles.Any(r => r.Role == RoleType.Customer);
            var isSupplier = party.Roles.Any(r => r.Role == RoleType.Supplier);

            // Choose header: prefer Customers when both (you can change this policy later)
            string headerCode = isCustomer ? "62" : "61";

            // Find parent header
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == headerCode);
            if (parent == null)
            {
                MessageBox.Show($"Chart of Accounts header {headerCode} not found. Seed CoA first.", "Parties");
                return;
            }

            // Create or update the linked account
            Account? linked = null;
            if (party.AccountId.HasValue)
            {
                linked = await db.Accounts.FirstOrDefaultAsync(a => a.Id == party.AccountId.Value);
                if (linked == null) party.AccountId = null; // dangling link, treat as missing
            }

            if (linked == null)
            {
                var code = await GenerateNextChildCodeAsync(db, parent, forHeader: false);
                linked = new Account
                {
                    Code = code,
                    Name = party.Name,
                    Type = AccountType.Parties,
                    NormalSide = isSupplier && !isCustomer ? NormalSide.Credit : NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    ParentId = parent.Id
                };
                db.Accounts.Add(linked);
                await db.SaveChangesAsync();   // get Id
                party.AccountId = linked.Id;
            }
            else
            {
                // Keep it tidy: reflect name changes; if parent header changed by roles, move it
                linked.Name = party.Name;
                if (linked.ParentId != parent.Id)
                    linked.ParentId = parent.Id;
                // Update natural side based on role (credit for pure supplier)
                linked.NormalSide = isSupplier && !isCustomer ? NormalSide.Credit : NormalSide.Debit;
            }

            await db.SaveChangesAsync();
            // notify CoA and any other listeners that accounts changed
            AppEvents.RaiseAccountsChanged();

            _partyId ??= party.Id; // set after create
            DialogResult = true;
            Close();

            static RoleType? ifTrue(bool cond, RoleType val) => cond ? val : (RoleType?)null;
        }

        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, bool forHeader)
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
            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }


        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Save_Click(sender, e);
        }
    }
}
