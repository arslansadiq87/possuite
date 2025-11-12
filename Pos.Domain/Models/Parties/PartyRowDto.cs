// Pos.Domain/Models/Parties/PartyRowDto.cs
namespace Pos.Domain.Models.Parties
{
    public sealed class PartyRowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? TaxNumber { get; set; }
        public bool IsActive { get; set; }
        public bool IsSharedAcrossOutlets { get; set; }
        public string RolesText { get; set; } = "";
    }
}
