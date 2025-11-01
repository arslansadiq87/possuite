using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Client.Wpf.Windows.Settings;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IDbContextFactory<PosClientDbContext> _dbf;
    private readonly IUserPreferencesService _svc;

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<Warehouse> Warehouses { get; } = new();
    public string[] BarcodeTypes { get; } = new[] { "EAN13", "Code128", "QR" };

    // Bound fields
    [ObservableProperty] private string purchaseDestinationScope = "Outlet"; // "Outlet" | "Warehouse"
    [ObservableProperty] private int? purchaseDestinationId;                 // outletId or warehouseId depending on scope
    [ObservableProperty] private string defaultBarcodeType = "EAN13";

    [ObservableProperty] private bool isBusy;

    public PreferencesViewModel(IDbContextFactory<PosClientDbContext> dbf, IUserPreferencesService svc)
    {
        _dbf = dbf;
        _svc = svc;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var db = await _dbf.CreateDbContextAsync();
            Outlets.Clear();
            Warehouses.Clear();

            foreach (var o in await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync())
                Outlets.Add(o);

            foreach (var w in await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync())
                Warehouses.Add(w);

            var p = await _svc.GetAsync();
            PurchaseDestinationScope = p.PurchaseDestinationScope ?? "Outlet";
            PurchaseDestinationId = p.PurchaseDestinationId;
            DefaultBarcodeType = string.IsNullOrWhiteSpace(p.DefaultBarcodeType) ? "EAN13" : p.DefaultBarcodeType;
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
                DefaultBarcodeType = DefaultBarcodeType
            };
            await _svc.SaveAsync(p);
            MessageBox.Show("Preferences saved.", "Preferences", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { IsBusy = false; }
    }
}
