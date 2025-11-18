namespace Pos.Domain.Models
{
    public sealed record BankAccountUpsertDto(
        int? Id,
        int? AccountId,      // null on create
        string Name,         // GL account display name
        string BankName,
        string? Branch,
        string? AccountNumber,
        string? IBAN,
        string? SwiftBic,
        string? Notes,
        bool IsActive
    );

    public sealed record BankAccountViewDto(
        int Id,
        int AccountId,
        string Code,
        string Name,
        string BankName,
        string? Branch,
        string? AccountNumber,
        string? IBAN,
        string? SwiftBic,
        string? Notes,
        bool IsActive
    );
}
