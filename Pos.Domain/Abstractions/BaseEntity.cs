// Pos.Domain/Abstractions/BaseEntity.cs
namespace Pos.Domain.Abstractions
{
    public abstract class BaseEntity
    {
        public int Id { get; set; } // internal DB key
        public Guid PublicId { get; set; } = Guid.NewGuid(); // for sync APIs
        public DateTime CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public byte[]? RowVersion { get; set; } // concurrency token
    }
}