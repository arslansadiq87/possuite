using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Printing
{
    public interface IRawPrinterService
    {
        Task SendAsync(string printerName, string tsplCommands, CancellationToken ct = default);
    }

    public sealed class RawPrinterService : IRawPrinterService
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string? pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? pDatatype;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public async Task SendAsync(string printerName, string tsplCommands, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Printer name is required.", nameof(printerName));

            // TSPL uses CRLF line endings; ensure they’re present
            if (!tsplCommands.EndsWith("\r\n", StringComparison.Ordinal))
                tsplCommands += "\r\n";

            await Task.Run(() =>
            {
                if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
                    throw new InvalidOperationException($"OpenPrinter failed for '{printerName}' (err {Marshal.GetLastWin32Error()}).");

                try
                {
                    var di = new DOC_INFO_1
                    {
                        pDocName = "TSPL Direct",
                        pDatatype = "RAW"
                    };
                    if (!StartDocPrinter(hPrinter, 1, di)) throw new InvalidOperationException($"StartDocPrinter failed (err {Marshal.GetLastWin32Error()}).");
                    try
                    {
                        if (!StartPagePrinter(hPrinter)) throw new InvalidOperationException($"StartPagePrinter failed (err {Marshal.GetLastWin32Error()}).");
                        try
                        {
                            var bytes = Encoding.ASCII.GetBytes(tsplCommands);
                            var unmanaged = Marshal.AllocHGlobal(bytes.Length);
                            try
                            {
                                Marshal.Copy(bytes, 0, unmanaged, bytes.Length);
                                if (!WritePrinter(hPrinter, unmanaged, bytes.Length, out var written) || written != bytes.Length)
                                    throw new InvalidOperationException($"WritePrinter failed (err {Marshal.GetLastWin32Error()}).");
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(unmanaged);
                            }
                        }
                        finally
                        {
                            EndPagePrinter(hPrinter);
                        }
                    }
                    finally
                    {
                        EndDocPrinter(hPrinter);
                    }
                }
                finally
                {
                    ClosePrinter(hPrinter);
                }
            }, ct);
        }
    }
}
