namespace Pos.Domain.Models
{
    // Shared UI↔Service DTO (core business flow)
    public sealed class OtherAccountUpsertDto
    {
        public int? Id { get; init; }
        public string? Code { get; init; }
        public string Name { get; init; } = "";
        public string? Phone { get; init; }
        public string? Email { get; init; }
    }
}
