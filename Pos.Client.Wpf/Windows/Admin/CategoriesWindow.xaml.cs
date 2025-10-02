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

            // Optional: include inactive by default (match Brands behavior if desired)
            // ShowInactive.IsChecked = true;

            Loaded += (_, __) => LoadRows();
            SizeChanged += (_, __) => UpdateSearchVisibilitySoon();
        }

        private bool Ready => !_design && _dbf != null;

        private sealed class CategoryRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public int ItemCount { get; set; }
            public DateTime? CreatedAtUtc { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
        }

        private void LoadRows()
        {
            if (!Ready) return;

            try
            {
                using var db = _dbf!.CreateDbContext();

                var term = (SearchBox.Text ?? "").Trim().ToLower();
                var cats = db.Categories.AsNoTracking();

                if (ShowInactive.IsChecked != true)
                    cats = cats.Where(c => c.IsActive);

                if (!string.IsNullOrWhiteSpace(term))
                    cats = cats.Where(c => c.Name.ToLower().Contains(term));

                // ---------- COUNT SKUs PER CATEGORY ----------
                // Filters
                var products = db.Products.AsNoTracking()
                    .Where(p => p.IsActive && !p.IsVoided && p.CategoryId != null);

                var items = db.Items.AsNoTracking()
                    .Where(i => i.IsActive && !i.IsVoided);

                // 1) Products WITH variants → count variants per product, then sum per product.CategoryId
                var prodWithVarCounts =
                    items.Where(i => i.ProductId != null)
                         .GroupBy(i => i.ProductId!.Value)
                         .Select(g => new { ProductId = g.Key, VariantCount = g.Count() })
                         .Join(products,
                               pvc => pvc.ProductId,
                               p => p.Id,
                               (pvc, p) => new { CategoryId = p.CategoryId!.Value, Count = pvc.VariantCount })
                         .GroupBy(x => x.CategoryId)
                         .Select(g => new { CategoryId = g.Key, Count = g.Sum(x => x.Count) });

                // 2) Products WITHOUT variants → each product counts as 1, sum per product.CategoryId
                var prodNoVarCounts =
                    products
                        .Where(p => !items.Any(i => i.ProductId == p.Id))
                        .GroupBy(p => p.CategoryId!.Value)
                        .Select(g => new { CategoryId = g.Key, Count = g.Count() });

                // 3) Standalone items (no parent product) → each counts as 1, sum per item.CategoryId
                var standaloneItemCounts =
                    items.Where(i => i.ProductId == null && i.CategoryId != null)
                         .GroupBy(i => i.CategoryId!.Value)
                         .Select(g => new { CategoryId = g.Key, Count = g.Count() });

                // Combine all sources
                var skuCountsByCategory =
                    prodWithVarCounts
                        .Concat(prodNoVarCounts)
                        .Concat(standaloneItemCounts)
                        .GroupBy(x => x.CategoryId)
                        .Select(g => new { CategoryId = g.Key, Count = g.Sum(x => x.Count) });

                var rows = cats
                    .GroupJoin(skuCountsByCategory,
                               c => c.Id,
                               sc => sc.CategoryId,
                               (c, sc) => new { c, sc = sc.FirstOrDefault() })
                    .Select(x => new CategoryRow
                    {
                        Id = x.c.Id,
                        Name = x.c.Name,
                        IsActive = x.c.IsActive,
                        ItemCount = x.sc != null ? x.sc.Count : 0,
                        CreatedAtUtc = x.c.CreatedAtUtc,
                        UpdatedAtUtc = x.c.UpdatedAtUtc
                    })
                    .OrderBy(r => r.Name)
                    .Take(2000)
                    .ToList();

                CategoriesList.ItemsSource = rows;

                UpdateActionButtons();
                UpdateSearchVisibilitySoon();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load categories: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private CategoryRow? Selected() => CategoriesList.SelectedItem as CategoryRow;

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

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Ready) UpdateActionButtons();
        }

        private void List_MouseDoubleClick(object s, MouseButtonEventArgs e)
        {
            if (Ready) Edit_Click(s, e);
        }

        private void SearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            if (Ready) LoadRows();
        }

        private void FilterChanged(object s, RoutedEventArgs e)
        {
            if (Ready) LoadRows();
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
            c.UpdatedAtUtc = DateTime.UtcNow;
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
            c.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();

            LoadRows();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Edit_Click(sender, e);
        }

        // ---------- Search visibility (only when scrolling) ----------
        private void UpdateSearchVisibilitySoon()
        {
            Dispatcher.InvokeAsync(UpdateSearchVisibility);
        }

        private void UpdateSearchVisibility()
        {
            var sv = FindDescendant<ScrollViewer>(CategoriesList);
            if (sv == null)
            {
                SearchPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var needsSearch = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible
                              || sv.ScrollableHeight > 0;

            SearchPanel.Visibility = needsSearch ? Visibility.Visible : Visibility.Collapsed;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var d = FindDescendant<T>(child);
                if (d != null) return d;
            }
            return null;
        }
    }
}
