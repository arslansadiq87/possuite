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
    public partial class BrandsWindow : Window
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private Func<EditBrandWindow>? _editBrandFactory;
        private readonly bool _design;

        public BrandsWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return; // allow XAML designer to load without services

            // resolve here just like your ProductsItemsWindow
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _editBrandFactory = () => App.Services.GetRequiredService<EditBrandWindow>();
            //ShowInactive.IsChecked = true; // <- include inactive on first open
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
                var q = db.Brands.AsNoTracking();

                // Include inactive only when the checkbox is checked
                if (ShowInactive.IsChecked != true)
                    q = q.Where(b => b.IsActive);

                if (!string.IsNullOrWhiteSpace(term))
                    q = q.Where(b => b.Name.ToLower().Contains(term));

                var rows = q.OrderBy(b => b.Name).Take(1000).ToList();

                // (optional) quick visibility check in Output window
                System.Diagnostics.Debug.WriteLine($"[BrandsWindow] rows={rows.Count} " +
                    $"conn={db.Database.GetDbConnection().Database}");

                BrandsGrid.ItemsSource = rows;
                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load brands: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ready) UpdateActionButtons();
        }

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            if (Ready) LoadRows(); // LoadRows() will call UpdateActionButtons()
        }

        private void FilterChanged(object s, RoutedEventArgs e)
        {
            if (Ready) LoadRows(); // LoadRows() will call UpdateActionButtons()
        }


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

            // Always allow Edit when a row is selected
            EditBtn.Visibility = Visibility.Visible;

            // Show exactly one of Enable/Disable based on IsActive
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


        private Brand? Selected() => BrandsGrid.SelectedItem as Brand;


        //private void SearchBox_TextChanged(object s, TextChangedEventArgs e) { if (Ready) LoadRows(); }
        //private void FilterChanged(object s, RoutedEventArgs e) { if (Ready) LoadRows(); }
        private void Grid_MouseDoubleClick(object s, MouseButtonEventArgs e) { if (Ready) Edit_Click(s, e); }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var dlg = _editBrandFactory!();
            dlg.Owner = this;
            dlg.EditId = null;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Edit_Click(object? sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            var dlg = _editBrandFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            if (MessageBox.Show($"Disable brand “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            using var db = _dbf!.CreateDbContext();
            var b = db.Brands.FirstOrDefault(x => x.Id == row.Id); if (b == null) return;
            b.IsActive = false;
            b.UpdatedAtUtc = DateTime.UtcNow;   // or UpdatedAt if that’s your column
            db.SaveChanges();

            LoadRows();
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            using var db = _dbf!.CreateDbContext();
            var b = db.Brands.FirstOrDefault(x => x.Id == row.Id); if (b == null) return;
            b.IsActive = true;
            b.UpdatedAtUtc = DateTime.UtcNow;   // or UpdatedAt
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
