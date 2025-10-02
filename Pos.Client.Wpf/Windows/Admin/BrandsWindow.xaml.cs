using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
            if (_design) return;

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _editBrandFactory = () => App.Services.GetRequiredService<EditBrandWindow>();

            Loaded += (_, __) => LoadRows();
            SizeChanged += (_, __) => UpdateSearchVisibilitySoon();
        }

        private bool Ready => !_design && _dbf != null;

        // Row view model for the list
        private sealed class BrandRow
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
                var brands = db.Brands.AsNoTracking();

                if (ShowInactive.IsChecked != true)
                    brands = brands.Where(b => b.IsActive);

                if (!string.IsNullOrWhiteSpace(term))
                    brands = brands.Where(b => b.Name.ToLower().Contains(term));

                // Pre-calc item counts by brand (Items.BrandId)
                // Adjust "Items" & "BrandId" names if your DbSet/property differ
                // Pre-calc item counts by brand (Items.BrandId)
                // Adjust "Items" & "BrandId" names if your DbSet/property differ
                // Filters
                // --- filters ---
                var products = db.Products.AsNoTracking()
                    .Where(p => p.IsActive && !p.IsVoided && p.BrandId != null);

                var items = db.Items.AsNoTracking()
                    .Where(i => i.IsActive && !i.IsVoided);

                // 1) products WITH variants → count variants per product, then sum per brand
                var productsWithVariantsCounts =
                    items.Where(i => i.ProductId != null)
                         .GroupBy(i => i.ProductId!.Value)
                         .Select(g => new { ProductId = g.Key, VariantCount = g.Count() })
                         .Join(products,
                               pvc => pvc.ProductId,
                               p => p.Id,
                               (pvc, p) => new { BrandId = p.BrandId!.Value, Count = pvc.VariantCount })
                         .GroupBy(x => x.BrandId)
                         .Select(g => new { BrandId = g.Key, Count = g.Sum(x => x.Count) });

                // 2) products WITHOUT variants → each product counts as 1, sum per brand
                var productsWithoutVariantsCounts =
                    products
                        .Where(p => !items.Any(i => i.ProductId == p.Id))   // no variants
                        .GroupBy(p => p.BrandId!.Value)
                        .Select(g => new { BrandId = g.Key, Count = g.Count() });

                // 3) standalone items (no parent product) → each counts as 1, sum per brand
                var standaloneItemCounts =
                    items.Where(i => i.ProductId == null && i.BrandId != null)
                         .GroupBy(i => i.BrandId!.Value)
                         .Select(g => new { BrandId = g.Key, Count = g.Count() });

                // combine all three
                var skuCountsByBrand =
                    productsWithVariantsCounts
                        .Concat(productsWithoutVariantsCounts)
                        .Concat(standaloneItemCounts)
                        .GroupBy(x => x.BrandId)
                        .Select(g => new { BrandId = g.Key, Count = g.Sum(x => x.Count) });

                // build rows
                var rows =
                    brands
                        .GroupJoin(skuCountsByBrand,
                                   b => b.Id,
                                   sc => sc.BrandId,
                                   (b, sc) => new { b, sc = sc.FirstOrDefault() })
                        .Select(x => new BrandRow
                        {
                            Id = x.b.Id,
                            Name = x.b.Name,
                            IsActive = x.b.IsActive,
                            ItemCount = x.sc != null ? x.sc.Count : 0,
                            CreatedAtUtc = x.b.CreatedAtUtc,
                            UpdatedAtUtc = x.b.UpdatedAtUtc
                        })
                        .OrderBy(r => r.Name)
                        .Take(2000)
                        .ToList();



                BrandsList.ItemsSource = rows;

                UpdateActionButtons();
                // Check whether list scrolls; if it does, show search
                UpdateSearchVisibilitySoon();

                System.Diagnostics.Debug.WriteLine($"[BrandsWindow] rows={rows.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load brands: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        private BrandRow? Selected() => BrandsList.SelectedItem as BrandRow;

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
            b.UpdatedAtUtc = DateTime.UtcNow;
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
            b.UpdatedAtUtc = DateTime.UtcNow;
            db.SaveChanges();

            LoadRows();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Edit_Click(sender, e);
        }

        // --- Search visibility (only when scrolling) -------------------------

        private void UpdateSearchVisibilitySoon()
        {
            // Defer till layout completes so ScrollViewer has correct size
            Dispatcher.InvokeAsync(UpdateSearchVisibility);
        }

        private void UpdateSearchVisibility()
        {
            var sv = FindDescendant<ScrollViewer>(BrandsList);
            if (sv == null)
            {
                SearchPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // If vertical scroll bar is visible OR content is taller than viewport → show search
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
