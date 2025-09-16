// Pos.Domain/Entities/Product.cs
using Pos.Domain.Abstractions;
using Pos.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = "";
    // Optional FK refs (nullable)
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    public bool IsVoided { get; set; }               // NEW
    public DateTime? VoidedAtUtc { get; set; }       // NEW
    public string? VoidedBy { get; set; }            // NEW
    public ICollection<Item> Variants { get; set; } = new List<Item>();
}
