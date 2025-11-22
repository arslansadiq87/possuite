using System;
using System.Collections.Generic;
using System.Text;

namespace Pos.Client.Wpf.Printing
{
    internal static class EscPosUtil
    {
        public static byte[] Txt(string s) => Encoding.ASCII.GetBytes(s);
        public static byte[] LF => new byte[] { 0x0A };
        public static byte[] Bold(bool on) => on ? new byte[] { 0x1B, 0x45, 0x01 } : new byte[] { 0x1B, 0x45, 0x00 };
        public static byte[] AlignCenter => new byte[] { 0x1B, 0x61, 0x01 };
        public static byte[] AlignLeft => new byte[] { 0x1B, 0x61, 0x00 };
        public static byte[] AlignRight => new byte[] { 0x1B, 0x61, 0x02 };
        public static byte[] DoubleH(bool on) => new byte[] { 0x1D, 0x21, (byte)(on ? 0x10 : 0x00) };
        public static byte[] DoubleW(bool on) => new byte[] { 0x1D, 0x21, (byte)(on ? 0x20 : 0x00) };

        // Code128 (ESC/POS): GS k 73 n <data> (function b)
        public static byte[] Code128(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return Array.Empty<byte>();
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x1D, 0x6B, 0x49, (byte)data.Length }); // k=73, length
            bytes.AddRange(Encoding.ASCII.GetBytes(data));
            bytes.Add(0x0A);
            return bytes.ToArray();
        }
    }
}
