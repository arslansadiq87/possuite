using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Domain.Services;   // IWarehouseService

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditWarehouseWindow : Window
    {
        private readonly IWarehouseService _svc;

        public int? EditId { get; set; } // null = add, non-null = edit

        // Prefer ctor injection; this window is already registered in DI as Transient
        public EditWarehouseWindow()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<IWarehouseService>();
            Loaded += async (_, __) => await LoadModelAsync();
        }

        private async Task LoadModelAsync()
        {
            if (EditId == null)
            {
                CodeBox.Text = await GenerateNextWarehouseCodeAsync();
                ActiveBox.IsChecked = true;
                return;
            }

            var w = await _svc.GetWarehouseAsync(EditId.Value);
            if (w == null) { DialogResult = false; Close(); return; }

            CodeBox.Text = w.Code;
            NameBox.Text = w.Name;
            ActiveBox.IsChecked = w.IsActive;
            CityBox.Text = w.City;
            PhoneBox.Text = w.Phone;
            NoteBox.Text = w.Note;
        }

        // No EF here: ask the service for data and compute next code in UI.
        // (If you expect very large row counts, consider adding a SuggestNextCodeAsync() to IWarehouseService later.)
        private async Task<string> GenerateNextWarehouseCodeAsync()
        {
            const string prefix = "WH-";
            var list = await _svc.SearchAsync(term: null, showInactive: true, take: 10_000);

            var max = list
                .Select(w => w.Code)
                .Where(c => !string.IsNullOrWhiteSpace(c) && c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(c =>
                {
                    var tail = c!.Substring(prefix.Length);
                    return int.TryParse(tail, out var n) ? n : 0;
                })
                .DefaultIfEmpty(0)
                .Max();

            return $"{prefix}{(max + 1):D3}";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var code = (CodeBox.Text ?? "").Trim().ToUpperInvariant();
            var name = (NameBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Code is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                CodeBox.Focus(); return;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                NameBox.Focus(); return;
            }

            // Build the DTO/entity and let the service enforce uniqueness + upsert
            var w = new Warehouse
            {
                Id = EditId ?? 0,
                Code = code,
                Name = name,
                IsActive = ActiveBox.IsChecked == true,
                City = string.IsNullOrWhiteSpace(CityBox.Text) ? null : CityBox.Text.Trim(),
                Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim(),
                // CreatedAtUtc / UpdatedAtUtc are handled in the service
            };

            try
            {
                await _svc.SaveWarehouseAsync(w);
                DialogResult = true;
                Close();
            }
            catch (InvalidOperationException ex)
            {
                // e.g., duplicate code error thrown by service
                MessageBox.Show(ex.Message, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape) Close();
            else if (e.Key == System.Windows.Input.Key.Enter) Save_Click(sender, e);
        }
    }
}
