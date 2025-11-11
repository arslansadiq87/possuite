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
using Pos.Domain.Utils;
using Pos.Domain.Services;
//using Pos.Persistence.Services;   // service pattern

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OutletsCountersView : UserControl
    {
        public event EventHandler? RequestClose;

        // Service-based: the view owns NO DbContext or Outbox.
        private readonly IOutletCounterService _svc;

        // Lightweight DTOs for safe binding (kept local to avoid XAML churn)
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

        // Selection memory + views for filtering
        private int? _selectedOutletId;
        private ICollectionView? _outletView;
        private ICollectionView? _counterView;

        // parameterless: this is a UserControl loaded in XAML
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

        // -------------------- Data loading via service --------------------
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

            // restore selection if possible
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

        // -------------------- Filters & search boxes --------------------
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

        // -------------------- Grid selection & refresh --------------------
        private async void OutletsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var row = OutletsGrid.SelectedItem as OutletRow;
                _selectedOutletId = row?.Id;

                // clear previous counter search so new outlet’s counters show up
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

        // -------------------- CRUD — Outlets (dialogs + service) --------------------
        private async void AddOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                //var dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                var dlg = new EditOutletWindow(); // was: new EditOutletWindow(dbf)
                if (dlg.ShowDialog() == true && dlg.SavedOutletId is int id)
                {
                    // Ensure outbox upsert (reload + enqueue inside service)
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
                //var dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
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

        // -------------------- CRUD — Counters (dialogs + service) --------------------
        private async void AddCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }
                //var dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                var dlg = new EditCounterWindow(outletId); // was: new EditCounterWindow(_dbf, outletId)
                if (dlg.ShowDialog() == true)
                {
                    // We don't have SavedCounterId; reload last created or simply refresh list.
                    // To ensure outbox upsert, call service to upsert the most recent counter id (optional),
                    // but simplest is to reload the grid:
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
                //var dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
                var dlg = new EditCounterWindow(row.Id); // was: new EditCounterWindow(_dbf, row.Id)
                if (dlg.ShowDialog() == true)
                {
                    // Ensure outbox upsert
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

        // -------------------- Assign / Unassign this PC --------------------
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

                // Keep existing CounterBindingService for local side-effects if needed
                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.AssignThisPcToCounter(outletId, row.Id); // local config (if any)

                var machine = Environment.MachineName;
                await _svc.AssignThisPcAsync(outletId, row.Id, machine);

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
                // Keep existing CounterBindingService for local side-effects if needed
                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.UnassignThisPc();

                var machine = Environment.MachineName;
                await _svc.UnassignThisPcAsync(machine);

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

        // -------------------- UI helpers --------------------
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

            if (!IsCurrentUserAdmin())
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
