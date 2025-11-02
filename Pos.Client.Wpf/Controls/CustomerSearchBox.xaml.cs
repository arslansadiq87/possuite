using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Controls
{
    public partial class CustomerSearchBox : UserControl
    {
        public ObservableCollection<CustomerLookupRow> Suggestions { get; } = new();

        private readonly DispatcherTimer _debounce;
        private CancellationTokenSource? _cts;

        public CustomerSearchBox()
        {
            InitializeComponent();

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += async (_, __) => await RunSearchAsync();
            Loaded += (_, __) =>
            {
                QueryBox.KeyDown += QueryBox_KeyDown;
                SuggestList.KeyDown += SuggestList_KeyDown;
            };
        }

        #region Bindable props

        public static readonly DependencyProperty SelectedCustomerProperty =
            DependencyProperty.Register(nameof(SelectedCustomer), typeof(CustomerLookupRow),
                typeof(CustomerSearchBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public CustomerLookupRow? SelectedCustomer
        {
            get => (CustomerLookupRow?)GetValue(SelectedCustomerProperty);
            set => SetValue(SelectedCustomerProperty, value);
        }

        public static readonly DependencyProperty SelectedCustomerIdProperty =
            DependencyProperty.Register(nameof(SelectedCustomerId), typeof(int?),
                typeof(CustomerSearchBox), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        private void SetPopup(bool open)
        {
            if (SuggestPopup != null)
                SuggestPopup.IsOpen = open;
        }


        public int? SelectedCustomerId
        {
            get => (int?)GetValue(SelectedCustomerIdProperty);
            set => SetValue(SelectedCustomerIdProperty, value);
        }

        public static readonly DependencyProperty QueryProperty =
            DependencyProperty.Register(nameof(Query), typeof(string),
                typeof(CustomerSearchBox), new PropertyMetadata("", OnQueryChanged));

        public string Query
        {
            get => (string)GetValue(QueryProperty);
            set => SetValue(QueryProperty, value);
        }

        public static readonly RoutedEvent CustomerPickedEvent =
            EventManager.RegisterRoutedEvent(nameof(CustomerPicked), RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(CustomerSearchBox));

        public event RoutedEventHandler CustomerPicked
        {
            add => AddHandler(CustomerPickedEvent, value);
            remove => RemoveHandler(CustomerPickedEvent, value);
        }

        #endregion

        public bool IsOpen { get; set; }

        private static void OnQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (CustomerSearchBox)d;
            self._debounce.Stop();
            self._debounce.Start();
        }

        private async Task RunSearchAsync()
        {
            _debounce.Stop();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var q = (Query ?? "").Trim();
            if (q.Length < 2)
            {
                Suggestions.Clear();
                SetPopup(false);
                return;
            }

            try
            {
                var svc = App.Services.GetRequiredService<PartyLookupService>();
                var termCtx = App.Services.GetRequiredService<ITerminalContext>();
                var outletId = termCtx.OutletId;

                var parties = await svc.SearchCustomersAsync(q, outletId, 20, ct);

                Suggestions.Clear();
                foreach (var p in parties)
                {
                    Suggestions.Add(new CustomerLookupRow
                    {
                        Id = p.Id,
                        Name = p.Name ?? "",
                        Phone = p.Phone ?? ""
                    });
                }

                OpenAndFocusIfAny();

            }
            catch
            {
                Suggestions.Clear();
                SetPopup(false);
            }
        }

        private void Pick(CustomerLookupRow row)
        {
            SelectedCustomer = row;
            SelectedCustomerId = row?.Id;
            Query = row?.ToString() ?? "";
            SetPopup(false);
            RaiseEvent(new RoutedEventArgs(CustomerPickedEvent));
        }


        private void SuggestList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SuggestList.SelectedItem is CustomerLookupRow row) Pick(row);
        }

        private void QueryBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (SuggestList.Items.Count > 0)
                {
                    // ensure it opens and focus moves into the list
                    SetPopup(true);
                    FocusFirstItem();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (SuggestList.SelectedItem is CustomerLookupRow row)
                {
                    Pick(row);
                }
                else if (SuggestList.Items.Count > 0)
                {
                    SuggestList.SelectedIndex = 0;
                    if (SuggestList.SelectedItem is CustomerLookupRow first) Pick(first);
                }
                e.Handled = true;
            }

            else if (e.Key == Key.Escape)
            {
                SetPopup(false);
                e.Handled = true;
            }
        }



        private void FocusFirstItem()
        {
            if (SuggestList.Items.Count > 0)
            {
                SuggestList.SelectedIndex = 0;
                var item = SuggestList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                item?.Focus();
            }
        }

        private void OpenAndFocusIfAny()
        {
            SetPopup(Suggestions.Any());
            if (SuggestPopup.IsOpen) FocusFirstItem();
        }

        private void SuggestList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SuggestList.SelectedItem is CustomerLookupRow row)
            {
                Pick(row);
                e.Handled = true;
            }
        }


        public sealed class CustomerLookupRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Code { get; set; } = "";  // optional if you have p.Code
    public override string ToString() => string.IsNullOrWhiteSpace(Phone) ? Name : $"{Name}  ({Phone})";
}

    }
}
