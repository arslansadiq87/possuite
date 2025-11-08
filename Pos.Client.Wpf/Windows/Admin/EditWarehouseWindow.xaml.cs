using System;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Sync;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditWarehouseWindow : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly IOutboxWriter _outbox; // + add

        public int? EditId { get; set; } // null = add, non-null = edit

        public EditWarehouseWindow()
        {
            InitializeComponent();
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            _outbox = App.Services.GetRequiredService<IOutboxWriter>(); // + add
            Loaded += (_, __) => LoadModel();
        }

        private void LoadModel()
        {
            using var db = _dbf.CreateDbContext();
            if (EditId == null)
            {
                // Add mode: prefill code and defaults
                CodeBox.Text = GenerateNextWarehouseCode(db);
                ActiveBox.IsChecked = true;
                return;
            }

            var w = db.Warehouses.AsNoTracking().FirstOrDefault(x => x.Id == EditId.Value);
            if (w == null) { DialogResult = false; Close(); return; }

            CodeBox.Text = w.Code;
            NameBox.Text = w.Name;
            ActiveBox.IsChecked = w.IsActive;
            CityBox.Text = w.City;
            PhoneBox.Text = w.Phone;
            NoteBox.Text = w.Note;
        }

        private static string GenerateNextWarehouseCode(PosClientDbContext db)
        {
            const string prefix = "WH-";
            var max = db.Warehouses
                .AsNoTracking()
                .Select(w => w.Code)
                .Where(c => c != null && c.StartsWith(prefix))
                .AsEnumerable()
                .Select(c => int.TryParse(c!.Substring(prefix.Length), out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max();
            return $"{prefix}{(max + 1):D3}";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var code = (CodeBox.Text ?? "").Trim().ToUpperInvariant();
            var name = (NameBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(code)) { MessageBox.Show("Code is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); CodeBox.Focus(); return; }
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); NameBox.Focus(); return; }

            using var db = _dbf.CreateDbContext();
            Warehouse w;

            // Uniqueness
            var codeTaken = db.Warehouses.AsNoTracking()
                .Any(x => x.Code == code && (EditId == null || x.Id != EditId.Value));
            if (codeTaken) { MessageBox.Show("This Code already exists. Please choose a different code.", "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning); CodeBox.Focus(); return; }

            if (EditId == null)
            {
                w = new Warehouse
                {
                    Code = code,
                    Name = name,
                    IsActive = ActiveBox.IsChecked == true,
                    City = string.IsNullOrWhiteSpace(CityBox.Text) ? null : CityBox.Text.Trim(),
                    Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim(),
                    Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim(),
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Warehouses.Add(w);
            }
            else
            {
                w = db.Warehouses.FirstOrDefault(x => x.Id == EditId.Value)!;
                if (w == null) { DialogResult = false; Close(); return; }

                w.Code = code;
                w.Name = name;
                w.IsActive = ActiveBox.IsChecked == true;
                w.City = string.IsNullOrWhiteSpace(CityBox.Text) ? null : CityBox.Text.Trim();
                w.Phone = string.IsNullOrWhiteSpace(PhoneBox.Text) ? null : PhoneBox.Text.Trim();
                w.Note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();
                w.UpdatedAtUtc = DateTime.UtcNow;
                db.Warehouses.Update(w);
            }

            try
            {
                // 1) persist entity changes (ensures Id for new rows)
                await db.SaveChangesAsync();

                // 2) enqueue upsert for sync
                await _outbox.EnqueueUpsertAsync(db, w);

                // 3) persist outbox record
                await db.SaveChangesAsync();

                DialogResult = true;
                Close();
            }
            catch (DbUpdateException ex)
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
