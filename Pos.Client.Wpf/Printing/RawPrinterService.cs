// Pos.Client.Wpf/Printing/RawPrinter.cs
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Printing
{
    public interface IRawPrinterService
    {
        /// <summary>Send raw bytes to the printer (ESC/POS or any RAW payload).</summary>
        Task SendBytesAsync(string printerName, byte[] data, string? docName = null, string dataType = "RAW", CancellationToken ct = default);

        /// <summary>Send ESC/POS bytes to the printer.</summary>
        Task SendEscPosAsync(string printerName, byte[] escpos, CancellationToken ct = default);

        /// <summary>Send TSPL commands (string) to the printer. Will enforce CRLF line endings.</summary>
        Task SendTsplAsync(string printerName, string tsplCommands, CancellationToken ct = default);
    }

    /// <summary>
    /// Single, merged raw printer implementation (replaces RawPrinterHelper + RawPrinterService).
    /// Async, cancellation-aware, unified error handling with Win32Exception.
    /// </summary>
    public sealed class RawPrinterService : IRawPrinterService
    {
        // ----- WinSpool interop (Unicode) -----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            public string? pDocName;
            public string? pOutputFile;
            public string? pDatatype;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);

        [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        // ----- Public API -----
        public Task SendEscPosAsync(string printerName, byte[] escpos, CancellationToken ct = default)
            => SendBytesAsync(printerName, escpos, docName: "ESC/POS Document", dataType: "RAW", ct);

        public Task SendTsplAsync(string printerName, string tsplCommands, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Printer name is required.", nameof(printerName));

            // TSPL expects CRLF for line ends; enforce a trailing CRLF too.
            // (Many commands are line-based; missing CRLF can cause ignored last line.)
            var normalized = tsplCommands.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            if (!normalized.EndsWith("\r\n", StringComparison.Ordinal))
                normalized += "\r\n";

            // TSPL is ASCII-oriented. If you need a different codepage, inject it here.
            var bytes = Encoding.ASCII.GetBytes(normalized);
            return SendBytesAsync(printerName, bytes, docName: "TSPL Direct", dataType: "RAW", ct);
        }

        public async Task SendBytesAsync(string printerName, byte[] data, string? docName = null, string dataType = "RAW", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(printerName))
                throw new ArgumentException("Printer name is required.", nameof(printerName));
            if (data is null || data.Length == 0)
                return;

            // Offload the blocking WinSpool sequence to the thread pool and honor cancellation.
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
                    ThrowWin32("OpenPrinter failed", printerName);

                try
                {
                    var di = new DOC_INFO_1
                    {
                        pDocName = string.IsNullOrWhiteSpace(docName) ? "RAW Document" : docName,
                        pDatatype = dataType
                    };

                    if (!StartDocPrinter(hPrinter, 1, di))
                        ThrowWin32("StartDocPrinter failed", printerName);

                    try
                    {
                        if (!StartPagePrinter(hPrinter))
                            ThrowWin32("StartPagePrinter failed", printerName);

                        try
                        {
                            // Allocate unmanaged buffer and write in chunks (some drivers prefer <=64K).
                            var total = data.Length;
                            var ptr = Marshal.AllocHGlobal(total);
                            try
                            {
                                Marshal.Copy(data, 0, ptr, total);
                                const int Chunk = 64 * 1024; // 64 KiB
                                var offset = 0;

                                while (offset < total)
                                {
                                    ct.ThrowIfCancellationRequested();

                                    var toWrite = Math.Min(Chunk, total - offset);
                                    var chunkPtr = IntPtr.Add(ptr, offset);

                                    if (!WritePrinter(hPrinter, chunkPtr, toWrite, out var written) || written != toWrite)
                                        ThrowWin32("WritePrinter failed", printerName);

                                    offset += written;
                                }
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(ptr);
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

        // ----- Helpers -----
        private static void ThrowWin32(string message, string printerName)
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"{message} for '{printerName}' (0x{err:X}).");
        }
    }
}
