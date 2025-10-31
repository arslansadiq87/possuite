namespace Pos.Domain.Entities;

public class BarcodeLabelSettings
{
    public int Id { get; set; }
    public int? OutletId { get; set; }       // per outlet (or null = global)
    public string? PrinterName { get; set; } // usually a label printer
    // Sheet/roll geometry (mm)
    public int LabelWidthMm { get; set; } = 38;
    public int LabelHeightMm { get; set; } = 25;
    public int HorizontalGapMm { get; set; } = 2;
    public int VerticalGapMm { get; set; } = 2;
    public int MarginLeftMm { get; set; } = 2;
    public int MarginTopMm { get; set; } = 2;
    // Grid (for sheet printers; for rolls, keep Columns=1, Rows=1)
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    // Content
    public string CodeType { get; set; } = "Code128"; // EAN13|UPC|Code128…
    public bool ShowName { get; set; } = true;
    public bool ShowPrice { get; set; } = true;
    public bool ShowSku { get; set; } = false;
    public int FontSizePt { get; set; } = 9;
    public int Dpi { get; set; } = 203;               // 203 or 300 typical
    // NEW: Free-positioned text coordinates (mm from label's top-left)
    public double NameXmm { get; set; } = 4.0;
    public double NameYmm { get; set; } = 18.0;
    public double PriceXmm { get; set; } = 4.0;
    public double PriceYmm { get; set; } = 22.0;
    public double SkuXmm { get; set; } = 4.0;
    public double SkuYmm { get; set; } = 26.0;
    // Barcode block placement (mm)
    public double BarcodeMarginLeftMm { get; set; } = 2.0;
    public double BarcodeMarginTopMm { get; set; } = 2.0;
    public double BarcodeMarginRightMm { get; set; } = 2.0;
    public double BarcodeMarginBottomMm { get; set; } = 12.0; // leaves room for text by default

    /// <summary>
    /// Optional fixed height for the barcode block (mm). If > 0, overrides the height implied by margins,
    /// but still clipped to label height. Width always uses Left/Right margins.
    /// </summary>
    public double BarcodeHeightMm { get; set; } = 0.0;

    // Business name text (content + visibility + position)
    public bool ShowBusinessName { get; set; } = false;
    public string? BusinessName { get; set; } = null;
    public double BusinessXmm { get; set; } = 4.0;
    public double BusinessYmm { get; set; } = 6.0;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

}
