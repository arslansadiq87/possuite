// Pos.Domain/Entities/ItemImage.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    /// <summary>
    /// Optional image specific to a variant (Item). If absent, UI should fallback to Product’s primary image.
    /// We still allow multiple per item (gallery), but usually one is enough.
    /// </summary>
    public class ItemImage : BaseEntity
    {
        public int ItemId { get; set; }
        public Item? Item { get; set; }

        public bool IsPrimary { get; set; }
        public int SortOrder { get; set; }

        public string? LocalOriginalPath { get; set; }
        public string? LocalThumbPath { get; set; }

        public string? ServerOriginalUrl { get; set; }
        public string? ServerThumbUrl { get; set; }

        public int? Width { get; set; }
        public int? Height { get; set; }
        public long? SizeBytes { get; set; }
        public string? ContentHashSha1 { get; set; }
    }
}
