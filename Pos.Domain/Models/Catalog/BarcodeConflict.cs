namespace Pos.Domain.Models.Catalog
{
    public sealed class BarcodeConflict
    {
        public required string Code { get; init; }
        public int ItemId { get; init; }
        public int? ProductId { get; init; }
        public string? ProductName { get; init; }
        public required string ItemName { get; init; }
    }
}
