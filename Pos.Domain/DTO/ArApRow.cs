// Pos.Domain/DTO/ArApRow.cs
namespace Pos.Domain.DTO
{
    public record ArApRow(int PartyId, string PartyName, int? OutletId, string? OutletName, decimal Balance);
}
