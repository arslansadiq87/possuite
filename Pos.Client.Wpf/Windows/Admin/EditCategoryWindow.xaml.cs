using System;
using System.ComponentModel;                 // DesignerProperties
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditCategoryWindow : Window
    {
        private IDbContextFactory<PosClientDbContext>? _dbf;
        private readonly bool _design;

        // set by caller before ShowDialog()
        public int? EditId { get; set; }

        public EditCategoryWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            Loaded += (_, __) => LoadOrInit();
        }

        private bool Ready => !_design && _dbf != null;

        private void LoadOrInit()
        {
            if (!Ready || EditId is null) return;

            using var db = _dbf!.CreateDbContext();
            var row = db.Categories.AsNoTracking().FirstOrDefault(c => c.Id == EditId.Value);
            if (row is null) { DialogResult = false; Close(); return; }

            NameBox.Text = row.Name;
            IsActiveBox.IsChecked = row.IsActive;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Ready) return;

            var name = (NameBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Name is required.");
                return;
            }

            using var db = _dbf!.CreateDbContext();
            var exists = db.Categories.Any(c => c.Name.ToLower() == name.ToLower() && c.Id != (EditId ?? 0));
            if (exists)
            {
                MessageBox.Show("A category with this name already exists.");
                return;
            }

            if (EditId is null)
            {
                var now = DateTime.UtcNow;
                db.Categories.Add(new Category
                {
                    Name = name,
                    IsActive = IsActiveBox.IsChecked ?? true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                var row = db.Categories.First(c => c.Id == EditId.Value);
                row.Name = name;
                row.IsActive = IsActiveBox.IsChecked ?? true;
                row.UpdatedAtUtc = DateTime.UtcNow;
            }

            db.SaveChanges();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object s, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
