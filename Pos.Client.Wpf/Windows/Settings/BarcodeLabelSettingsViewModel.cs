using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Domain.Entities;
using System.Threading;
using Pos.Domain.Services;
using Pos.Domain.Settings;               // InvoiceSettingsLocal
using System.Windows;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class BarcodeLabelSettingsViewModel : ObservableObject
{
    

    private readonly ILookupService _lookup;
    private readonly IBarcodeLabelSettingsService _svc;
    private readonly ILabelPrintService? _labelPrinter;
    private readonly Pos.Client.Wpf.Printing.ITscCommandService? _tsc;
    private readonly IInvoiceSettingsLocalService _invoiceLocal;
    private readonly ITerminalContext _ctx;
    private readonly IIdentitySettingsService _identity;

    // Preview zoom factor (applies to the whole preview surface via LayoutTransform)
    [ObservableProperty] private double previewZoom = 2.0;

    private CancellationTokenSource? _previewCts;
    public void ForceRefreshPreview() => RefreshPreviewDebounced();

    public ObservableCollection<Outlet> Outlets { get; } = new();
    public ObservableCollection<string> InstalledPrinters { get; } =
        new(PrinterSettings.InstalledPrinters.Cast<string>());

    public double PreviewWidthDip => LabelWidthMm * (96.0 / 25.4);
    public double PreviewHeightDip => LabelHeightMm * (96.0 / 25.4);

    // mm <-> DIP helpers
    private double MmToDip(double mm) => mm * (96.0 / 25.4);
    private double DipToMm(double dip) => dip / (96.0 / 25.4);

    // Proxies that XAML binds to (Canvas coords)
    public double NameXDip { get => MmToDip(NameXmm); set => NameXmm = DipToMm(value); }
    public double NameYDip { get => MmToDip(NameYmm); set => NameYmm = DipToMm(value); }
    public double PriceXDip { get => MmToDip(PriceXmm); set => PriceXmm = DipToMm(value); }
    public double PriceYDip { get => MmToDip(PriceYmm); set => PriceYmm = DipToMm(value); }
    public double SkuXDip { get => MmToDip(SkuXmm); set => SkuXmm = DipToMm(value); }
    public double SkuYDip { get => MmToDip(SkuYmm); set => SkuYmm = DipToMm(value); }
    
    public double BusinessXDip { get => MmToDip(BusinessXmm); set => BusinessXmm = DipToMm(value); }
    public double BusinessYDip { get => MmToDip(BusinessYmm); set => BusinessYmm = DipToMm(value); }


    // NEW: free-positioned coords (mm)
    [ObservableProperty] private double nameXmm = 4.0;
    [ObservableProperty] private double nameYmm = 18.0;
    [ObservableProperty] private double priceXmm = 4.0;
    [ObservableProperty] private double priceYmm = 22.0;
    [ObservableProperty] private double skuXmm = 4.0;
    [ObservableProperty] private double skuYmm = 26.0;

    // Barcode block
    [ObservableProperty] private double barcodeMarginLeftMm = 2.0;
    [ObservableProperty] private double barcodeMarginTopMm = 2.0;
    [ObservableProperty] private double barcodeMarginRightMm = 2.0;
    [ObservableProperty] private double barcodeMarginBottomMm = 12.0;
    [ObservableProperty] private double barcodeHeightMm = 0.0; // 0 => auto by margins

    // Business name
    [ObservableProperty] private bool showBusinessName = false;
    [ObservableProperty] private string businessName = "My Business";
    [ObservableProperty] private double businessXmm = 4.0;
    [ObservableProperty] private double businessYmm = 6.0;

    
    [ObservableProperty] private double barcodeZoomPct = 100.0; // 30..200 UI range recommended
    partial void OnBarcodeZoomPctChanged(double value) => RefreshPreviewDebounced();
    public double BarcodeZoomScale => Math.Clamp(BarcodeZoomPct / 100.0, 0.3, 2.0);

    public int[] DpiOptions { get; } = new[] { 203, 300 };
    public string[] CodeTypes { get; } = new[] { "Code128", "EAN13", "EAN8", "UPCA" };

    partial void OnLabelWidthMmChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewWidthDip));   // notify the Image/Canvas width binding
        RefreshPreviewDebounced();
    }

    partial void OnLabelHeightMmChanged(int value)
    {
        OnPropertyChanged(nameof(PreviewHeightDip));  // notify the Image/Canvas height binding
        RefreshPreviewDebounced();
    }

    partial void OnMarginLeftMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnMarginTopMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnDpiChanged(int value) => RefreshPreviewDebounced();
    //partial void OnCodeTypeChanged(string value) => RefreshPreviewDebounced();
    // Put this inside the same class, replacing your existing one-liner.
    // Requires: using System.Linq;
    partial void OnCodeTypeChanged(string value)
    {
        string v = (value ?? "").ToUpperInvariant();
        string digits = new string((SampleCode ?? "").Where(char.IsDigit).ToArray());

        if (v is "EAN8" or "EAN-8")
        {
            // EAN-8 expects 7 or 8 digits. If 7, preview will compute the check digit.
            if (digits.Length != 7 && digits.Length != 8)
                SampleCode = "5512345"; // 7-digit seed; preview adds check
        }
        else if (v is "EAN13" or "EAN-13")
        {
            // EAN-13 expects 12 digits (check digit computed in preview/writer)
            if (digits.Length != 12)
                SampleCode = "590123412345";
        }
        else if (v is "UPCA" or "UPC-A")
        {
            // UPC-A expects 11 digits (check digit computed)
            if (digits.Length != 11)
                SampleCode = "04210000526";
        }
        // Code128 accepts any content; keep whatever the user had.

        RefreshPreviewDebounced();
    }

    partial void OnShowNameChanged(bool value) => RefreshPreviewDebounced();
    partial void OnShowPriceChanged(bool value) => RefreshPreviewDebounced();
    partial void OnShowSkuChanged(bool value) => RefreshPreviewDebounced();
    partial void OnFontSizePtChanged(int value) => RefreshPreviewDebounced();
    partial void OnSampleCodeChanged(string value) => RefreshPreviewDebounced();
    partial void OnSampleNameChanged(string value) => RefreshPreviewDebounced();
    partial void OnSamplePriceChanged(string value) => RefreshPreviewDebounced();
    partial void OnSampleSkuChanged(string value) => RefreshPreviewDebounced();
    partial void OnNameXmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnNameYmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnPriceXmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnPriceYmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnSkuXmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnSkuYmmChanged(double value) => RefreshPreviewDebounced();

    partial void OnBarcodeMarginLeftMmChanged(double value) => RefreshPreviewDebounced();
    partial void OnBarcodeMarginTopMmChanged(double value) => RefreshPreviewDebounced();
    partial void OnBarcodeMarginRightMmChanged(double value) => RefreshPreviewDebounced();
    partial void OnBarcodeMarginBottomMmChanged(double value) => RefreshPreviewDebounced();
    partial void OnBarcodeHeightMmChanged(double value) => RefreshPreviewDebounced();

    partial void OnShowBusinessNameChanged(bool value) => RefreshPreviewDebounced();
    partial void OnBusinessNameChanged(string value) => RefreshPreviewDebounced();
    partial void OnBusinessXmmChanged(double value) => RefreshPreviewDebounced();
    partial void OnBusinessYmmChanged(double value) => RefreshPreviewDebounced();

    // Rebuild preview when any alignment changes
    partial void OnNameAlignChanged(string value) => RefreshPreviewDebounced();
    partial void OnPriceAlignChanged(string value) => RefreshPreviewDebounced();
    partial void OnSkuAlignChanged(string value) => RefreshPreviewDebounced();
    partial void OnBusinessAlignChanged(string value) => RefreshPreviewDebounced();


    [ObservableProperty] private ImageSource? previewImage;

    [ObservableProperty] private string sampleCode = "123456789012";
    [ObservableProperty] private string sampleName = "Sample Item";
    [ObservableProperty] private string samplePrice = "Rs 999";
    [ObservableProperty] private string sampleSku = "SKU-001";

    [ObservableProperty] private bool isGlobal = true;
    [ObservableProperty] private Outlet? selectedOutlet;

    // geometry
    [ObservableProperty] private int labelWidthMm = 38;
    [ObservableProperty] private int labelHeightMm = 25;
    [ObservableProperty] private int horizontalGapMm = 2;
    [ObservableProperty] private int verticalGapMm = 2;
    [ObservableProperty] private int marginLeftMm = 2;
    [ObservableProperty] private int marginTopMm = 2;
    [ObservableProperty] private int columns = 1;
    [ObservableProperty] private int rows = 1;

    // content
    [ObservableProperty] private string codeType = "Code128";
    [ObservableProperty] private bool showName = true;
    [ObservableProperty] private bool showPrice = true;
    [ObservableProperty] private bool showSku = false;
    [ObservableProperty] private int fontSizePt = 9;
    [ObservableProperty] private int dpi = 203;

    // printer
    [ObservableProperty] private string? printerName;

    private BarcodeLabelSettings? _loaded;

    // ctor param: add ITscCommandService tsc = null
    public BarcodeLabelSettingsViewModel(
        IInvoiceSettingsLocalService invoiceLocal,
        ITerminalContext ctx,
        IIdentitySettingsService identity,
        ILookupService lookup,
        IBarcodeLabelSettingsService svc,
        ILabelPrintService? labelPrinter = null,
        Pos.Client.Wpf.Printing.ITscCommandService? tsc = null)
    {
        _invoiceLocal = invoiceLocal;
        _ctx = ctx;
        _identity = identity;
        _lookup = lookup;
        _svc = svc;
        _labelPrinter = labelPrinter;
        _tsc = tsc;
        _ = InitAsync();
    }

    // --- TSC Media / Setup (defaults matching common 38x25mm roll) ---
    [ObservableProperty] private double tscMediaWidthMm = 38.0;
    [ObservableProperty] private double tscMediaHeightMm = 25.0;
    [ObservableProperty] private double tscGapMm = 2.0;
    [ObservableProperty] private double tscGapOffsetMm = 0.0;

    [ObservableProperty] private int tscSpeed = 3;     // 1..5 (model dependent)
    [ObservableProperty] private int tscDensity = 8;   // 0..15
    [ObservableProperty] private int tscDirection = 1; // 0/1
    [ObservableProperty] private bool tscTear = true;
    [ObservableProperty] private bool tscPeel = false; // model must support peel

    [ObservableProperty] private string tscCodepage = "1252"; // 437, 850, 852, 1252 etc.
    [ObservableProperty] private int tscBeepTimes = 1;   // 1..9
    [ObservableProperty] private int tscBeepMs = 100;    // 10..1000
    public string[] QuickBarcodeTypes { get; } = new[] { "CODE128", "EAN13", "EAN8", "UPCA", "QRCODE" };

    [ObservableProperty] private string quickText = "Sample";
    [ObservableProperty] private string quickBarcodeType = "CODE128";
    [ObservableProperty] private string quickBarcodeData = "123456789012";
    [ObservableProperty] private double quickBarcodeHeightMm = 20.0; // used for linear barcodes
    [ObservableProperty] private int quickCopies = 1;

    // How big each nudge is (in dots). 203 dpi ≈ 8 dots/mm. So 16 = ~2 mm
    //[ObservableProperty] private int nudgeDots = 16;
    [ObservableProperty] private double nudgeMm = 2.0;

    // Convert millimeters to device dots using current DPI
    private int ToDots(double mm)
    {
        // guard + clamp: at least 1 dot to ensure a move
        var dots = (int)Math.Round(mm * Dpi / 25.4);
        return Math.Max(1, dots);
    }

    [RelayCommand]
    private async Task TscApplyMediaAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        {
            System.Windows.MessageBox.Show("TSC service not available or no printer selected.");
            return;
        }

        var p = PrinterName!;
        await _tsc.SetSizeMmAsync(p, TscMediaWidthMm, TscMediaHeightMm);
        await _tsc.SetGapMmAsync(p, TscGapMm, TscGapOffsetMm);
        await _tsc.SetSpeedAsync(p, Math.Clamp(TscSpeed, 1, 5));
        await _tsc.SetDensityAsync(p, Math.Clamp(TscDensity, 0, 15));
        await _tsc.SetDirectionAsync(p, TscDirection == 0 ? 0 : 1);
        await _tsc.SetTearAsync(p, TscTear);
        await _tsc.SetPeelAsync(p, TscPeel);
        await _tsc.SetCodePageAsync(p, TscCodepage);
        await _tsc.HomeAsync(p);
    }

    [RelayCommand]
    private async Task TscFeedMmAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.FeedAsync(PrinterName!, ToDots(NudgeMm));
    }

    [RelayCommand]
    private async Task TscBackfeedMmAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.BackfeedAsync(PrinterName!, ToDots(NudgeMm));
    }

    [RelayCommand]
    private async Task TscNextLabelAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.FormfeedAsync(PrinterName!);
    }

    [RelayCommand]
    private async Task TscHomeAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.HomeAsync(PrinterName!);
    }

    [RelayCommand]
    private async Task TscAutodetectAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.AutoDetectAsync(PrinterName!);
    }

    [RelayCommand]
    private async Task TscBeepAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }
        await _tsc.BeepAsync(PrinterName!, Math.Clamp(TscBeepTimes, 1, 9), Math.Clamp(TscBeepMs, 10, 1000));
    }

    [RelayCommand]
    private async Task TscQuickLabelAsync()
    {
        if (_tsc == null || string.IsNullOrWhiteSpace(PrinterName))
        { System.Windows.MessageBox.Show("TSC service not available or no printer selected."); return; }

        var p = PrinterName!;
        var w = TscMediaWidthMm;
        var h = TscMediaHeightMm;

        await _tsc.PrintQuickLabelAsync(p, w, h, lb =>
        {
            // Simple layout: text at top, barcode under it
            // 203dpi => ~8 dots per mm; we’ll place at 4mm left margin
            int x = ToDots(4);
            int yText = ToDots(3);
            int yBarcode = ToDots(10);

            // Text (font "3" is readable on 203dpi)
            lb.Text(x, yText, font: "3", rotation: 0, xMul: 1, yMul: 1, text: QuickText ?? "");

            if (QuickBarcodeType.Equals("QRCODE", StringComparison.OrdinalIgnoreCase))
            {
                // QRCODE cell size auto-ish (6 is medium); place it below text
                lb.QrCode(x, yBarcode, ecc: "H", cell: 6, data: QuickBarcodeData ?? "");
            }
            else
            {
                // Linear barcode height in dots from mm
                int bh = ToDots(Math.Max(8.0, QuickBarcodeHeightMm)); // min ~8mm for readability
                                                                      // TSC BARCODE narrow/wide: 2/4 are safe for 203dpi; readable=1 prints human text
                lb.Barcode(
                    x, yBarcode,
                    type: QuickBarcodeType.ToUpperInvariant(),
                    heightDots: bh,
                    readable: 1,
                    rotation: 0,
                    narrow: 2,
                    wide: 4,
                    data: QuickBarcodeData ?? "123456789012");
            }
        }, copies: Math.Clamp(QuickCopies, 1, 999));
    }


    private async Task InitAsync()
    {
        var local = await _invoiceLocal.GetForCounterWithFallbackAsync(_ctx.CounterId);
        SelectedLabelPrinter = local.LabelPrinterName ?? "(not selected in Invoice Settings)";

        OnPropertyChanged(nameof(IsTscSelected));

        // 2) Business name from Identity settings (respect scope)
        // Prefer outlet-specific; fallback to global
        var id = await _identity.GetAsync(_ctx.CounterId);
        BusinessName = id?.OutletDisplayName ?? string.Empty;
        var outlets = await _lookup.GetOutletsAsync();
        Outlets.Clear();
        foreach (var o in outlets)
            Outlets.Add(o);

        await LoadAsync();
    }

    // -------- Read-only selected printer label ----------
    [ObservableProperty] private string? selectedLabelPrinter;
    public bool IsTscSelected =>
        !string.IsNullOrWhiteSpace(SelectedLabelPrinter) &&
        SelectedLabelPrinter!.IndexOf("TSC", StringComparison.OrdinalIgnoreCase) >= 0;

    // -------- Business name (content comes from Identity), checkbox decides visibility ----------
    //[ObservableProperty] private bool showBusinessName;
    //[ObservableProperty] private string? businessName;

    // -------- Alignment options ----------
    public IReadOnlyList<string> AlignOptions { get; } = new[] { "Left", "Center", "Right" };

    [ObservableProperty] private string nameAlign = "Left";
    [ObservableProperty] private string priceAlign = "Left";
    [ObservableProperty] private string skuAlign = "Left";
    [ObservableProperty] private string businessAlign = "Left";

    // DIP proxies already exist, keep them:
    // public double NameXDip { get => MmToDip(NameXmm); set => NameXmm = DipToMm(value); } etc.

    // When building preview, pass the new align values (wire below in the calling code).


    partial void OnIsGlobalChanged(bool value) => _ = LoadAsync();
    partial void OnSelectedOutletChanged(Outlet? value) { if (!IsGlobal) _ = LoadAsync(); }

    private async Task LoadAsync()
    {
        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;
        _loaded = await _svc.GetAsync(outletId);

        // snapshot
        PrinterName = _loaded.PrinterName;
        Dpi = _loaded.Dpi;
        LabelWidthMm = _loaded.LabelWidthMm;
        LabelHeightMm = _loaded.LabelHeightMm;
        HorizontalGapMm = _loaded.HorizontalGapMm;
        VerticalGapMm = _loaded.VerticalGapMm;
        MarginLeftMm = _loaded.MarginLeftMm;
        MarginTopMm = _loaded.MarginTopMm;
        Columns = _loaded.Columns;
        Rows = _loaded.Rows;

        CodeType = _loaded.CodeType;
        ShowName = _loaded.ShowName;
        ShowPrice = _loaded.ShowPrice;
        ShowSku = _loaded.ShowSku;
        FontSizePt = _loaded.FontSizePt;
        NameXmm = _loaded.NameXmm;
        NameYmm = _loaded.NameYmm;
        PriceXmm = _loaded.PriceXmm;
        PriceYmm = _loaded.PriceYmm;
        SkuXmm = _loaded.SkuXmm;
        SkuYmm = _loaded.SkuYmm;

        BarcodeMarginLeftMm = _loaded.BarcodeMarginLeftMm;
        BarcodeMarginTopMm = _loaded.BarcodeMarginTopMm;
        BarcodeMarginRightMm = _loaded.BarcodeMarginRightMm;
        BarcodeMarginBottomMm = _loaded.BarcodeMarginBottomMm;
        BarcodeHeightMm = _loaded.BarcodeHeightMm;

        ShowBusinessName = _loaded.ShowBusinessName;
        var id = await _identity.GetAsync(_ctx.OutletId); // outlet-scoped if available
        BusinessName = id?.OutletDisplayName
                       ?? "My Business";
        BusinessXmm = _loaded.BusinessXmm;
        BusinessYmm = _loaded.BusinessYmm;
        BarcodeZoomPct = _loaded.BarcodeZoomPct ?? 100.0;



        RefreshPreviewDebounced();

    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;
        var s = _loaded ?? new BarcodeLabelSettings { OutletId = outletId };

        s.OutletId = outletId;
        s.PrinterName = PrinterName;
        s.Dpi = Dpi;

        s.LabelWidthMm = LabelWidthMm;
        s.LabelHeightMm = LabelHeightMm;
        s.HorizontalGapMm = HorizontalGapMm;
        s.VerticalGapMm = VerticalGapMm;
        s.MarginLeftMm = MarginLeftMm;
        s.MarginTopMm = MarginTopMm;
        s.Columns = Math.Max(1, Columns);
        s.Rows = Math.Max(1, Rows);

        s.CodeType = CodeType;
        s.ShowName = ShowName;
        s.ShowPrice = ShowPrice;
        s.ShowSku = ShowSku;
        s.FontSizePt = FontSizePt;
        s.NameXmm = NameXmm;
        s.NameYmm = NameYmm;
        s.PriceXmm = PriceXmm;
        s.PriceYmm = PriceYmm;
        s.SkuXmm = SkuXmm;
        s.SkuYmm = SkuYmm;
        s.BarcodeMarginLeftMm = BarcodeMarginLeftMm;
        s.BarcodeMarginTopMm = BarcodeMarginTopMm;
        s.BarcodeMarginRightMm = BarcodeMarginRightMm;
        s.BarcodeMarginBottomMm = BarcodeMarginBottomMm;
        s.BarcodeHeightMm = BarcodeHeightMm;

        s.ShowBusinessName = ShowBusinessName;
        s.BusinessName = BusinessName;
        s.BusinessXmm = BusinessXmm;
        s.BusinessYmm = BusinessYmm;
        s.BarcodeZoomPct = BarcodeZoomPct;


        await _svc.SaveAsync(s);
        _loaded = s;

        MessageBox.Show("Label settings saved.",
            "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private Task ResetAsync() => LoadAsync();

    [RelayCommand]
    private async Task TestPrintAsync()
    {
        if (_labelPrinter == null)
        {
            System.Windows.MessageBox.Show("Label print service not wired yet.", "Info");
            return;
        }

        var s = new BarcodeLabelSettings
        {
            OutletId = IsGlobal ? (int?)null : SelectedOutlet?.Id,
            PrinterName = PrinterName,
            Dpi = Dpi,
            LabelWidthMm = LabelWidthMm,
            LabelHeightMm = LabelHeightMm,
            HorizontalGapMm = HorizontalGapMm,
            VerticalGapMm = VerticalGapMm,
            MarginLeftMm = MarginLeftMm,
            MarginTopMm = MarginTopMm,
            Columns = Math.Max(1, Columns),
            Rows = Math.Max(1, Rows),
            CodeType = CodeType,
            ShowName = ShowName,
            ShowPrice = ShowPrice,
            ShowSku = ShowSku,
            FontSizePt = FontSizePt,
            NameXmm = NameXmm,
            NameYmm = NameYmm,
            PriceXmm = PriceXmm,
            PriceYmm = PriceYmm,
            SkuXmm = SkuXmm,
            SkuYmm = SkuYmm,
            BarcodeMarginLeftMm = BarcodeMarginLeftMm,
            BarcodeMarginTopMm = BarcodeMarginTopMm,
            BarcodeMarginRightMm = BarcodeMarginRightMm,
            BarcodeMarginBottomMm = BarcodeMarginBottomMm,
            BarcodeHeightMm = BarcodeHeightMm,
            ShowBusinessName = ShowBusinessName,
            BusinessName = BusinessName,
            BusinessXmm = BusinessXmm,
            BusinessYmm = BusinessYmm
        };

        await _labelPrinter.PrintSampleAsync(
            s,
            sampleCode: SampleCode,
            sampleName: SampleName,
            samplePrice: SamplePrice,
            sampleSku: SampleSku,
            showBusinessName: ShowBusinessName,
            businessName: BusinessName
        );
    }



    private async void RefreshPreviewDebounced()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        try
        {
            await Task.Delay(120, token); // debounce
            token.ThrowIfCancellationRequested();

            // IMPORTANT: Do the WPF rendering on the UI (STA) thread.
            var img = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                BarcodePreviewBuilder.Build(
                    LabelWidthMm, LabelHeightMm,
                    MarginLeftMm, MarginTopMm,
                    Dpi,
                    CodeType, SampleCode,
                    ShowName, ShowPrice, ShowSku,
                    SampleName, SamplePrice, SampleSku,
                    FontSizePt,
                    NameXmm, NameYmm,
                    PriceXmm, PriceYmm,
                    SkuXmm, SkuYmm,
                    // NEW barcode block controls
                    BarcodeMarginLeftMm, BarcodeMarginTopMm,
                    BarcodeMarginRightMm, BarcodeMarginBottomMm,
                    BarcodeHeightMm,
                    // NEW business line
                    ShowBusinessName, BusinessName, BusinessXmm, BusinessYmm,
                    // NEW: alignments
                    nameAlign: NameAlign,
                    priceAlign: PriceAlign,
                    skuAlign: SkuAlign,
                    businessAlign: BusinessAlign,
                    barcodeZoom: BarcodeZoomScale   // <--- NEW

                ));



            PreviewImage = img;
        }
        catch (OperationCanceledException) { /* debounced */ }
        catch (Exception ex)
        {
            // Optional: log so failures aren’t silent
            System.Diagnostics.Debug.WriteLine($"Preview error: {ex}");
        }
    }


}
