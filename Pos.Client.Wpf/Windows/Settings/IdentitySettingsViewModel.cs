// Pos.Client.Wpf/Windows/Settings/IdentitySettingsViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Client.Wpf.Security;
using Pos.Client.Wpf.Services;
using System.Windows;


// Pos.Client.Wpf/Windows/Settings/IdentitySettingsViewModel.cs
namespace Pos.Client.Wpf.Windows.Settings;

public partial class IdentitySettingsViewModel : ObservableObject
{
    private readonly IIdentitySettingsService _svc;
    private readonly ILookupService _lookup;

    public ObservableCollection<Outlet> Outlets { get; } = new();

    // Scope
    [ObservableProperty] private bool isGlobal = true;
    [ObservableProperty] private Outlet? selectedOutlet;

    // Identity
    [ObservableProperty] private string? outletDisplayName;
    [ObservableProperty] private string? addressLine1;
    [ObservableProperty] private string? addressLine2;
    [ObservableProperty] private string? phone;

    // NTN / FBR
    [ObservableProperty] private string? businessNtn;
    [ObservableProperty] private bool showBusinessNtn;
    [ObservableProperty] private bool enableFbr;
    [ObservableProperty] private string? fbrPosId;

    // Logo
    [ObservableProperty] private byte[]? logoPng;

    // Permissions
    [ObservableProperty] private bool canEdit;        // Manager or above
    [ObservableProperty] private bool canEditGlobal;  // Admin only

    // Dirty state
    [ObservableProperty] private bool hasChanges;

    public bool CanSave => CanEdit && HasChanges;

    private IdentitySettings? _loaded;

    public IdentitySettingsViewModel(
        IIdentitySettingsService svc,
        ILookupService lookup)
    {
        _svc = svc;
        _lookup = lookup;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Load outlets
        var outlets = await _lookup.GetOutletsAsync();
        Outlets.Clear();
        foreach (var o in outlets)
            Outlets.Add(o);

        // Permissions
        CanEdit = await AuthZ.IsManagerOrAboveAsync();  // manager+ can edit
        CanEditGlobal = await AuthZ.IsAdminAsync();           // only admin can touch Global

        // If not admin, force user's own outlet and disallow global
        if (!CanEditGlobal)
        {
            var currentOutletId = AppState.Current.CurrentOutletId;
            var own = Outlets.FirstOrDefault(o => o.Id == currentOutletId);

            IsGlobal = false;
            SelectedOutlet = own;
        }

        await LoadAsync();
        HasChanges = false;      // freshly loaded → clean
    }

    // ---------------- Scope change hooks ----------------

    partial void OnIsGlobalChanged(bool value)
    {
        if (!CanEditGlobal && value)
        {
            // Non-admin cannot switch to Global → force back to outlet
            IsGlobal = false;
            return;
        }

        _ = LoadAsync();
    }

    partial void OnSelectedOutletChanged(Outlet? value)
    {
        if (!CanEditGlobal)
        {
            // Non-admin cannot change outlet; reset to own
            var currentOutletId = AppState.Current.CurrentOutletId;
            SelectedOutlet = Outlets.FirstOrDefault(o => o.Id == currentOutletId);
            return;
        }

        if (!IsGlobal && value != null)
            _ = LoadAsync();
    }

    // ---------------- Dirty tracking hooks ----------------

    partial void OnOutletDisplayNameChanged(string? value) => MarkDirty();
    partial void OnAddressLine1Changed(string? value) => MarkDirty();
    partial void OnAddressLine2Changed(string? value) => MarkDirty();
    partial void OnPhoneChanged(string? value) => MarkDirty();

    partial void OnBusinessNtnChanged(string? value) => MarkDirty();
    partial void OnShowBusinessNtnChanged(bool value) => MarkDirty();
    partial void OnEnableFbrChanged(bool value) => MarkDirty();
    partial void OnFbrPosIdChanged(string? value) => MarkDirty();

    partial void OnLogoPngChanged(byte[]? value) => MarkDirty();

    partial void OnHasChangesChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnCanEditChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    private void MarkDirty()
    {
        if (!CanEdit) return;   // ignore changes if user isn't allowed to edit
        HasChanges = true;
    }

    // ---------------- Load / Save ----------------

    private async Task LoadAsync()
    {
        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;
        var s = await _svc.GetAsync(outletId);

        _loaded = s;

        OutletDisplayName = s.OutletDisplayName;
        AddressLine1 = s.AddressLine1;
        AddressLine2 = s.AddressLine2;
        Phone = s.Phone;

        BusinessNtn = s.BusinessNtn;
        ShowBusinessNtn = s.ShowBusinessNtn;
        EnableFbr = s.EnableFbr;
        FbrPosId = s.FbrPosId;

        LogoPng = s.LogoPng;

        HasChanges = false;   // just loaded from DB
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loaded == null || !CanEdit) return;

        _loaded.OutletId = IsGlobal ? null : SelectedOutlet?.Id;
        _loaded.OutletDisplayName = OutletDisplayName;
        _loaded.AddressLine1 = AddressLine1;
        _loaded.AddressLine2 = AddressLine2;
        _loaded.Phone = Phone;

        _loaded.BusinessNtn = BusinessNtn;
        _loaded.ShowBusinessNtn = ShowBusinessNtn;
        _loaded.EnableFbr = EnableFbr;
        _loaded.FbrPosId = FbrPosId;

        _loaded.LogoPng = LogoPng;
        _loaded.UpdatedAtUtc = DateTime.UtcNow;

        await _svc.SaveAsync(_loaded);

        HasChanges = false;

        MessageBox.Show("Identity settings saved.",
            "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        await LoadAsync();
        HasChanges = false;
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        LogoPng = null;
    }

    [RelayCommand]
    private void UploadLogo()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "PNG Images|*.png",
            Title = "Select Logo (PNG)"
        };
        if (dlg.ShowDialog() == true)
            LogoPng = File.ReadAllBytes(dlg.FileName);
    }
}
