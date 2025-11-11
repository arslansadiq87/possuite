// Pos.Domain/Models/Accounting/VoucherDtos.cs
using System;
using System.Collections.Generic;
using Pos.Domain.Accounting;

namespace Pos.Domain.Models.Accounting
{
    // Edit screen line DTO (UI-safe, shared domain model)
    public sealed record VoucherEditLineDto(int AccountId, string? Description, decimal Debit, decimal Credit);

    public sealed record VoucherEditLoadDto(
        int Id,
        DateTime TsUtc,
        int? OutletId,
        string? RefNo,
        string? Memo,
        VoucherType Type,
        IReadOnlyList<VoucherEditLineDto> Lines
    );

    // Listing row for grids
    public sealed record VoucherRowDto(
        int Id,
        DateTime TsUtc,
        VoucherType Type,
        string? Memo,
        int? OutletId,
        VoucherStatus Status,
        int RevisionNo,
        decimal TotalDebit,
        decimal TotalCredit,
        bool HasRevisions
    );

    public sealed record VoucherLineDto(
        int AccountId,
        string AccountName,
        string? Description,
        decimal Debit,
        decimal Credit
    );
}
