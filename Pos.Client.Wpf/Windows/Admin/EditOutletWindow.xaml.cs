using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class EditOutletWindow : Window
    {
        private enum Mode { Create, Edit }

        private readonly OutletCounterService _svc;
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
        public int SavedOutletId { get; private set; }

        // CREATE
        public EditOutletWindow()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<OutletCounterService>();
            _mode = Mode.Create;
            DataContext = VM;
            Title = "Add Outlet";
            VM.IsActive = true;
        }

        // EDIT
        public EditOutletWindow(int outletId)
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<OutletCounterService>();
            _mode = Mode.Edit;
            DataContext = VM;
            Title = "Edit Outlet";
            _ = LoadAsync(outletId);
        }

        private async Task LoadAsync(int outletId)
        {
            try
            {
                var o = await _svc.GetOutletAsync(outletId);
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

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var code = (VM.Code ?? "").Trim();
            var name = (VM.Name ?? "").Trim();
            var address = string.IsNullOrWhiteSpace(VM.Address) ? null : VM.Address!.Trim();

            if (code.Length == 0) { MessageBox.Show("Code is required."); return; }
            if (name.Length == 0) { MessageBox.Show("Name is required."); return; }
            if (code.Length > 16) { MessageBox.Show("Code must be ≤ 16 characters."); return; }
            if (name.Length > 80) { MessageBox.Show("Name must be ≤ 80 characters."); return; }

            try
            {
                // uniqueness
                var taken = await _svc.IsOutletCodeTakenAsync(code, excludingId: _mode == Mode.Edit ? VM.Id : (int?)null);
                if (taken)
                {
                    MessageBox.Show("Another outlet already uses this Code. Choose a different one.",
                        "Duplicate Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var entity = new Outlet
                {
                    Id = _mode == Mode.Edit ? VM.Id : 0,
                    Code = code,
                    Name = name,
                    Address = address,
                    IsActive = VM.IsActive
                };

                // one call handles create vs update + outbox
                var savedId = await _svc.AddOrUpdateOutletAsync(entity);
                SavedOutletId = savedId;

                // also enqueue a fresh upsert read to guarantee payload has latest fields
                await _svc.UpsertOutletByIdAsync(savedId);

                DialogResult = true; // closes
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save outlet:\n\n" + ex.Message, "Outlets",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
