using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities;

public class UserPreference : BaseEntity
{
    public string MachineName { get; set; } = Environment.MachineName;

    // Purchase defaults
    public string PurchaseDestinationScope { get; set; } = "Outlet"; // "Outlet" | "Warehouse"
    public int? PurchaseDestinationId { get; set; }

    // Items defaults
    public string DefaultBarcodeType { get; set; } = "EAN13"; // EAN13/Code128/QR...

    // Extend later (default language, default paper, etc.)
}
