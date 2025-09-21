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
    public partial class CategoriesWindow : Window
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private Func<EditCategoryWindow>? _editCategoryFactory;
        private readonly bool _design;

        public CategoriesWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _editCategoryFactory = () => App.Services.GetRequiredService<EditCategoryWindow>();

            ShowInactive.IsChecked = true; // include inactive on first open
            Loaded += (_, __) => LoadRows();
        }

        private bool Ready => !_design && _dbf != null;

        private void LoadRows()
        {
            if (!Ready) return;
            using var db = _dbf!.CreateDbContext();

            var term = (SearchBox.Text ?? "").Trim().ToLower();
            var q = db.Categories.AsNoTracking();

            if (ShowInactive.IsChecked != true)
                q = q.Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(term))
                q = q.Where(c => c.Name.ToLower().Contains(term));

            var rows = q.OrderBy(c => c.Name).Take(1000).ToList();

            CategoriesGrid.ItemsSource = rows;
            UpdateActionButtons(); // <-- refresh toolbar buttons
        }

        private Category? Selected() => CategoriesGrid.SelectedItem as Category;

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

            // Edit is visible when something is selected
            EditBtn.Visibility = Visibility.Visible;

            // Show exactly one of Enable/Disable
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

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e) { if (Ready) LoadRows(); }
        private void FilterChanged(object s, RoutedEventArgs e) { if (Ready) LoadRows(); }
        private void Grid_MouseDoubleClick(object s, MouseButtonEventArgs e) { if (Ready) Edit_Click(s, e); }

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ready) UpdateActionButtons();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var dlg = _editCategoryFactory!();
            dlg.Owner = this;
            dlg.EditId = null;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Edit_Click(object? sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            var dlg = _editCategoryFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true) LoadRows();
        }

        private void Disable_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            if (MessageBox.Show($"Disable category “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            using var db = _dbf!.CreateDbContext();
            var c = db.Categories.FirstOrDefault(x => x.Id == row.Id); if (c == null) return;
            c.IsActive = false;
            c.UpdatedAtUtc = DateTime.UtcNow;   // <-- use UpdatedAt if that's your column
            db.SaveChanges();

            LoadRows();
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;
            var row = Selected(); if (row is null) return;

            using var db = _dbf!.CreateDbContext();
            var c = db.Categories.FirstOrDefault(x => x.Id == row.Id); if (c == null) return;
            c.IsActive = true;
            c.UpdatedAtUtc = DateTime.UtcNow;   // <-- use UpdatedAt if that's your column
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
