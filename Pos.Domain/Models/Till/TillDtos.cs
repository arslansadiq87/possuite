using System;

namespace Pos.Domain.Models.Till
{
    public sealed class TillStatusDto
    {
        public bool IsOpen { get; init; }
        public int? TillSessionId { get; init; }
        public DateTime? OpenedAtUtc { get; init; }
        public string Text => IsOpen
            ? $"Till: OPEN (Id={TillSessionId}, Opened {OpenedAtUtc:HH:mm})"
            : "Till: Closed";
    }

    public enum CashCollectionMode
    {
        Till,
        CashInHand
    }

    public sealed class CashCollectionStatusDto
    {
        public CashCollectionMode Mode { get; init; }
        public TillStatusDto? Till { get; init; } // only when Mode == Till
        public string Text =>
            Mode == CashCollectionMode.Till
                ? (Till?.Text ?? "Till: Closed")
                : "Cash route: Cash-in-Hand";
    }

    public sealed class TillOpenResultDto
    {
        public int TillSessionId { get; init; }
        public DateTime OpenedAtUtc { get; init; }
        public decimal OpeningFloat { get; init; }
    }

    public sealed class TillCloseResultDto
    {
        public int TillSessionId { get; init; }
        public DateTime ClosedAtUtc { get; init; }

        public decimal SalesTotal { get; init; }
        public decimal ReturnsTotalAbs { get; init; }
        public decimal NetTotal => SalesTotal - ReturnsTotalAbs;

        public decimal OpeningFloat { get; init; }
        public decimal ExpectedCash { get; init; }
        public decimal DeclaredCash { get; init; }
        public decimal OverShort => DeclaredCash - ExpectedCash;

        /// <summary>System cash from final sales (excl. opening float).</summary>
        public decimal SystemCash { get; init; }
        /// <summary>Declared cash to move (excl. opening float).</summary>
        public decimal DeclaredToMove { get; init; }
    }

    public sealed class TillClosePreviewDto
    {
        public decimal OpeningFloat { get; init; }
        public decimal SalesTotal { get; init; }
        public decimal ReturnsTotalAbs { get; init; }
        public decimal NetTotal => SalesTotal - ReturnsTotalAbs;

        /// <summary>Cash expected in till at close (opening float + sales cash – refunds cash).</summary>
        public decimal ExpectedCash { get; init; }

        /// <summary>System cash from final sales (excl. opening float). Useful for GL preview.</summary>
        public decimal SystemCash { get; init; }
    }
}
