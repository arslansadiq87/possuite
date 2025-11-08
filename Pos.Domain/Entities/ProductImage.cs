// Pos.Domain/Entities/ProductImage.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    /// <summary>
    /// Image linked to a Product (default/primary + gallery). Variants (Items) can still fallback to Product image.
    /// Originals are staged on disk; only thumbnails are used by POS UI for speed.
    /// </summary>
    public class ProductImage : BaseEntity
    {
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        /// <summary>True = the “main/default” product image used when a variant has no image.</summary>
        public bool IsPrimary { get; set; }

        /// <summary>Gallery sort order (0..N). Primary can be 0.</summary>
        public int SortOrder { get; set; }

        // Local file system (offline-first)
        public string? LocalOriginalPath { get; set; }  // where the original file is staged locally
        public string? LocalThumbPath { get; set; }     // POS uses this (small, fast)

        // Future server pointers (for web/e-commerce)
        public string? ServerOriginalUrl { get; set; }
        public string? ServerThumbUrl { get; set; }

        // Basic metadata (useful for UI decisions)
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long? SizeBytes { get; set; }

        /// <summary>For dedup & integrity.</summary>
        public string? ContentHashSha1 { get; set; }
    }
}
