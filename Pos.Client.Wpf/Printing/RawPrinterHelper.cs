//Pos.Client.Wpf/Printing/RawPrinterHelper
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pos.Client.Wpf.Printing
{
    // Sends raw bytes directly to a printer using WinSpool.
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            public string pDocName = "ESC/POS Document";
            public string pOutputFile;
            public string pDatatype = "RAW";
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern bool OpenPrinter(string src, out IntPtr hPrinter, IntPtr pd);
        [DllImport("winspool.Drv", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ClosePrinter(IntPtr hPrinter);
        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);
        [DllImport("winspool.Drv", SetLastError = true)]
        static extern bool EndDocPrinter(IntPtr hPrinter);
        [DllImport("winspool.Drv", SetLastError = true)]
        static extern bool StartPagePrinter(IntPtr hPrinter);
        [DllImport("winspool.Drv", SetLastError = true)]
        static extern bool EndPagePrinter(IntPtr hPrinter);
        [DllImport("winspool.Drv", SetLastError = true)]
        static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static void SendBytesToPrinter(string printerName, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Printer name is empty. Set your ESC/POS printer name.");

            if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenPrinter failed for '{printerName}'");

            try
            {
                var di = new DOC_INFO_1();
                if (!StartDocPrinter(hPrinter, 1, di)) throw new Win32Exception(Marshal.GetLastWin32Error(), "StartDocPrinter failed");
                try
                {
                    if (!StartPagePrinter(hPrinter)) throw new Win32Exception(Marshal.GetLastWin32Error(), "StartPagePrinter failed");
                    try
                    {
                        var unmanagedPointer = Marshal.AllocHGlobal(bytes.Length);
                        try
                        {
                            Marshal.Copy(bytes, 0, unmanagedPointer, bytes.Length);
                            if (!WritePrinter(hPrinter, unmanagedPointer, bytes.Length, out var _))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "WritePrinter failed");
                        }
                        finally { Marshal.FreeHGlobal(unmanagedPointer); }
                    }
                    finally { EndPagePrinter(hPrinter); }
                }
                finally { EndDocPrinter(hPrinter); }
            }
            finally { ClosePrinter(hPrinter); }
        }
    }
}
