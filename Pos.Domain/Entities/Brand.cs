// Pos.Domain/Entities/Brands.cs
using Pos.Domain.Abstractions;

namespace Pos.Domain.Entities
{
    public class Brand : BaseEntity
    {
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<Item> Items { get; set; } = new List<Item>();
    }
}
