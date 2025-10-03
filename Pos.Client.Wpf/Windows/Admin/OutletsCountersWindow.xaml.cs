// Pos.Client.Wpf/Windows/Admin/OutletsCountersWindow.xaml.cs
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Common;
using Pos.Client.Wpf.Services;
using Pos.Domain;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class OutletsCountersWindow : Window
    {
        // Lightweight DTOs for safe binding
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

        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        // Selection memory
        private int? _selectedOutletId;

        // Views for in-memory filtering
        private ICollectionView? _outletView;
        private ICollectionView? _counterView;

        public OutletsCountersWindow(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;

            Loaded += (_, __) =>
            {
                SafeLoadOutlets();
                // monitor layout/size so we can show/hide search rows based on scrollability
                OutletsGrid.SizeChanged += (_, __2) => UpdateOutletSearchRowVisibility();
                OutletsGrid.LayoutUpdated += (_, __2) => UpdateOutletSearchRowVisibility();
                CountersGrid.SizeChanged += (_, __2) => UpdateCounterSearchRowVisibility();
                CountersGrid.LayoutUpdated += (_, __2) => UpdateCounterSearchRowVisibility();
            };
        }

        // -------------------- Data loading --------------------

        private void SafeLoadOutlets()
        {
            try
            {
                using var db = _dbf.CreateDbContext();
                var outlets = db.Outlets.AsNoTracking()
                    .OrderBy(o => o.Name)
                    .Select(o => new OutletRow
                    {
                        Id = o.Id,
                        Code = o.Code,
                        Name = o.Name,
                        Address = o.Address,
                        IsActive = o.IsActive
                    })
                    .ToList();

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

            // restore selection
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

        private void SafeLoadCounters(int outletId)
        {
            try
            {
                using var db = _dbf.CreateDbContext();
                var counters = db.Counters.AsNoTracking()
                    .Where(c => c.OutletId == outletId)
                    .OrderBy(c => c.Name)
                    .Select(c => new CounterRow
                    {
                        Id = c.Id,
                        OutletId = c.OutletId,
                        Name = c.Name,
                        IsActive = c.IsActive,
                        AssignedTo = db.CounterBindings
                            .Where(b => b.CounterId == c.Id)
                            .Select(b => b.MachineName)
                            .FirstOrDefault()
                    })
                    .ToList();

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


        // -------------------- Filters & search --------------------

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

        // -------------------- Selection & refresh --------------------

        private void OutletsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var row = OutletsGrid.SelectedItem as OutletRow;
                _selectedOutletId = row?.Id;

                // ✨ Clear any previous counter search so new outlet’s counters show up
                if (CounterSearchBox != null && CounterSearchBox.Text?.Length > 0)
                    CounterSearchBox.Text = string.Empty;

                CountersGrid.ItemsSource = null;
                _counterView = null;

                if (_selectedOutletId is int id)
                {
                    SafeLoadCounters(id);
                }
                else
                {
                    UpdateCounterSearchRowVisibility(); // no counters
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Selection error:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Refresh_Click(object sender, RoutedEventArgs e) => SafeLoadOutlets();

        // -------------------- Outlet CRUD --------------------

        private void AddOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new EditOutletWindow(_dbf); // create mode
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    _selectedOutletId = dlg.SavedOutletId;
                    SafeLoadOutlets();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OutletsGrid.SelectedItem is not OutletRow sel)
                {
                    MessageBox.Show("Select an outlet.");
                    return;
                }

                var dlg = new EditOutletWindow(_dbf, sel.Id); // edit mode
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    _selectedOutletId = dlg.SavedOutletId;
                    SafeLoadOutlets();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void DeleteOutlet_Click(object sender, RoutedEventArgs e)
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

                using var db = _dbf.CreateDbContext();
                var dbO = db.Outlets.Include(o => o.Counters)
                                    .FirstOrDefault(o => o.Id == sel.Id);
                if (dbO == null) { MessageBox.Show("Outlet not found."); return; }

                if (dbO.Counters.Any())
                {
                    MessageBox.Show("Outlet has counters. Delete counters first.");
                    return;
                }

                db.Outlets.Remove(dbO);
                db.SaveChanges();
                _selectedOutletId = null;
                SafeLoadOutlets();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------- Counter CRUD --------------------

        private void AddCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }

                var dlg = new EditCounterWindow(_dbf, outletId) { Owner = this }; // CREATE
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    SafeLoadCounters(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditCounter_Click(object sender, RoutedEventArgs e)
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

                var dlg = new EditCounterWindow(_dbf, row.Id) { Owner = this }; // EDIT
                dlg.Owner = this;
                if (dlg.ShowDialog() == true)
                {
                    SafeLoadCounters(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void DeleteCounter_Click(object sender, RoutedEventArgs e)
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

                using var db = _dbf.CreateDbContext();
                var dbC = db.Counters.Find(row.Id);
                if (dbC == null) { MessageBox.Show("Counter not found."); return; }

                db.Counters.Remove(dbC);
                db.SaveChanges();
                SafeLoadCounters(outletId);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to delete counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------- Assign / Unassign This PC --------------------

        private void AssignThisPc_Click(object sender, RoutedEventArgs e)
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

                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.AssignThisPcToCounter(outletId, row.Id);

                SafeLoadCounters(outletId);
                MessageBox.Show("This PC is now assigned to the selected counter.",
                    "Binding", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to assign this PC:\n\n" + ex.Message, "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnassignThisPc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.UnassignThisPc();

                if (_selectedOutletId is int outletId)
                    SafeLoadCounters(outletId);

                MessageBox.Show("This PC has been unassigned.", "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to unassign:\n\n" + ex.Message, "Binding",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------- Simple inline editors --------------------

        

        // -------------------- Search row visibility logic --------------------
        // Show search row only when:
        // 1) there are rows AND
        // 2) the DataGrid is vertically scrollable (more rows than visible)

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

            // Either a visible vertical scrollbar, or a positive scrollable height
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
            // Prefer the selected row (gives you Code/Name), but fall back to _selectedOutletId
            var row = OutletsGrid?.SelectedItem as OutletRow;
            int id = row?.Id ?? (_selectedOutletId ?? 0);
            if (id == 0) return;

            if (!IsCurrentUserAdmin())
            {
                MessageBox.Show("Only Admin can create or edit Opening Stock.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build the label. If row is null (e.g., selection cleared but id remains), fetch minimal info.
            string label = row != null
                ? $"{row.Code} - {row.Name}"
                : $"Outlet #{id}";

            var dlg = new OpeningStockDialog(
                InventoryLocationType.Outlet,
                id,
                label);

            dlg.Owner = this;
            dlg.ShowDialog();
        }



        private static bool IsCurrentUserAdmin()
        {
            var s = AppState.Current;
            if (s.CurrentUser != null) return s.CurrentUser.Role == UserRole.Admin;
            return string.Equals(s.CurrentUserRole, "Admin", StringComparison.OrdinalIgnoreCase);
        }
    }
}
