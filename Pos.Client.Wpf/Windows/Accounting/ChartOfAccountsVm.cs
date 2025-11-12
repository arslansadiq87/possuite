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
//using Pos.Persistence;
using System.Windows.Threading;
using Pos.Client.Wpf.Infrastructure;
using System; // <-- add this
using Pos.Domain.Accounting; // 👈 add this
using Pos.Persistence.Sync;                 // IOutboxWriter
using Microsoft.Extensions.DependencyInjection; // App.Services.GetRequiredService
using Pos.Domain.Services;        // IOutboxWriter
using Pos.Domain.Models.Accounting;

namespace Pos.Client.Wpf.Windows.Accounting
{
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
        [ObservableProperty] private bool isSystem;   // <- NEW: for UI guard rails
        [ObservableProperty] private SystemAccountKey? systemKey; // 👈 NEW

        public bool IsOpeningEditable =>
            !IsHeader
            && !IsOpeningLocked
            && CanEditOpenings
            && SystemKey != SystemAccountKey.CashInTillOutlet;   // 👈 block Till accounts

        public bool CanEditOpenings { get; set; }

        public ObservableCollection<AccountNode> Children { get; } = new();

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
    public partial class ChartOfAccountsVm : ObservableObject, IDisposable
    {
        private readonly ICoaService _coa;   // NEW

        public ObservableCollection<AccountNode> Roots { get; } = new();

        public ObservableCollection<AccountFlatRow> Flat { get; } = new();
       
        private readonly HashSet<int> _expanded = new();
        private static bool IsPartyTree(AccountNode n) => n.Type == AccountType.Parties;

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

        private static bool CanHaveChildren(AccountNode n) => IsHeaderNode(n);

