using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Client.Wpf.Windows.Common;
using Pos.Client.Wpf.Services;
using Pos.Domain;
using Pos.Domain.Services.Admin;
using Pos.Domain.Services;
using Pos.Domain.Services.System;
using Pos.Client.Wpf.Security;


namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OutletsCountersView : UserControl
    {
        public event EventHandler? RequestClose;
        private readonly IOutletCounterService _svc;
        private sealed class OutletRow
        {
            public int Id { get; init; }
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
            public string? Address { get; init; }
            public bool IsActive { get; init; }
        }

        private sealed class CounterRow
        {
            public int Id { get; init; }
            public int OutletId { get; init; }
            public string Name { get; init; } = "";
            public bool IsActive { get; init; }
            public string? AssignedTo { get; init; }
        }

        private int? _selectedOutletId;
        private ICollectionView? _outletView;
        private ICollectionView? _counterView;

        public OutletsCountersView()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<IOutletCounterService>();
            Loaded += async (_, __) =>
            {
                await SafeLoadOutletsAsync();
                OutletsGrid.SizeChanged += (_, __2) => UpdateOutletSearchRowVisibility();
                OutletsGrid.LayoutUpdated += (_, __2) => UpdateOutletSearchRowVisibility();
                CountersGrid.SizeChanged += (_, __2) => UpdateCounterSearchRowVisibility();
                CountersGrid.LayoutUpdated += (_, __2) => UpdateCounterSearchRowVisibility();
            };
        }

        private async Task SafeLoadOutletsAsync()
        {
            try
            {
                var list = await _svc.GetOutletsAsync();
                var outlets = list.Select(o => new OutletRow
                {
                    Id = o.Id,
                    Code = o.Code,
                    Name = o.Name,
                    Address = o.Address,
                    IsActive = o.IsActive
                }).ToList();
                UsersafeBindOutlets(outlets);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load outlets:\n\n" + ex.Message,
                    "Outlets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UsersafeBindOutlets(List<OutletRow> outlets)
        {
            OutletsGrid.ItemsSource = outlets;
            _outletView = CollectionViewSource.GetDefaultView(OutletsGrid.ItemsSource);
            _outletView.Filter = OutletFilter;
            if (_selectedOutletId is int id)
            {
                var again = outlets.FirstOrDefault(o => o.Id == id);
                if (again != null)
                {
                    OutletsGrid.SelectedItem = again;
                }
                else
                {
                    CountersGrid.ItemsSource = null;
                    _counterView = null;
                }
            }
            _outletView.Refresh();
            UpdateOutletSearchRowVisibility();
        }

        private async Task SafeLoadCountersAsync(int outletId)
        {
            try
            {
                var list = await _svc.GetCountersAsync(outletId);
                var counters = list.Select(c => new CounterRow
                {
                    Id = c.Id,
                    OutletId = c.OutletId,
                    Name = c.Name,
                    IsActive = c.IsActive,
                    AssignedTo = c.AssignedTo
                }).ToList();
                CountersGrid.ItemsSource = counters;
                _counterView = CollectionViewSource.GetDefaultView(CountersGrid.ItemsSource);
                _counterView.Filter = CounterFilter;
                _counterView.Refresh();              // ensure filter re-evaluates with cleared query
                UpdateCounterSearchRowVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load counters:\n\n" + ex.Message,
                    "Counters", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool OutletFilter(object obj)
        {
            if (obj is not OutletRow row) return false;
            var q = OutletSearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            q = q.ToLowerInvariant();
            return (row.Code?.ToLowerInvariant().Contains(q) == true)
                || (row.Name?.ToLowerInvariant().Contains(q) == true)
                || (row.Address?.ToLowerInvariant().Contains(q) == true)
                || (row.IsActive && "active".Contains(q))
                || (!row.IsActive && "inactive".Contains(q));
        }

        private void OutletSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _outletView?.Refresh();
            UpdateOutletSearchRowVisibility();
        }

        private bool CounterFilter(object obj)
        {
            if (obj is not CounterRow row) return false;
            var q = CounterSearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            q = q.ToLowerInvariant();
            return (row.Name?.ToLowerInvariant().Contains(q) == true)
                || (row.AssignedTo?.ToLowerInvariant().Contains(q) == true)
                || (row.IsActive && "active".Contains(q))
                || (!row.IsActive && "inactive".Contains(q));
        }

        private void CounterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _counterView?.Refresh();
            UpdateCounterSearchRowVisibility();
        }

        private async void OutletsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var row = OutletsGrid.SelectedItem as OutletRow;
                _selectedOutletId = row?.Id;
                if (CounterSearchBox != null && CounterSearchBox.Text?.Length > 0)
                    CounterSearchBox.Text = string.Empty;
                CountersGrid.ItemsSource = null;
                _counterView = null;
                if (_selectedOutletId is int id)
                    await SafeLoadCountersAsync(id);
                else
                    UpdateCounterSearchRowVisibility(); // no counters
            }
            catch (Exception ex)
            {
                MessageBox.Show("Selection error:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
            => await SafeLoadOutletsAsync();
        private async void AddOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new EditOutletWindow(); // was: new EditOutletWindow(dbf)
                if (dlg.ShowDialog() == true && dlg.SavedOutletId is int id)
                {
                    await _svc.UpsertOutletByIdAsync(id);
                    _selectedOutletId = id;
                    await SafeLoadOutletsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EditOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OutletsGrid.SelectedItem is not OutletRow sel)
                {
                    MessageBox.Show("Select an outlet.");
                    return;
                }
                var dlg = new EditOutletWindow(sel.Id); // was: new EditOutletWindow(dbf, sel.Id)
                if (dlg.ShowDialog() == true)
                {
                    await _svc.UpsertOutletByIdAsync(sel.Id);
                    _selectedOutletId = sel.Id;
                    await SafeLoadOutletsAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OutletsGrid.SelectedItem is not OutletRow sel)
                {
                    MessageBox.Show("Select an outlet.");
                    return;
                }
                if (MessageBox.Show($"Delete outlet '{sel.Name}'?", "Confirm",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
                await _svc.DeleteOutletAsync(sel.Id);
                _selectedOutletId = null;
                await SafeLoadOutletsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AddCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }
                var dlg = new EditCounterWindow(outletId); // was: new EditCounterWindow(_dbf, outletId)
                if (dlg.ShowDialog() == true)
                {
                    await SafeLoadCountersAsync(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EditCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }
                if (CountersGrid.SelectedItem is not CounterRow row)
                {
                    MessageBox.Show("Select a counter.");
                    return;
                }
                var dlg = new EditCounterWindow(row.Id); // was: new EditCounterWindow(_dbf, row.Id)
                if (dlg.ShowDialog() == true)
                {
                    await _svc.UpsertCounterByIdAsync(row.Id);
                    await SafeLoadCountersAsync(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }
                if (CountersGrid.SelectedItem is not CounterRow row)
                {
                    MessageBox.Show("Select a counter.");
                    return;
                }
                if (MessageBox.Show($"Delete counter '{row.Name}'?", "Confirm",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                await _svc.DeleteCounterAsync(row.Id);
                await SafeLoadCountersAsync(outletId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AssignThisPc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }
                if (CountersGrid.SelectedItem is not CounterRow row)
                {
                    MessageBox.Show("Select a counter.");
                    return;
                }

                var sp = App.Services;
                var midS = sp.GetRequiredService<IMachineIdentityService>();
                var svc = sp.GetRequiredService<ICounterBindingService>();

                var machineId = await midS.GetMachineIdAsync();
                var machineName = await midS.GetMachineNameAsync();

                await svc.AssignAsync(machineId, machineName, outletId, row.Id, CancellationToken.None);
                await SafeLoadCountersAsync(outletId);

                MessageBox.Show("This PC is now assigned to the selected counter.",
                    "Binding", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to assign this PC:\n\n" + ex.Message, "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void UnassignThisPc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = App.Services;
                var midS = sp.GetRequiredService<IMachineIdentityService>();
                var svc = sp.GetRequiredService<ICounterBindingService>();

                var machineId =await midS.GetMachineIdAsync();

                await svc.UnassignAsync(machineId, CancellationToken.None);

                if (_selectedOutletId is int outletId)
                    await SafeLoadCountersAsync(outletId);

                MessageBox.Show("This PC has been unassigned.", "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to unassign:\n\n" + ex.Message, "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void UpdateOutletSearchRowVisibility()
        {
            try
            {
                if (OutletSearchRow == null || OutletsGrid == null) return;
                bool hasRows = OutletsGrid.Items != null && OutletsGrid.Items.Count > 0;
                bool isScrollable = IsGridVerticallyScrollable(OutletsGrid);
                OutletSearchRow.Visibility = (hasRows && isScrollable) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { /* ignore */ }
        }

        private void UpdateCounterSearchRowVisibility()
        {
            try
            {
                if (CounterSearchRow == null || CountersGrid == null) return;
                bool hasRows = CountersGrid.Items != null && CountersGrid.Items.Count > 0;
                bool isScrollable = IsGridVerticallyScrollable(CountersGrid);
                CounterSearchRow.Visibility = (hasRows && isScrollable) ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { /* ignore */ }
        }

        private static bool IsGridVerticallyScrollable(DataGrid grid)
        {
            var sv = FindScrollViewer(grid);
            if (sv == null) return false;
            return sv.ComputedVerticalScrollBarVisibility == Visibility.Visible
                   || sv.ScrollableHeight > 0;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            if (root is ScrollViewer sv) return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private void OpenOpeningStock_Click(object sender, RoutedEventArgs e)
        {
            var row = OutletsGrid?.SelectedItem as OutletRow;
            int id = row?.Id ?? (_selectedOutletId ?? 0);
            if (id == 0) return;

            if (!AuthZ.IsAdminCached())
            {
                MessageBox.Show("Only Admin can create or edit Opening Stock.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string label = row != null ? $"{row.Code} - {row.Name}" : $"Outlet #{id}";

            var nav = App.Services.GetRequiredService<IViewNavigator>();
            var make = App.Services.GetRequiredService<Func<InventoryLocationType, int, string, OpeningStockView>>();
            string ctx = $"Opening:{InventoryLocationType.Outlet}:{id}";
            if ((nav as ViewNavigator)?.TryActivateByContext(ctx) == true) return;

            var view = make(InventoryLocationType.Outlet, id, label);
            var tab = nav.OpenTab(view, title: $"Opening Stock – {label}", contextKey: ctx);
            view.CloseRequested += (_, __) => nav.CloseTab(tab);
        }

        private static bool IsCurrentUserAdmin()
        {
            var s = AppState.Current;
            if (s.CurrentUser != null) return s.CurrentUser.Role == UserRole.Admin;
            return string.Equals(s.CurrentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        void OnAssigned()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
