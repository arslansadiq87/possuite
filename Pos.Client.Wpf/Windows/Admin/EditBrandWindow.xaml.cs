using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Persistence.Services;   // BrandService
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditBrandWindow : Window
    {
        private readonly bool _design;
        private BrandService? _svc;

        public int? EditId { get; set; }

        public EditBrandWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<BrandService>();
            Loaded += async (_, __) => await LoadOrInitAsync();
        }

        private async Task LoadOrInitAsync()
        {
            if (_design || _svc == null) return;

            if (EditId is null) return; // creating new

            var row = await _svc.GetBrandAsync(EditId.Value);
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
                await _svc.SaveBrandAsync(EditId, name, active);
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
