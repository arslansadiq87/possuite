using System;

namespace Pos.Domain.DTO
{
    public sealed class TillSessionSummaryDto
    {
        // Identity / scope
        public int TillId { get; init; }
        public int OutletId { get; init; }
        public int CounterId { get; init; }

        // Timestamps (UTC)
        public DateTime OpenedAtUtc { get; init; }
        public DateTime? LastTxUtc { get; init; }

        // Activity (latest surviving docs only)
        public int SalesCount { get; init; }
        public int ReturnsCount { get; init; }
        public int DocsCount { get; init; }

        // Items (latest surviving docs only)
        public decimal ItemsSoldQty { get; init; }
        public decimal ItemsReturnedQty { get; init; }
        public decimal ItemsNetQty { get; init; }

        // Money (latest surviving docs only)
        public decimal SalesTotal { get; init; }           // positive
        public decimal ReturnsTotalAbs { get; init; }      // positive magnitude
        public decimal NetTotal => SalesTotal - ReturnsTotalAbs;

        // Tax (latest surviving docs only)
        public decimal TaxCollected { get; init; }
        public decimal TaxRefundedAbs { get; init; }

        // Movements (ALL non-voided revisions; sign-aware)
        public decimal OpeningFloat { get; init; }
        public decimal SalesCash { get; init; }            // cash from sales
        public decimal RefundsCashAbs { get; init; }       // positive magnitude
        public decimal ExpectedCash { get; init; }         // opening + signed net cash

        public decimal SalesCard { get; init; }
        public decimal RefundsCardAbs { get; init; }

        // Amendments & voids (session-scoped)
        public int SalesAmendments { get; init; }
        public int ReturnAmendments { get; init; }
        public int VoidsCount { get; init; }
    }
}
