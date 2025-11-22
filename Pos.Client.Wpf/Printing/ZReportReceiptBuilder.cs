using System;
using System.Collections.Generic;
using System.Text;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Printing
{
    public sealed class ZReportModel
    {
        public int TillSessionId { get; set; }
        public DateTime OpenedAtUtc { get; set; }
        public DateTime ClosedAtUtc { get; set; }

        // Inputs
        public decimal OpeningFloat { get; set; }
        public decimal SalesTotal { get; set; }
        public decimal ReturnsTotalAbs { get; set; }   // absolute returns value (positive)

        // Derived
        public decimal NetTotal => SalesTotal - ReturnsTotalAbs;
        public decimal CashCounted { get; set; }
        public decimal ExpectedCash => OpeningFloat + NetTotal;
        public decimal OverShort => CashCounted - ExpectedCash;

        // New: for printing on Z-Report
        public string? CashierName { get; set; }
    }

    public static class ZReportReceiptBuilder
    {
        public static byte[] Build(ZReportModel z, ReceiptTemplate tpl)
        {
            var bytes = new List<byte>();
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(z.CashierName))
                sb.AppendLine($"Cashier:       {z.CashierName}");
            var cols = 42;
            sb.AppendLine(new string('=', cols));
            sb.AppendLine(Center("Z-REPORT", cols));
            sb.AppendLine(new string('=', cols));
            // avoid extra blank lines; keep a single LF between semantic groups

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

        private static string Center(string s, int w)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            if (s.Length >= w) return s;
            int pad = Math.Max(0, (w - s.Length) / 2);
            return new string(' ', pad) + s;
        }
    }
}
