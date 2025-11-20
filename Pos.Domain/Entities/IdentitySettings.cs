// Pos.Domain/Entities/IdentitySettings.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    /// <summary>
    /// Identity + FBR settings per outlet (or global).
    /// Completely separate from InvoiceSettings.
    /// </summary>
    public class IdentitySettings : BaseEntity
    {
        public int? OutletId { get; set; }   // null = global

        // Identity
        public string? OutletDisplayName { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Phone { get; set; }

        // NTN / FBR
        public string? BusinessNtn { get; set; }
        public bool ShowBusinessNtn { get; set; } = false;
        public bool EnableFbr { get; set; } = false;
        public string? FbrPosId { get; set; }

        // Logo for receipts / print
        public byte[]? LogoPng { get; set; }

        public Outlet? Outlet { get; set; }

        // NEW — Currency config
        public bool CurrencyEnabled { get; set; } = false;
        public string? CurrencyCode { get; set; }   // e.g., "PKR", "USD"
        public string? CurrencySymbol { get; set; } // e.g., "Rs", "$"
    }
}
