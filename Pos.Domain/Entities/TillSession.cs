// Pos.Domain/Entities/TillSession.cs
namespace Pos.Domain.Entities;
using Pos.Domain.Abstractions;
public class TillSession : BaseEntity
{
    public int OutletId { get; set; }
    public int CounterId { get; set; }

    public DateTime OpenTs { get; set; }
    public decimal OpeningFloat { get; set; }

    public DateTime? CloseTs { get; set; }
    public decimal? DeclaredCash { get; set; }
    public decimal? OverShort { get; set; }

    public bool IsOpen => CloseTs == null;
}
