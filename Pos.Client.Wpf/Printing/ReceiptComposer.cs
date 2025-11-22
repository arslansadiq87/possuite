// Pos.Client.Wpf/Printing/ReceiptComposer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Pos.Client.Wpf.Models; // CartLine
using Pos.Domain.Entities;   // Sale, ReceiptTemplate

namespace Pos.Client.Wpf.Printing
{
    public enum TextAlign { Left, Center, Right }

    public interface IBlock { }

    public sealed class ReceiptLayout
    {
        public int PaperWidthDots { get; init; } = 576; // 80mm default; 58mm ~384
        public List<IBlock> Blocks { get; } = new();
    }

    public sealed class TextBlockRun : IBlock
    {
        public string Text { get; init; } = "";
        public bool Bold { get; init; } = false;
        public double ScaleX { get; init; } = 1;   // printer scaling intent (preview may ignore)
        public double ScaleY { get; init; } = 1;
        public TextAlign Align { get; init; } = TextAlign.Left;
        public double? FontSizePt { get; init; }   // preview-only; printer uses ScaleX/ScaleY
        public bool Mono { get; init; } = true;
    }

    public sealed class RuleBlock : IBlock
    {
        public int ThicknessPx { get; init; } = 1;
    }

    public sealed class SpacerBlock : IBlock
    {
        public int HeightPx { get; init; } = 8;
    }

    public static class ReceiptComposer
    {
        public static ReceiptLayout ComposeSale(
            Sale sale,
            IEnumerable<CartLine> cart,
            ReceiptTemplate tpl,
            string storeName,
            string cashier,
            string? salesman)
        {
            // Use template paper width; default dots for 80mm / 58mm
            var layout = new ReceiptLayout
            {
                PaperWidthDots = (tpl?.PaperWidthMm ?? 80) >= 80 ? 576 : 384
            };

            // ---- Header (simple, safe defaults) ----
            layout.Blocks.Add(new TextBlockRun
            {
                Text = string.IsNullOrWhiteSpace(storeName) ? "My Store" : storeName,
                Bold = true,
                Align = TextAlign.Center,
                // FontSizePt is preview-only; keep null if you don't have a fancy preview yet
                FontSizePt = null,
                ScaleX = 1,
                ScaleY = 1
            });

            layout.Blocks.Add(new SpacerBlock { HeightPx = 6 });
            layout.Blocks.Add(new RuleBlock());

            // ---- Body: lines (safe fields only) ----
            foreach (var l in cart ?? Enumerable.Empty<CartLine>())
            {
                if (!string.IsNullOrWhiteSpace(l.DisplayName))
                    layout.Blocks.Add(new TextBlockRun { Text = l.DisplayName, Align = TextAlign.Left, Mono = true });

                // qty/unit/line total
                layout.Blocks.Add(new TextBlockRun
                {
                    Text = $"x{l.Qty} @ {l.UnitNet:0.##}",
                    Align = TextAlign.Left,
                    Mono = true
                });
                layout.Blocks.Add(new TextBlockRun
                {
                    Text = l.LineTotal.ToString("0.##"),
                    Align = TextAlign.Right,
                    Mono = true
                });
            }

            layout.Blocks.Add(new RuleBlock());

            // ---- Totals ----
            var grand = (cart ?? Enumerable.Empty<CartLine>()).Sum(x => x.LineTotal);
            layout.Blocks.Add(new TextBlockRun
            {
                Text = $"Grand Total  {grand:0.00}",
                Align = TextAlign.Right,
                Bold = true,
                Mono = true
            });

            // If you want a basic footer line, keep it static for now (no tpl.FooterNote)
            layout.Blocks.Add(new SpacerBlock { HeightPx = 4 });
            layout.Blocks.Add(new TextBlockRun
            {
                Text = "Thank you!",
                Align = TextAlign.Center,
                Mono = true
            });

            return layout;
        }
    }
}
