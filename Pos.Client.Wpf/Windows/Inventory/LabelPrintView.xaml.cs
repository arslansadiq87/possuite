// Pos.Client.Wpf/Windows/Inventory/LabelPrintView.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Windows.Settings; // BarcodePreviewBuilder
using Pos.Client.Wpf.Services;
using Pos.Domain.DTO;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using System.Globalization;
using System.Windows.Media.Imaging;



namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class LabelPrintView : UserControl
    {
        // Purchase rows (NO AMOUNTS)
        private sealed class UiPurchaseRow
        {
            public int PurchaseId { get; init; }
            public string DocNoOrId { get; init; } = "";
            public string Supplier { get; init; } = "";
            public string TsLocal { get; init; } = "";
            public string Status { get; init; } = "";
        }

        // Lines (NO COST)
        private sealed class UiLineRow
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public decimal Qty { get; set; }

            // Sale price (from item master, NOT purchase cost)
            public decimal Price { get; set; }

            // Effective barcode code (Barcode or fallback to SKU)
            public string Barcode { get; set; } = "";

            public int PrintQty { get; set; }
        }

        // Custom text label positioning (9 positions)
        private enum CustomTextPosition
        {
            TopLeft,
            TopCenter,
            TopRight,
            MiddleLeft,
            MiddleCenter,
            MiddleRight,
            BottomLeft,
            BottomCenter,
            BottomRight
        }


        // Single label payload
        private sealed class LabelPrintItem
        {
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
            public string PriceText { get; init; } = "";
            public string Sku { get; init; } = "";
            public string Barcode { get; set; } = "";

            public bool IsCustomText { get; init; }
            public string CustomText { get; init; } = "";
            public CustomTextPosition CustomPosition { get; init; } = CustomTextPosition.MiddleCenter;
            public double CustomFontSizePt { get; init; } = 0.0;
            public bool CustomBold { get; init; }
            public bool CustomItalic { get; init; }
            // NEW: Font family name for custom text
            public string? CustomFontFamilyName { get; init; }
        }

        private readonly ObservableCollection<UiPurchaseRow> _purchases = new();
        private readonly ObservableCollection<UiLineRow> _lines = new();

        private readonly IPurchaseCenterReadService _purchaseRead;
        private readonly IItemsReadService _itemsRead;
        private readonly IBarcodeLabelSettingsService _labelSettings;
        private readonly IInvoiceSettingsLocalService _invoiceLocal;
        private readonly ITerminalContext _terminal;
        private readonly BarcodeLabelSettingsViewModel _tscVm;
        public BarcodeLabelSettingsViewModel TscVm => _tscVm;

        public LabelPrintView()
        {
            InitializeComponent();

            var sp = App.Services;
            _purchaseRead = sp.GetRequiredService<IPurchaseCenterReadService>();
            _itemsRead = sp.GetRequiredService<IItemsReadService>();
            _labelSettings = sp.GetRequiredService<IBarcodeLabelSettingsService>();
            _invoiceLocal = sp.GetRequiredService<IInvoiceSettingsLocalService>();
            _terminal = sp.GetRequiredService<ITerminalContext>();
            _tscVm = sp.GetRequiredService<BarcodeLabelSettingsViewModel>();

            PurchasesGrid.ItemsSource = _purchases;
            LinesGrid.ItemsSource = _lines;

            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;
            LoadSystemFonts();

            Loaded += async (_, __) => await LoadPurchasesAsync();
        }

        private void LoadSystemFonts()
        {
            // Get all installed system font families, sorted by name
            var fonts = Fonts.SystemFontFamilies
                             .OrderBy(ff => ff.Source)
                             .ToList();

            CustomFontFamilyBox.ItemsSource = fonts;

            // Try to select the default Windows UI font if present; fallback to first
            var defaultFamily = SystemFonts.MessageFontFamily;
            var match = fonts.FirstOrDefault(f => string.Equals(f.Source, defaultFamily.Source, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                CustomFontFamilyBox.SelectedItem = match;
            else if (fonts.Count > 0)
                CustomFontFamilyBox.SelectedIndex = 0;
        }


        // --------- TAB 1: BY PURCHASE ---------

        private async Task LoadPurchasesAsync()
        {
            _purchases.Clear();
            _lines.Clear();

            DateTime? fromUtc = FromDate.SelectedDate?.Date.ToUniversalTime();
            DateTime? toUtc = ToDate.SelectedDate?.AddDays(1).Date.ToUniversalTime();
            var term = (SearchBox.Text ?? string.Empty).Trim();

            // Warehouse: only FINAL, non-voided, with Doc #
            bool wantFinal = true;
            bool wantDraft = false;
            bool wantVoided = false;
            bool onlyWithDoc = true;

            var rows = await _purchaseRead.SearchAsync(
                fromUtc, toUtc, term, wantFinal, wantDraft, wantVoided, onlyWithDoc);

            foreach (var r in rows)
            {
                _purchases.Add(new UiPurchaseRow
                {
                    PurchaseId = r.PurchaseId,
                    DocNoOrId = r.DocNoOrId,
                    Supplier = r.Supplier,
                    TsLocal = r.TsLocal,
                    Status = r.Status
                });
            }
        }

        private async void SearchPurchases_Click(object sender, RoutedEventArgs e)
        {
            await LoadPurchasesAsync();
        }

        private async void PurchasesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _lines.Clear();

            if (PurchasesGrid.SelectedItem is not UiPurchaseRow sel)
                return;

            // Build item index once (for sale price + barcode)
            var index = await _itemsRead.BuildIndexAsync();
            var byId = index.ToDictionary(i => i.Id);

            var rows = await _purchaseRead.GetPreviewLinesAsync(sel.PurchaseId);

            foreach (var r in rows)
            {
                var defaultPrintQty = (int)Math.Max(
                    0,
                    Math.Round(r.Qty, MidpointRounding.AwayFromZero));

                decimal salePrice = 0m;
                string barcode = "";

                if (byId.TryGetValue(r.ItemId, out var meta))
                {
                    salePrice = meta.Price; // POS sale price
                    barcode = string.IsNullOrWhiteSpace(meta.Barcode)
                                ? meta.Sku
                                : meta.Barcode;
                }

                _lines.Add(new UiLineRow
                {
                    ItemId = r.ItemId,
                    Sku = r.Sku,
                    DisplayName = r.DisplayName,
                    Qty = r.Qty,
                    Price = salePrice,
                    Barcode = barcode,
                    PrintQty = defaultPrintQty
                });
            }
        }



        private async void PrintFromPurchase_Click(object sender, RoutedEventArgs e)
        {
            if (PurchasesGrid.SelectedItem is not UiPurchaseRow)
            {
                MessageBox.Show("Select a purchase first.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_lines.Count == 0)
            {
                MessageBox.Show("No items loaded for this purchase.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var linesToPrint = _lines.Where(l => l.PrintQty > 0).ToList();
            if (linesToPrint.Count == 0)
            {
                MessageBox.Show("Set Print Qty > 0 for at least one item.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Build item index (Id -> ItemIndexDto) so we can get barcode & price
                var index = await _itemsRead.BuildIndexAsync();
                var byId = index.ToDictionary(i => i.Id);

                var labels = new List<LabelPrintItem>();

                foreach (var line in linesToPrint)
                {
                    var code = string.IsNullOrWhiteSpace(line.Barcode)
                        ? line.Sku
                        : line.Barcode;

                    var name = line.DisplayName;
                    var priceText = line.Price.ToString("0.00");
                    var sku = line.Sku;

                    for (int i = 0; i < line.PrintQty; i++)
                    {
                        labels.Add(new LabelPrintItem
                        {
                            Code = code,
                            Name = name,
                            PriceText = priceText,
                            Sku = sku
                        });
                    }
                }


                await PrintLabelsAsync(labels, CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Label print failed:\n" + ex.Message, "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --------- TAB 2: BY ITEM ---------

        private async void PrintFromItem_Click(object sender, RoutedEventArgs e)
        {
            // 1) try selected item
            ItemIndexDto? item = ItemSearch.SelectedItem;

            // 2) If not selected (user just typed), resolve via query
            if (item is null)
            {
                var query = (ItemSearch.Query ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(query))
                {
                    try
                    {
                        item = await _itemsRead.FindOneAsync(query);
                    }
                    catch
                    {
                        // ignore; we'll error below if still null
                    }
                }
            }

            if (item is null)
            {
                MessageBox.Show("Select an item (or scan / type code and press Enter) before printing.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Keep labels & preview in sync
            await RefreshItemSelectionAsync(item, CancellationToken.None);

            if (!int.TryParse(ItemPrintQtyBox.Text, out var qty) || qty <= 0)
            {
                MessageBox.Show("Enter a valid Print Qty (positive integer).", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var code = string.IsNullOrWhiteSpace(item.Barcode)
                ? item.Sku
                : item.Barcode;

            var name = item.DisplayName;
            var priceText = item.Price.ToString("0.00");
            var sku = item.Sku;

            var labels = new List<LabelPrintItem>();
            for (int i = 0; i < qty; i++)
            {
                labels.Add(new LabelPrintItem
                {
                    Code = code,
                    Name = name,
                    PriceText = priceText,
                    Sku = sku
                });
            }

            try
            {
                await PrintLabelsAsync(labels, CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Label print failed:\n" + ex.Message, "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // --------- TAB 3: CUSTOM TEXT ---------

        private async void PrintCustomLabel_Click(object sender, RoutedEventArgs e)
        {
            var text = (CustomTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("Enter label text before printing.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!int.TryParse(CustomPrintQtyBox.Text, out var qty) || qty <= 0)
            {
                MessageBox.Show("Enter a valid Print Qty (positive integer).", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double fontSize;
            if (!double.TryParse(CustomFontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out fontSize) || fontSize <= 0)
            {
                fontSize = 10.0; // fallback
            }

            var position = GetCustomPosition();
            bool bold = CustomBoldCheck.IsChecked == true;
            bool italic = CustomItalicCheck.IsChecked == true;
            string fontFamilyName = GetCustomFontFamilyName();

            var labels = new List<LabelPrintItem>();
            for (int i = 0; i < qty; i++)
            {
                labels.Add(new LabelPrintItem
                {
                    IsCustomText = true,
                    CustomText = text,
                    CustomPosition = position,
                    CustomFontSizePt = fontSize,
                    CustomBold = bold,
                    CustomItalic = italic,
                    CustomFontFamilyName = fontFamilyName   // ⬅ NEW
                });
            }

            try
            {
                await PrintLabelsAsync(labels, CancellationToken.None);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Label print failed:\n" + ex.Message, "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetCustomFontFamilyName()
        {
            if (CustomFontFamilyBox?.SelectedItem is FontFamily ff)
                return ff.Source;

            // Fallback to system UI font
            return SystemFonts.MessageFontFamily.Source;
        }



        private CustomTextPosition GetCustomPosition()
        {
            return CustomPositionBox.SelectedIndex switch
            {
                0 => CustomTextPosition.TopLeft,
                1 => CustomTextPosition.TopCenter,
                2 => CustomTextPosition.TopRight,
                3 => CustomTextPosition.MiddleLeft,
                4 => CustomTextPosition.MiddleCenter,
                5 => CustomTextPosition.MiddleRight,
                6 => CustomTextPosition.BottomLeft,
                7 => CustomTextPosition.BottomCenter,
                8 => CustomTextPosition.BottomRight,
                _ => CustomTextPosition.MiddleCenter
            };
        }

        private async void CustomTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            await RefreshCustomPreviewAsync(CancellationToken.None);
        }

        private async void CustomFontSizeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            await RefreshCustomPreviewAsync(CancellationToken.None);
        }

        private async void CustomPositionBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await RefreshCustomPreviewAsync(CancellationToken.None);
        }

        private async void CustomFormatCheckBox_Click(object sender, RoutedEventArgs e)
        {
            await RefreshCustomPreviewAsync(CancellationToken.None);
        }

        private async Task RefreshCustomPreviewAsync(CancellationToken ct)
        {
            if (!IsLoaded) return;

            var text = (CustomTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                CustomLabelPreview.Source = null;
                return;
            }

            double fontSize;
            if (!double.TryParse(CustomFontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out fontSize) || fontSize <= 0)
            {
                fontSize = 10.0;
            }

            var position = GetCustomPosition();
            bool bold = CustomBoldCheck.IsChecked == true;
            bool italic = CustomItalicCheck.IsChecked == true;
            string fontFamilyName = GetCustomFontFamilyName();


            try
            {
                var settings = await _labelSettings.GetAsync(_terminal.OutletId, ct);

                var item = new LabelPrintItem
                {
                    IsCustomText = true,
                    CustomText = text,
                    CustomPosition = position,
                    CustomFontSizePt = fontSize,
                    CustomBold = bold,
                    CustomItalic = italic,
                    CustomFontFamilyName = fontFamilyName   // ⬅ NEW

                };

                var img = BuildCustomLabelImage(settings, item);
                CustomLabelPreview.Source = img;
            }
            catch
            {
                CustomLabelPreview.Source = null;
            }
        }

        private async void CustomFontFamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await RefreshCustomPreviewAsync(CancellationToken.None);
        }


        private ImageSource BuildCustomLabelImage(BarcodeLabelSettings s, LabelPrintItem item)
        {
            double dipPerMm = 96.0 / 25.4;
            double widthDip = Math.Max(32, s.LabelWidthMm * dipPerMm);
            double heightDip = Math.Max(32, s.LabelHeightMm * dipPerMm);

            double pxPerDip = s.Dpi / 96.0;
            int widthPx = Math.Max(32, (int)Math.Round(widthDip * pxPerDip));
            int heightPx = Math.Max(32, (int)Math.Round(heightDip * pxPerDip));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background
                var rect = new Rect(0, 0, widthDip, heightDip);
                dc.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), rect);

                double fontSizePt = item.CustomFontSizePt > 0 ? item.CustomFontSizePt : s.FontSizePt;
                double fontSizeDip = fontSizePt * 96.0 / 72.0;
                var fontFamilyName = string.IsNullOrWhiteSpace(item.CustomFontFamilyName)
                    ? SystemFonts.MessageFontFamily.Source
                    : item.CustomFontFamilyName;

                var typeface = new Typeface(
                    new FontFamily(fontFamilyName),
                    item.CustomItalic ? FontStyles.Italic : FontStyles.Normal,
                    item.CustomBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal);


                double pixelsPerDip = s.Dpi / 96.0;

                var ft = new FormattedText(
                    item.CustomText ?? string.Empty,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSizeDip,
                    Brushes.Black,
                    pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(0, widthDip - 4.0),
                    TextAlignment = TextAlignment.Left
                };

                double textWidth = Math.Min(ft.Width, widthDip - 4.0);
                double textHeight = Math.Min(ft.Height, heightDip - 4.0);

                double x = 2.0;
                double y = 2.0;

                switch (item.CustomPosition)
                {
                    case CustomTextPosition.TopLeft:
                        x = 2.0;
                        y = 2.0;
                        break;
                    case CustomTextPosition.TopCenter:
                        x = (widthDip - textWidth) / 2.0;
                        y = 2.0;
                        break;
                    case CustomTextPosition.TopRight:
                        x = widthDip - textWidth - 2.0;
                        y = 2.0;
                        break;
                    case CustomTextPosition.MiddleLeft:
                        x = 2.0;
                        y = (heightDip - textHeight) / 2.0;
                        break;
                    case CustomTextPosition.MiddleCenter:
                        x = (widthDip - textWidth) / 2.0;
                        y = (heightDip - textHeight) / 2.0;
                        break;
                    case CustomTextPosition.MiddleRight:
                        x = widthDip - textWidth - 2.0;
                        y = (heightDip - textHeight) / 2.0;
                        break;
                    case CustomTextPosition.BottomLeft:
                        x = 2.0;
                        y = heightDip - textHeight - 2.0;
                        break;
                    case CustomTextPosition.BottomCenter:
                        x = (widthDip - textWidth) / 2.0;
                        y = heightDip - textHeight - 2.0;
                        break;
                    case CustomTextPosition.BottomRight:
                        x = widthDip - textWidth - 2.0;
                        y = heightDip - textHeight - 2.0;
                        break;
                }

                dc.DrawText(ft, new Point(x, y));
            }

            var bmp = new RenderTargetBitmap(widthPx, heightPx, s.Dpi, s.Dpi, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // --------- CORE PRINTING (shared) ---------

        private async Task PrintLabelsAsync(
            IReadOnlyList<LabelPrintItem> items,
            CancellationToken ct)
        {
            if (items.Count == 0)
            {
                MessageBox.Show("Nothing to print.", "Labels",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var outletId = _terminal.OutletId;
            var settings = await _labelSettings.GetAsync(outletId, ct);

            // Resolve printer name:
            // 1) BarcodeLabelSettings.PrinterName
            // 2) InvoiceSettingsLocal.LabelPrinterName
            var local = await _invoiceLocal.GetForCounterWithFallbackAsync(_terminal.CounterId, ct);

            var printerName = !string.IsNullOrWhiteSpace(settings.PrinterName)
                ? settings.PrinterName
                : local?.LabelPrinterName;

            if (string.IsNullOrWhiteSpace(printerName))
            {
                MessageBox.Show(
                    "No label printer configured. Set it in Backstage → Barcode Labels or Invoice Settings.",
                    "Labels",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            settings.PrinterName = printerName;

            var doc = BuildLabelsDocument(settings, items);
            var queue = ResolvePrintQueue(printerName);
            var pd = new PrintDialog
            {
                PrintQueue = queue
            };

            double pageWdip = doc.DocumentPaginator.PageSize.Width;
            double pageHdip = doc.DocumentPaginator.PageSize.Height;

            var ticket = queue.UserPrintTicket ?? new PrintTicket();
            ticket.PageMediaSize = new PageMediaSize(PageMediaSizeName.Unknown, pageWdip, pageHdip);

            var result = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, ticket);
            pd.PrintTicket = result.ValidatedPrintTicket ?? ticket;

            pd.PrintDocument(doc.DocumentPaginator, "POS – Item Labels");
        }

        private FixedDocument BuildLabelsDocument(
    BarcodeLabelSettings s,
    IReadOnlyList<LabelPrintItem> items)
        {
            double mmToDip = 96.0 / 25.4;

            int columns = Math.Max(1, s.Columns);
            int rows = Math.Max(1, s.Rows);

            double labelWidthDip = s.LabelWidthMm * mmToDip;
            double labelHeightDip = s.LabelHeightMm * mmToDip;
            double hGapDip = s.HorizontalGapMm * mmToDip;
            double vGapDip = s.VerticalGapMm * mmToDip;

            double pageWidthDip = columns * labelWidthDip + (columns - 1) * hGapDip;
            double pageHeightDip = rows * labelHeightDip + (rows - 1) * vGapDip;

            var doc = new FixedDocument();
            doc.DocumentPaginator.PageSize = new Size(pageWidthDip, pageHeightDip);

            // 🔴 EXACT SAME ZOOM LOGIC AS SETTINGS + SAMPLE PRINT
            double zoom = s.BarcodeZoomPct.HasValue
                ? Math.Clamp(s.BarcodeZoomPct.Value / 100.0, 0.3, 2.0)
                : 1.0;

            int idx = 0;
            while (idx < items.Count)
            {
                var page = new FixedPage
                {
                    Width = pageWidthDip,
                    Height = pageHeightDip
                };

                for (int r = 0; r < rows && idx < items.Count; r++)
                {
                    for (int c = 0; c < columns && idx < items.Count; c++)
                    {
                        var item = items[idx++];
                        ImageSource imgSrc;
                        if (item.IsCustomText)
                        {
                            imgSrc = BuildCustomLabelImage(s, item);
                        }
                        else
                        { 
                            imgSrc = BarcodePreviewBuilder.Build(
                            labelWidthMm: s.LabelWidthMm,
                            labelHeightMm: s.LabelHeightMm,
                            marginLeftMm: s.MarginLeftMm,
                            marginTopMm: s.MarginTopMm,
                            dpi: s.Dpi,
                            codeType: s.CodeType,
                            payload: item.Code,
                            showName: s.ShowName,
                            showPrice: s.ShowPrice,
                            showSku: s.ShowSku,
                            nameText: item.Name,
                            priceText: item.PriceText,
                            skuText: item.Sku,
                            fontSizePt: s.FontSizePt,
                            nameXmm: s.NameXmm,
                            nameYmm: s.NameYmm,
                            priceXmm: s.PriceXmm,
                            priceYmm: s.PriceYmm,
                            skuXmm: s.SkuXmm,
                            skuYmm: s.SkuYmm,
                            barcodeMarginLeftMm: s.BarcodeMarginLeftMm,
                            barcodeMarginTopMm: s.BarcodeMarginTopMm,
                            barcodeMarginRightMm: s.BarcodeMarginRightMm,
                            barcodeMarginBottomMm: s.BarcodeMarginBottomMm,
                            barcodeHeightMm: s.BarcodeHeightMm,
                            showBusinessName: s.ShowBusinessName,
                            businessName: s.BusinessName ?? string.Empty,
                            businessXmm: s.BusinessXmm,
                            businessYmm: s.BusinessYmm,
                            // alignments – we’re using defaults in builder for now
                            barcodeZoom: zoom   // ✅ exact zoom from settings
                        );
                    }
                        var img = new Image
                        {
                            Source = imgSrc,
                            Width = labelWidthDip,
                            Height = labelHeightDip
                        };

                        double x = c * (labelWidthDip + hGapDip);
                        double y = r * (labelHeightDip + vGapDip);

                        FixedPage.SetLeft(img, x);
                        FixedPage.SetTop(img, y);
                        page.Children.Add(img);
                    }
                }

                var pc = new PageContent();
                ((IAddChild)pc).AddChild(page);
                doc.Pages.Add(pc);
            }

            return doc;
        }

        private static PrintQueue ResolvePrintQueue(string printerName)
        {
            var server = new LocalPrintServer();
            var queues = server.GetPrintQueues();

            // Try exact match
            var q = queues.FirstOrDefault(p =>
                string.Equals(p.FullName, printerName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase));

            if (q != null) return q;

            // Relaxed contains-based match
            q = queues.FirstOrDefault(p =>
                p.FullName.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.Name.IndexOf(printerName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (q != null) return q;

            throw new InvalidOperationException($"Printer not found: {printerName}");
        }

        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            if (ItemSearch.SelectedItem is ItemIndexDto item)
            {
                await RefreshItemSelectionAsync(item, CancellationToken.None);
            }
        }

        private async void ClearItemSelection_Click(object sender, RoutedEventArgs e)
        {
            //ItemSearch.SelectedItem = null;
            ItemSearch.Query = string.Empty;

            await RefreshItemSelectionAsync(null, CancellationToken.None);
        }

        private async Task RefreshItemSelectionAsync(ItemIndexDto? item, CancellationToken ct)
        {
            if (item == null)
            {
                LblItemName.Text = string.Empty;
                LblItemSku.Text = string.Empty;
                LblItemBarcode.Text = string.Empty;
                LblItemPrice.Text = string.Empty;
                ItemBarcodePreview.Source = null;
                return;
            }

            LblItemName.Text = item.DisplayName;
            LblItemSku.Text = item.Sku;
            LblItemPrice.Text = item.Price.ToString("0.00");

            var code = string.IsNullOrWhiteSpace(item.Barcode)
                ? item.Sku
                : item.Barcode;

            LblItemBarcode.Text = string.IsNullOrWhiteSpace(code) ? "(no barcode)" : code;

            try
            {
                var settings = await _labelSettings.GetAsync(_terminal.OutletId, ct);

                // SAME ZOOM as settings page
                double zoom = settings.BarcodeZoomPct.HasValue
                    ? Math.Clamp(settings.BarcodeZoomPct.Value / 100.0, 0.3, 2.0)
                    : 1.0;

                var imgSrc = BarcodePreviewBuilder.Build(
                    labelWidthMm: settings.LabelWidthMm,
                    labelHeightMm: settings.LabelHeightMm,
                    marginLeftMm: settings.MarginLeftMm,
                    marginTopMm: settings.MarginTopMm,
                    dpi: settings.Dpi,
                    codeType: settings.CodeType,
                    payload: code,
                    showName: settings.ShowName,
                    showPrice: settings.ShowPrice,
                    showSku: settings.ShowSku,
                    nameText: item.DisplayName,
                    priceText: item.Price.ToString("0.00"),
                    skuText: item.Sku,
                    fontSizePt: settings.FontSizePt,
                    nameXmm: settings.NameXmm,
                    nameYmm: settings.NameYmm,
                    priceXmm: settings.PriceXmm,
                    priceYmm: settings.PriceYmm,
                    skuXmm: settings.SkuXmm,
                    skuYmm: settings.SkuYmm,
                    barcodeMarginLeftMm: settings.BarcodeMarginLeftMm,
                    barcodeMarginTopMm: settings.BarcodeMarginTopMm,
                    barcodeMarginRightMm: settings.BarcodeMarginRightMm,
                    barcodeMarginBottomMm: settings.BarcodeMarginBottomMm,
                    barcodeHeightMm: settings.BarcodeHeightMm,
                    showBusinessName: settings.ShowBusinessName,
                    businessName: settings.BusinessName ?? string.Empty,
                    businessXmm: settings.BusinessXmm,
                    businessYmm: settings.BusinessYmm,
                    barcodeZoom: zoom
                );

                ItemBarcodePreview.Source = imgSrc;
            }
            catch
            {
                ItemBarcodePreview.Source = null;
            }
        }

    }
}
