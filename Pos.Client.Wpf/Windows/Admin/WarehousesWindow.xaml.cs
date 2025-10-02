using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class WarehousesWindow : Window
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private readonly bool _design;
        private Func<EditWarehouseWindow>? _editWarehouseFactory;

        public WarehousesWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return; // let designer load

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _editWarehouseFactory = () => App.Services.GetRequiredService<EditWarehouseWindow>();

            Loaded += (_, __) => LoadRows();
        }

        private bool Ready => !_design && _dbf != null;

        private void LoadRows()
        {
            if (!Ready) return;

            try
            {
                using var db = _dbf!.CreateDbContext();

                var term = (SearchBox.Text ?? "").Trim().ToLower();
                var q = db.Warehouses.AsNoTracking();

                if (ShowInactive.IsChecked != true)
                    q = q.Where(w => w.IsActive);

                if (!string.IsNullOrWhiteSpace(term))
                {
                    q = q.Where(w =>
                        (w.Name ?? "").ToLower().Contains(term) ||
                        (w.Code ?? "").ToLower().Contains(term) ||
                        (w.City ?? "").ToLower().Contains(term) ||
                        (w.Phone ?? "").ToLower().Contains(term) ||
                        (w.Note ?? "").ToLower().Contains(term));
                }

                var rows = q.OrderByDescending(w => w.IsActive)
                            .ThenBy(w => w.Name)
                            .Take(1000)
                            .ToList();

                WarehousesGrid.ItemsSource = rows;

                UpdateActionButtons();
                // after items bind, decide if search should show
                UpdateSearchRowVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load warehouses: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Warehouse? Selected() => WarehousesGrid.SelectedItem as Warehouse;

        private void UpdateActionButtons()
        {
            var row = Selected();
            if (row is null)
            {
                EditBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Collapsed;
                DisableBtn.Visibility = Visibility.Collapsed;
                return;
            }

            EditBtn.Visibility = Visibility.Visible;

            if (row.IsActive)
            {
                DisableBtn.Visibility = Visibility.Visible;
                EnableBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                DisableBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Visible;
            }
        }

        // --- Search row visibility: only when vertical scrollbar is visible

        private void UpdateSearchRowVisibility()
        {
            var sv = FindDescendant<ScrollViewer>(WarehousesGrid);
            if (sv == null)
            {
                SearchRow.Visibility = Visibility.Collapsed;
                return;
            }

            // If the grid needs a vertical scrollbar, show search; else hide it
            SearchRow.Visibility = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void WarehousesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSearchRowVisibility();
        }

        private void WarehousesGrid_Loaded(object sender, RoutedEventArgs e)
        {
            // Loaded can fire before ItemsSource; schedule a layout pass
            WarehousesGrid.Dispatcher.InvokeAsync(UpdateSearchRowVisibility);
        }

        // --- Generic visual tree helper
        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var sub = FindDescendant<T>(child);
                if (sub != null) return sub;
            }
            return null;
        }

        // --- Events

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
        }

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            // Only effective when visible; harmless otherwise
            LoadRows();
        }

        private void FilterChanged(object s, RoutedEventArgs e)
        {
            LoadRows();
        }

        private void Grid_MouseDoubleClick(object s, MouseButtonEventArgs e)
        {
            Edit_Click(s, e);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;

            var dlg = _editWarehouseFactory!();
            dlg.Owner = this;
            dlg.EditId = null;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Edit_Click(object? sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row is null) return;

            var dlg = _editWarehouseFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row is null) return;

            if (MessageBox.Show($"Disable warehouse “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            using var db = _dbf!.CreateDbContext();
            var w = db.Warehouses.FirstOrDefault(x => x.Id == row.Id); if (w == null) return;
            w.IsActive = false;
            w.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();

            LoadRows();
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row is null) return;

            using var db = _dbf!.CreateDbContext();
            var w = db.Warehouses.FirstOrDefault(x => x.Id == row.Id); if (w == null) return;
            w.IsActive = true;
            w.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();

            LoadRows();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Edit_Click(sender, e);
        }
    }
}
