using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Persistence.Services;   // CategoryService
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditCategoryWindow : Window
    {
        private readonly bool _design;
        private CategoryService? _svc;

        // set by caller before ShowDialog()
        public int? EditId { get; set; }

        public EditCategoryWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<CategoryService>();
            Loaded += async (_, __) => await LoadOrInitAsync();
        }

        private async Task LoadOrInitAsync()
        {
            if (_design || _svc == null) return;
            if (EditId is null) return; // new category

            var row = await _svc.GetCategoryAsync(EditId.Value);
            if (row is null) { DialogResult = false; Close(); return; }

            NameBox.Text = row.Name;
            IsActiveBox.IsChecked = row.IsActive;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_svc == null) return;

            var name = (NameBox.Text ?? "").Trim();
            var active = IsActiveBox.IsChecked ?? true;

            try
            {
                await _svc.SaveCategoryAsync(EditId, name, active);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object s, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
