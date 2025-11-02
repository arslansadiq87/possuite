using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Pos.Client.Wpf.Windows.Accounting
{
    public sealed class AccountFlatRow : INotifyPropertyChanged
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
