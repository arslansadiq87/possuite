using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Domain.Services;


namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditCounterWindow : Window
    {
        private enum Mode { Create, Edit }

        private readonly IOutletCounterService _svc;
        private readonly Mode _mode;

        private int _fixedOutletId;
        // set in create; resolved from entity in edit
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
        public EditCounterWindow(int outletId)
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<IOutletCounterService>();
            _mode = Mode.Create;
            _fixedOutletId = outletId;

            DataContext = VM;
            Title = "Add Counter";
            VM.IsActive = true;
        }

        // EDIT
        public EditCounterWindow(int counterId, bool load = true)
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<IOutletCounterService>();
            _mode = Mode.Edit;

            DataContext = VM;
            Title = "Edit Counter";
            if (load)
                _ = LoadAsync(counterId);
        }

        private async Task LoadAsync(int counterId)
        {
            try
            {
                var c = await _svc.GetCounterAsync(counterId);
                if (c == null)
                {
                    MessageBox.Show("Counter not found.", "Counters",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }
                // Lock this editor to the same outlet
                _fixedOutletId = c.OutletId;
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

        private async void Save_Click(object? sender, RoutedEventArgs e)
        {
            var name = (VM.Name ?? "").Trim();
            if (name.Length == 0) { MessageBox.Show("Name is required."); return; }
            if (name.Length > 80) { MessageBox.Show("Name must be ≤ 80 characters."); return; }

            try
            {
                // uniqueness per-outlet
                var taken = await _svc.IsCounterNameTakenAsync(_fixedOutletId, name, excludingId: _mode == Mode.Edit ? VM.Id : (int?)null);
                if (taken)
                {
                    MessageBox.Show("Another counter with this name already exists in this outlet.",
                        "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var entity = new Counter
                {
                    Id = _mode == Mode.Edit ? VM.Id : 0,
                    OutletId = _fixedOutletId,
                    Name = name,
                    IsActive = VM.IsActive
                };

                var savedId = await _svc.AddOrUpdateCounterAsync(entity);
                SavedCounterId = savedId;

                // ensure an upsert is enqueued with the latest snapshot
                await _svc.UpsertCounterByIdAsync(savedId);

                DialogResult = true; // close the dialog
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save counter:\n\n" + ex.Message, "Counters",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
