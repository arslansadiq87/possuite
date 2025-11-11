using System;

namespace Pos.Domain.Models.Catalog
{
    public sealed class CategoryRowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public int ItemCount { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
