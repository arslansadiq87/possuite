using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;         
using Pos.Persistence.Services;
namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class VariantBatchDialog : Window
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public enum Mode { Batch, EditSingle, Sequential }
        private bool _suppressValidation;          // prevents validation during programmatic focus
        private BarcodeRow? _newlyAddedRow;        // track the just-added manual row
        private bool _justRemovedRow;              // suppress one validation cycle after removal
        private bool _isClosing;  // NEW: avoid validation popups during window close
        public bool IsStandaloneMode { get; private set; }
        public Func<Item, System.Threading.Tasks.Task<bool>>? SaveOneAsync { get; set; }
        public bool SaveImmediately { get; set; } = true;
        private readonly Mode _mode;
        private Product? _product;
        private readonly List<Item> _staged = new(); // will be returned as CreatedItems
        private int _seqSku;      // sequences persist while dialog is open
        private int _seqBarcode;
        private string Axis1Single => Axis1SingleBox?.Text?.Trim() ?? "";
        private string Axis2Single => Axis2SingleBox?.Text?.Trim() ?? "";
        private readonly ObservableCollection<VariantRow> _rows = new();
        public IReadOnlyList<Item> CreatedItems { get; private set; } = Array.Empty<Item>();
        public VariantBatchDialog() : this(Mode.Batch) { }
        public VariantBatchDialog(Mode mode)
        {
            _mode = mode;
            InitializeComponent();
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
            BarcodesGrid.ItemsSource = _barcodeRows;
            BarcodesGrid.RowEditEnding += BarcodesGrid_RowEditEnding;
            BarcodesGrid.CellEditEnding += BarcodesGrid_CellEditEnding;
            BarcodesGrid.CurrentCellChanged += BarcodesGrid_CurrentCellChanged;
            BarcodesGrid.PreviewKeyDown += BarcodesGrid_PreviewKeyDown;
            _barcodeRows.CollectionChanged += (_, __) => HookRowEvents();
            HookRowEvents();
            if (AutoBarcodeBox.IsChecked == true && !_barcodeRows.Any(r => r.IsPrimary))
            {
                var code = GenerateEan13(BarcodePrefix, _seqBarcode);
                _barcodeRows.Add(new BarcodeRow
                {
                    Code = code,
                    Label = "1 pc",
                    Symbology = BarcodeSymbology.Ean13,
                    QuantityPerScan = 1,
                    IsPrimary = true
                });
                _seqBarcode++;
            }
            AutoBarcodeBox.Checked += (_, __) => EnsureDefaultPrimary();
            AutoBarcodeBox.Unchecked += (_, __) => { };
            this.Closing += VariantBatchDialog_Closing;
        }

        private void VariantBatchDialog_Closing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;
            _suppressValidation = true;
            try
            {
                var incomplete = _barcodeRows.Where(r => string.IsNullOrWhiteSpace(r.Code) || r.QuantityPerScan <= 0).ToList();
                if (incomplete.Any())
                {
                    var result = MessageBox.Show(
                        "There are incomplete barcode rows.\n\nClose and discard them?",
                        "Unsaved entries",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var r in incomplete) _barcodeRows.Remove(r);
                        return;
                    }
                    e.Cancel = true;
                    var row = incomplete.First();
                    var col = FirstMissingField(row) ?? ColCode;
                    Dispatcher.BeginInvoke(new Action(() => SelectRowForEdit(row, col)),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            finally
            {
                if (e.Cancel) { _suppressValidation = false; _isClosing = false; }
            }
        }

        private void SetSelectedSymbology(BarcodeSymbology sym)
        {
            string tag = sym switch
            {
                BarcodeSymbology.Ean8 => "Ean8",
                BarcodeSymbology.UpcA => "UpcA",
                BarcodeSymbology.Code128 => "Code128",
                _ => "Ean13"
            };
            foreach (var obj in BarcodeSymbologyBox.Items)
            {
                if (obj is ComboBoxItem cbi && (cbi.Tag as string) == tag)
                {
                    BarcodeSymbologyBox.SelectedItem = cbi;
                    return;
                }
            }
            BarcodeSymbologyBox.SelectedIndex = 0;
        }

        private void ApplyStickyDefaults()
        {
            var prefs = Services.UserPrefsService.Load();
            SetSelectedSymbology(prefs.LastSymbology);
            SkuPrefixBox.Text = string.IsNullOrWhiteSpace(prefs.LastSkuPrefix) ? "ITEM" : prefs.LastSkuPrefix;
            SkuStartBox.Text = Math.Max(1, prefs.LastSkuStart).ToString(CultureInfo.InvariantCulture);
            BarcodePrefixBox.Text = string.IsNullOrWhiteSpace(prefs.LastBarcodePrefix) ? "978000" : prefs.LastBarcodePrefix;
            BarcodeStartBox.Text = Math.Max(1, prefs.LastBarcodeStart).ToString(CultureInfo.InvariantCulture);
        }

        private void PersistStickyFromCurrentControls()
        {
            var currentSym = GetSelectedSymbology();
            Services.UserPrefsService.Save(p =>
            {
                p.LastSymbology = currentSym;
                p.LastSkuPrefix = SkuPrefixBox.Text?.Trim() ?? "ITEM";
                p.LastSkuStart = ParseInt(SkuStartBox.Text, 1);
                p.LastSkuNext = _seqSku;                       // what to use next
                p.LastBarcodePrefix = BarcodePrefixBox.Text?.Trim() ?? "978000";
                p.LastBarcodeStart = ParseInt(BarcodeStartBox.Text, 1);
                p.LastBarcodeNext = _seqBarcode;

            });
        }

        private void HookRowEvents()
        {
            foreach (var row in _barcodeRows)
            {
                row.PropertyChanged -= Row_PropertyChanged;
                row.PropertyChanged += Row_PropertyChanged;
            }
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not BarcodeRow changed) return;
            if (e.PropertyName == nameof(BarcodeRow.IsPrimary) && changed.IsPrimary)
            {
                foreach (var r in _barcodeRows.Where(r => !ReferenceEquals(r, changed)))
                    if (r.IsPrimary) r.IsPrimary = false;
            }
        }

        private void EnsureDefaultPrimary()
        {
            if (!_barcodeRows.Any(r => r.IsPrimary))
            {
                var code = GenerateEan13(BarcodePrefix, _seqBarcode);
                _barcodeRows.Add(new BarcodeRow
                {
                    Code = code,
                    Label = "1 pc",
                    Symbology = BarcodeSymbology.Ean13,
                    QuantityPerScan = 1,
                    IsPrimary = true
                });
                _seqBarcode++;
            }
        }

        public class BarcodeRow : INotifyPropertyChanged
        {
            private string _code = "";
            private string? _label;
            private BarcodeSymbology _symbology = BarcodeSymbology.Ean13;
            private int _qty = 1;
            private bool _isPrimary;
            public string Code { get => _code; set { if (_code != value) { _code = value; OnPropertyChanged(nameof(Code)); } } }
            public string? Label { get => _label; set { if (_label != value) { _label = value; OnPropertyChanged(nameof(Label)); } } }
            public BarcodeSymbology Symbology { get => _symbology; set { if (_symbology != value) { _symbology = value; OnPropertyChanged(nameof(Symbology)); } } }
            public int QuantityPerScan { get => _qty; set { if (_qty != value) { _qty = value; OnPropertyChanged(nameof(QuantityPerScan)); } } }

            public bool IsPrimary
            {
                get => _isPrimary;
                set
                {
                    if (_isPrimary != value)
                    {
                        _isPrimary = value;
                        OnPropertyChanged(nameof(IsPrimary));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly ObservableCollection<BarcodeRow> _barcodeRows = new();
//public IReadOnlyList<BarcodeRow> BarcodeRows => _barcodeRows;

//public List<BarcodeSymbology> BarcodeTypes { get; } =
//    new() { BarcodeSymbology.Ean13, BarcodeSymbology.Ean8, BarcodeSymbology.UpcA, BarcodeSymbology.Code128 };

        private Item BuildSingleItem()
        {
            var baseName = _product?.Name ?? (NameBox?.Text?.Trim() ?? "");
            if (string.IsNullOrWhiteSpace(baseName))
                throw new InvalidOperationException("Enter the item name.");
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Axis1NameBox.Text) && !string.IsNullOrWhiteSpace(Axis1Single))
                parts.Add($"{Axis1NameBox.Text}:{Axis1Single}");
            if (!string.IsNullOrWhiteSpace(Axis2NameBox.Text) && !string.IsNullOrWhiteSpace(Axis2Single))
                parts.Add($"{Axis2NameBox.Text}:{Axis2Single}");
            var suffix = parts.Count > 0 ? " — " + string.Join(", ", parts) : "";
            var now = DateTime.UtcNow;
            var item = new Item
            {
                ProductId = _product?.Id == 0 ? null : _product?.Id,
                Product = _product?.Id == 0 ? _product : null,
                Name = baseName + suffix,
                Sku = AutoSku ? $"{SkuPrefix}-{_seqSku:000}" : "",
                Price = Price,
                TaxCode = TaxCode,
                DefaultTaxRatePct = TaxPct,
                TaxInclusive = TaxInclusive,
                DefaultDiscountPct = DefaultDiscPct,
                DefaultDiscountAmt = DefaultDiscAmt,
                Variant1Name = string.IsNullOrWhiteSpace(Axis1NameBox.Text) ? null : Axis1NameBox.Text.Trim(),
                Variant1Value = string.IsNullOrWhiteSpace(Axis1Single) ? null : Axis1Single,
                Variant2Name = string.IsNullOrWhiteSpace(Axis2NameBox.Text) ? null : Axis2NameBox.Text.Trim(),
                Variant2Value = string.IsNullOrWhiteSpace(Axis2Single) ? null : Axis2Single,
                BrandId = IsStandaloneMode ? (int?)BrandBox.SelectedValue : _product?.BrandId,
                CategoryId = IsStandaloneMode ? (int?)CategoryBox.SelectedValue : _product?.CategoryId,
                IsActive = MarkActive,
                IsVoided = false,
                VoidedAtUtc = null,
                VoidedBy = null,
                UpdatedAt = now,
                Barcodes = _barcodeRows.Select(r => new ItemBarcode
                {
                    Code = r.Code?.Trim() ?? "",
                    Symbology = r.Symbology,
                    QuantityPerScan = Math.Max(1, r.QuantityPerScan),
                    IsPrimary = r.IsPrimary,
                    Label = string.IsNullOrWhiteSpace(r.Label) ? null : r.Label,
                    CreatedAt = now,
                    UpdatedAt = now
                }).Where(b => !string.IsNullOrEmpty(b.Code)).ToList()
            };
            if (AutoSku) _seqSku++;
            if (AutoBarcode) _seqBarcode++;
            return item;
        }

        private async void SaveAndAddAnother_Click(object sender, RoutedEventArgs e)
        {
            if (_mode != Mode.Sequential) return;
            var codes = _barcodeRows
        .Select(b => b.Code)
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s!.Trim())
        .ToList();
            await using (var db = await _dbf.CreateDbContextAsync())
            {
                var svc = new CatalogService(db);
                var conflicts = await svc.FindBarcodeConflictsAsync(codes, excludeItemId: null);
                if (conflicts.Count > 0)
                {
                    var lines = conflicts
                        .GroupBy(c => c.Code)
                        .Select(g =>
                        {
                            var x = g.First();
                            var owner = !string.IsNullOrWhiteSpace(x.ProductName)
                                        ? $"Product: {x.ProductName}, Variant: {x.ItemName}"
                                        : $"Item: {x.ItemName}";
                            return $"• {g.Key} → already used by {owner}";
                        });
                    MessageBox.Show(
                        "One or more barcodes are already in use:\n\n" +
                        string.Join("\n", lines) +
                        "\n\nPlease change these barcodes.",
                        "Duplicate barcode(s) found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return; // don’t proceed; user must fix
                }
            }
            SaveAddBtn.IsEnabled = false;
            try
            {
                var item = BuildSingleItem();
                if (SaveImmediately && SaveOneAsync != null)
                {
                    var ok = await SaveOneAsync(item);
                    if (!ok)
                    {
                        MessageBox.Show("Couldn’t save the variant. Please try again.");
                        return;
                    }
                    PersistStickyFromCurrentControls();
                }
                else
                {
                    _staged.Add(item);
                }
                _rows.Add(new VariantRow
                {
                    ExistingItemId = null,
                    Variant1Name = item.Variant1Name ?? "",
                    Variant1Value = item.Variant1Value ?? "",
                    Variant2Name = item.Variant2Name ?? "",
                    Variant2Value = item.Variant2Value ?? "",
                    Name = item.Name,
                    Sku = item.Sku,
                    Barcode = item.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                      ?? item.Barcodes?.FirstOrDefault()?.Code
                      ?? "",
                    Price = item.Price,
                    TaxCode = item.TaxCode,
                    DefaultTaxRatePct = item.DefaultTaxRatePct,
                    TaxInclusive = item.TaxInclusive,
                    DefaultDiscountPct = item.DefaultDiscountPct,
                    DefaultDiscountAmt = item.DefaultDiscountAmt,
                    IsActive = item.IsActive
                });

                Axis1SingleBox.Text = "";
                Axis2SingleBox.Text = "";
                Axis1SingleBox.Focus();
                Axis1SingleBox.CaretIndex = Axis1SingleBox.Text.Length;
                _barcodeRows.Clear();
                if (AutoBarcodeBox.IsChecked == true)
                    EnsureDefaultPrimary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                SaveAddBtn.IsEnabled = true;
            }
        }


        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!IsStandaloneMode && _product == null && _mode != Mode.Sequential)
            {
                MessageBox.Show("No product selected.");
                return;
            }
            if (_mode == Mode.Sequential)
            {
                // In standalone mode, we always consider there to be a "pending" item (name + optional barcodes),
                // even if Axis1/Axis2 single values are empty/hidden.
                bool hasPending = IsStandaloneMode
                    ? true
                    : (!string.IsNullOrWhiteSpace(Axis1Single) || !string.IsNullOrWhiteSpace(Axis2Single));

                if (hasPending)
                {
                    try { _staged.Add(BuildSingleItem()); }
                    catch (Exception ex) { MessageBox.Show(ex.Message); return; }
                }

                if (_staged.Count == 0)
                {
                    // Keep the old message for variant flow; standalone shouldn't reach here
                    MessageBox.Show(IsStandaloneMode ? "Enter the item name." : "No variants to save.");
                    return;
                }

                var allCodes = _staged
                    .SelectMany(it => it.Barcodes ?? Enumerable.Empty<ItemBarcode>())
                    .Select(b => b.Code)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim())
                    .ToList();

                await using (var db = await _dbf.CreateDbContextAsync())
                {
                    var svc = new CatalogService(db);
                    var conflicts = await svc.FindBarcodeConflictsAsync(allCodes, excludeItemId: null);
                    if (conflicts.Count > 0)
                    {
                        var lines = conflicts.GroupBy(c => c.Code).Select(g =>
                        {
                            var x = g.First();
                            var owner = !string.IsNullOrWhiteSpace(x.ProductName)
                                ? $"Product: {x.ProductName}, Variant: {x.ItemName}"
                                : $"Item: {x.ItemName}";
                            return $"• {g.Key} → already used by {owner}";
                        });
                        MessageBox.Show("One or more barcodes are already in use:\n\n" +
                                        string.Join("\n", lines) +
                                        "\n\nPlease change these barcodes.",
                                        "Duplicate barcode(s) found",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                CreatedItems = _staged.ToList();
                PersistStickyFromCurrentControls();
                DialogResult = true;
                return;
            }


            if (_mode == Mode.EditSingle)
            {
                if (_rows.Count != 1) { MessageBox.Show("Nothing to save."); return; }
                string v1Name = Axis1NameBox.Text?.Trim() ?? "";
                string v1Val = Axis1SingleBox.Text?.Trim() ?? "";
                string v2Name = Axis2NameBox.Text?.Trim() ?? "";
                string v2Val = Axis2SingleBox.Text?.Trim() ?? "";
                var pieces = new List<string>();
                if (!string.IsNullOrWhiteSpace(v1Name) && !string.IsNullOrWhiteSpace(v1Val)) pieces.Add($"{v1Name}:{v1Val}");
                if (!string.IsNullOrWhiteSpace(v2Name) && !string.IsNullOrWhiteSpace(v2Val)) pieces.Add($"{v2Name}:{v2Val}");
                string suffix = pieces.Count > 0 ? " — " + string.Join(", ", pieces) : "";
                string displayName = _product != null
                    ? (_product.Name + suffix)
                    : (NameBox?.Text?.Trim() ?? _rows[0].Name);

                var edited = new Item
                {
                    Id = _rows[0].ExistingItemId ?? 0,
                    ProductId = _product?.Id == 0 ? null : _product?.Id,
                    Product = _product?.Id == 0 ? _product : null,
                    Name = displayName,                   // updated from axes
                    Sku = _rows[0].Sku ?? "",
                    Price = ParseDec(PriceBox.Text),
                    TaxCode = string.IsNullOrWhiteSpace(TaxCodeBox.Text) ? null : TaxCodeBox.Text.Trim(),
                    DefaultTaxRatePct = ParseDec(TaxPctBox.Text),
                    TaxInclusive = TaxInclusiveBox.IsChecked == true,
                    DefaultDiscountPct = ParseNullableDec(DiscPctBox.Text),
                    DefaultDiscountAmt = ParseNullableDec(DiscAmtBox.Text),
                    Variant1Name = string.IsNullOrWhiteSpace(v1Name) ? null : v1Name,
                    Variant1Value = string.IsNullOrWhiteSpace(v1Val) ? null : v1Val,
                    Variant2Name = string.IsNullOrWhiteSpace(v2Name) ? null : v2Name,
                    Variant2Value = string.IsNullOrWhiteSpace(v2Val) ? null : v2Val,
                    BrandId = IsStandaloneMode ? (int?)BrandBox.SelectedValue : _product?.BrandId,
                    CategoryId = IsStandaloneMode ? (int?)CategoryBox.SelectedValue : _product?.CategoryId,
                    IsActive = ActiveBox.IsChecked == true,
                    IsVoided = false,
                    VoidedAtUtc = null,
                    VoidedBy = null,
                    UpdatedAt = DateTime.UtcNow,
                    Barcodes = _barcodeRows
                        .Where(br => !string.IsNullOrWhiteSpace(br.Code))
                        .Select(br => new ItemBarcode
                        {
                            Code = br.Code.Trim(),
                            Symbology = br.Symbology,
                            QuantityPerScan = Math.Max(1, br.QuantityPerScan),
                            IsPrimary = br.IsPrimary,
                            Label = string.IsNullOrWhiteSpace(br.Label) ? null : br.Label,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        }).ToList()
                };
                var codes = edited.Barcodes.Select(b => b.Code)
                                           .Where(s => !string.IsNullOrWhiteSpace(s))
                                           .Select(s => s!.Trim());
                await using (var db = await _dbf.CreateDbContextAsync())
                {
                    var svc = new CatalogService(db);
                    var conflicts = await svc.FindBarcodeConflictsAsync(codes, excludeItemId: edited.Id);
                    if (conflicts.Count > 0)
                    {
                        var lines = conflicts
                            .GroupBy(c => c.Code)
                            .Select(g =>
                            {
                                var x = g.First();
                                var owner = !string.IsNullOrWhiteSpace(x.ProductName)
                                            ? $"Product: {x.ProductName}, Variant: {x.ItemName}"
                                            : $"Item: {x.ItemName}";
                                return $"• {g.Key} → already used by {owner}";
                            });
                        MessageBox.Show(
                            "One or more barcodes are already in use:\n\n" +
                            string.Join("\n", lines) +
                            "\n\nPlease change these barcodes.",
                            "Duplicate barcode(s) found",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return; // stop; user must fix
                    }
                }
                CreatedItems = new List<Item> { edited };
                DialogResult = true;
                return;
            }

            var now = DateTime.UtcNow;
            var created = new List<Item>();
            foreach (var r in _rows)
            {
                var item = new Item
                {
                    Id = r.ExistingItemId ?? 0,
                    ProductId = _product.Id == 0 ? null : _product.Id,
                    Product = _product.Id == 0 ? _product : null,
                    Name = string.IsNullOrWhiteSpace(r.Name) ? _product.Name : r.Name,
                    Sku = r.Sku ?? "",
                    Price = r.Price,
                    TaxCode = r.TaxCode,
                    DefaultTaxRatePct = r.DefaultTaxRatePct,   // ✅ correct
                    TaxInclusive = r.TaxInclusive,
                    DefaultDiscountPct = r.DefaultDiscountPct,
                    DefaultDiscountAmt = r.DefaultDiscountAmt,
                    Variant1Name = string.IsNullOrWhiteSpace(r.Variant1Name) ? null : r.Variant1Name,
                    Variant1Value = string.IsNullOrWhiteSpace(r.Variant1Value) ? null : r.Variant1Value,
                    Variant2Name = string.IsNullOrWhiteSpace(r.Variant2Name) ? null : r.Variant2Name,
                    Variant2Value = string.IsNullOrWhiteSpace(r.Variant2Value) ? null : r.Variant2Value,
                    BrandId = null,
                    CategoryId = null,
                    IsActive = r.IsActive,
                    IsVoided = false,
                    VoidedAtUtc = null,
                    VoidedBy = null,
                    UpdatedAt = now,
                    Barcodes = string.IsNullOrWhiteSpace(r.Barcode)
                    ? new List<ItemBarcode>()
                    : new List<ItemBarcode>
                      {
                          new ItemBarcode
                          {
                              Code = r.Barcode.Trim(),
                              Symbology = GetSelectedSymbology(), // or BarcodeSymbology.Ean13
                              QuantityPerScan = 1,
                              IsPrimary = true,
                              CreatedAt = DateTime.UtcNow,
                              UpdatedAt = DateTime.UtcNow
                          }
                      }
                };
                              created.Add(item);
            }
            CreatedItems = created;
            DialogResult = true;
        }

        private void VariantBatchDialog_Loaded(object? sender, RoutedEventArgs e)
        {
            ApplyStickyDefaults();
            UpdateTitle();
            if (_mode == Mode.EditSingle)
            {
                AxesGroup.Visibility = Visibility.Collapsed;
                AxesGroup.IsEnabled = true;
                HideBatchValuesRows();             // keeps AxisSingleValuesPanel visible
                //AxisSingleValuesPanel.Visibility = Visibility.Visible;
                //AxisSingleValuesPanel.IsEnabled = true;
                //Axis1NameBox.IsEnabled = true;
                //Axis1NameBox.IsReadOnly = false;
                //Axis1SingleBox.IsEnabled = true;
                //Axis1SingleBox.IsReadOnly = false;
                //Axis2NameBox.IsEnabled = true;
                //Axis2NameBox.IsReadOnly = false;
                //Axis2SingleBox.IsEnabled = true;
                //Axis2SingleBox.IsReadOnly = false;
                //Axis1NameBox.TabIndex = 0;
                //Axis1SingleBox.TabIndex = 1;
                //Axis2NameBox.TabIndex = 2;
                //Axis2SingleBox.TabIndex = 3;
                SaveBtn.Content = "Save";
                SaveAddBtn.Visibility = Visibility.Collapsed;
            }
            else if (_mode == Mode.Sequential)
            {
                HideBatchValuesRows();         // helper below
                SaveBtn.Content = "Save & Close";
                SaveAddBtn.Visibility = Visibility.Visible;
                _seqSku = SkuStart;
                _seqBarcode = BarcodeStart;
            }
            else
            {
                SaveBtn.Content = "Create Variants";
                SaveAddBtn.Visibility = Visibility.Collapsed;
            }
        }

        public void HideAxesForStandalone()
        {
            AxesGroup.Visibility = Visibility.Collapsed;
        }

        private void UpdateTitle()
        {
            if (IsStandaloneMode)
                Title = _mode == Mode.EditSingle ? "Edit Item" : "Add Item";
            else
                Title = _mode == Mode.EditSingle ? "Edit Variant"
                     : _mode == Mode.Sequential ? "Add Variant"
                     : "Add Variants";
            if (!IsStandaloneMode && _product != null && _mode != Mode.Batch)
                Title += $" — {_product.Name}";
        }

        private void HideBatchValuesRows()
        {
            Axis1ValuesPanel.Visibility = Visibility.Collapsed;
            Axis2ValuesPanel.Visibility = Visibility.Collapsed;
            AxisSingleValuesPanel.Visibility = Visibility.Visible;
        }

        public class VariantRow
        {
            public string Variant1Name { get; set; } = "";
            public string Variant1Value { get; set; } = "";
            public string Variant2Name { get; set; } = "";
            public string Variant2Value { get; set; } = "";
            public string Name { get; set; } = "";   // Display name (Product + suffix)
            public string Sku { get; set; } = "";
            public string Barcode { get; set; } = "";
            public decimal Price { get; set; }
            public string? TaxCode { get; set; }
            public decimal DefaultTaxRatePct { get; set; }
            public bool TaxInclusive { get; set; }
            public decimal? DefaultDiscountPct { get; set; }
            public decimal? DefaultDiscountAmt { get; set; }
            public bool IsActive { get; set; } = true;
            public int? ExistingItemId { get; set; }
        }

        public string Axis1Name => Axis1NameBox.Text.Trim();
        public string Axis2Name => Axis2NameBox.Text.Trim();
        public IEnumerable<string> Axis1Values =>
            (Axis1ValuesBox.Text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        public IEnumerable<string> Axis2Values =>
            (Axis2ValuesBox.Text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
        public decimal Price => ParseDec(PriceBox.Text);
        public decimal TaxPct => ParseDec(TaxPctBox.Text);
        public string? TaxCode => string.IsNullOrWhiteSpace(TaxCodeBox.Text) ? null : TaxCodeBox.Text.Trim();
        public bool TaxInclusive => TaxInclusiveBox.IsChecked == true;
        public decimal? DefaultDiscPct => ParseNullableDec(DiscPctBox.Text);
        public decimal? DefaultDiscAmt => ParseNullableDec(DiscAmtBox.Text);
        public bool AutoSku => AutoSkuBox.IsChecked == true;
        public string SkuPrefix => (SkuPrefixBox.Text ?? "ITEM").Trim();
        public int SkuStart => ParseInt(SkuStartBox.Text, 1);
        public bool AutoBarcode => AutoBarcodeBox.IsChecked == true;
        public string BarcodePrefix => (BarcodePrefixBox.Text ?? "").Trim();
        public int BarcodeStart => ParseInt(BarcodeStartBox.Text, 1);
        public bool MarkActive => ActiveBox.IsChecked == true;

        public void PrefillProduct(Product p)
        {
            _product = p;
            IsStandaloneMode = false;
            ProductText.Text = $"Product: {p.Name}" + (p.Brand != null ? $"  ({p.Brand.Name})" : "");
            UpdateTitle();
        }

        private static decimal ParseDec(string? s) =>
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        private static decimal? ParseNullableDec(string? s)
            => string.IsNullOrWhiteSpace(s)
               ? null
               : (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null);

        private static int ParseInt(string? s, int fallback)
            => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_product == null) { MessageBox.Show("Pick a product first."); return; }

            var a1Vals = Axis1Values.ToList();
            var a2Vals = Axis2Values.ToList();
            if (!a1Vals.Any() && !a2Vals.Any())
            {
                MessageBox.Show("Enter at least one value in either Axis 1 or Axis 2.");
                return;
            }
            if (!a1Vals.Any()) a1Vals = new List<string> { "" };
            if (!a2Vals.Any()) a2Vals = new List<string> { "" };
            _rows.Clear();
            int seqSku = SkuStart;
            int seqBarcode = BarcodeStart;
            foreach (var v1 in a1Vals)
                foreach (var v2 in a2Vals)
                {
                    var suffixParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(Axis1Name) && !string.IsNullOrWhiteSpace(v1)) suffixParts.Add($"{Axis1Name}:{v1}");
                    if (!string.IsNullOrWhiteSpace(Axis2Name) && !string.IsNullOrWhiteSpace(v2)) suffixParts.Add($"{Axis2Name}:{v2}");
                    var suffix = suffixParts.Count > 0 ? " — " + string.Join(", ", suffixParts) : "";
                    var row = new VariantRow
                    {
                        Variant1Name = string.IsNullOrWhiteSpace(Axis1Name) ? "" : Axis1Name,
                        Variant1Value = string.IsNullOrWhiteSpace(v1) ? "" : v1,
                        Variant2Name = string.IsNullOrWhiteSpace(Axis2Name) ? "" : Axis2Name,
                        Variant2Value = string.IsNullOrWhiteSpace(v2) ? "" : v2,
                        Name = _product.Name + suffix,
                        Sku = AutoSku ? $"{SkuPrefix}-{seqSku:000}" : "",
                        Barcode = AutoBarcode ? GenerateEan13(BarcodePrefix, seqBarcode) : "",
                        Price = Price,
                        TaxCode = TaxCode,
                        DefaultTaxRatePct = TaxPct,
                        TaxInclusive = TaxInclusive,
                        DefaultDiscountPct = DefaultDiscPct,
                        DefaultDiscountAmt = DefaultDiscAmt,
                        IsActive = MarkActive
                    };
                    _rows.Add(row);
                    if (AutoSku) seqSku++;
                    if (AutoBarcode) seqBarcode++;
                }
            if (_rows.Count == 0)
                MessageBox.Show("No rows generated.");
        }


        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            if (_product == null) { DialogResult = false; return; }
            var a1Vals = Axis1Values.ToList();
            var a2Vals = Axis2Values.ToList();
            if (!a1Vals.Any() && !a2Vals.Any())
            {
                MessageBox.Show("Enter at least one value in either Axis 1 or Axis 2.");
                return;
            }
            if (!a1Vals.Any()) a1Vals = new List<string> { "" };
            if (!a2Vals.Any()) a2Vals = new List<string> { "" };
            var created = new List<Item>();
            var now = DateTime.UtcNow;
            int seqSku = SkuStart;
            int seqBarcode = BarcodeStart;
            foreach (var v1 in a1Vals)
                foreach (var v2 in a2Vals)
                {
                    var variantNamePieces = new List<string>();
                    if (!string.IsNullOrEmpty(Axis1Name) && !string.IsNullOrEmpty(v1)) variantNamePieces.Add($"{Axis1Name}:{v1}");
                    if (!string.IsNullOrEmpty(Axis2Name) && !string.IsNullOrEmpty(v2)) variantNamePieces.Add($"{Axis2Name}:{v2}");
                    var displayNameSuffix = variantNamePieces.Count > 0 ? " — " + string.Join(", ", variantNamePieces) : "";
                    var item = new Item
                    {
                        ProductId = _product.Id == 0 ? null : _product.Id,
                        Product = _product.Id == 0 ? _product : null,
                        Name = _product.Name + displayNameSuffix,
                        Sku = AutoSku ? $"{SkuPrefix}-{seqSku:000}" : "",
                        Price = Price,
                        TaxCode = TaxCode,
                        DefaultTaxRatePct = TaxPct,
                        TaxInclusive = TaxInclusive,
                        DefaultDiscountPct = DefaultDiscPct,
                        DefaultDiscountAmt = DefaultDiscAmt,
                        Variant1Name = string.IsNullOrWhiteSpace(Axis1Name) ? null : Axis1Name,
                        Variant1Value = string.IsNullOrWhiteSpace(v1) ? null : v1,
                        Variant2Name = string.IsNullOrWhiteSpace(Axis2Name) ? null : Axis2Name,
                        Variant2Value = string.IsNullOrWhiteSpace(v2) ? null : v2,
                        BrandId = IsStandaloneMode ? (int?)BrandBox.SelectedValue : _product?.BrandId,
                        CategoryId = IsStandaloneMode ? (int?)CategoryBox.SelectedValue : _product?.CategoryId,
                        IsActive = MarkActive,
                        IsVoided = false,
                        VoidedAtUtc = null,
                        VoidedBy = null,
                        UpdatedAt = now
                    };
                    string? primary = AutoBarcode ? GenerateEan13(BarcodePrefix, seqBarcode) : null;
                    item.Barcodes = string.IsNullOrWhiteSpace(primary)
                        ? new List<ItemBarcode>()
                        : new List<ItemBarcode>
                          {
                  new ItemBarcode
                  {
                      Code = primary.Trim(),
                      Symbology = GetSelectedSymbology(),
                      QuantityPerScan = 1,
                      IsPrimary = true,
                      CreatedAt = DateTime.UtcNow,
                      UpdatedAt = DateTime.UtcNow
                  }
                          };
                    created.Add(item);
                    if (AutoSku) seqSku++;
                    if (AutoBarcode) seqBarcode++;
                }
            var allCodes = created
    .SelectMany(it => it.Barcodes ?? Enumerable.Empty<ItemBarcode>())
    .Select(b => b.Code)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Select(s => s!.Trim())
    .ToList();

            await using (var db = await _dbf.CreateDbContextAsync())
            {
                var svc = new CatalogService(db);
                var conflicts = await svc.FindBarcodeConflictsAsync(allCodes, excludeItemId: null);
                if (conflicts.Count > 0)
                {
                    var lines = conflicts.GroupBy(c => c.Code).Select(g =>
                    {
                        var x = g.First();
                        var owner = !string.IsNullOrWhiteSpace(x.ProductName)
                            ? $"Product: {x.ProductName}, Variant: {x.ItemName}"
                            : $"Item: {x.ItemName}";
                        return $"• {g.Key} → already used by {owner}";
                    });
                    MessageBox.Show("One or more barcodes are already in use:\n\n" +
                                    string.Join("\n", lines) +
                                    "\n\nPlease change these barcodes.",
                                    "Duplicate barcode(s) found",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            CreatedItems = created;
            PersistStickyFromCurrentControls();
            DialogResult = true;
        }


        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private static string GenerateEan13(string prefix, int number)
        {
            var digits = new string((prefix ?? "").Where(char.IsDigit).ToArray()) + number.ToString(CultureInfo.InvariantCulture);
            digits = new string(digits.Take(12).ToArray());
            digits = digits.PadLeft(12, '0');
            var check = ComputeEan13CheckDigit(digits);
            return digits + check.ToString();
        }

        private static int ComputeEan13CheckDigit(string first12Digits)
        {
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int d = first12Digits[i] - '0';
                sum += (i % 2 == 0) ? d : (3 * d);
            }
            return (10 - (sum % 10)) % 10;
        }

        private void AddManualBarcode_Click(object sender, RoutedEventArgs e)
        {
            var row = new BarcodeRow
            {
                Symbology = BarcodeSymbology.Ean13,
                QuantityPerScan = 1,
                IsPrimary = !_barcodeRows.Any(r => r.IsPrimary)
            };
            _barcodeRows.Add(row);
            _newlyAddedRow = row;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SelectRowForEdit(row, ColCode);
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void SelectRowForEdit(BarcodeRow row, DataGridColumn startColumn)
        {
            _suppressValidation = true;
            try
            {
                BarcodesGrid.UpdateLayout();
                BarcodesGrid.SelectedItem = row;
                BarcodesGrid.ScrollIntoView(row, startColumn);
                BarcodesGrid.CurrentCell = new DataGridCellInfo(row, startColumn);
                BarcodesGrid.Focus();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    BarcodesGrid.BeginEdit();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            finally
            {
                Dispatcher.BeginInvoke(new Action(() => _suppressValidation = false),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }



        private async void GeneratePrimaryBarcode_Click(object sender, RoutedEventArgs e)
        {
            var sym = GetSelectedSymbology();
            await using var db = await _dbf.CreateDbContextAsync();
            var svc = new CatalogService(db);
            var (code, advanceBy) = await svc.GenerateUniqueBarcodeAsync(sym, BarcodePrefix, _seqBarcode);
            foreach (var r in _barcodeRows) r.IsPrimary = false;
            var row = new BarcodeRow
            {
                Code = code,
                Label = "1 pc",
                Symbology = sym,
                QuantityPerScan = 1,
                IsPrimary = true
            };
            _barcodeRows.Add(row);
            _seqBarcode += advanceBy;
            SelectRowForEdit(row, ColCode);
        }

        private BarcodeSymbology GetSelectedSymbology()
        {
            var item = BarcodeSymbologyBox.SelectedItem as ComboBoxItem;
            var tag = (item?.Tag as string) ?? "Ean13";
            return tag switch
            {
                "Ean8" => BarcodeSymbology.Ean8,
                "UpcA" => BarcodeSymbology.UpcA,
                "Code128" => BarcodeSymbology.Code128,
                _ => BarcodeSymbology.Ean13
            };
        }
        
        private void BarcodesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
        }

        private void BarcodesGrid_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (_suppressValidation || _justRemovedRow || _isClosing) return;
            CommitAndValidateActiveRow();
        }

        private void BarcodesGrid_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
        {
            if (_suppressValidation || _justRemovedRow || _isClosing) return;
            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (!ValidateOrHandleIncomplete(e.Row.Item as BarcodeRow, leavingRow: true))
                    e.Cancel = true;
            }
        }

        private void BarcodesGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                MoveToNextRequiredCell();
            }
            else if (e.Key == System.Windows.Input.Key.Delete)
            {
                e.Handled = true;
                RemoveSelectedBarcode();
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (BarcodesGrid.SelectedItem is BarcodeRow row)
                {
                    e.Handled = true;
                    ConfirmDeleteOrContinue(row, "Cancel barcode row?");
                }
            }
        }

        private void CommitAndValidateActiveRow()
        {
            if (_suppressValidation) return;
            BarcodesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            BarcodesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (BarcodesGrid.SelectedItem is BarcodeRow row)
                ValidateOrHandleIncomplete(row, leavingRow: false);
        }

        private bool ValidateOrHandleIncomplete(BarcodeRow? row, bool leavingRow)
        {
            if (row is null || _suppressValidation || _isClosing) return true;
            bool isNewRow = ReferenceEquals(row, _newlyAddedRow);
            bool missingCode = string.IsNullOrWhiteSpace(row.Code);
            bool qtyBad = row.QuantityPerScan <= 0;
            bool totallyBlank = missingCode && qtyBad;
            if (isNewRow && totallyBlank && leavingRow)
            {
                _suppressValidation = true;
                try
                {
                    _barcodeRows.Remove(row);
                    _newlyAddedRow = null;
                    _justRemovedRow = true;
                }
                finally { _suppressValidation = false; }
                return true;
            }

            if (!leavingRow)
            {
                var missingSilent = FirstMissingField(row);
                if (missingSilent is not null)
                {
                    FocusBack(row, missingSilent);
                    return false;
                }
            }

            var missing = FirstMissingField(row);
            if (missing is not null)
            {
                return ConfirmDeleteOrContinue(row);
            }
            if (qtyBad)
            {
                MessageBox.Show("Qty/Scan must be at least 1.", "Invalid quantity",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                FocusBack(row, ColQty);
                return false;
            }
            if (_barcodeRows.Any(r =>
                !ReferenceEquals(r, row) &&
                string.Equals(r.Code?.Trim(), row.Code?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("This barcode already exists. Codes must be unique.",
                                "Duplicate barcode", MessageBoxButton.OK, MessageBoxImage.Error);
                FocusBack(row, ColCode);
                return false;
            }
            if (isNewRow && !totallyBlank) _newlyAddedRow = null;
            return true;
        }

        private DataGridColumn? FirstMissingField(BarcodeRow r)
        {
            if (string.IsNullOrWhiteSpace(r.Code)) return ColCode;
            if (r.QuantityPerScan <= 0) return ColQty;
            return null;
        }

        private void FocusBack(BarcodeRow row, DataGridColumn col)
        {
            _suppressValidation = true;
            try
            {
                Dispatcher.BeginInvoke(new Action(() => SelectRowForEdit(row, col)),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            finally
            {
                Dispatcher.BeginInvoke(new Action(() => _suppressValidation = false),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void FocusField(BarcodeRow row, DataGridColumn? col)
        {
            if (col is null) return;
            SelectRowForEdit(row, col);
        }

        private void MoveToNextRequiredCell()
        {
            if (BarcodesGrid.SelectedItem is not BarcodeRow row) return;

            // Required order now: Code -> Type -> Qty (Label is optional)
            if (string.IsNullOrWhiteSpace(row.Code)) { FocusField(row, ColCode); return; }

            if (BarcodesGrid.CurrentCell.Column != ColSymbology) { FocusField(row, ColSymbology); return; }

            if (row.QuantityPerScan <= 0 || BarcodesGrid.CurrentCell.Column != ColQty) { FocusField(row, ColQty); return; }

            BarcodesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            BarcodesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void RemoveSelectedBarcode_Click(object sender, RoutedEventArgs e) => RemoveSelectedBarcode();

        private void RemoveSelectedBarcode()
        {
            var row = BarcodesGrid.SelectedItem as BarcodeRow;
            if (row is null) return;
            bool untouched = string.IsNullOrWhiteSpace(row.Code) &&
                             string.IsNullOrWhiteSpace(row.Label) &&
                             row.QuantityPerScan <= 0;
            if (!untouched)
            {
                var result = MessageBox.Show("Remove the selected barcode row?",
                                             "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }
            _suppressValidation = true;
            try
            {
                bool wasPrimary = row.IsPrimary;
                _barcodeRows.Remove(row);
                _newlyAddedRow = ReferenceEquals(_newlyAddedRow, row) ? null : _newlyAddedRow;
                _justRemovedRow = true;
                if (wasPrimary && AutoBarcodeBox.IsChecked == true && !_barcodeRows.Any(r => r.IsPrimary))
                {
                    EnsureDefaultPrimary();
                }
            }
            finally
            {
                _suppressValidation = false;
            }
        }

        private void RowDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not BarcodeRow row) return;
            _suppressValidation = true;
            try
            {
                bool wasPrimary = row.IsPrimary;
                _barcodeRows.Remove(row);
                if (ReferenceEquals(_newlyAddedRow, row)) _newlyAddedRow = null;
                _justRemovedRow = true;

                if (wasPrimary && AutoBarcodeBox.IsChecked == true && !_barcodeRows.Any(r => r.IsPrimary))
                    EnsureDefaultPrimary();
            }
            finally
            {
                _suppressValidation = false;
            }
        }

        private bool ConfirmDeleteOrContinue(BarcodeRow row, string title = "Incomplete barcode row")
        {
            bool totallyBlank = string.IsNullOrWhiteSpace(row.Code) && row.QuantityPerScan <= 0;
            if (totallyBlank)
            {
                _suppressValidation = true;
                try
                {
                    bool wasPrimary = row.IsPrimary;
                    _barcodeRows.Remove(row);
                    _newlyAddedRow = ReferenceEquals(_newlyAddedRow, row) ? null : _newlyAddedRow;
                    _justRemovedRow = true;
                    if (wasPrimary && AutoBarcodeBox.IsChecked == true && !_barcodeRows.Any(r => r.IsPrimary))
                        EnsureDefaultPrimary();
                }
                finally { _suppressValidation = false; }
                return true;
            }

            var result = MessageBox.Show(
                "This barcode row is incomplete.\n\nDelete this row?",
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result == MessageBoxResult.Yes)
            {
                _suppressValidation = true;
                try
                {
                    bool wasPrimary = row.IsPrimary;
                    _barcodeRows.Remove(row);
                    if (ReferenceEquals(_newlyAddedRow, row)) _newlyAddedRow = null;
                    _justRemovedRow = true;

                    if (wasPrimary && AutoBarcodeBox.IsChecked == true && !_barcodeRows.Any(r => r.IsPrimary))
                        EnsureDefaultPrimary();
                }
                finally { _suppressValidation = false; }
                return true;
            }
            var missing = FirstMissingField(row) ?? ColCode;
            FocusBack(row, missing);
            return false;
        }

        public void PrefillBarcodesForEdit(IEnumerable<ItemBarcode>? barcodes)
        {
            _barcodeRows.Clear();
            if (barcodes == null) return;
            bool anyPrimary = barcodes.Any(b => b.IsPrimary);
            foreach (var b in barcodes)
            {
                _barcodeRows.Add(new BarcodeRow
                {
                    Code = b.Code ?? "",
                    Label = b.Label,
                    Symbology = b.Symbology,
                    QuantityPerScan = Math.Max(1, b.QuantityPerScan),
                    IsPrimary = anyPrimary ? b.IsPrimary : false
                });
            }

            if (_barcodeRows.Count > 0 && !_barcodeRows.Any(r => r.IsPrimary))
                _barcodeRows[0].IsPrimary = true;
        }

        public void PrefillForEdit(Item item)
        {
            _rows.Clear();
            _rows.Add(new VariantRow
            {
                ExistingItemId = item.Id,
                Variant1Name = item.Variant1Name ?? "",
                Variant1Value = item.Variant1Value ?? "",
                Variant2Name = item.Variant2Name ?? "",
                Variant2Value = item.Variant2Value ?? "",
                Name = item.Name,
                Sku = item.Sku,
                Barcode = item.Barcodes?.FirstOrDefault(b => b.IsPrimary)?.Code
                       ?? item.Barcodes?.FirstOrDefault()?.Code
                       ?? "",
                Price = item.Price,
                TaxCode = item.TaxCode,
                DefaultTaxRatePct = item.DefaultTaxRatePct,
                TaxInclusive = item.TaxInclusive,
                DefaultDiscountPct = item.DefaultDiscountPct,
                DefaultDiscountAmt = item.DefaultDiscountAmt,
                IsActive = item.IsActive
            });
            PriceBox.Text = item.Price.ToString(CultureInfo.InvariantCulture);
            TaxPctBox.Text = item.DefaultTaxRatePct.ToString(CultureInfo.InvariantCulture);
            TaxCodeBox.Text = item.TaxCode ?? "";
            DiscPctBox.Text = item.DefaultDiscountPct?.ToString(CultureInfo.InvariantCulture) ?? "";
            DiscAmtBox.Text = item.DefaultDiscountAmt?.ToString(CultureInfo.InvariantCulture) ?? "";
            ActiveBox.IsChecked = item.IsActive;
            Axis1NameBox.Text = item.Variant1Name ?? "";
            Axis1SingleBox.Text = item.Variant1Value ?? "";
            Axis2NameBox.Text = item.Variant2Name ?? "";
            Axis2SingleBox.Text = item.Variant2Value ?? "";
            AutoSkuBox.IsChecked = false;
            AutoBarcodeBox.IsChecked = false;
        }

        public void PrefillStandalone()
        {
            IsStandaloneMode = true;
            _product = null;
            ProductText.Text = "Standalone Item";
            ProductText.Visibility = Visibility.Collapsed;
            StandaloneNameRow.Visibility = Visibility.Visible;

            AxesGroup.Visibility = Visibility.Collapsed;
            HideBatchValuesRows();

            // ✅ Enable auto-SKU for standalone by default
            AutoSkuBox.IsChecked = true;

            SaveBtn.Content = "Save & Close";
            SaveAddBtn.Visibility = Visibility.Visible;
            Title = "Add Standalone Item";

            _seqSku = SkuStart;       // will be adjusted below
            _seqBarcode = BarcodeStart;

            UpdateTitle();

            // ✅ After window is ready, compute a safe next SKU number based on existing data
            Loaded += async (_, __) =>
            {
                await using var db = await _dbf.CreateDbContextAsync();

                // Load brands
                var brands = await db.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
                BrandBox.ItemsSource = brands;

                // Load categories
                var cats = await db.Categories.OrderBy(c => c.Name).ToListAsync();
                CategoryBox.ItemsSource = cats;

                _seqSku = await GetNextSkuSequenceAsync(SkuPrefix, SkuStart);
                // (Optional) reflect the computed start back into the textbox
                SkuStartBox.Text = _seqSku.ToString(CultureInfo.InvariantCulture);
            };
        }

        public async Task PrefillStandaloneForEditAsync(Item item)
        {
            // Mark dialog as standalone and hide product/axes just like Add-Standalone
            IsStandaloneMode = true;
            _product = null;

            ProductText.Visibility = Visibility.Collapsed;
            StandaloneNameRow.Visibility = Visibility.Visible; // show Name/Brand/Category row
            AxesGroup.Visibility = Visibility.Collapsed;
            HideBatchValuesRows();

            // Buttons/Title same vibe as add-standalone, but editing now
            SaveBtn.Content = "Save";
            SaveAddBtn.Visibility = Visibility.Collapsed;
            Title = "Edit Standalone Item";
            UpdateTitle(); // keeps "Edit Item" when standalone + EditSingle

            // Load lookups (same as PrefillStandalone Loaded handler)
            await using (var db = await _dbf.CreateDbContextAsync())
            {
                var brands = await db.Brands.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
                BrandBox.ItemsSource = brands;

                var cats = await db.Categories.OrderBy(c => c.Name).ToListAsync();
                CategoryBox.ItemsSource = cats;
            }

            // Pre-fill editable fields
            NameBox.Text = item.Name;
            BrandBox.SelectedValue = item.BrandId;
            CategoryBox.SelectedValue = item.CategoryId;

            // Reuse your existing population logic for the rest
            PrefillForEdit(item);                 // price/tax/discount/axes textboxes/_rows[0] etc.
            PrefillBarcodesForEdit(item.Barcodes);
        }


        private async Task<int> GetNextSkuSequenceAsync(string prefix, int fallbackStart)
        {
            try
            {
                await using var db = await _dbf.CreateDbContextAsync();

                // Fetch only SKUs that start with "<prefix>-", then parse the numeric tail
                var likePrefix = prefix + "-";
                var list = await db.Items
                    .Where(i => i.Sku != null && i.Sku.StartsWith(likePrefix))
                    .Select(i => i.Sku)
                    .ToListAsync();

                int maxNum = 0;
                foreach (var sku in list)
                {
                    // Expect formats like PREFIX-001, PREFIX-25, etc.
                    var tail = sku.Substring(likePrefix.Length);
                    if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        if (n > maxNum) maxNum = n;
                }

                // Next available number (at least fallbackStart)
                return Math.Max(maxNum + 1, fallbackStart);
            }
            catch
            {
                // If anything goes wrong, stick to the UI-provided start
                return fallbackStart;
            }
        }


    }
}