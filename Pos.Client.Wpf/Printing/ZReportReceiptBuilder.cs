using System;
using System.Collections.Generic;
using System.Text;
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
            var sb = new StringBuilder();

            sb.AppendLine("*** Z REPORT — TILL CLOSE ***");
            sb.AppendLine($"Session:       {z.TillSessionId}");
            sb.AppendLine($"Opened (UTC):  {z.OpenedAtUtc:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"Closed (UTC):  {z.ClosedAtUtc:yyyy-MM-dd HH:mm}");
            sb.AppendLine("----------------------------");
            sb.AppendLine($"Opening Float: {z.OpeningFloat:0.00}");
            sb.AppendLine($"Sales:         {z.SalesTotal:0.00}");
            sb.AppendLine($"Returns:       {z.ReturnsTotalAbs:0.00}");
            sb.AppendLine($"Net:           {z.NetTotal:0.00}");
            sb.AppendLine($"Cash Counted:  {z.CashCounted:0.00}");
            sb.AppendLine($"Over/Short:    {z.OverShort:0.00}");
            sb.AppendLine("----------------------------");
            sb.AppendLine("Thank you.");

            bytes.AddRange(Encoding.ASCII.GetBytes(sb.ToString()));
            return bytes.ToArray();
        }
    }
}
