using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Printing
{
    public sealed class ZReportModel
    {
        public int TillSessionId { get; init; }
        public DateTime OpenedAtUtc { get; init; }
        public DateTime ClosedAtUtc { get; init; }
        public decimal OpeningFloat { get; init; }
        public decimal SalesTotal { get; init; }
        public decimal ReturnsTotalAbs { get; init; }
        public decimal NetTotal => SalesTotal - ReturnsTotalAbs;
        public decimal CashCounted { get; init; }
        public decimal OverShort => CashCounted - (OpeningFloat + NetTotal);
    }

    public static class ZReportReceiptBuilder
    {
        public static byte[] Build(ZReportModel z, ReceiptTemplate tpl)
        {
            var bytes = new List<byte>();
            // Title: "Z REPORT — TILL CLOSE"
            // Show session id, open/close times, OpeningFloat, Sales, ReturnsAbs, Net, CashCounted, Over/Short
            return bytes.ToArray();
        }
    }

}
