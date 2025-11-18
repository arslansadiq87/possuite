using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Controls
{
    public partial class AccountSearchBox : UserControl
    {
        // ------------ Dependency Properties ------------
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<Account>), typeof(AccountSearchBox),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable<Account>? ItemsSource
        {
            get => (IEnumerable<Account>?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedAccountProperty =
            DependencyProperty.Register(nameof(SelectedAccount), typeof(Account), typeof(AccountSearchBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedAccountChanged));

        public Account? SelectedAccount
        {
            get => (Account?)GetValue(SelectedAccountProperty);
            set => SetValue(SelectedAccountProperty, value);
        }

        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(AccountSearchBox),
                new PropertyMetadata(string.Empty, OnSearchTextChanged));

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        // ------------ Routed event fired on commit ------------
        public static readonly RoutedEvent AccountCommittedEvent =
            EventManager.RegisterRoutedEvent(nameof(AccountCommitted), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(AccountSearchBox));

        public event RoutedEventHandler AccountCommitted
        {
            add => AddHandler(AccountCommittedEvent, value);
            remove => RemoveHandler(AccountCommittedEvent, value);
        }

        // ------------ Internals ------------
        private readonly ObservableCollection<Account> _filtered = new();
        public ObservableCollection<Account> Filtered => _filtered;

        private List<(Account acc, string key)> _index = new();
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private string _pendingQuery = "";

        public AccountSearchBox()
        {
            InitializeComponent();
            //DataContext = this;

            _debounce.Tick += (_, __) =>
            {
                _debounce.Stop();
                ApplyFilter(_pendingQuery);
            };

            Loaded += (_, __) =>
            {
                // On first focus: show full list if empty query
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    Input.Focus();
                    Keyboard.Focus(Input);
                    if (string.IsNullOrWhiteSpace(SearchText))
                        OpenWithFullList();
                    else
                        ApplyFilter(SearchText);
                }));
            };
        }

        public double DropDownMaxHeight
        {
            get => (double)GetValue(DropDownMaxHeightProperty);
            set => SetValue(DropDownMaxHeightProperty, value);
        }
        public static readonly DependencyProperty DropDownMaxHeightProperty =
            DependencyProperty.Register(
                nameof(DropDownMaxHeight),
                typeof(double),
                typeof(AccountSearchBox),
                new PropertyMetadata(360d)); // default cap (pixels)


        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (AccountSearchBox)d;
            var list = ctl.ItemsSource ?? Enumerable.Empty<Account>();
            ctl._index = list.Select(a =>
            {
                var code = a.Code ?? "";
                var name = a.Name ?? "";
                var key = (code + " " + name).ToLowerInvariant();
                return (a, key);
            }).ToList();

            // If no text, keep full list ready but don't auto-select anything
            ctl.ApplyFilter(ctl.SearchText);
        }

        private static void OnSelectedAccountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (AccountSearchBox)d;
            // Show selected account name in the textbox, but only when user did not type custom text
            if (ctl.SelectedAccount != null && !ctl._duringTyping)
            {
                ctl.SearchText = ctl.SelectedAccount.Name ?? "";
                ctl.Popup.IsOpen = false;
            }
            if (ctl.SelectedAccount == null)
            {
                // Keep user text; popup will open as they type
            }
        }

        private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctl = (AccountSearchBox)d;
            ctl._pendingQuery = (e.NewValue as string) ?? "";
            ctl._duringTyping = true;
            ctl._debounce.Stop();
            ctl._debounce.Start();
        }

        private bool _duringTyping = false;

        private void OpenWithFullList()
        {
            _filtered.Clear();
            foreach (var a in _index.Select(t => t.acc)) _filtered.Add(a);
            List.SelectedIndex = -1;
            Popup.IsOpen = true;
        }

        private void ApplyFilter(string text)
        {
            _duringTyping = false;

            var q = (text ?? "").Trim().ToLowerInvariant();
            var tokens = q.Length == 0
                ? Array.Empty<string>()
                : q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            IEnumerable<Account> results;
            if (tokens.Length == 0)
                results = _index.Select(t => t.acc);        // full list
            else
                results = _index.Where(t => tokens.All(tok => t.key.Contains(tok)))
                                .Select(t => t.acc);

            // cap to keep UI snappy
            results = results.Take(500);

            _filtered.Clear();
            foreach (var a in results) _filtered.Add(a);

            // DO NOT preselect anything
            List.SelectedIndex = -1;

            // Keep popup visible while searching
            Popup.IsOpen = true;
        }

        // ------------ UI events ------------
        private void Input_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                OpenWithFullList();
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // navigation in list without changing textbox content
            if (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End)
            {
                e.Handled = true;
                if (!Popup.IsOpen) Popup.IsOpen = true;

                int count = _filtered.Count;
                if (count == 0) return;

                int pos = List.SelectedIndex; // -1 initially
                switch (e.Key)
                {
                    case Key.Down: pos = Math.Min(pos + 1, count - 1); break;
                    case Key.Up: pos = Math.Max(pos - 1, 0); break;
                    case Key.PageDown: pos = Math.Min(pos + 10, count - 1); break;
                    case Key.PageUp: pos = Math.Max(pos - 10, 0); break;
                    case Key.Home: pos = 0; break;
                    case Key.End: pos = count - 1; break;
                }
                List.SelectedIndex = pos;
                List.ScrollIntoView(List.SelectedItem);
                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitSelection();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Popup.IsOpen = false;
                return;
            }

            // Backspace: if an account was set and all text is selected -> clear
            if (e.Key == Key.Back && SelectedAccount != null && Input.SelectionLength == Input.Text.Length)
            {
                SelectedAccount = null;   // clears binding
                SearchText = "";
                OpenWithFullList();
                e.Handled = true;
                return;
            }

            // For all normal typing keys: let TextBox update SearchText; debounce will filter.
        }

        private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => CommitSelection();

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CommitSelection();

        private void CommitSelection()
        {
            if (List.SelectedItem is Account a)
            {
                SelectedAccount = a;          // update DP (writes to VM binding)
                SearchText = a.Name ?? "";    // reflect in textbox
                Popup.IsOpen = false;

                // bubble an event so parent grid can move to Description cell
                RaiseEvent(new RoutedEventArgs(AccountCommittedEvent, this));
            }
        }

        // (E) Commit when any list item is clicked (works regardless of inner template content)
        private void ListItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem lbi)
            {
                lbi.IsSelected = true;   // ensure SelectedItem is set
                                         // Defer one tick so binding updates (SelectedItem) are settled
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(CommitSelection));
                e.Handled = true;
            }
        }

        // (D) Scroll even when mouse is NOT on the scrollbar
        private void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (FindScrollViewer(List) is ScrollViewer sv)
            {
                if (e.Delta < 0) sv.LineDown(); else sv.LineUp();
                e.Handled = true;
            }
        }

        // (A) Also catch wheel on the whole control (text area, padding, etc.)
        private void Root_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Popup.IsOpen) return;
            if (FindScrollViewer(List) is ScrollViewer sv)
            {
                if (e.Delta < 0) sv.LineDown(); else sv.LineUp();
                e.Handled = true;
            }
        }

        // Helper: find the dropdown ScrollViewer reliably
        private static ScrollViewer? FindScrollViewer(DependencyObject? root)
        {
            if (root is null) return null;
            if (root is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

    }
}
