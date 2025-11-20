using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pos.Client.Wpf.Printing
{
    public interface ITscCommandService
    {
        // ---- Motion / media handling ----
        Task FeedAsync(string printerName, int dots, CancellationToken ct = default);       // FEED n
        Task BackfeedAsync(string printerName, int dots, CancellationToken ct = default);   // BACKFEED n
        Task FormfeedAsync(string printerName, CancellationToken ct = default);            // FORMFEED
        Task HomeAsync(string printerName, CancellationToken ct = default);                // HOME
        Task AutoDetectAsync(string printerName, CancellationToken ct = default);          // AUTODETECT (detect gap/mark)
        Task ResetAsync(string printerName, CancellationToken ct = default);               // RESET

        // ---- Printer setup (sticky until changed) ----
        Task SetSizeMmAsync(string printerName, double widthMm, double heightMm, CancellationToken ct = default);   // SIZE w,h
        Task SetGapMmAsync(string printerName, double gapMm, double offsetMm, CancellationToken ct = default);      // GAP m,n
        Task SetBlackMarkMmAsync(string printerName, double markMm, double offsetMm, CancellationToken ct = default); // BLINE m,n
        Task SetSpeedAsync(string printerName, int level, CancellationToken ct = default);       // SPEED 1..5 (model dep.)
        Task SetDensityAsync(string printerName, int level, CancellationToken ct = default);     // DENSITY 0..15
        Task SetDirectionAsync(string printerName, int dir, CancellationToken ct = default);     // DIRECTION 0/1
        Task SetReferenceAsync(string printerName, int xDots, int yDots, CancellationToken ct = default); // REFERENCE x,y
        Task SetOffsetAsync(string printerName, int dots, CancellationToken ct = default);       // OFFSET n
        Task SetTearAsync(string printerName, bool on, CancellationToken ct = default);          // SET TEAR ON/OFF
        Task SetPeelAsync(string printerName, bool on, CancellationToken ct = default);          // SET PEEL ON/OFF (if equipped)
        Task SetCodePageAsync(string printerName, string codepage, CancellationToken ct = default); // CODEPAGE xxx

        // ---- Sound / UI feedback ----
        Task BeepAsync(string printerName, int times = 1, int ms = 100, CancellationToken ct = default); // BEEP n,t

        // ---- Page buffer operations ----
        Task ClearPageAsync(string printerName, CancellationToken ct = default); // CLS
        Task PrintCopiesAsync(string printerName, int copies = 1, CancellationToken ct = default); // PRINT n

        // ---- High-level helpers ----
        Task PrintQuickLabelAsync(string printerName, double widthMm, double heightMm,
                                  Action<TscLabelBuilder> build, int copies = 1, CancellationToken ct = default);
    }

    public sealed class TscCommandService : ITscCommandService
    {
        private readonly IRawPrinterService _raw;
        public TscCommandService(IRawPrinterService raw) => _raw = raw;

        // --- Motion ---
        public Task FeedAsync(string printerName, int dots, CancellationToken ct = default)
            => Send(printerName, $"FEED {Clamp(dots, 1, 20000)}", ct);
        public Task BackfeedAsync(string printerName, int dots, CancellationToken ct = default)
            => Send(printerName, $"BACKFEED {Clamp(dots, 1, 20000)}", ct);
        public Task FormfeedAsync(string printerName, CancellationToken ct = default)
            => Send(printerName, "FORMFEED", ct);
        public Task HomeAsync(string printerName, CancellationToken ct = default)
            => Send(printerName, "HOME", ct);
        public Task AutoDetectAsync(string printerName, CancellationToken ct = default)
            => Send(printerName, "AUTODETECT", ct);
        public Task ResetAsync(string printerName, CancellationToken ct = default)
            => Send(printerName, "RESET", ct);

        // --- Setup ---
        public Task SetSizeMmAsync(string printerName, double widthMm, double heightMm, CancellationToken ct = default)
            => Send(printerName, $"SIZE {Fm(widthMm)},{Fm(heightMm)}", ct);
        public Task SetGapMmAsync(string printerName, double gapMm, double offsetMm, CancellationToken ct = default)
            => Send(printerName, $"GAP {Fm(gapMm)},{Fm(offsetMm)}", ct);
        public Task SetBlackMarkMmAsync(string printerName, double markMm, double offsetMm, CancellationToken ct = default)
            => Send(printerName, $"BLINE {Fm(markMm)},{Fm(offsetMm)}", ct);
        public Task SetSpeedAsync(string printerName, int level, CancellationToken ct = default)
            => Send(printerName, $"SPEED {Clamp(level, 1, 5)}", ct);
        public Task SetDensityAsync(string printerName, int level, CancellationToken ct = default)
            => Send(printerName, $"DENSITY {Clamp(level, 0, 15)}", ct);
        public Task SetDirectionAsync(string printerName, int dir, CancellationToken ct = default)
            => Send(printerName, $"DIRECTION {(dir == 0 ? 0 : 1)}", ct);
        public Task SetReferenceAsync(string printerName, int xDots, int yDots, CancellationToken ct = default)
            => Send(printerName, $"REFERENCE {Clamp(xDots, 0, 9999)},{Clamp(yDots, 0, 9999)}", ct);
        public Task SetOffsetAsync(string printerName, int dots, CancellationToken ct = default)
            => Send(printerName, $"OFFSET {Clamp(dots, -9999, 9999)}", ct);
        public Task SetTearAsync(string printerName, bool on, CancellationToken ct = default)
            => Send(printerName, $"SET TEAR {(on ? "ON" : "OFF")}", ct);
        public Task SetPeelAsync(string printerName, bool on, CancellationToken ct = default)
            => Send(printerName, $"SET PEEL {(on ? "ON" : "OFF")}", ct);
        public Task SetCodePageAsync(string printerName, string codepage, CancellationToken ct = default)
            // common: 437, 850, 852, 1252, UTF-8 may be "UTF-8" or not supported; 244 Pro is usually single-byte cp
            => Send(printerName, $"CODEPAGE {codepage}", ct);

        // --- Sound ---
        public Task BeepAsync(string printerName, int times = 1, int ms = 100, CancellationToken ct = default)
            => Send(printerName, $"BEEP {Clamp(times, 1, 9)},{Clamp(ms, 10, 1000)}", ct);

        // --- Page buffer ---
        public Task ClearPageAsync(string printerName, CancellationToken ct = default)
            => Send(printerName, "CLS", ct);
        public Task PrintCopiesAsync(string printerName, int copies = 1, CancellationToken ct = default)
            => Send(printerName, $"PRINT {Clamp(copies, 1, 999)}", ct);

        // --- High-level one-shot label ---
        public async Task PrintQuickLabelAsync(string printerName, double widthMm, double heightMm,
                                               Action<TscLabelBuilder> build, int copies = 1, CancellationToken ct = default)
        {
            var lb = new TscLabelBuilder();
            build(lb);

            // Compose a complete TSPL job
            var sb = new StringBuilder();
            sb.AppendLine($"SIZE {Fm(widthMm)},{Fm(heightMm)}"); // mm
            sb.AppendLine("GAP 2,0");                 // safe default; adjust in UI as needed
            sb.AppendLine("DENSITY 8");               // moderate heat
            sb.AppendLine("SPEED 3");                 // moderate speed
            sb.AppendLine("DIRECTION 1");             // top-feed common
            sb.AppendLine("REFERENCE 0,0");
            sb.AppendLine("CLS");
            sb.Append(lb.Body);                       // TEXT/BARCODE/QRCODE etc.
            sb.AppendLine($"PRINT {Clamp(copies, 1, 999)}");

            await _raw.SendTsplAsync(printerName, sb.ToString(), ct);
        }

        // --- helpers ---
        private Task Send(string printerName, string cmd, CancellationToken ct)
            => _raw.SendTsplAsync(printerName, cmd, ct);

        private static string Fm(double mm) => mm.ToString("0.##", CultureInfo.InvariantCulture);
        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }

    /// <summary>
    /// Tiny helper to build TSPL label bodies (add TEXT, BARCODE, QRCODE, BOX, LINE, etc.)
    /// Units: x,y in dots; for TTP-244 Pro at 203 dpi, ~8 dots = 1 mm.
    /// </summary>
    public sealed class TscLabelBuilder
    {
        public StringBuilder Body { get; } = new();

        public TscLabelBuilder Text(int x, int y, string font = "3", int rotation = 0, int xMul = 1, int yMul = 1, string text = "")
        {
            // TEXT x,y,"font",rotation,x_mul,y_mul,"content"
            Body.AppendLine($"TEXT {x},{y},\"{font}\",{rotation},{xMul},{yMul},\"{Escape(text)}\"");
            return this;
        }

        public TscLabelBuilder Barcode(int x, int y, string type, int heightDots, int readable, int rotation, int narrow, int wide, string data)
        {
            // BARCODE x,y,"type",height,readable,rotation,narrow,wide,"content"
            Body.AppendLine($"BARCODE {x},{y},\"{type}\",{heightDots},{readable},{rotation},{narrow},{wide},\"{Escape(data)}\"");
            return this;
        }

        public TscLabelBuilder QrCode(int x, int y, string ecc = "H", int cell = 6, string data = "")
        {
            // QRCODE x,y,ECC,cell,mode,rotation,"content"
            Body.AppendLine($"QRCODE {x},{y},\"{ecc}\",{cell},A,0,\"{Escape(data)}\"");
            return this;
        }

        public TscLabelBuilder Box(int x, int y, int x2, int y2, int thickness)
        {
            // BOX x,y,x_end,y_end,thickness
            Body.AppendLine($"BOX {x},{y},{x2},{y2},{thickness}");
            return this;
        }

        public TscLabelBuilder Line(int x, int y, int length, int thickness)
        {
            // LINE x,y,length,thickness
            Body.AppendLine($"LINE {x},{y},{length},{thickness}");
            return this;
        }

        private static string Escape(string s) => s?.Replace("\"", "\\\"") ?? "";
    }
}
