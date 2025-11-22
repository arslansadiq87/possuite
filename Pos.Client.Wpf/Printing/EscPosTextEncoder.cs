// Pos.Client.Wpf/Printing/EscPosTextEncoder.cs
using System.Text;

namespace Pos.Client.Wpf.Printing
{
    public static class EscPosTextEncoder
    {
        public static byte[] FromPlainText(string text)
        {
            // Initialize printer, set defaults
            var init = new byte[] { 0x1B, 0x40 }; // ESC @
            // NOTE: If your printer expects a specific codepage, set it here via ESC t n

            var body = Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n"));
            return Combine(init, body);
        }

        private static byte[] Combine(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            a.CopyTo(r, 0); b.CopyTo(r, a.Length);
            return r;
        }
    }
}
