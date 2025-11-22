using System;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Collections.Generic;
using Pos.Domain.Entities;
using System.Windows.Markup;

namespace Pos.Client.Wpf.Printing
{
    public static class WindowsReceiptPrintService
    {
        public static bool IsVirtualWindowsPrinter(string printerName)
        {
            try
            {
                var server = new LocalPrintServer();
                var queues = server.GetPrintQueues();
                var q = queues.FirstOrDefault(p =>
                       p.FullName.Equals(printerName, StringComparison.OrdinalIgnoreCase)
                    || p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                    ?? queues.FirstOrDefault(p =>
                       p.FullName.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0
                    || p.Name.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (q == null) return false;

                var s = ((q.Name ?? "") + " " + (q.QueueDriver?.Name ?? "")).ToLowerInvariant();
                return s.Contains("microsoft print to pdf") || s.Contains("xps") || s.Contains("onenote");
            }
            catch { return false; }
        }

        public static Task PrintAsync(
            string printerName,
            ReceiptTemplate tpl,
            IReadOnlyList<string> lines,
            CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var app = Application.Current;

                if (app?.Dispatcher != null)
                {
                    await app.Dispatcher.InvokeAsync(() =>
                    {
                        DoWindowsPrint(printerName, tpl, lines);
                    });
                }
                else
                {
                    Exception? ex = null;
                    var th = new Thread(() =>
                    {
                        try { DoWindowsPrint(printerName, tpl, lines); }
                        catch (Exception e) { ex = e; }
                    });
                    th.SetApartmentState(ApartmentState.STA);
                    th.Start();
                    th.Join();
                    if (ex != null) throw ex;
                }
            }, ct);
        }

        // -------- internal helpers --------

        private static double MmToDiu(double mm) => (mm / 25.4) * 96.0;

        private static string Sanitize(string s)
        {
            // keep CR/LF; drop all other < 0x20 control chars (typical ESC/POS artifacts)
            return new string(s.Where(ch => ch == '\n' || ch == '\r' || ch >= ' ').ToArray());
        }

        private static void DoWindowsPrint(string printerName, ReceiptTemplate tpl, IReadOnlyList<string> rawLines)
        {
            // 1) PAGE SIZE: exact 80mm or 58mm width, generous height
            var paperMm = tpl.PaperWidthMm >= 80 ? 80 : 58;
            var width = MmToDiu(paperMm);
            // Height: enough for typical receipts (adjust if you want tighter pagination)
            var height = MmToDiu(300); // ~30 cm

            // 2) Build one TextBlock with NoWrap monospace text
            var text = string.Join("\n", rawLines.Select(Sanitize));

            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = tpl.PaperWidthMm >= 80 ? 13 : 12,
                Text = text,
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(6, 6, 6, 6), // small margins
                Width = width - 12
            };

            // 3) FixedPage inside FixedDocument ensures printer honors size
            var page = new FixedPage
            {
                Width = width,
                Height = height,
                Background = Brushes.White
            };
            page.Children.Add(tb);
            FixedPage.SetLeft(tb, 0);
            FixedPage.SetTop(tb, 0);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);

            var doc = new FixedDocument
            {
                DocumentPaginator =
                {
                    PageSize = new Size(width, height)
                }
            };
            doc.Pages.Add(pageContent);

            // 4) Route to the exact print queue with a print ticket that forces size
            var server = new LocalPrintServer();
            var q = server.GetPrintQueues().FirstOrDefault(p =>
                   p.FullName.Equals(printerName, StringComparison.OrdinalIgnoreCase)
                || p.Name.Equals(printerName, StringComparison.OrdinalIgnoreCase))
                ?? server.GetPrintQueues().FirstOrDefault(p =>
                   p.FullName.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0
                || p.Name.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? throw new InvalidOperationException($"Printer \"{printerName}\" not found.");

            // Clone ticket and force media size + orientation + borderless if supported
            var ticket = q.UserPrintTicket?.Clone() ?? new PrintTicket();
            ticket.PageOrientation = PageOrientation.Portrait;
            ticket.PageBorderless = PageBorderless.Borderless;
            ticket.PageMediaSize = new PageMediaSize(width, height); // DIU: 1/96"

            var writer = PrintQueue.CreateXpsDocumentWriter(q);
            writer.Write(((IDocumentPaginatorSource)doc).DocumentPaginator, ticket);
        }
    }
}
