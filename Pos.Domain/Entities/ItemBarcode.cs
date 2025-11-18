using System.Text.Json.Serialization;

namespace Pos.Domain.Entities
{
    public enum BarcodeSymbology
    {
        Ean13 = 1,
        Ean8 = 2,
        UpcA = 3,
        Code128 = 10
    }

    public class ItemBarcode
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        [JsonIgnore]                 // <— prevent cycles and huge payloads
        public Item Item { get; set; } = null!;
        public string Code { get; set; } = "";  // UNIQUE
        public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Ean13;
        /// <summary>Units to add when this barcode is scanned (1 = single piece, >1 = pack/box).</summary>
        public int QuantityPerScan { get; set; } = 1;
        /// <summary>Optional tag shown to users, like “Box”, “Inner”, “Carton”.</summary>
        public string? Label { get; set; }
        public bool IsPrimary { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}