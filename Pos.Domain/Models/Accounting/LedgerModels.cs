// Pos.Domain/Models/Accounting/LedgerModels.cs
using System;

namespace Pos.Domain.Models.Accounting
{
    // Generic account ledger row (UI/server shared)
    public readonly record struct LedgerRow(
        DateTime TsUtc, string? Memo, decimal Debit, decimal Credit, decimal Running
    );

    // Richer row for Cash Book
    public sealed class CashBookRowDto
    {
        public DateTime TsUtc { get; set; }
        public string? Memo { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Running { get; set; }
        public bool IsVoided { get; set; }     // inferred (until column exists)
        public int? TillId { get; set; }       // reserved for future schema
        public string? SourceRef { get; set; } // "PO #x", "Sale #x", etc.
    }

    public enum CashBookScope
    {
        HandOnly = 0,   // 11101-OUT
        TillOnly = 1,   // 11102-OUT
        Both = 2        // 11101-OUT + 11102-OUT
    }
}