        private AccountNode? _selectedNode;
        public AccountNode? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (SetProperty(ref _selectedNode, value))
                {
                    EditAccountCommand.NotifyCanExecuteChanged();
                    DeleteAccountCommand.NotifyCanExecuteChanged();
                    NewHeaderCommand.NotifyCanExecuteChanged();
                    NewAccountCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(CanRename));
                    OnPropertyChanged(nameof(CanDelete));
                }
            }
        }

        private bool CanCreateUnderSelection()
    => SelectedNode != null && CanHaveChildren(SelectedNode) && !IsPartyTree(SelectedNode);

        private bool CanEditOrDelete()
            => SelectedNode != null && !IsPartyTree(SelectedNode);

        private static string AskName(string title, string suggested)
        {
            var name = Prompt(title, suggested);
            name = string.IsNullOrWhiteSpace(name) ? suggested : name.Trim();
            return name;
        }

        public ChartOfAccountsVm(ICoaService coa)
        {
            _coa = coa;
            AppEvents.AccountsChanged += OnAccountsChanged;
        }

        private async void OnAccountsChanged()
        {
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp != null && !disp.CheckAccess())
                {
                    await disp.InvokeAsync(async () => await LoadAsync(), DispatcherPriority.Background);
                }
                else
                {
                    await LoadAsync();
                }
            }
            catch
            {
            }
        }

        private static bool IsAdmin()
        {
            var u = AppState.Current?.CurrentUser;
            return (u != null && (u.Role == UserRole.Admin));
        }
        private static bool CanEditOpenings() => IsAdmin();
        private static bool CanManageCoA() => IsAdmin();
        private static bool CanLockOpenings() => IsAdmin();

        [RelayCommand]
        public async Task LoadAsync()
        {
            var canEdit = CanEditOpenings();
            var rows = await _coa.GetAllAsync();

            var nodes = rows.ToDictionary(
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
                    CanEditOpenings = canEdit,
                    IsSystem = a.IsSystem,
                    SystemKey = a.SystemKey
                });

            Roots.Clear();
            foreach (var a in rows)
            {
                if (a.ParentId.HasValue && nodes.TryGetValue(a.ParentId.Value, out var parent))
                    parent.Children.Add(nodes[a.Id]);
                else
                    Roots.Add(nodes[a.Id]);
            }

            _expanded.Clear();
            foreach (var r in Roots)
                foreach (var n in Enumerate(r))
                    if (n.Children.Any()) _expanded.Add(n.Id);

            RebuildFlat();
        }

        private bool CanRenameSelected()
    => SelectedNode != null
    && !IsPartyTree(SelectedNode)
    && !(SelectedNode?.IsSystem ?? true);                // cannot rename system nodes

        private bool CanDeleteSelected()
            => SelectedNode != null
            && !IsPartyTree(SelectedNode)
            && !(SelectedNode?.IsSystem ?? true)                 // cannot delete system nodes
            && (SelectedNode?.AllowPosting ?? false)             // only delete posting leaves
            && !(SelectedNode?.IsHeader ?? false);               // (redundant with AllowPosting, but explicit)
        public bool CanRename => CanRenameSelected();
        public bool CanDelete => CanDeleteSelected();

        [RelayCommand(CanExecute = nameof(CanCreateUnderSelection))]
        public async Task NewHeaderAsync()
        {
            if (!CanManageCoA() || SelectedNode is null) return;
            if (!CanHaveChildren(SelectedNode) || IsPartyTree(SelectedNode))
            {
                MessageBox.Show("Select a header (non-posting) under a non-Party branch.", "Chart of Accounts");
                return;
            }
            var name = AskName("Header name:", "New Header");
            await _coa.CreateHeaderAsync(SelectedNode.Id, name);
            await LoadAsync();
        }

        [RelayCommand(CanExecute = nameof(CanCreateUnderSelection))]
        public async Task NewAccountAsync()
        {
            if (!CanManageCoA() || SelectedNode is null) return;
            if (!CanHaveChildren(SelectedNode) || IsPartyTree(SelectedNode))
            {
                MessageBox.Show("Select a header (non-posting) under a non-Party branch.", "Chart of Accounts");
                return;
            }
            var name = AskName("Account name:", "New Account");
            await _coa.CreateAccountAsync(SelectedNode.Id, name);
            await LoadAsync();
        }

        [RelayCommand]
        public async Task AddCashForOutletAsync()
        {
            if (!CanManageCoA()) return;
            await _coa.AddCashForOutletAsync();
            await LoadAsync();
        }

        [RelayCommand]
        public async Task AddStaffAccountAsync()
        {
            if (!CanManageCoA()) return;
            await _coa.AddStaffAccountAsync();
            await LoadAsync();
        }

        [RelayCommand]
        public async Task LockOpeningsAsync()
        {
            if (!CanLockOpenings()) return;
            await _coa.LockAllOpeningsAsync();
            await LoadAsync();
        }

        [RelayCommand]
        public async Task SaveOpeningsAsync()
        {
            if (!CanEditOpenings()) return;

            var editable = AllNodes()
                .Where(n => !n.IsHeader && !n.IsOpeningLocked && n.CanEditOpenings && n.SystemKey != SystemAccountKey.CashInTillOutlet)
                .Select(n => new OpeningChange(n.Id, n.OpeningDebit, n.OpeningCredit))
                .ToList();

            if (editable.Count == 0) return;

            try
            {
                await _coa.SaveOpeningsAsync(editable);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Openings");
            }
        }

        [RelayCommand(CanExecute = nameof(CanRenameSelected))]
        public async Task EditAccountAsync()
        {
            if (!CanManageCoA() || SelectedNode is null) return;
            if (IsPartyTree(SelectedNode)) { MessageBox.Show("Party accounts are managed from their own forms."); return; }
            if (SelectedNode.IsSystem) { MessageBox.Show("System accounts cannot be edited."); return; }
            var newCode = Prompt("Account Code:", SelectedNode.Code) ?? SelectedNode.Code;
            var newName = Prompt("Account Name:", SelectedNode.Name) ?? SelectedNode.Name;
            var isHeader = MessageBoxYesNo("Mark as Header (no posting)?", SelectedNode.IsHeader);
            var allowPosting = !isHeader && MessageBoxYesNo("Allow Posting on this account?", SelectedNode.AllowPosting);
            try
            {
                await _coa.EditAsync(new AccountEdit(SelectedNode.Id, newCode, newName, isHeader, allowPosting));
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Edit Account");
            }
        }

        [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
        public async Task DeleteAccountAsync()
        {
            if (!CanManageCoA() || SelectedNode is null) return;
            if (IsPartyTree(SelectedNode)) { MessageBox.Show("Party accounts are managed from their own forms."); return; }
            if (MessageBox.Show($"Delete account '{SelectedNode.Code} - {SelectedNode.Name}'?",
                "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                await _coa.DeleteAsync(SelectedNode.Id);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete Account");
            }
        }

        [RelayCommand]
        private void ToggleExpandCmd(AccountFlatRow row)
        {
            if (!row.HasChildren) return;
            row.IsExpanded = !row.IsExpanded;
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
                
        public void Dispose()
        {
            AppEvents.AccountsChanged -= OnAccountsChanged;
        }

        private static string? Prompt(string caption, string defaultValue)
        {
            var owner = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0] : null;
            var text = Pos.Client.Wpf.Windows.Common.InputDialog.Show(owner, caption, "", defaultValue);
            return text;
        }
        private static bool MessageBoxYesNo(string message, bool defaultValue)
        {
            var r = MessageBox.Show(message, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            return r == MessageBoxResult.Yes;
        }
    }
}
