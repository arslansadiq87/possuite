using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ProductNameDialog : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf =
            App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();

        private readonly ObservableCollection<Brand> _brands = new();
        private readonly ObservableCollection<Category> _categories = new();

        public ProductNameDialog()
        {
            InitializeComponent();
            Loaded += async (_, __) =>
            {
                await using var db = await _dbf.CreateDbContextAsync();

                _brands.Clear();
                foreach (var b in await db.Brands.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync())
                    _brands.Add(b);

                _categories.Clear();
                foreach (var c in await db.Categories.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync())
                    _categories.Add(c);

                BrandCombo.ItemsSource = _brands;
                CategoryCombo.ItemsSource = _categories;
            };
        }

        // Expose values to caller
        public string ProductName => NameBox.Text.Trim();

        public int? BrandId
        {
            get
            {
                // SelectedValue is object; cast safely to int?
                if (BrandCombo.SelectedValue is int id) return id;
                return null;
            }
        }

        public int? CategoryId
        {
            get
            {
                if (CategoryCombo.SelectedValue is int id) return id;
                return null;
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProductName))
            {
                MessageBox.Show("Please enter a product name.");
                return;
            }
            DialogResult = true;
        }

        // Called by the Products window when editing an existing product
        public void Prefill(string name, int? brandId, int? categoryId)
        {
            // Fill name
            NameBox.Text = name ?? "";

            // These lines rely on SelectedValuePath="Id" in XAML
            if (brandId.HasValue)
                BrandCombo.SelectedValue = brandId.Value;
            else
                BrandCombo.SelectedIndex = -1;

            if (categoryId.HasValue)
                CategoryCombo.SelectedValue = categoryId.Value;
            else
                CategoryCombo.SelectedIndex = -1;

            // Optional UX: focus the name field and select text
            NameBox.Focus();
            NameBox.SelectAll();
        }

    }
}
