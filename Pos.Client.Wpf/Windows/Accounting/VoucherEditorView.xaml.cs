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
        private ListCollectionView? _accountView;   // per-active editor
        private ComboBox? _activeAccountCombo;      // current cell's editor
        private readonly ObservableCollection<Account> _accountFiltered = new(); // data for ComboBox
        private List<(Account acc, string key)> _accountIndex = new();           // precomputed lowercase "code name"
        private DispatcherTimer? _accountDebounce;
        private string _pendingQuery = "";
        private bool _lastEditCancelled = false;
        private bool _suppressNextAmountValidation = false;

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

            // --- Enter navigation on header controls ---
            TypeBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { DateBox.Focus(); e.Handled = true; }
            };

            DateBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    var v = (VoucherEditorVm)DataContext;
                    // If admin, go to Outlet; if not admin but multiple outlets visible, still go to outlet (it may be locked)
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

            _pendingQuery = (sender as TextBox)?.Text ?? "";
            EnsureDebouncer();
            _accountDebounce!.Stop();
            _accountDebounce!.Start();

            // UX: keep list open and no selection while user types
            _activeAccountCombo.IsDropDownOpen = true;
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
                // swallow the duplicate validation call that immediately follows the first
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

            // 3a) Amount validation per voucher type
            if (colHeader == "Debit" || colHeader == "Credit")
            {
                // <-- NEW: always find the inner TextBox inside template
                TextBox? tb = e.EditingElement as TextBox
                              ?? (e.EditingElement as FrameworkElement != null
                                  ? GetInnerTextBox((FrameworkElement)e.EditingElement)
                                  : null);

                decimal val;
                if (tb == null || !decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out val))
                    val = 0m;

                var vt = Enum.Parse<VoucherType>(vm.Type);


                if (vt == VoucherType.Debit)
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
                else if (vt == VoucherType.Credit)
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


            // 4) Auto-fill Description when left blank
            if (colHeader == "Description")
            {
                if (e.EditingElement is TextBox tbDesc)
                {
                    var text = (tbDesc.Text ?? "").Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        string auto = vm.Type == nameof(VoucherType.Debit) ? "Cash Payment Voucher"
                                    : vm.Type == nameof(VoucherType.Credit) ? "Cash Receiving Voucher"
                                    : "Journal Voucher";

                        // write both to textbox and to model
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
                                                 // If an editor is open, re-apply current query to keep UX seamless
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

            // optional: keep list small for UI: start empty
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
            string q = (query ?? "").Trim().ToLowerInvariant();
            var tokens = q.Length == 0
                ? Array.Empty<string>()
                : q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            IEnumerable<Account> results;

            if (tokens.Length == 0)
            {
                // NEW: show full list on blank query
                results = _accountIndex.Select(t => t.acc);
            }
            else
            {
                results = _accountIndex.Where(t =>
                {
                    foreach (var tok in tokens)
                        if (!t.key.Contains(tok)) return false;
                    return true;
                })
                .Select(t => t.acc);
            }

            // optional cap to keep dropdown snappy; adjust or remove
            results = results.Take(500);

            _accountFiltered.Clear();
            foreach (var a in results) _accountFiltered.Add(a);

            if (_activeAccountCombo != null)
            {
                _activeAccountCombo.IsDropDownOpen = true;

                // IMPORTANT: do NOT auto-select anything here
                _activeAccountCombo.SelectedIndex = -1;
                _activeAccountCombo.SelectedItem = null;
            }
        }


        private void FocusGridAccountFirstCell()
        {
            if (LinesGrid.Items.Count == 0) return;

            LinesGrid.SelectedIndex = 0;
            var firstItem = LinesGrid.Items[0];

            // Try by header first, then by index fallback
            var accountCol = LinesGrid.Columns.FirstOrDefault(c => c.Header?.ToString() == "Account")
                             ?? LinesGrid.Columns.ElementAtOrDefault(1);
            if (accountCol == null) return;

            LinesGrid.CurrentCell = new DataGridCellInfo(firstItem, accountCol);
            LinesGrid.ScrollIntoView(firstItem, accountCol);

            // IMPORTANT: BeginEdit must be deferred to allow the cell to realize
            LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                LinesGrid.BeginEdit(); // triggers AccountCombo_Loaded → focuses editable textbox
            }));
        }


        // Open dropdown and put caret in the editable textbox

        private void AccountCombo_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb) return;

            _activeAccountCombo = cb;

            // use the fast filtered list
            cb.IsSynchronizedWithCurrentItem = false;
            cb.ItemsSource = _accountFiltered;

            // If no account bound -> open unselected & empty
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

                tb.CaretIndex = tb.Text?.Length ?? 0;
                tb.Focus();
                Keyboard.Focus(tb);

                // NEW: show full list when editor opens blank
                ApplyAccountFilter(tb.Text ?? "");
            }

        }

        private void AccountEditor_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (_activeAccountCombo == null) return;

            // We need access to the editor textbox to preserve user text
            var tb = _activeAccountCombo.Template?.FindName("PART_EditableTextBox", _activeAccountCombo) as TextBox;

            if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)
            {
                e.Handled = true;

                int count = _accountFiltered.Count;
                if (count == 0) return;

                // Preserve current typed text and caret BEFORE we change SelectedIndex
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

                // Move visual highlight
                _activeAccountCombo.SelectedIndex = pos;
                _activeAccountCombo.IsDropDownOpen = true;

                // RESTORE the user's typed text & caret so query does NOT change
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

                // If nothing highlighted but results exist, take first result
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

            // All other keys (letters/digits/backspace) fall through:
            // the TextBox handles typing, our debounce will re-filter.
        }

        private void AccountSearch_AccountCommitted(object sender, RoutedEventArgs e)
        {
            // Only proceed if Account is actually set
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
            // Esc handled in AccountEditor_PreviewKeyDown
        }

        private void AccountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                var cb = (ComboBox)sender;
                BindingOperations.GetBindingExpression(cb, ComboBox.SelectedItemProperty)
                                 ?.UpdateSource();
                CommitAccountAndGoToDescription();
            }
        }


        private void CommitAccountAndGoToDescription()
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            MoveToColumn("Description", beginEdit: true);
        }



        // Commit when user clicks an item in the dropdown
        private void AccountComboItem_Click(object sender, MouseButtonEventArgs e)
        {
            CommitAccountAndGoToDescription();
            e.Handled = true;
        }


        private void CommitCurrentCell()
        {
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
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

        // ...

        private void AccountSearchBox_Loaded(object sender, RoutedEventArgs e)
        {
            // When the Account cell enters edit mode, this is invoked.
            // Ensure the box gets keyboard focus immediately.
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
            var isDebit = vm.Type == nameof(VoucherType.Debit);
            var isCredit = vm.Type == nameof(VoucherType.Credit);
            var isJournal = vm.Type == nameof(VoucherType.Journal);

            DataGridColumn? targetCol = null;

            if (isDebit || isJournal)
                targetCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Debit")
                            ?? LinesGrid.Columns.ElementAtOrDefault(3);

            if ((isCredit || isJournal) && (targetCol == null || isCredit))
                targetCol = LinesGrid.Columns.FirstOrDefault(c => (c.Header?.ToString() ?? "") == "Credit")
                            ?? LinesGrid.Columns.ElementAtOrDefault(4);

            if (targetCol == null || LinesGrid.SelectedItem == null) return;

            // Commit description, move, then DEFER editing
            LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            LinesGrid.CurrentCell = new DataGridCellInfo(LinesGrid.SelectedItem, targetCol);
            LinesGrid.ScrollIntoView(LinesGrid.SelectedItem, targetCol);

            LinesGrid.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                LinesGrid.BeginEdit();

                // focus the numeric TextBox inside the cell
                var content = targetCol.GetCellContent(LinesGrid.SelectedItem);
                if (content != null)
                {
                    // TemplateColumn → content is TextBlock in view mode, but TextBox in edit container
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
