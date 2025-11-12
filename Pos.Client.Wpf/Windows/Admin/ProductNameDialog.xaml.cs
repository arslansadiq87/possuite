// Pos.Client.Wpf/Windows/Admin/ProductNameDialog.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Pos.Domain.Entities;
using Pos.Domain.Services;   // ⬅ service layer (no EF in the dialog!)

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class ProductNameDialog : Window
    {
        private readonly ICatalogService _svc =
            App.Services.GetRequiredService<ICatalogService>();

        private string? _primaryImagePath;
        private readonly List<string> _galleryImagePaths = new();

        public string? PrimaryImagePath => _primaryImagePath;
        public IReadOnlyList<string> GalleryImagePaths => _galleryImagePaths;

        private readonly ObservableCollection<string> _thumbs = new();
        public ObservableCollection<string> ThumbPreview => _thumbs;

        private readonly ObservableCollection<Brand> _brands = new();
        private readonly ObservableCollection<Category> _categories = new();
        public bool IsEditMode { get; private set; } = false;
        public ProductNameDialog()
        {
            InitializeComponent();
            DataContext = this;
            Title = "New Product";

            Loaded += async (_, __) =>
            {
                _brands.Clear();
                foreach (var b in await _svc.GetActiveBrandsAsync())
                    _brands.Add(b);
                _categories.Clear();
                foreach (var c in await _svc.GetAllCategoriesAsync())
                    _categories.Add(c);
                BrandCombo.ItemsSource = _brands;
                CategoryCombo.ItemsSource = _categories;
            };
        }

        public string ProductName => NameBox.Text.Trim();

        public int? BrandId =>
            BrandCombo.SelectedValue is int b ? b : (int?)null;

        public int? CategoryId =>
            CategoryCombo.SelectedValue is int c ? c : (int?)null;

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProductName))
            {
                MessageBox.Show("Please enter a product name.");
                return;
            }
            DialogResult = true;
        }

        public void Prefill(string name, int? brandId, int? categoryId)
        {
            IsEditMode = true;

            NameBox.Text = name ?? "";

            void applySelection()
            {
                if (brandId.HasValue) BrandCombo.SelectedValue = brandId.Value; else BrandCombo.SelectedIndex = -1;
                if (categoryId.HasValue) CategoryCombo.SelectedValue = categoryId.Value; else CategoryCombo.SelectedIndex = -1;
            }
            if (BrandCombo.ItemsSource is null || CategoryCombo.ItemsSource is null)
            {
                Loaded += (_, __) => applySelection();
            }
            else applySelection();
            Title = "Edit Product";
            if (BtnSave != null) BtnSave.Content = "Update";
            NameBox.Focus();
            NameBox.SelectAll();
        }

        private void BtnPickPrimary_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose Product Primary Image",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                _primaryImagePath = dlg.FileName;
                if (ThumbPreview.Count == 0) ThumbPreview.Add(_primaryImagePath);
                else ThumbPreview[0] = _primaryImagePath;
            }
        }

        private void BtnPickGallery_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Add Product Gallery Images",
                Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    if (!_galleryImagePaths.Contains(f, StringComparer.OrdinalIgnoreCase))
                        _galleryImagePaths.Add(f);
                ThumbPreview.Clear();
                if (_primaryImagePath != null) ThumbPreview.Add(_primaryImagePath);
                foreach (var g in _galleryImagePaths) ThumbPreview.Add(g);
            }
        }
    }
}