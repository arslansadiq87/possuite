using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Windows.Settings;
using System;
using System.Linq;
namespace Pos.Client.Wpf.Windows.Settings;
using Pos.Domain.Services;
public partial class PreferencesViewModel : ObservableObject
{
    private readonly ILookupService _lookup;
    private readonly IUserPreferencesService _svc;
    
    // inside class PreferencesViewModel
    public ObservableCollection<TimeZoneInfo> TimeZones { get; } = new();

    [ObservableProperty] private string? selectedTimeZoneId;

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<Warehouse> Warehouses { get; } = new();
    public string[] BarcodeTypes { get; } = new[] { "EAN13", "Code128", "QR" };

    // Bound fields
    [ObservableProperty] private string purchaseDestinationScope = "Outlet"; // "Outlet" | "Warehouse"
    [ObservableProperty] private int? purchaseDestinationId;                 // outletId or warehouseId depending on scope
    [ObservableProperty] private string defaultBarcodeType = "EAN13";

    [ObservableProperty] private bool isBusy;

    public PreferencesViewModel(ILookupService lookup, IUserPreferencesService svc)
    {
     _lookup = lookup;
     _svc = svc;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Outlets.Clear();
            Warehouses.Clear();
            var outlets = await _lookup.GetOutletsAsync();
            foreach (var o in outlets.OrderBy(x => x.Name)) Outlets.Add(o);

            var warehouses = await _lookup.GetWarehousesAsync();
            foreach (var w in warehouses.OrderBy(x => x.Name)) Warehouses.Add(w);

            var p = await _svc.GetAsync();
            PurchaseDestinationScope = p.PurchaseDestinationScope ?? "Outlet";
            PurchaseDestinationId = p.PurchaseDestinationId;
            DefaultBarcodeType = string.IsNullOrWhiteSpace(p.DefaultBarcodeType) ? "EAN13" : p.DefaultBarcodeType;

            // in LoadAsync(), after you load outlets/warehouses and p:
            TimeZones.Clear();
            foreach (var tz in TimeZoneInfo.GetSystemTimeZones())
                TimeZones.Add(tz);

            SelectedTimeZoneId = string.IsNullOrWhiteSpace(p.DisplayTimeZoneId)
                ? TimeZoneInfo.Local.Id
                : p.DisplayTimeZoneId;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var p = new UserPreference
            {
                PurchaseDestinationScope = PurchaseDestinationScope,
                PurchaseDestinationId = PurchaseDestinationId,
                DefaultBarcodeType = DefaultBarcodeType,
                DisplayTimeZoneId = SelectedTimeZoneId
            };

            await _svc.SaveAsync(p);

            // OS/UI concern: stays in Client
            Pos.Client.Wpf.Services.TimeService.SetTimeZone(SelectedTimeZoneId);

            MessageBox.Show("Preferences saved.", "Preferences",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { IsBusy = false; }
    }

}
