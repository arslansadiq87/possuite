using System;
using System.Globalization;
using System.Linq;
using Pos.Domain.Entities; // for BarcodeSymbology

namespace Pos.Domain.Utils
{
    public static class BarcodeUtil
    {
        public static string GenerateBySymbology(BarcodeSymbology sym, string prefix, int number) =>
            sym switch
            {
                BarcodeSymbology.Ean13 => GenerateEan13(prefix, number),
                BarcodeSymbology.Ean8 => GenerateEan8(prefix, number),
                BarcodeSymbology.UpcA => GenerateUpcA(prefix, number),
                BarcodeSymbology.Code128 => GenerateCode128(prefix, number),
                _ => GenerateEan13(prefix, number),
            };

        public static string GenerateEan13(string prefix, int number)
        {
            var digits = new string((prefix ?? "").Where(char.IsDigit).ToArray()) + number.ToString(CultureInfo.InvariantCulture);
            digits = new string(digits.Take(12).ToArray()).PadLeft(12, '0');
            var check = ComputeEan13CheckDigit(digits);
            return digits + check.ToString(CultureInfo.InvariantCulture);
        }

        public static int ComputeEan13CheckDigit(string first12)
        {
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int d = first12[i] - '0';
                sum += (i % 2 == 0) ? d : 3 * d;
            }
            return (10 - (sum % 10)) % 10;
        }

        public static string GenerateEan8(string prefix, int number)
        {
            var digits = new string((prefix ?? "").Where(char.IsDigit).ToArray()) + number.ToString(CultureInfo.InvariantCulture);
            digits = new string(digits.Take(7).ToArray()).PadLeft(7, '0');
            int check = ComputeEan8CheckDigit(digits);
            return digits + check.ToString(CultureInfo.InvariantCulture);
        }

        public static int ComputeEan8CheckDigit(string first7)
        {
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                int d = first7[i] - '0';
                sum += (i % 2 == 0) ? 3 * d : d;
            }
            return (10 - (sum % 10)) % 10;
        }

        public static string GenerateUpcA(string prefix, int number)
        {
            var digits = new string((prefix ?? "").Where(char.IsDigit).ToArray()) + number.ToString(CultureInfo.InvariantCulture);
            digits = new string(digits.Take(11).ToArray()).PadLeft(11, '0');
            int check = ComputeUpcACheckDigit(digits);
            return digits + check.ToString(CultureInfo.InvariantCulture);
        }

        public static int ComputeUpcACheckDigit(string first11)
        {
            int odd = 0, even = 0;
            for (int i = 0; i < 11; i++)
            {
                int d = first11[i] - '0';
                if ((i % 2) == 0) odd += d; else even += d;
            }
            int sum = odd * 3 + even;
            return (10 - (sum % 10)) % 10;
        }

        public static string GenerateCode128(string prefix, int number)
        {
            var raw = new string((prefix ?? "").Where(char.IsLetterOrDigit).ToArray()) + number.ToString(CultureInfo.InvariantCulture);
            return raw.Length > 32 ? raw[..32] : raw;
        }
    }
}
