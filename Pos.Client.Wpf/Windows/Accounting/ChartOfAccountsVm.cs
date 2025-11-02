// Pos.Client.Wpf/Windows/Accounting/ChartOfAccountsVm.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Accounting
{
    

    // ---------- Node shown in the tree/grid ----------
    public partial class AccountNode : ObservableObject
    {
        [ObservableProperty] private int id;
        [ObservableProperty] private string code = "";
        [ObservableProperty] private string name = "";
        [ObservableProperty] private AccountType type;
        [ObservableProperty] private bool isHeader;
        [ObservableProperty] private bool allowPosting;
        [ObservableProperty] private decimal openingDebit;
        [ObservableProperty] private decimal openingCredit;
        [ObservableProperty] private bool isOpeningLocked;

        // Set from VM based on current user's permissions
        public bool CanEditOpenings { get; set; }

        public ObservableCollection<AccountNode> Children { get; } = new();

        public bool IsOpeningEditable => !IsHeader && !IsOpeningLocked && CanEditOpenings;
    }

    // ---------- Flat row for the GridView pseudo-tree ----------
    public sealed class AccountFlatRow : System.ComponentModel.INotifyPropertyChanged
    {
        public AccountNode Node { get; }
        public int Level { get; }
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }
        public bool HasChildren => Node.Children != null && Node.Children.Any();

        public AccountFlatRow(AccountNode node, int level, bool expanded = true)
        {
            Node = node;
            Level = level;
            _isExpanded = expanded;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    // ---------- ViewModel ----------
    public partial class ChartOfAccountsVm : ObservableObject
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        // Hierarchical roots (unused by the view directly, but used to build Flat)
        public ObservableCollection<AccountNode> Roots { get; } = new();

        // Flat rows bound to the ListView/GridView
        public ObservableCollection<AccountFlatRow> Flat { get; } = new();
       
        private readonly HashSet<int> _expanded = new();

        private static NormalSide DefaultNormalFor(AccountType t) => t switch
        {
            AccountType.Asset => NormalSide.Debit,
            AccountType.Expense => NormalSide.Debit,
            AccountType.Liability => NormalSide.Credit,
            AccountType.Equity => NormalSide.Credit,
            AccountType.Income => NormalSide.Credit,
            AccountType.Parties => NormalSide.Debit,  // default; AR-style. Adjust later if you add Customer/Supplier subtypes
            AccountType.System => NormalSide.Debit,  // neutral bucket; won’t be posted anyway
            _ => NormalSide.Debit
        };

        private static bool IsHeaderNode(AccountNode n) => n.IsHeader || !n.AllowPosting;
        private static bool IsPostingLeaf(AccountNode n) => !n.IsHeader && n.AllowPosting;

        // Only allow creating children under header/grouping nodes
        private static bool CanHaveChildren(AccountNode n) => IsHeaderNode(n);


        // Selection (toolbar commands use this)
        // Selection (toolbar commands use this)
        private AccountNode? _selectedNode;
        public AccountNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    // Re-evaluate toolbar button CanExecute when selection changes
                    EditAccountCommand.NotifyCanExecuteChanged();
                    DeleteAccountCommand.NotifyCanExecuteChanged();
                }
            }
        }

        // Generate next numeric child code under the parent.
        // Headers: ..-01, ..-02 (2 digits)
        // Accounts: ..-001, ..-002 (3 digits)
        private static async Task<string> GenerateNextChildCodeAsync(PosClientDbContext db, Account parent, bool forHeader)
        {
            var siblingsCodes = await db.Accounts
                .AsNoTracking()
                .Where(a => a.ParentId == parent.Id)
                .Select(a => a.Code)
                .ToListAsync();

            int max = 0;
            foreach (var code in siblingsCodes)
            {
                var lastSeg = code?.Split('-').LastOrDefault();
                if (int.TryParse(lastSeg, out var num))
                    if (num > max) max = num;
            }

            var next = max + 1;
            var suffix = forHeader ? next.ToString("D2") : next.ToString("D3");
            return $"{parent.Code}-{suffix}";
        }


        // Simple name prompt wrapper (uses your existing MessageBox prompt pattern)
        private static string AskName(string title, string suggested)
        {
            // If you later add a real input dialog, wire it here.
            // For now, reuse your Prompt stub to keep flow consistent.
            var name = Prompt(title, suggested);
            name = string.IsNullOrWhiteSpace(name) ? suggested : name.Trim();
            return name;
        }

        public ChartOfAccountsVm(IDbContextFactory<PosClientDbContext> dbf)
        {
            _dbf = dbf;
        }

        // ---- Role helpers ----
        private static bool IsAdmin()
        {
            var u = AppState.Current?.CurrentUser;
            return (u != null && (u.Role == UserRole.Admin));
        }
        private static bool CanEditOpenings() => IsAdmin();
        private static bool CanManageCoA() => IsAdmin();
        private static bool CanLockOpenings() => IsAdmin();

        // ---- Load tree and build flat list ----
        [RelayCommand]
        public async Task LoadAsync()
        {
            using var db = _dbf.CreateDbContext();
            var canEdit = CanEditOpenings();

            var accounts = await db.Accounts.AsNoTracking()
                                .OrderBy(a => a.Code)
                                .ToListAsync();

            var nodes = accounts.ToDictionary(
                a => a.Id,
                a => new AccountNode
                {
                    Id = a.Id,
                    Code = a.Code,
                    Name = a.Name,
                    Type = a.Type,
                    IsHeader = a.IsHeader,
                    AllowPosting = a.AllowPosting,
                    OpeningDebit = a.OpeningDebit,
                    OpeningCredit = a.OpeningCredit,
                    IsOpeningLocked = a.IsOpeningLocked,
                    CanEditOpenings = canEdit
                });

            Roots.Clear();
            foreach (var a in accounts)
            {
                if (a.ParentId.HasValue && nodes.TryGetValue(a.ParentId.Value, out var parent))
                    parent.Children.Add(nodes[a.Id]);
                else
                    Roots.Add(nodes[a.Id]);
            }
            // reset default: expand all nodes that have children the first time
            _expanded.Clear();
            foreach (var r in Roots)
            {
                foreach (var n in Enumerate(r))
                    if (n.Children.Any())
                        _expanded.Add(n.Id);
            }

            RebuildFlat();
        }

        // ---- New header/account ----
        [RelayCommand]
        public async Task NewHeaderAsync()
        {
            if (!CanManageCoA()) return;

            if (SelectedNode is null)
            {
                MessageBox.Show("Select a parent header first.", "Chart of Accounts");
                return;
            }
            if (!CanHaveChildren(SelectedNode))
            {
                MessageBox.Show("You cannot add under a posting account. Convert it to a header (no posting) first.", "Chart of Accounts");
                return;
            }

            using var db = _dbf.CreateDbContext();
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == SelectedNode.Id);
            if (parent == null) return;

            var name = AskName("Header name:", "New Header");
            var code = await GenerateNextChildCodeAsync(db, parent, forHeader: true);

            var acc = new Account
            {
                Code = code,
                Name = name,
                Type = parent.Type,                   // inherit reporting bucket
                NormalSide = DefaultNormalFor(parent.Type), // implied by bucket
                IsHeader = true,
                AllowPosting = false,
                ParentId = parent.Id,
                OutletId = parent.OutletId
            };

            db.Accounts.Add(acc);
            await db.SaveChangesAsync();
            await LoadAsync();
        }



        [RelayCommand]
        public async Task NewAccountAsync()
        {
            if (!CanManageCoA()) return;

            if (SelectedNode is null)
            {
                MessageBox.Show("Select a parent header first.", "Chart of Accounts");
                return;
            }
            if (!CanHaveChildren(SelectedNode))
            {
                MessageBox.Show("You cannot add under a posting account. Convert it to a header (no posting) first.", "Chart of Accounts");
                return;
            }

            using var db = _dbf.CreateDbContext();
            var parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == SelectedNode.Id);
            if (parent == null) return;

            var name = AskName("Account name:", "New Account");
            var code = await GenerateNextChildCodeAsync(db, parent, forHeader: false);

            var acc = new Account
            {
                Code = code,
                Name = name,
                Type = parent.Type,                   // inherit reporting bucket
                NormalSide = DefaultNormalFor(parent.Type), // implied by bucket
                IsHeader = false,
                AllowPosting = true,                          // leaf = posting
                ParentId = parent.Id,
                OutletId = parent.OutletId
            };

            db.Accounts.Add(acc);
            await db.SaveChangesAsync();
            await LoadAsync();
        }



        // ---- Convenience creators ----
        [RelayCommand]
        public async Task AddCashForOutletAsync()
        {
            if (!CanManageCoA()) return;

            using var db = _dbf.CreateDbContext();
            var outlet = await db.Outlets.AsNoTracking().FirstOrDefaultAsync();
            if (outlet == null) return;

            var code = $"CASH-{outlet.Code}";
            var exists = await db.Accounts.AnyAsync(a => a.OutletId == outlet.Id && a.Code == code);
            if (!exists)
            {
                db.Accounts.Add(new Account
                {
                    Code = code,
                    Name = $"Cash — {outlet.Name}",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true,
                    OutletId = outlet.Id
                });
                await db.SaveChangesAsync();
            }
            await LoadAsync();
        }

        [RelayCommand]
        public async Task AddStaffAccountAsync()
        {
            if (!CanManageCoA()) return;

            using var db = _dbf.CreateDbContext();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync();
            if (user == null) return;

            var code = $"STAFF-{user.Username}";
            var exists = await db.Accounts.AnyAsync(a => a.Code == code);
            if (!exists)
            {
                db.Accounts.Add(new Account
                {
                    Code = code,
                    Name = $"Staff — {user.Username}",
                    Type = AccountType.Asset,
                    NormalSide = NormalSide.Debit,
                    IsHeader = false,
                    AllowPosting = true
                });
                await db.SaveChangesAsync();
            }
            await LoadAsync();
        }

        // ---- Lock & save openings ----
        [RelayCommand]
        public async Task LockOpeningsAsync()
        {
            if (!CanLockOpenings()) return;

            using var db = _dbf.CreateDbContext();
            var rows = await db.Accounts.Where(a => !a.IsOpeningLocked).ToListAsync();
            foreach (var a in rows) a.IsOpeningLocked = true;
            await db.SaveChangesAsync();
            await LoadAsync();
        }

        [RelayCommand]
        public async Task SaveOpeningsAsync()
        {
            if (!CanEditOpenings()) return;

            using var db = _dbf.CreateDbContext();

            var editable = AllNodes().Where(n => !n.IsHeader && !n.IsOpeningLocked && n.CanEditOpenings).ToList();
            if (editable.Count == 0) return;

            var ids = editable.Select(n => n.Id).ToArray();
            var dbAccounts = await db.Accounts.Where(a => ids.Contains(a.Id)).ToListAsync();

            foreach (var a in dbAccounts)
            {
                if (a.OpeningDebit != 0m && a.OpeningCredit != 0m)
                {
                    MessageBox.Show($"Opening for {a.Code} has both Dr and Cr. Please keep only one side.", "Openings");
                    return;
                }
                var vm = editable.First(n => n.Id == a.Id);
                a.OpeningDebit = vm.OpeningDebit;
                a.OpeningCredit = vm.OpeningCredit;
            }

            await db.SaveChangesAsync();
            await LoadAsync();
        }

        // ---- Edit/Delete ----
        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        public async Task EditAccountAsync()
        {
            if (!CanManageCoA()) return;
            if (SelectedNode is null) return;

            using var db = _dbf.CreateDbContext();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == SelectedNode.Id);
            if (acc == null) return;

            var newCode = Prompt("Account Code:", acc.Code);
            var newName = Prompt("Account Name:", acc.Name);
            var isHeader = MessageBoxYesNo("Mark as Header (no posting)?", acc.IsHeader);
            var allowPosting = !isHeader && MessageBoxYesNo("Allow Posting on this account?", acc.AllowPosting);

            acc.Code = string.IsNullOrWhiteSpace(newCode) ? acc.Code : newCode.Trim();
            acc.Name = string.IsNullOrWhiteSpace(newName) ? acc.Name : newName.Trim();
            acc.IsHeader = isHeader;
            acc.AllowPosting = allowPosting;

            await db.SaveChangesAsync();
            await LoadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanEditOrDelete))]
        public async Task DeleteAccountAsync()
        {
            if (!CanManageCoA()) return;
            if (SelectedNode is null) return;

            using var db = _dbf.CreateDbContext();
            var acc = await db.Accounts.FirstOrDefaultAsync(a => a.Id == SelectedNode.Id);
            if (acc == null) return;

            var hasChildren = await db.Accounts.AnyAsync(a => a.ParentId == acc.Id);
            if (acc.IsSystem || hasChildren)
            {
                MessageBox.Show("Cannot delete system or parent accounts.", "Chart of Accounts");
                return;
            }

            var usedInGl = await db.JournalLines.AnyAsync(l => l.AccountId == acc.Id);
            var usedByParty = await db.Parties.AnyAsync(p => p.AccountId == acc.Id);
            if (usedInGl || usedByParty)
            {
                MessageBox.Show("This account is in use and cannot be deleted.", "Chart of Accounts");
                return;
            }

            if (MessageBox.Show($"Delete account '{acc.Code} - {acc.Name}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            db.Accounts.Remove(acc);
            await db.SaveChangesAsync();
            await LoadAsync();
        }


        private bool CanEditOrDelete() => SelectedNode != null;

        // ---- Expand/Collapse for GridView pseudo-tree ----
        [RelayCommand]
        private void ToggleExpandCmd(AccountFlatRow row)
        {
            if (!row.HasChildren) return;

            // flip the row flag
            row.IsExpanded = !row.IsExpanded;

            // persist per-node expansion state
            if (row.IsExpanded) _expanded.Add(row.Node.Id);
            else _expanded.Remove(row.Node.Id);

            RebuildFlat();
        }

        private void RebuildFlat()
        {
            Flat.Clear();
            foreach (var r in Roots)
                Append(r, level: 0);
        }

        private void Append(AccountNode node, int level)
        {
            var expanded = _expanded.Contains(node.Id);
            var flatRow = new AccountFlatRow(node, level, expanded);
            Flat.Add(flatRow);

            if (expanded && node.Children.Any())
            {
                foreach (var child in node.Children.OrderBy(c => c.Code))
                    Append(child, level + 1);
            }
        }

        // ---- Helpers ----
        private static IEnumerable<AccountNode> Enumerate(AccountNode n)
        {
            yield return n;
            foreach (var c in n.Children)
                foreach (var d in Enumerate(c))
                    yield return d;
        }

        private IEnumerable<AccountNode> AllNodes()
        {
            foreach (var r in Roots)
                foreach (var n in Enumerate(r))
                    yield return n;
        }


        // Tiny stubs (replace with proper dialogs if needed)
        private static string? Prompt(string caption, string defaultValue) => defaultValue;
        private static bool MessageBoxYesNo(string message, bool defaultValue)
        {
            var r = MessageBox.Show($"{message}\n(Yes = true, No = false)", "Edit", MessageBoxButton.YesNo);
            return r == MessageBoxResult.Yes;
        }
    }
}
