// Pos.Client.Wpf/Printing/Layout/ReceiptLayout.cs
public sealed class ReceiptLayout
{
    public int PaperWidthDots { get; init; } = 576; // 80mm typical (58mm = 384)
    public List<IBlock> Blocks { get; } = new();
}

public interface IBlock { }

public enum TextAlign { Left, Center, Right }
public sealed class TextBlockRun : IBlock
{
    public string Text { get; init; } = "";
    public bool Bold { get; init; }
    public double ScaleX { get; init; } = 1; // 1,2 (double width)
    public double ScaleY { get; init; } = 1; // 1,2 (double height)
    public TextAlign Align { get; init; } = TextAlign.Left;
    public double? FontSizePt { get; init; } // preview-only (screen points)
    public bool Mono { get; init; } = true;
}

public sealed class RuleBlock : IBlock { public int ThicknessPx { get; init; } = 1; }

public enum BarcodeSymbologys { Code128, Ean13, Qr }
public sealed class BarcodeBlock : IBlock
{
    public BarcodeSymbologys Symbology { get; init; }
    public string Data { get; init; } = "";
    public int HeightPx { get; init; } = 80; // for 1D barcodes
    public TextAlign Align { get; init; } = TextAlign.Center;
}

public sealed class ImageBlock : IBlock
{
    public byte[] PixelsOrPng { get; init; } = Array.Empty<byte>(); // logo
    public int? TargetWidthPx { get; init; }
    public TextAlign Align { get; init; } = TextAlign.Center;
}

public sealed class SpacerBlock : IBlock { public int HeightPx { get; init; } = 8; }
