using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pos.Domain.Accounting;
using System.Windows.Threading; // at top with usings
using System.Windows.Media;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using Pos.Domain.Entities;
using System.Collections.ObjectModel;
using System.Timers;
using System.Globalization;


namespace Pos.Client.Wpf.Windows.Accounting
{
    public partial class VoucherEditorView : UserControl
    {
        private ComboBox? _activeAccountCombo;      // current cell's editor
        private readonly ObservableCollection<Account> _accountFiltered = new(); // data for ComboBox
        private List<(Account acc, string key)> _accountIndex = new();           // precomputed lowercase "code name"
        private DispatcherTimer? _accountDebounce;
        private string _pendingQuery = "";
        private bool _lastEditCancelled = false;
        private bool _suppressNextAmountValidation = false;
        private bool _suppressAccountFilter;     // true while user is clicking in dropdown
        private bool _committingAccountClick;    // true while we’re committing the clicked item

        private bool _wired;
        public VoucherEditorView()
        {
            InitializeComponent();
        }

        public VoucherEditorView(VoucherEditorVm vm) : this()
        {
            AttachVm(vm);
        }

        public void AttachVm(VoucherEditorVm vm)
        {
            if (_wired) return;   // prevents double wiring
            _wired = true;
            DataContext = vm;
            
            LinesGrid.CellEditEnding += LinesGrid_CellEditEnding_ValidateAmountAndDescription;

            Loaded += async (_, __) =>
            {
                await vm.LoadAsync();
                BuildAccountIndex(vm);               // <-- build once
                HookReload(vm);                      // <-- listen Clear/Save
                UpdateAmountColumnVisibility(); // NEW
                TypeBox.Focus();
            };

            LinesGrid.CurrentCellChanged += (s, e) =>
            {
                var col = LinesGrid.CurrentColumn?.Header?.ToString();
                if (col == "Account")
                {
                    LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        LinesGrid.BeginEdit(); // will run AccountCombo_Loaded and focus textbox
                    }));
                }
            };

            LinesGrid.PreviewMouseDoubleClick += (s, e) =>
            {
                // find the clicked cell
                var cell = e.OriginalSource as DependencyObject;
                while (cell != null && cell is not DataGridCell) cell = VisualTreeHelper.GetParent(cell);
                if (cell is not DataGridCell dgCell) return;

                if ((dgCell.Column.Header?.ToString() ?? "") == "Account")
                {
                    e.Handled = true;
                    LinesGrid.BeginEdit(); // triggers AccountCombo_Loaded, opens dropdown
                }
            };

            TypeBox.SelectionChanged += (_, __) => UpdateAmountColumnVisibility();

            TypeBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { DateBox.Focus(); e.Handled = true; }
            };

            DateBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var v = (VoucherEditorVm)DataContext;
                    if (v.IsOutletSelectable || (!v.IsOutletSelectable && v.Outlets.Count > 1))
                        OutletBox.Focus();
                    else
                        RefBox.Focus();
                    e.Handled = true;
                }
            };

            OutletBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { RefBox.Focus(); e.Handled = true; }
            };

            RefBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    FocusGridAccountFirstCell();   // jumps to Account and begins edit
                }
            };

            LinesGrid.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                var col = LinesGrid.CurrentColumn?.Header?.ToString();
                if (string.IsNullOrEmpty(col)) return;
                if (col == "Description")
                {
                    e.Handled = true;   // stop DataGrid’s default commit navigation
                    MoveToAmountColumn();
                    return;
                }
                if (col == "Debit" || col == "Credit")
                {
                    e.Handled = true;
                    CommitCurrentCell();
                    if (_lastEditCancelled)
                    {
                        _lastEditCancelled = false; // reset
                        return;                     // stay on the same cell; no new row
                    }
                    var vm = (VoucherEditorVm)DataContext;
                    vm.AddLine();
                    LinesGrid.SelectedIndex = LinesGrid.Items.Count - 1;
                    var newItem = LinesGrid.Items[LinesGrid.SelectedIndex];
                    var accountCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Account")
                                     ?? LinesGrid.Columns.ElementAtOrDefault(1);
                    if (accountCol != null)
                    {
                        LinesGrid.CurrentCell = new DataGridCellInfo(newItem, accountCol);
                        LinesGrid.ScrollIntoView(newItem, accountCol);

                        LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            LinesGrid.BeginEdit(); // triggers AccountCombo_Loaded
                        }));
                    }
                }
            };
        }

        private void AccountEditor_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_activeAccountCombo == null) return;

            // NEW: don't refilter while a click is happening/committing
            if (_suppressAccountFilter || _committingAccountClick) return;

            _pendingQuery = (sender as TextBox)?.Text ?? "";
            EnsureDebouncer();
            _accountDebounce!.Stop();
            _accountDebounce!.Start();
            _activeAccountCombo.IsDropDownOpen = true;

            // only clear selection when we are actively typing, not during click
            _activeAccountCombo.SelectedIndex = -1;
            _activeAccountCombo.SelectedItem = null;
        }


        private static TextBox? GetInnerTextBox(FrameworkElement root)
        {
            if (root is TextBox t) return t;
            return FindVisualChild<TextBox>(root);
        }

        private void LinesGrid_CellEditEnding_ValidateAmountAndDescription(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (_suppressNextAmountValidation)
            {
                _suppressNextAmountValidation = false;
                _lastEditCancelled = true;   // keep the "don’t advance" behavior
                e.Cancel = true;
                return;
            }
            _lastEditCancelled = false;
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (LinesGrid.SelectedItem is not VoucherLineVm line) return;
            var vm = (VoucherEditorVm)DataContext;
            var colHeader = e.Column.Header?.ToString() ?? "";
            if (colHeader == "Debit" || colHeader == "Credit")
            {
                TextBox? tb = e.EditingElement as TextBox
                              ?? (e.EditingElement as FrameworkElement != null
                                  ? GetInnerTextBox((FrameworkElement)e.EditingElement)
                                  : null);
                decimal val;
                if (tb == null || !decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out val))
                    val = 0m;
                var vt = Enum.Parse<VoucherType>(vm.Type);
                if (vt == VoucherType.Payment)
                {
                    if (colHeader == "Debit" && val <= 0m)
                    {
                        e.Cancel = true;
                        _lastEditCancelled = true;
                        _suppressNextAmountValidation = true;   // <-- add this line
                        if (tb != null)
                        {
                            tb.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                tb.Focus();
                                tb.SelectAll();
                            }));
                        }
                        MessageBox.Show("Amount must be greater than zero for Debit vouchers.",
                            "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else if (vt == VoucherType.Receipt)
                {
                    if (colHeader == "Credit" && val <= 0m)
                    {
                        e.Cancel = true;
                        _lastEditCancelled = true;
                        _suppressNextAmountValidation = true;   // <-- add this line
                        if (tb != null)
                        {
                            tb.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                tb.Focus();
                                tb.SelectAll();
                            }));
                        }
                        MessageBox.Show("Amount must be greater than zero for Credit vouchers.",
                            "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else // Journal
                {
                    var newDebit = (colHeader == "Debit") ? val : line.Debit;
                    var newCredit = (colHeader == "Credit") ? val : line.Credit;
                    if (newDebit <= 0m && newCredit <= 0m)
                    {
                        e.Cancel = true;
                        _lastEditCancelled = true;
                        _suppressNextAmountValidation = true;   // <-- add this line
                        if (tb != null)
                        {
                            tb.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                tb.Focus();
                                tb.SelectAll();
                            }));
                        }
                        MessageBox.Show("For Journal, either Debit or Credit must be greater than zero on each row.",
                            "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            if (colHeader == "Description")
            {
                if (e.EditingElement is TextBox tbDesc)
                {
                    var text = (tbDesc.Text ?? "").Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        string auto = vm.Type == nameof(VoucherType.Payment) ? "Cash Payment Voucher"
                                    : vm.Type == nameof(VoucherType.Receipt) ? "Cash Receiving Voucher"
                                    : "Journal Voucher";
                        tbDesc.Text = auto;
                        if (LinesGrid.SelectedItem is VoucherLineVm ln)
                            ln.Description = auto;
                    }
                }
            }
        }


        private void HookReload(VoucherEditorVm vm)
        {
            vm.AccountsReloadRequested += async () =>
            {
                await vm.ReloadAccountsAsync();  // fetch fresh list from DB
                BuildAccountIndex(vm);           // rebuild in-memory index
                if (_activeAccountCombo?.Template.FindName("PART_EditableTextBox", _activeAccountCombo) is TextBox tb)
                    ApplyAccountFilter(tb.Text);
            };
        }

        private void BuildAccountIndex(VoucherEditorVm vm)
        {
            _accountIndex = vm.Accounts
                .Select(a =>
                {
                    string code = a.Code ?? "";
                    string name = a.Name ?? "";
                    string key = (code + " " + name).ToLowerInvariant();
                    return (a, key);
                })
                .ToList();
            _accountFiltered.Clear();
        }

        private void EnsureDebouncer()
        {
            _accountDebounce ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _accountDebounce.Tick -= Debounce_Tick;
            _accountDebounce.Tick += Debounce_Tick;
        }

        private void Debounce_Tick(object? sender, EventArgs e)
        {
            _accountDebounce?.Stop();
            ApplyAccountFilter(_pendingQuery);
        }

        private void ApplyAccountFilter(string query)
        {
            if (_suppressAccountFilter || _committingAccountClick) return; // NEW

            string q = (query ?? "").Trim().ToLowerInvariant();
            var tokens = q.Length == 0
                ? Array.Empty<string>()
                : q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            IEnumerable<Account> results = tokens.Length == 0
                ? _accountIndex.Select(t => t.acc)
                : _accountIndex.Where(t =>
                {
                    foreach (var tok in tokens)
                        if (!t.key.Contains(tok)) return false;
                    return true;
                })
                  .Select(t => t.acc);

            results = results.Take(500);

            _accountFiltered.Clear();
            foreach (var a in results) _accountFiltered.Add(a);

            if (_activeAccountCombo != null)
            {
                _activeAccountCombo.IsDropDownOpen = true;

                // IMPORTANT: don't clear SelectedItem here; that kills a just-clicked selection
                // Only clear selection when the user is actively typing (handled in TextChanged).
            }
        }


        private void FocusGridAccountFirstCell()
        {
            if (LinesGrid.Items.Count == 0) return;
            LinesGrid.SelectedIndex = 0;
            var firstItem = LinesGrid.Items[0];
            var accountCol = LinesGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Account")
                             ?? LinesGrid.Columns.ElementAtOrDefault(1);
            if (accountCol == null) return;
            LinesGrid.CurrentCell = new DataGridCellInfo(firstItem, accountCol);
            LinesGrid.ScrollIntoView(firstItem, accountCol);
            LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                LinesGrid.BeginEdit(); // triggers AccountCombo_Loaded → focuses editable textbox
            }));
        }

        private void AccountCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            _activeAccountCombo = cb;

            cb.IsSynchronizedWithCurrentItem = false;
            cb.ItemsSource = _accountFiltered;

            if (cb.SelectedItem is not Account)
            {
                cb.SelectedIndex = -1;
                cb.SelectedItem = null;
                cb.Text = "";
            }

            cb.IsDropDownOpen = true;
            cb.Focus();
            Keyboard.Focus(cb);

            if (cb.Template.FindName("PART_EditableTextBox", cb) is TextBox tb)
            {
                tb.PreviewKeyDown -= AccountEditor_PreviewKeyDown;
                tb.PreviewKeyDown += AccountEditor_PreviewKeyDown;

                tb.TextChanged -= AccountEditor_TextChanged;
                tb.TextChanged += AccountEditor_TextChanged;

                tb.PreviewMouseWheel -= ForwardWheelToDropdown;
                tb.PreviewMouseWheel += ForwardWheelToDropdown;

                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.Focus();
                Keyboard.Focus(tb);
                ApplyAccountFilter(tb.Text ?? "");
            }

            // NEW: commit-on-click for dropdown items (bubbling handler on the ComboBox)
            cb.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(AccountDropdown_ClickCommit));
            cb.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(AccountDropdown_ClickCommit), true);
        }


        private void AccountEditor_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_activeAccountCombo == null) return;
            var tb = _activeAccountCombo.Template?.FindName("PART_EditableTextBox", _activeAccountCombo) as TextBox;
            if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)
            {
                e.Handled = true;
                int count = _accountFiltered.Count;
                if (count == 0) return;
                string prevText = tb?.Text ?? "";
                int caret = tb?.CaretIndex ?? prevText.Length;
                int selLen = tb != null ? tb.SelectionLength : 0;
                int selStart = tb != null ? tb.SelectionStart : caret;
                int pos = _activeAccountCombo.SelectedIndex; // -1 initially
                switch (e.Key)
                {
                    case Key.Down: pos = Math.Min(pos + 1, count - 1); break;
                    case Key.Up: pos = Math.Max(pos - 1, 0); break;
                    case Key.PageDown: pos = Math.Min(pos + 10, count - 1); break;
                    case Key.PageUp: pos = Math.Max(pos - 10, 0); break;
                    case Key.Home: pos = 0; break;
                    case Key.End: pos = count - 1; break;
                }
                _activeAccountCombo.SelectedIndex = pos;
                _activeAccountCombo.IsDropDownOpen = true;
                if (tb != null)
                {
                    tb.Text = prevText;
                    tb.CaretIndex = Math.Min(caret, tb.Text.Length);
                    if (selLen > 0 && selStart + selLen <= tb.Text.Length)
                    {
                        tb.SelectionStart = selStart;
                        tb.SelectionLength = selLen;
                    }
                }
                return;
            }
            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                // NEW: if exactly one, force-select it
                if (_activeAccountCombo.SelectedItem == null && _accountFiltered.Count == 1)
                    _activeAccountCombo.SelectedIndex = 0;

                if (_activeAccountCombo.SelectedItem == null && _accountFiltered.Count > 0)
                    _activeAccountCombo.SelectedIndex = 0;

                if (_activeAccountCombo.SelectedItem != null)
                {
                    BindingOperations.GetBindingExpression(_activeAccountCombo, ComboBox.SelectedItemProperty)
                                     ?.UpdateSource();
                    CommitAccountAndGoToDescription();
                }
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                LinesGrid.CancelEdit();
                LinesGrid.Focus();
                return;
            }
        }

        private void UpdateAmountColumnVisibility()
        {
            var vm = (VoucherEditorVm)DataContext;
            var vt = Enum.Parse<VoucherType>(vm.Type);

            var debitCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Debit");
            var creditCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Credit");

            if (debitCol == null || creditCol == null) return;

            switch (vt)
            {
                case VoucherType.Payment:
                    debitCol.Visibility = Visibility.Visible;
                    creditCol.Visibility = Visibility.Collapsed;
                    break;
                case VoucherType.Receipt:
                    debitCol.Visibility = Visibility.Collapsed;
                    creditCol.Visibility = Visibility.Visible;
                    break;
                default: // Journal
                    debitCol.Visibility = Visibility.Visible;
                    creditCol.Visibility = Visibility.Visible;
                    break;
            }
        }


        private void ForwardWheelToDropdown(object? sender, MouseWheelEventArgs e)
        {
            if (_activeAccountCombo?.IsDropDownOpen != true) return;

            var sv = FindVisualChild<ScrollViewer>(_activeAccountCombo);
            if (sv == null) return;

            if (e.Delta < 0) sv.LineDown();
            else sv.LineUp();
            e.Handled = true;
        }


        private void AccountSearch_AccountCommitted(object sender, RoutedEventArgs e)
        {
            var line = LinesGrid.SelectedItem as VoucherLineVm;
            if (line?.Account == null) return;
            CommitCurrentCell();
            MoveToColumn("Description", beginEdit: true);
        }

        private void AccountCombo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BindingOperations.GetBindingExpression(cb, ComboBox.SelectedItemProperty)
                                 ?.UpdateSource();
                CommitAccountAndGoToDescription();
            }
        }

        private void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var cb = (ComboBox)sender;
            if (cb.SelectedItem == null) return;

            BindingOperations.GetBindingExpression(cb, ComboBox.SelectedItemProperty)
                             ?.UpdateSource();
            CommitAccountAndGoToDescription();
        }

        private void CommitAccountAndGoToDescription()
        {
            _accountDebounce?.Stop();          // NEW: cancel late refilter ticks
            _suppressAccountFilter = false;    // ensure normal state
            _committingAccountClick = false;

            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MoveToColumn("Description", beginEdit: true);
        }

        // Commit the clicked account even if focus/popup closes first
        private void AccountSearchBox_ClickCommit(object sender, MouseButtonEventArgs e)
        {
            // Only react if the click bubbled from an item inside the dropdown
            var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject)
                       ?? FindVisualParent<ComboBoxItem>(e.OriginalSource as DependencyObject);
            if (item == null) return;

            // Defer: let the AccountSearchBox finish setting SelectedAccount first
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                // Reuse your existing flow (this expects line.Account to be set by binding)
                AccountSearch_AccountCommitted(sender, new RoutedEventArgs());
            }));

            e.Handled = true;
        }

        // Let the list scroll when the cursor is anywhere over the search box (not just the scrollbar)
        private void AccountSearchBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = FindVisualChild<ScrollViewer>(sender as DependencyObject);
            if (sv == null) return;

            if (e.Delta < 0) sv.LineDown(); else sv.LineUp();
            e.Handled = true;
        }


        private void CommitCurrentCell()
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T t) return t;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void AccountDropdown_ClickCommit(object? sender, MouseButtonEventArgs e)
        {
            if (_activeAccountCombo?.IsDropDownOpen != true) return;

            // only if clicking inside a ComboBoxItem
            var cbi = FindVisualParent<ComboBoxItem>(e.OriginalSource as DependencyObject);
            if (cbi == null) return;

            // prevent TextChanged/debounce from firing a refilter that clears the selection
            _suppressAccountFilter = true;

            // Defer so WPF can set SelectedItem first
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    _committingAccountClick = true;
                    _accountDebounce?.Stop(); // kill any pending refilter tick

                    if (_activeAccountCombo?.SelectedItem != null)
                    {
                        BindingOperations.GetBindingExpression(_activeAccountCombo, ComboBox.SelectedItemProperty)
                                         ?.UpdateSource();
                        CommitAccountAndGoToDescription();
                    }
                }
                finally
                {
                    _committingAccountClick = false;
                    _suppressAccountFilter = false;
                }
            }));

            e.Handled = true;
        }



        private void MoveToColumn(string header, bool beginEdit)
        {
            if (LinesGrid.SelectedItem == null) return;
            var col = LinesGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == header);
            if (col == null) return;
            LinesGrid.CurrentCell = new DataGridCellInfo(LinesGrid.SelectedItem, col);
            LinesGrid.ScrollIntoView(LinesGrid.SelectedItem, col);
            if (beginEdit)
                LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => LinesGrid.BeginEdit()));
        }

        private void AccountSearchBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                fe.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    fe.Focus(); // focus the AccountSearchBox
                    Keyboard.Focus(fe);
                }));
            }
        }

        private static TChild? FindVisualChild<TChild>(DependencyObject parent, Func<TChild, bool>? predicate = null)
            where TChild : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TChild typed && (predicate == null || predicate(typed)))
                    return typed;

                var result = FindVisualChild<TChild>(child, predicate);
                if (result != null) return result;
            }
            return null;
        }

        private static TChild? FindVisualChildByName<TChild>(DependencyObject parent, string name)
            where TChild : FrameworkElement
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TChild fe && fe.Name == name) return fe;

                var result = FindVisualChildByName<TChild>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void MoveToAmountColumn()
        {
            var vm = (VoucherEditorVm)DataContext;
            var isDebit = vm.Type == nameof(VoucherType.Payment);
            var isCredit = vm.Type == nameof(VoucherType.Receipt);
            var isJournal = vm.Type == nameof(VoucherType.Journal);
            DataGridColumn? targetCol = null;
            if (isDebit || isJournal)
                targetCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Debit")
                            ?? LinesGrid.Columns.ElementAtOrDefault(3);
            if ((isCredit || isJournal) && (targetCol == null || isCredit))
                targetCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Credit")
                            ?? LinesGrid.Columns.ElementAtOrDefault(4);
            if (targetCol == null || LinesGrid.SelectedItem == null) return;
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            LinesGrid.CurrentCell = new DataGridCellInfo(LinesGrid.SelectedItem, targetCol);
            LinesGrid.ScrollIntoView(LinesGrid.SelectedItem, targetCol);
            LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                LinesGrid.BeginEdit();
                var content = targetCol.GetCellContent(LinesGrid.SelectedItem);
                if (content != null)
                {
                    var tb = FindVisualChild<TextBox>(content) ?? content as TextBox;
                    if (tb != null)
                    {
                        tb.Focus();
                        Keyboard.Focus(tb);
                        tb.SelectAll(); // helpful for quick overwrite
                    }
                }
            }));
        }
    }
}