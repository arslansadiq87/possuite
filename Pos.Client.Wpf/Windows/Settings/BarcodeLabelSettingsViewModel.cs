using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Domain.Entities;
using System.Threading;
//using Pos.Persistence.Services;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class BarcodeLabelSettingsViewModel : ObservableObject
{
    

    private readonly ILookupService _lookup;
    private readonly IBarcodeLabelSettingsService _svc;
    private readonly ILabelPrintService? _labelPrinter;

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


    public int[] DpiOptions { get; } = new[] { 203, 300 };
    public string[] CodeTypes { get; } = new[] { "Code128", "EAN13", "UPCA" };

    partial void OnLabelWidthMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnLabelHeightMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnMarginLeftMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnMarginTopMmChanged(int value) => RefreshPreviewDebounced();
    partial void OnDpiChanged(int value) => RefreshPreviewDebounced();
    partial void OnCodeTypeChanged(string value) => RefreshPreviewDebounced();
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

    public BarcodeLabelSettingsViewModel(
    ILookupService lookup,
    IBarcodeLabelSettingsService svc,
    ILabelPrintService? labelPrinter = null)
    {
        _lookup = lookup;
        _svc = svc;
        _labelPrinter = labelPrinter;
        _ = InitAsync();
    }


    private async Task InitAsync()
    {
        var outlets = await _lookup.GetOutletsAsync();
        Outlets.Clear();
        foreach (var o in outlets)
            Outlets.Add(o);

        await LoadAsync();
    }

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
        BusinessName = string.IsNullOrWhiteSpace(_loaded.BusinessName) ? "My Business" : _loaded.BusinessName;
        BusinessXmm = _loaded.BusinessXmm;
        BusinessYmm = _loaded.BusinessYmm;


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


        await _svc.SaveAsync(s);
        _loaded = s;
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

        var outletId = IsGlobal ? (int?)null : SelectedOutlet?.Id;
        var settings = await _svc.GetAsync(outletId);
        await _labelPrinter.PrintSampleAsync(settings);
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
                    ShowBusinessName, BusinessName, BusinessXmm, BusinessYmm
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
