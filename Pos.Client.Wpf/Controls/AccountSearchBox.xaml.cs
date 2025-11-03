using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Controls
{
    public partial class AccountSearchBox : UserControl
    {
        public event EventHandler<Account>? AccountPicked;

        public static readonly DependencyProperty SelectedAccountProperty =
            DependencyProperty.Register(nameof(SelectedAccount), typeof(Account), typeof(AccountSearchBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public Account? SelectedAccount
        {
            get => (Account?)GetValue(SelectedAccountProperty);
            set => SetValue(SelectedAccountProperty, value);
        }

        // ViewModel-ish state (lightweight)
        public ObservableCollection<Account> Results { get; } = new();
        public string Query
        {
            get => _query; set { _query = value; _ = RefreshAsync(); }
        }
        private string _query = "";
        public bool IsOpen { get; set; }

        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private CancellationTokenSource? _cts;

        public AccountSearchBox()
        {
            InitializeComponent();
            DataContext = this;
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();

            SearchBox.TextChanged += async (_, __) => {
                // open popup when typing
                if (!IsOpen && !string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    IsOpen = true; Drop.IsOpen = true;
                }
                await RefreshAsync();
            };

            SearchBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Down && Drop.IsOpen && List.Items.Count > 0)
                {
                    List.SelectedIndex = 0;
                    List.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    PickFirstIfAny();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Drop.IsOpen = false; IsOpen = false;
                }
            };
        }

        private async Task RefreshAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var q = (Query ?? "").Trim();
            if (string.IsNullOrEmpty(q))
            {
                Results.Clear();
                return;
            }

            try
            {
                using var db = await _dbf.CreateDbContextAsync(ct);
                var hits = await db.Accounts.AsNoTracking()
                    .Where(a => EF.Functions.Like(a.Code, $"%{q}%")
                             || EF.Functions.Like(a.Name, $"%{q}%"))
                    .OrderBy(a => a.Code)
                    .Take(50)
                    .ToListAsync(ct);

                Results.Clear();
                foreach (var a in hits) Results.Add(a);

                IsOpen = Results.Count > 0;
                Drop.IsOpen = IsOpen;
            }
            catch { /* ignore canceled */ }
        }

        private void Pick(Account a)
        {
            SelectedAccount = a;
            AccountPicked?.Invoke(this, a);
            Drop.IsOpen = false; IsOpen = false;

            // move focus forward (Account -> Description)
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void PickFirstIfAny()
        {
            if (Results.Count > 0) Pick(Results[0]);
        }

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (List.SelectedItem is Account a) Pick(a);
        }

        private void List_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && List.SelectedItem is Account a)
            {
                Pick(a);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Drop.IsOpen = false; IsOpen = false;
                SearchBox.Focus();
                e.Handled = true;
            }
        }
    }
}
