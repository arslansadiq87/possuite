// Pos.Domain/Models/Accounting/CoaDtos.cs
using Pos.Domain.Accounting;
using Pos.Domain.Entities;

namespace Pos.Domain.Models.Accounting
{
    public sealed record CoaAccount(
        int Id,
        string Code,
        string Name,
        AccountType Type,
        bool IsHeader,
        bool AllowPosting,
        decimal OpeningDebit,
        decimal OpeningCredit,
        bool IsOpeningLocked,
        bool IsSystem,
        SystemAccountKey? SystemKey,
        int? ParentId,
        int? OutletId
    );

    public sealed record OpeningChange(int AccountId, decimal Debit, decimal Credit);

    public sealed record AccountEdit(int Id, string Code, string Name, bool IsHeader, bool AllowPosting);
}
