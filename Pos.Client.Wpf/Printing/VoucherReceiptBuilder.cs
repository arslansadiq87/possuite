using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pos.Domain.Accounting;
using Pos.Domain.Entities;

namespace Pos.Client.Wpf.Printing
{
    public static class VoucherReceiptBuilder
    {
        public static byte[] Build(Voucher voucher, ReceiptTemplate tpl)
        {
            // Width: 58mm ≈ 32 cols, 80mm ≈ 42 cols (fallback to 42 if template missing)
            var cols = (tpl?.PaperWidthMm ?? 80) <= 58 ? 32 : 42;

            var sb = new StringBuilder();

            // Header
            sb.AppendLine(Center($"*** {voucher.Type.ToString().ToUpper()} VOUCHER ***", cols));
            sb.AppendLine(Line("No:", Safe(voucher.RefNo, fallback: voucher.Id.ToString()), cols));
            sb.AppendLine(Line("Date (UTC):", voucher.TsUtc.ToString("yyyy-MM-dd HH:mm"), cols));
            sb.AppendLine(Line("Status:", voucher.Status.ToString(), cols));
            sb.AppendLine(Line("Revision:", voucher.RevisionNo.ToString(), cols));
            if (!string.IsNullOrWhiteSpace(voucher.Memo))
                sb.AppendLine(Line("Memo:", voucher.Memo!, cols));

            sb.AppendLine(new string('-', cols));

            // Lines header
            // Left side shows AccountId + (optional) Description, right side shows DR/CR
            sb.AppendLine(FixedColumns("Account/Desc", "DR", "CR", cols));

            decimal totalDr = 0m, totalCr = 0m;

            foreach (var ln in voucher.Lines ?? Enumerable.Empty<VoucherLine>())
            {
                // Only print non-zero lines to keep it clean
                if (ln.Debit == 0m && ln.Credit == 0m) continue;

                var left = $"Acc {ln.AccountId}" + (string.IsNullOrWhiteSpace(ln.Description) ? "" : $" - {ln.Description}");
                var dr = ln.Debit != 0m ? ln.Debit.ToString("0.00") : "";
                var cr = ln.Credit != 0m ? ln.Credit.ToString("0.00") : "";

                sb.AppendLine(FixedColumns(left, dr, cr, cols));

                totalDr += ln.Debit;
                totalCr += ln.Credit;
            }

            sb.AppendLine(new string('-', cols));
            sb.AppendLine(FixedColumns("TOTAL", totalDr.ToString("0.00"), totalCr.ToString("0.00"), cols));

            var diff = totalDr - totalCr;
            string balanceText = diff == 0m
                ? "Balanced"
                : (diff > 0m ? $"DR > CR by {diff:0.00}" : $"CR > DR by {Math.Abs(diff):0.00}");

            sb.AppendLine(Line("Check:", balanceText, cols));

            sb.AppendLine(new string('-', cols));
            sb.AppendLine(Center("Thank you.", cols));

            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        // ---------- helpers ----------

        private static string Safe(string? s, string fallback = "")
            => string.IsNullOrWhiteSpace(s) ? fallback : s;

        private static string Center(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return "\n";
            if (text.Length >= width) return text[..width] + "\n";
            var pad = Math.Max(0, (width - text.Length) / 2);
            return new string(' ', pad) + text + "\n";
        }

        // Simple key:value line with right-aligned value
        private static string Line(string key, string value, int width)
        {
            key ??= "";
            value ??= "";
            // ensure at least one space between
            var space = Math.Max(1, width - key.Length - value.Length);
            if (space < 1)
            {
                // truncate key if too long
                var maxKey = Math.Max(0, width - value.Length - 1);
                key = key.Length > maxKey ? key[..maxKey] : key;
                space = 1;
            }
            return key + new string(' ', space) + value + "\n";
        }

        // Makes three columns: Left (flex), DR (8), CR (8) with a single space between
        private static string FixedColumns(string left, string dr, string cr, int width)
        {
            const int amtWidth = 8;     // "123456.78"
            const int gap = 1;          // space between columns

            // total reserved for amounts and gaps
            int rightReserved = (amtWidth + gap) + (amtWidth);
            int leftWidth = Math.Max(0, width - rightReserved - gap); // extra gap before first amount

            var leftTxt = left ?? "";
            if (leftTxt.Length > leftWidth)
                leftTxt = leftTxt[..leftWidth];

            var drTxt = Right(dr ?? "", amtWidth);
            var crTxt = Right(cr ?? "", amtWidth);

            // left + gap + DR + gap + CR
            return leftTxt.PadRight(leftWidth) + new string(' ', gap) + drTxt + new string(' ', gap) + crTxt;
        }

        private static string Right(string s, int width)
        {
            s ??= "";
            return s.Length >= width ? s[^width..] : new string(' ', width - s.Length) + s;
        }
    }
}
