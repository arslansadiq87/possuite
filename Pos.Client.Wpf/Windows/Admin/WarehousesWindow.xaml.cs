using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            if (_design) return; // allow XAML designer to load without services

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            Loaded += (_, __) => LoadRows();

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
                    q = q.Where(w =>
                        w.Name.ToLower().Contains(term) ||
                        (w.Code ?? "").ToLower().Contains(term) ||
                        (w.City ?? "").ToLower().Contains(term) ||
                        (w.Phone ?? "").ToLower().Contains(term));

                var rows = q.OrderByDescending(w => w.IsActive)
                            .ThenBy(w => w.Name)
                            .Take(1000)
                            .ToList();

                System.Diagnostics.Debug.WriteLine($"[WarehousesWindow] rows={rows.Count} db={db.Database.GetDbConnection().Database}");

                WarehousesGrid.ItemsSource = rows;
                UpdateActionButtons();
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
            if (!Ready)
            {
                EditBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Collapsed;
                DisableBtn.Visibility = Visibility.Collapsed;
                return;
            }

            var row = Selected();
            if (row is null)
            {
                EditBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Collapsed;
                DisableBtn.Visibility = Visibility.Collapsed;
                return;
            }

            // Inline editing is always allowed; keep Edit visible
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

        // --- Events

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ready) UpdateActionButtons();
        }

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            if (Ready) LoadRows();
        }

        private void FilterChanged(object s, RoutedEventArgs e)
        {
            if (Ready) LoadRows();
        }

        private void Grid_MouseDoubleClick(object s, MouseButtonEventArgs e)
        {
            if (Ready) Edit_Click(s, e);
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
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            var dlg = _editWarehouseFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
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
            if (!Ready) return;
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
