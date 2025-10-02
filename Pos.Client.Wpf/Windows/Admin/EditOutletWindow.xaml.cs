using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditOutletWindow : Window
    {
        private enum Mode { Create, Edit }

        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly Mode _mode;

        public sealed class Vm : INotifyPropertyChanged
        {
            private int _id;
            private string _code = "";
            private string _name = "";
            private string? _address;
            private bool _isActive = true;

            public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
            public string Code { get => _code; set { _code = value; OnPropertyChanged(); } }
            public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
            public string? Address { get => _address; set { _address = value; OnPropertyChanged(); } }
            public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? prop = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public Vm VM { get; } = new();

        /// <summary>
        /// The id saved/edited by this dialog. 0 if not saved.
        /// </summary>
        public int SavedOutletId { get; private set; }

        /// <summary>
        /// Create mode (new outlet)
        /// </summary>
        public EditOutletWindow(IDbContextFactory<PosClientDbContext> dbf)
        {
            InitializeComponent();
            _dbf = dbf;
            _mode = Mode.Create;
            DataContext = VM;
            Title = "Add Outlet";
            VM.IsActive = true;
        }

        /// <summary>
        /// Edit mode (existing outlet)
        /// </summary>
        public EditOutletWindow(IDbContextFactory<PosClientDbContext> dbf, int outletId)
        {
            InitializeComponent();
            _dbf = dbf;
            _mode = Mode.Edit;
            DataContext = VM;
            Title = "Edit Outlet";

            try
            {
                using var db = _dbf.CreateDbContext();
                var o = db.Outlets.AsNoTracking().FirstOrDefault(x => x.Id == outletId);
                if (o == null)
                {
                    MessageBox.Show("Outlet not found.", "Outlets",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                VM.Id = o.Id;
                VM.Code = o.Code;
                VM.Name = o.Name;
                VM.Address = o.Address;
                VM.IsActive = o.IsActive;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        // ---------------- Save / Cancel ----------------

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            // Basic validation (mirrors your XAML MaxLength but also guards back-end)
            var code = (VM.Code ?? "").Trim();
            var name = (VM.Name ?? "").Trim();
            var address = string.IsNullOrWhiteSpace(VM.Address) ? null : VM.Address!.Trim();

            if (code.Length == 0) { MessageBox.Show("Code is required."); return; }
            if (name.Length == 0) { MessageBox.Show("Name is required."); return; }
            if (code.Length > 16) { MessageBox.Show("Code must be ≤ 16 characters."); return; }
            if (name.Length > 80) { MessageBox.Show("Name must be ≤ 80 characters."); return; }

            try
            {
                await using var db = await _dbf.CreateDbContextAsync();

                // Unique Code check (case-insensitive). Adjust if you want per-tenant rules, etc.
                var codeExists = await db.Outlets
                    .AnyAsync(o => o.Id != VM.Id &&
                                   o.Code.ToLower() == code.ToLower());

                if (codeExists)
                {
                    MessageBox.Show("Another outlet already uses this Code. Choose a different one.",
                        "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_mode == Mode.Create)
                {
                    var entity = new Outlet
                    {
                        Code = code,
                        Name = name,
                        Address = address,
                        IsActive = VM.IsActive
                    };
                    db.Outlets.Add(entity);
                    await db.SaveChangesAsync();

                    SavedOutletId = entity.Id;
                }
                else // Edit
                {
                    var entity = await db.Outlets.FirstOrDefaultAsync(o => o.Id == VM.Id);
                    if (entity == null)
                    {
                        MessageBox.Show("Outlet not found.", "Outlets",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    entity.Code = code;
                    entity.Name = name;
                    entity.Address = address;
                    entity.IsActive = VM.IsActive;

                    await db.SaveChangesAsync();
                    SavedOutletId = entity.Id;
                }

                DialogResult = true; // closes the window
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
