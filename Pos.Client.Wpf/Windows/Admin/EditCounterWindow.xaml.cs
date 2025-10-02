using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Common; // <- ensure this using exists

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditCounterWindow
    {
        private enum Mode { Create, Edit }

        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly Mode _mode;
        private readonly int _outletId; // fixed when creating; for edit we read from DB

        public sealed class Vm : INotifyPropertyChanged
        {
            private int _id;
            private string _name = "";
            private bool _isActive = true;

            public int Id { get => _id; set { _id = value; OnPropertyChanged(); } }
            public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
            public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? p = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public Vm VM { get; } = new();
        public int SavedCounterId { get; private set; }

        // CREATE
        public EditCounterWindow(IDbContextFactory<PosClientDbContext> dbf, int outletId)
        {
            InitializeComponent();
            _dbf = dbf;
            _mode = Mode.Create;
            _outletId = outletId;

            DataContext = VM;
            Title = "Add Counter";
            VM.IsActive = true;
        }

        // EDIT
        public EditCounterWindow(IDbContextFactory<PosClientDbContext> dbf, int counterId, bool load = true)
        {
            InitializeComponent();
            _dbf = dbf;
            _mode = Mode.Edit;

            DataContext = VM;
            Title = "Edit Counter";

            if (load)
            {
                try
                {
                    using var db = _dbf.CreateDbContext();
                    var c = db.Counters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == counterId).Result;
                    if (c == null)
                    {
                        MessageBox.Show("Counter not found.", "Counters",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        Close();
                        return;
                    }
                    _outletId = c.OutletId; // lock to the same outlet for this editor
                    VM.Id = c.Id;
                    VM.Name = c.Name;
                    VM.IsActive = c.IsActive;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to load counter:\n\n" + ex.Message, "Counters",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                }
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = (VM.Name ?? "").Trim();
            if (name.Length == 0) { MessageBox.Show("Name is required."); return; }
            if (name.Length > 80) { MessageBox.Show("Name must be ≤ 80 characters."); return; }

            try
            {
                await using var db = await _dbf.CreateDbContextAsync();

                // Optional: uniqueness per-outlet (case-insensitive)
                var exists = await db.Counters.AnyAsync(c =>
                    c.OutletId == _outletId &&
                    c.Id != VM.Id &&
                    c.Name.ToLower() == name.ToLower());

                if (exists)
                {
                    MessageBox.Show("Another counter with this name already exists in this outlet.",
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_mode == Mode.Create)
                {
                    var entity = new Counter
                    {
                        OutletId = _outletId,
                        Name = name,
                        IsActive = VM.IsActive
                    };
                    db.Counters.Add(entity);
                    await db.SaveChangesAsync();
                    SavedCounterId = entity.Id;
                }
                else
                {
                    var entity = await db.Counters.FirstOrDefaultAsync(c => c.Id == VM.Id);
                    if (entity == null)
                    {
                        MessageBox.Show("Counter not found.", "Counters",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    entity.Name = name;
                    entity.IsActive = VM.IsActive;

                    await db.SaveChangesAsync();
                    SavedCounterId = entity.Id;
                }

                DialogResult = true; // closes
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
