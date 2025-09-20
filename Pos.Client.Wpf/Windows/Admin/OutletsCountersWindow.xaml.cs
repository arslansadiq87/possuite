// Pos.Client.Wpf/Windows/Admin/OutletsCountersWindow.cs
using System;
using System.Linq;
using System.Windows;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Common;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;

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
            public string? AssignedTo { get; init; }  // NEW

        }

        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        // Keep the last selected outlet id to re-select after refresh
        private int? _selectedOutletId;

        public OutletsCountersWindow(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            Loaded += (_, __) => SafeLoadOutlets();
        }

        // ---------- Loaders ----------
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
                }
            }
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
                        // NEW: show machine name if bound
                        AssignedTo = db.CounterBindings
                            .Where(b => b.CounterId == c.Id)
                            .Select(b => b.MachineName)
                            .FirstOrDefault()
                    })
                    .ToList();

                CountersGrid.ItemsSource = counters;

            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load counters:\n\n" + ex.Message,
                    "Counters", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- UI Events ----------
        private void OutletsGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                var row = OutletsGrid.SelectedItem as OutletRow;
                _selectedOutletId = row?.Id;
                CountersGrid.ItemsSource = null;
                if (_selectedOutletId is int id)
                {
                    SafeLoadCounters(id);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Selection error:\n\n" + ex.Message, "Outlets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => SafeLoadOutlets();

        // ---------- Outlet CRUD ----------
        private void AddOutlet_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var o = new Outlet { Code = "NEW", Name = "New Outlet", IsActive = true };
                if (EditOutletDialog(ref o))
                {
                    using var db = _dbf.CreateDbContext();
                    db.Outlets.Add(o);
                    db.SaveChanges();
                    _selectedOutletId = o.Id;
                    SafeLoadOutlets();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add outlet:\n\n" + ex.Message, "Outlets", MessageBoxButton.OK, MessageBoxImage.Error);
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

                // load original entity
                using var db = _dbf.CreateDbContext();
                var dbO = db.Outlets.Find(sel.Id);
                if (dbO == null) { MessageBox.Show("Outlet not found."); return; }

                var copy = new Outlet
                {
                    Id = dbO.Id,
                    Code = dbO.Code,
                    Name = dbO.Name,
                    Address = dbO.Address,
                    IsActive = dbO.IsActive
                };

                if (EditOutletDialog(ref copy))
                {
                    dbO.Code = copy.Code;
                    dbO.Name = copy.Name;
                    dbO.Address = copy.Address;
                    dbO.IsActive = copy.IsActive;
                    db.SaveChanges();
                    _selectedOutletId = dbO.Id;
                    SafeLoadOutlets();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit outlet:\n\n" + ex.Message, "Outlets", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var dbO = db.Outlets.Include(o => o.Counters).FirstOrDefault(o => o.Id == sel.Id);
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
                MessageBox.Show("Failed to delete outlet:\n\n" + ex.Message, "Outlets", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Counter CRUD ----------
        private void AddCounter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId)
                {
                    MessageBox.Show("Select an outlet first.");
                    return;
                }

                var c = new Counter { OutletId = outletId, Name = "New Counter", IsActive = true };
                if (EditCounterDialog(ref c))
                {
                    using var db = _dbf.CreateDbContext();
                    db.Counters.Add(c);
                    db.SaveChanges();
                    SafeLoadCounters(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add counter:\n\n" + ex.Message, "Counters", MessageBoxButton.OK, MessageBoxImage.Error);
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

                using var db = _dbf.CreateDbContext();
                var dbC = db.Counters.Find(row.Id);
                if (dbC == null) { MessageBox.Show("Counter not found."); return; }

                var copy = new Counter { Id = dbC.Id, OutletId = dbC.OutletId, Name = dbC.Name, IsActive = dbC.IsActive };

                if (EditCounterDialog(ref copy))
                {
                    dbC.Name = copy.Name;
                    dbC.IsActive = copy.IsActive;
                    db.SaveChanges();
                    SafeLoadCounters(outletId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to edit counter:\n\n" + ex.Message, "Counters", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("Failed to delete counter:\n\n" + ex.Message, "Counters", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Tiny inline editors ----------
        private bool EditOutletDialog(ref Outlet o)
        {
            var dlg = new SimplePromptWindow(
                "Edit Outlet",
                ("Code", o.Code),
                ("Name", o.Name),
                ("Address", o.Address ?? ""),
                ("Active", o.IsActive)
            );

            if (dlg.ShowDialog() == true)
            {
                o.Code = dlg.GetText("Code");
                o.Name = dlg.GetText("Name");
                o.Address = dlg.GetText("Address");
                o.IsActive = dlg.GetBool("Active");
                return true;
            }
            return false;
        }

        private bool EditCounterDialog(ref Counter c)
        {
            var dlg = new SimplePromptWindow("Edit Counter", ("Name", c.Name), ("Active", c.IsActive));
            if (dlg.ShowDialog() == true)
            {
                c.Name = dlg.GetText("Name");
                c.IsActive = dlg.GetBool("Active");
                return true;
            }
            return false;
        }

        private void AssignThisPc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedOutletId is not int outletId) { MessageBox.Show("Select an outlet first."); return; }
                if (CountersGrid.SelectedItem is not CounterRow row) { MessageBox.Show("Select a counter."); return; }

                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.AssignThisPcToCounter(outletId, row.Id);
                SafeLoadCounters(outletId);
                MessageBox.Show("This PC is now assigned to the selected counter.", "Binding", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to assign this PC:\n\n" + ex.Message, "Binding", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnassignThisPc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var binder = App.Services.GetRequiredService<CounterBindingService>();
                binder.UnassignThisPc();

                if (_selectedOutletId is int outletId) SafeLoadCounters(outletId);

                MessageBox.Show("This PC has been unassigned.", "Binding", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to unassign:\n\n" + ex.Message, "Binding", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }
}
