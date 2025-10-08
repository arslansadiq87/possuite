using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Features.Transfers;
using Pos.Client.Wpf.Services;
using Pos.Persistence.Services;
using System.Windows.Documents;
using Pos.Domain;
using System.Windows.Input;
using Pos.Domain.Formatting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;



namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferView : UserControl
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ITransferService _transfer;
        private readonly CatalogService _catalog;
        private readonly AppState _state;
        private StockDoc? _doc;
        private ObservableCollection<StockDocLine> _lines = new();
        private readonly ITransferQueries _queries;
        private bool HasPersistedDoc => _doc != null && _doc.Id > 0;
        private List<Warehouse> _whs = new();
        private List<Outlet> _outs = new();
        // SHOW/HIDE helpers
        private void ShowAvailablePanel() => AvailablePanel.Visibility = Visibility.Visible;
        private void HideAvailablePanel() => AvailablePanel.Visibility = Visibility.Collapsed;
        private bool IsAvailableVisible => AvailablePanel.Visibility == Visibility.Visible;

        // When user focuses Scan/Search, hide
        private void SearchBox_GotFocus(object sender, RoutedEventArgs e) => HideAvailablePanel();


        public TransferView(
            IDbContextFactory<PosClientDbContext> dbf,
            ITransferService transfer,
            ITransferQueries queries,
            CatalogService catalog,
            AppState state)
        {
            InitializeComponent();
            _dbf = dbf;
            _transfer = transfer;
            _queries = queries;
            _catalog = catalog;
            _state = state;
            SearchList.ItemsSource = _suggestions;
            SearchBox.GotFocus += SearchBox_GotFocus;
            LinesGrid.LostKeyboardFocus += LinesGrid_LostKeyboardFocus;
            Loaded += OnLoaded;
            SearchBox.Focus();
        }

        private sealed class Suggestion
        {
            public int ItemId { get; init; }
            public string DisplayName { get; init; } = "";
            public string Sku { get; init; } = "";
            public string Barcode { get; init; } = "";
        }
        private readonly ObservableCollection<Suggestion> _suggestions = new();

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Role gate
            var u = _state.CurrentUser;
            bool allowed = (u.IsGlobalAdmin || u.Role == UserRole.Admin || u.Role == UserRole.Manager);
            if (!allowed)
            {
                MessageBox.Show("You do not have permission to manage transfers.", "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                //Close();
                return;
            }

            try
            {
                using var db = await _dbf.CreateDbContextAsync();
                _whs = await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
                _outs = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

                // Hook type change → rebind pickers
                FromTypeBox.SelectionChanged += (_, __) => BindPickerForType(FromTypeBox, FromPicker);
                ToTypeBox.SelectionChanged += (_, __) => BindPickerForType(ToTypeBox, ToPicker);
                // in OnLoaded, after you wire SelectionChanged:
                FromTypeBox.SelectionChanged += async (_, __) => await RefreshAvailableForCurrentAsync();
                ToTypeBox.SelectionChanged += async (_, __) => await RefreshAvailableForCurrentAsync();
                FromPicker.SelectionChanged += async (_, __) => await RefreshAvailableForCurrentAsync();


                // Ensure a selected type exists
                if (FromTypeBox.SelectedIndex < 0) FromTypeBox.SelectedIndex = 0; // Warehouse
                if (ToTypeBox.SelectedIndex < 0) ToTypeBox.SelectedIndex = 1; // Outlet

                // Bind pickers right now (not only via SelectionChanged)
                BindPickerForType(FromTypeBox, FromPicker);
                BindPickerForType(ToTypeBox, ToPicker);

                // Auto-select From if the user has exactly one assigned outlet (non-global)
                try
                {
                    if (!u.IsGlobalAdmin)
                    {
                        var myOutletIds = await db.Set<UserOutlet>().AsNoTracking()
                            .Where(o => o.UserId == u.Id)
                            .Select(o => o.OutletId)
                            .ToListAsync();

                        if (myOutletIds.Count == 1)
                        {
                            FromTypeBox.SelectedIndex = 1; // Outlet
                            BindPickerForType(FromTypeBox, FromPicker);
                            FromPicker.SelectedValue = myOutletIds[0];
                            FromTypeBox.IsEnabled = false;
                            FromPicker.IsEnabled = false;
                        }
                    }
                }
                catch { /* non-fatal */ }
                EffectiveDate.SelectedDate = DateTime.Today;
                LinesGrid.ItemsSource = _lines;
                // If you wired Sales-style search already, nothing else to hook here.
                UpdateUiState(); // will also hide receive columns on first load
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private bool FromTypeIsWarehouse => ((ComboBoxItem)FromTypeBox.SelectedItem)?.Content?.ToString() == "Warehouse";
        private bool ToTypeIsWarehouse => ((ComboBoxItem)ToTypeBox.SelectedItem)?.Content?.ToString() == "Warehouse";
        // --- Draft creation ---------------------------------------------------
   

        private async Task ReloadLinesAsync()
        {
            if (_doc == null) return;
            using var db = await _dbf.CreateDbContextAsync();
            var rows = await db.StockDocLines.AsNoTracking()
                .Where(l => l.StockDocId == _doc.Id)
                .OrderBy(l => l.Id)
                .ToListAsync();
            _lines = new ObservableCollection<StockDocLine>(rows);
            LinesGrid.ItemsSource = _lines;
            LinesCountText.Text = _lines.Count.ToString();
            TransferNoText.Text = _doc.TransferNo ?? "";
            UpdateUiState();
            RefreshRowHeaders();
            PrefillReceiveAndFocusIfNeeded();

        }

        private void DisableHeaderAfterDraft()
        {
            
            FromTypeBox.IsEnabled = false;
            FromPicker.IsEnabled = false;
            ToTypeBox.IsEnabled = false;
            ToPicker.IsEnabled = false;
            PrefillReceiveAndFocusIfNeeded();

        }
        // --- Lines: add & save ----------------------------------------------
        private async void BtnAddLine_Click(object sender, RoutedEventArgs e) => await AddFromSearchAsync();

        private async Task AddFromSearchAsync()
        {
            try
            {
                // Read term or selected suggestion
                var term = (SearchBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(term) && !(SearchPopup.IsOpen && SearchList.SelectedItem is Suggestion))
                    return;

                const decimal defaultQty = 1m;
                var remarks = ""; // remarks will be edited in-grid later

                int itemId = 0;

                // Prefer popup selection if open
                if (SearchPopup.IsOpen && SearchList.SelectedItem is Suggestion pick)
                {
                    itemId = pick.ItemId;
                }
                else
                {
                    using var db = await _dbf.CreateDbContextAsync();
                    // 1) Exact barcode
                    itemId = await db.ItemBarcodes.AsNoTracking()
                                .Where(b => b.Code == term)
                                .Select(b => b.ItemId)
                                .FirstOrDefaultAsync();
                    // 2) Exact SKU
                    if (itemId == 0)
                        itemId = await db.Items.AsNoTracking()
                                  .Where(i => i.Sku == term)
                                  .Select(i => i.Id)
                                  .FirstOrDefaultAsync();
                    // 3) Name LIKE
                    if (itemId == 0)
                        itemId = await db.Items.AsNoTracking()
                                  .Where(i => EF.Functions.Like(i.Name, $"{term}%") || EF.Functions.Like(i.Name, $"%{term}%"))
                                  .OrderBy(i => i.Name)
                                  .Select(i => i.Id)
                                  .FirstOrDefaultAsync();
                }

                if (itemId == 0)
                    throw new InvalidOperationException("Item not found.");

                StockDocLine? addedOrMerged = null;

                if (!HasPersistedDoc)
                {
                    using var db = await _dbf.CreateDbContextAsync();
                    var it = await db.Items.AsNoTracking()
                        .Where(x => x.Id == itemId)
                        .Select(x => new
                        {
                            x.Id,
                            x.Sku,
                            VariantName = x.Name,
                            ProductName = x.Product != null ? x.Product.Name : null,
                            x.Variant1Name,
                            x.Variant1Value,
                            x.Variant2Name,
                            x.Variant2Value
                        })
                        .FirstAsync();

                    var composed = ProductNameComposer.Compose(
                        it.ProductName, it.VariantName,
                        it.Variant1Name, it.Variant1Value,
                        it.Variant2Name, it.Variant2Value
                    );

                    var existing = _lines.FirstOrDefault(l => l.ItemId == itemId && l.Id == 0);
                    if (existing is null)
                    {
                        addedOrMerged = new StockDocLine
                        {
                            Id = 0,
                            StockDocId = 0,
                            ItemId = it.Id,
                            SkuSnapshot = it.Sku ?? "",
                            ItemNameSnapshot = composed,   // snapshot the Sales-style name
                            QtyExpected = 1m,
                            QtyReceived = null,
                            Remarks = ""                   // remarks now edited in-grid
                        };
                        _lines.Add(addedOrMerged);
                    }
                    else
                    {
                        existing.QtyExpected += 1m;
                        addedOrMerged = existing;
                    }

                    LinesGrid.ItemsSource = _lines;
                    LinesCountText.Text = _lines.Count.ToString();
                    if (!HasPersistedDoc && _lines.Count == 1)
                    {
                        FromTypeBox.IsEnabled = false;
                        FromPicker.IsEnabled = false;
                    }
                }


                else
                {
                    // Persisted doc → upsert immediately
                    var dto = new TransferLineDto
                    {
                        ItemId = itemId,
                        QtyExpected = defaultQty,
                        Remarks = remarks
                    };
                    _doc = await _transfer.UpsertLinesAsync(_doc!.Id, new[] { dto }, replaceAll: false);
                    await ReloadLinesAsync();
                    // After reload, fetch the line we just touched:
                    addedOrMerged = _lines.LastOrDefault(l => l.ItemId == itemId);
                }

                // Clear UI & popup
                SearchPopup.IsOpen = false;
                SearchBox.Clear();
                if (addedOrMerged != null)
                {
                    await UpdateAvailableBoxForItemAsync(addedOrMerged.ItemId);
                    ShowAvailablePanel();
                    BeginEditOn(addedOrMerged, QtyExpectedColumn);
                }

            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }


        private void BeginEditOn(StockDocLine line, DataGridColumn column)
        {
            if (line is null || column is null) return;

            LinesGrid.CommitEdit(); // commit any current edit
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            LinesGrid.SelectedItem = line;
            LinesGrid.ScrollIntoView(line, column);
            LinesGrid.CurrentCell = new DataGridCellInfo(line, column);

            // Start edit on next dispatcher tick so the cell is realized
            Dispatcher.BeginInvoke(new Action(() => {
                LinesGrid.BeginEdit();
                if (TryGetCurrentCellEditor(out var tb))
                {
                    tb.SelectAll();
                    tb.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool TryGetCurrentCellEditor(out TextBox editor)
        {
            editor = Keyboard.FocusedElement as TextBox;
            return editor != null;
        }

        // When entering a cell editor (qty/remarks/etc.) show & refresh
        private async void LinesGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Row.Item is StockDocLine row)
            {
                await UpdateAvailableBoxForItemAsync(row.ItemId);
                ShowAvailablePanel();
            }

            // (keep your existing KeyDown hookup)
            if (e.EditingElement is TextBox tb)
            {
                tb.KeyDown -= GridEditor_KeyDown;
                tb.KeyDown += GridEditor_KeyDown;
            }
        }

        private void GridEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var row = LinesGrid.CurrentItem as StockDocLine;
            if (row == null) return;

            var currentCol = LinesGrid.CurrentColumn;

            // NEW: Receiving flow — Enter in Qty Received jumps to next row's Qty Received
            if (currentCol == QtyReceivedColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

                int idx = _lines.IndexOf(row);
                if (idx >= 0 && idx < _lines.Count - 1)
                {
                    var next = _lines[idx + 1];
                    BeginEditOn(next, QtyReceivedColumn);
                }
                else
                {
                    // last row: stay on last cell, or move to VarianceNote if you prefer
                    BeginEditOn(row, VarianceNoteColumn);
                }
                return;
            }

            // Existing behavior (Draft): QtyExpected → Remarks → Variance Note → back to Search
            if (currentCol == QtyExpectedColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                BeginEditOn(row, RemarksColumn);
            }
            else if (currentCol == RemarksColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                BeginEditOn(row, VarianceNoteColumn);
            }
            else if (currentCol == VarianceNoteColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }




        private async void BtnSaveLines_Click(object sender, RoutedEventArgs e)
        {
            if (_lines.Count == 0) { MessageBox.Show("No lines to save."); return; }

            // Validate locations FIRST (and avoid name shadowing by using f*/t*)
            var loc = EnsureValidLocationsOrThrow();
            var fType = loc.fromType;
            var tType = loc.toType;
            var fId = loc.fromId;
            var tId = loc.toId;

            // Then validate quantities against the confirmed From location
            if (!await ValidateAllLinesAgainstOnHandAsync(fType, fId)) return;

            try
            {
                // Create draft if not yet persisted
                if (!HasPersistedDoc)
                {
                    var effLocal = EffectiveDate.SelectedDate ?? DateTime.Today;
                    var effUtc = DateTime.SpecifyKind(effLocal, DateTimeKind.Local).ToUniversalTime();

                    _doc = await _transfer.CreateDraftAsync(fType, fId, tType, tId, effUtc, _state.CurrentUser.Id);

                    // Push all staged lines
                    var dtos = _lines.Select(l => new TransferLineDto
                    {
                        ItemId = l.ItemId,
                        QtyExpected = l.QtyExpected,
                        Remarks = l.Remarks
                    }).ToList();

                    _doc = await _transfer.UpsertLinesAsync(_doc!.Id, dtos, replaceAll: true);
                    DisableHeaderAfterDraft();
                    await ReloadLinesAsync();
                    UpdateUiState();
                }
                else
                {
                    // Normal save for existing draft
                    var dtos = _lines.Select(l => new TransferLineDto
                    {
                        ItemId = l.ItemId,
                        QtyExpected = l.QtyExpected,
                        Remarks = l.Remarks
                    }).ToList();

                    _doc = await _transfer.UpsertLinesAsync(_doc!.Id, dtos, replaceAll: true);
                    RefreshFromDoc(_doc);
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void RefreshFromDoc(StockDoc fresh)
        {
            _doc = fresh;
            await ReloadLinesAsync();
            UpdateUiState();
        }
        // --- Dispatch & Receive ---------------------------------------------
        private async void BtnDispatch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 0) Prevent re-entrancy
                BtnDispatch.IsEnabled = false;
                BtnSaveLines.IsEnabled = false;   // make sure this matches your Save Lines button name

                // 1) Make (or get) Draft
                var doc = await EnsureDraftAsync(); // your helper; keeps _doc and locks FROM pickers
                if (doc.Id <= 0)
                    throw new InvalidOperationException("Draft creation failed (doc.Id == 0).");

                if (doc.Status != StockDocStatus.Draft)
                    throw new InvalidOperationException("Only Draft transfers can be dispatched.");

                // 2) Save what is on the grid BEFORE dispatch
                var lines = BuildUpsertLines(); // maps _lines -> List<TransferLineDto> using QtyExpected
                if (lines.Count == 0)
                    throw new InvalidOperationException("No valid lines to dispatch.");

                await _transfer.UpsertLinesAsync(doc.Id, lines, replaceAll: true);

                // 3) Dispatch with correct effective date and user
                var dateLocal = (EffectiveDate?.SelectedDate ?? DateTime.Today).Date;   // <-- adjust control name if different
                var effectiveUtc = DateTime.SpecifyKind(dateLocal, DateTimeKind.Local).ToUniversalTime();
                var userId = _state.CurrentUser?.Id ?? 0;
                if (userId <= 0) throw new InvalidOperationException("No signed-in user.");

                await _transfer.DispatchAsync(doc.Id, effectiveUtc, userId);

                // 4) (Optional) refresh the header from DB
                // _doc = await _queries.GetAsync(doc.Id);

                MessageBox.Show("Transfer dispatched.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ClearFormToNew();

            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (ex.InnerException != null)
                    msg += "\n\nInner: " + ex.InnerException.Message;

                MessageBox.Show(msg, "Dispatch failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable buttons; Dispatch stays enabled only if still Draft (optional)
                BtnSaveLines.IsEnabled = true;
                BtnDispatch.IsEnabled = true;
            }
        }



        private async void BtnReceive_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            try
            {
                var lines = _lines.Select(l => new ReceiveLineDto
                {
                    LineId = l.Id,
                    QtyReceived = l.QtyReceived ?? 0m,
                    VarianceNote = l.VarianceNote
                }).ToList();
                var whenUtc = (_doc.ReceivedAtUtc ?? DateTime.UtcNow);
                _doc = await _transfer.ReceiveAsync(_doc.Id, whenUtc, lines, _state.CurrentUser.Id);
                await ReloadLinesAsync();
                MessageBox.Show("Transfer received.", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
                ClearFormToNew();


            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // --- UI state toggles ------------------------------------------------
        private void UpdateUiState()
        {
            var status = _doc?.TransferStatus ?? TransferStatus.Draft;
            bool hasDoc = _doc != null && _doc.Id > 0;

            BtnReceive.IsEnabled = hasDoc && status == TransferStatus.Dispatched;
            BtnDispatch.IsEnabled = _lines.Count > 0 && (status == TransferStatus.Draft || _doc == null);
            BtnPrintDispatch.IsEnabled = hasDoc && status != TransferStatus.Draft;
            BtnPrintReceive.IsEnabled = hasDoc && status == TransferStatus.Received;
            BtnUndoDispatch.IsEnabled = hasDoc && status == TransferStatus.Dispatched;
            BtnUndoReceive.IsEnabled = hasDoc && status == TransferStatus.Received;



            var colExpected = LinesGrid.Columns.FirstOrDefault(c => (c as DataGridTextColumn)?.Header?.ToString() == "Qty Expected");
            var colReceived = QtyReceivedColumn;

            if (colExpected != null) colExpected.IsReadOnly = !(status == TransferStatus.Draft || _doc == null);
            if (colReceived != null) colReceived.IsReadOnly = !(status == TransferStatus.Dispatched);

            bool showReceiveCols = (status == TransferStatus.Dispatched || status == TransferStatus.Received);
            QtyReceivedColumn.Visibility = showReceiveCols ? Visibility.Visible : Visibility.Collapsed;
            ShortColumn.Visibility = showReceiveCols ? Visibility.Visible : Visibility.Collapsed;
            OverColumn.Visibility = showReceiveCols ? Visibility.Visible : Visibility.Collapsed;

            // NEW: delete only visible while Draft (including staged, _doc == null)
            DeleteColumn.Visibility = (status == TransferStatus.Draft) ? Visibility.Visible : Visibility.Collapsed;
            // NEW: show scan/search only for new/draft. Hide during receive (Dispatched/Received).
            var showScan = (status == TransferStatus.Draft || _doc == null);
            SearchAddBar.Visibility = showScan ? Visibility.Visible : Visibility.Collapsed;
            PrefillReceiveAndFocusIfNeeded();

            TransferNoText.Text = hasDoc ? (_doc!.TransferNo ?? "") : "(not saved yet)";
        }


        // --- Printing (A4 FlowDocument) -------------------------------------
        private void BtnPrintDispatch_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            var doc = BuildDispatchDoc(_doc);
            PrintFlow(doc);
        }

        private void BtnPrintReceive_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;
            var doc = BuildReceiveDoc(_doc);
            PrintFlow(doc);
        }

        private FlowDocument BuildDispatchDoc(StockDoc d)
        {
            var fd = NewA4("Transfer Dispatch Slip");
            fd.Blocks.Add(HeaderTable(d, includeReceive: false));
            var table = NewLinesTable(new[] { "SKU", "Item", "Qty Exp", "Unit Cost", "Remarks" }, new[] { 100, 320, 80, 80, 200 });
            foreach (var l in _lines.OrderBy(x => x.ItemNameSnapshot))
            {
                AddRow(table, l.SkuSnapshot, l.ItemNameSnapshot,
                    Fmt(l.QtyExpected), Fmt(l.UnitCostExpected), l.Remarks ?? "");
            }
            fd.Blocks.Add(table);
            fd.Blocks.Add(Signatures());
            return fd;
        }

        private FlowDocument BuildReceiveDoc(StockDoc d)
        {
            var fd = NewA4("Transfer Receive Slip");
            fd.Blocks.Add(HeaderTable(d, includeReceive: true));
            var table = NewLinesTable(new[] { "SKU", "Item", "Qty Exp", "Qty Rec", "Short", "Over", "Unit Cost", "Var Note" },
                                      new[] { 90, 280, 70, 70, 70, 70, 80, 170 });
            foreach (var l in _lines.OrderBy(x => x.ItemNameSnapshot))
            {
                var shortQ = Math.Max(l.QtyExpected - (l.QtyReceived ?? 0m), 0m);
                var overQ = Math.Max((l.QtyReceived ?? 0m) - l.QtyExpected, 0m);
                AddRow(table, l.SkuSnapshot, l.ItemNameSnapshot,
                    Fmt(l.QtyExpected), Fmt(l.QtyReceived), Fmt(shortQ), Fmt(overQ),
                    Fmt(l.UnitCostExpected), l.VarianceNote ?? "");
            }
            fd.Blocks.Add(table);
            fd.Blocks.Add(Signatures());
            return fd;
        }

        private static string Fmt(decimal? v) => v.HasValue ? v.Value.ToString("0.####") : "";
        // FlowDocument helpers (A4)
        private FlowDocument NewA4(string title)
        {
            var fd = new FlowDocument
            {
                PagePadding = new Thickness(48),
                ColumnWidth = double.PositiveInfinity
            };
            fd.Blocks.Add(new Paragraph(new Bold(new Run(title))) { FontSize = 18, Margin = new Thickness(0, 0, 0, 12) });
            return fd;
        }

        private Table HeaderTable(StockDoc d, bool includeReceive)
        {
            string from = $"{d.LocationType} #{d.LocationId}";
            string to = $"{d.ToLocationType} #{d.ToLocationId}";
            var t = NewLinesTable(new[] { "Field", "Value", "Field", "Value" }, new[] { 90, 220, 90, 220 });
            var dispatchStr = d.EffectiveDateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var receivedStr = d.ReceivedAtUtc.HasValue ? d.ReceivedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
            AddRow(t, "Transfer No", d.TransferNo ?? "", "Status", d.TransferStatus?.ToString() ?? "Draft");
            AddRow(t, "From", from, "To", to);
            AddRow(t, "Dispatch", dispatchStr, includeReceive ? "Received" : "—", includeReceive ? receivedStr : "—");
            return t;
        }

        private Table NewLinesTable(string[] headers, int[] widths)
        {
            var t = new Table();
            for (int i = 0; i < headers.Length; i++)
                t.Columns.Add(new TableColumn { Width = new GridLength(widths[i]) });
            var header = new TableRowGroup();
            var row = new TableRow();
            foreach (var h in headers)
                row.Cells.Add(new TableCell(new Paragraph(new Bold(new Run(h)))) { Padding = new Thickness(2, 0, 2, 4) });
            header.Rows.Add(row);
            t.RowGroups.Add(header);
            t.RowGroups.Add(new TableRowGroup()); // data rows
            return t;
        }

        private void AddRow(Table t, params string[] cells)
        {
            var data = t.RowGroups[1];
            var r = new TableRow();
            foreach (var c in cells)
                r.Cells.Add(new TableCell(new Paragraph(new Run(c))) { Padding = new Thickness(2, 0, 2, 2) });
            data.Rows.Add(r);
        }

        private Block Signatures()
        {
            var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            g.Children.Add(new TextBlock { Text = "Dispatched By (Sign)", Margin = new Thickness(0, 24, 40, 0) });
            Grid.SetColumn(g.Children[^1], 0); Grid.SetRow(g.Children[^1], 1);
            g.Children.Add(new TextBlock { Text = "Received By (Sign)", Margin = new Thickness(40, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(g.Children[^1], 1); Grid.SetRow(g.Children[^1], 1);
            var b = new BlockUIContainer(g);
            return b;
        }

        private void PrintFlow(FlowDocument fd)
        {
            var pd = new PrintDialog();
            if (pd.ShowDialog() != true) return;
            // A4 defaults handled by printer settings. FlowDocument prints to selected queue.
            fd.PageWidth = pd.PrintableAreaWidth;
            fd.PageHeight = pd.PrintableAreaHeight;
            var dv = new FlowDocumentScrollViewer { Document = fd };
            pd.PrintDocument(((IDocumentPaginatorSource)fd).DocumentPaginator, "Stock Transfer");
        }

        // --- Utils -----------------------------------------------------------
        private static void ShowError(Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async Task LoadDocAsync(int stockDocId)
        {
            using var db = await _dbf.CreateDbContextAsync();
            _doc = await db.StockDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == stockDocId);
            if (_doc == null) throw new InvalidOperationException("Transfer not found.");
            await ReloadLinesAsync();
            DisableHeaderAfterDraft();
            EffectiveDate.SelectedDate = _doc.EffectiveDateUtc.ToLocalTime().Date;
            UpdateUiState();
            PrefillReceiveAndFocusIfNeeded();

        }


        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = (SearchBox.Text ?? "").Trim();
            _suggestions.Clear();
            SearchPopup.IsOpen = false;
            if (string.IsNullOrWhiteSpace(term)) return;

            try
            {
                using var db = await _dbf.CreateDbContextAsync();

                // 1) Exact barcode first
                var exactBarcode = await db.ItemBarcodes.AsNoTracking()
                    .Where(b => b.Code == term)
                    .Select(b => new { b.ItemId, b.Code })
                    .FirstOrDefaultAsync();

                if (exactBarcode != null)
                {
                    var it = await db.Items.AsNoTracking()
                        .Where(x => x.Id == exactBarcode.ItemId)
                        .Select(x => new
                        {
                            x.Id,
                            x.Sku,
                            VariantName = x.Name,
                            ProductName = x.Product != null ? x.Product.Name : null,
                            x.Variant1Name,
                            x.Variant1Value,
                            x.Variant2Name,
                            x.Variant2Value
                        })
                        .FirstOrDefaultAsync();

                    if (it != null)
                    {
                        var composed = ProductNameComposer.Compose(
                            it.ProductName, it.VariantName,
                            it.Variant1Name, it.Variant1Value,
                            it.Variant2Name, it.Variant2Value
                        );

                        _suggestions.Add(new Suggestion
                        {
                            ItemId = it.Id,
                            DisplayName = composed,
                            Sku = it.Sku ?? "",
                            Barcode = exactBarcode.Code
                        });
                    }
                }

                // 2) SKU / Item.Name / Product.Name matches (limit 20)
                var like = term;
                var items = await db.Items.AsNoTracking()
                    .Where(x =>
                        EF.Functions.Like(x.Sku ?? "", $"%{like}%") ||
                        EF.Functions.Like(x.Name, $"{like}%") ||
                        EF.Functions.Like(x.Name, $"%{like}%") ||
                        (x.Product != null && EF.Functions.Like(x.Product.Name, $"%{like}%"))
                    )
                    .OrderBy(x => x.Product != null ? x.Product.Name : x.Name)
                    .ThenBy(x => x.Name)
                    .Select(x => new
                    {
                        x.Id,
                        x.Sku,
                        VariantName = x.Name,
                        ProductName = x.Product != null ? x.Product.Name : null,
                        x.Variant1Name,
                        x.Variant1Value,
                        x.Variant2Name,
                        x.Variant2Value
                    })
                    .Take(20)
                    .ToListAsync();

                foreach (var it in items)
                {
                    if (_suggestions.Any(s => s.ItemId == it.Id)) continue;

                    var anyBarcode = await db.ItemBarcodes.AsNoTracking()
                        .Where(b => b.ItemId == it.Id)
                        .OrderBy(b => b.Id)
                        .Select(b => b.Code)
                        .FirstOrDefaultAsync();

                    var composed = ProductNameComposer.Compose(
                        it.ProductName, it.VariantName,
                        it.Variant1Name, it.Variant1Value,
                        it.Variant2Name, it.Variant2Value
                    );

                    _suggestions.Add(new Suggestion
                    {
                        ItemId = it.Id,
                        DisplayName = composed,
                        Sku = it.Sku ?? "",
                        Barcode = anyBarcode ?? ""
                    });
                }

                if (_suggestions.Count > 0)
                {
                    SearchList.SelectedIndex = 0;
                    SearchPopup.IsOpen = true;
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }



        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && _suggestions.Count > 0)
            {
                if (!SearchPopup.IsOpen) SearchPopup.IsOpen = true;
                SearchList.Focus();
                SearchList.SelectedIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                // Enter from textbox = add current selection (or fall back search)
                _ = AddFromSearchAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void SearchList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _ = AddFromSearchAsync();
        }

        private void SearchList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _ = AddFromSearchAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SearchPopup.IsOpen = false;
                SearchBox.Focus();
                e.Handled = true;
            }
        }

        private async void BtnOpenDrafts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new TransferPickerWindow(App.Services, _dbf, _queries, _state, TransferPickerWindow.PickerMode.Drafts)
                {
                    //Owner = this
                };
                if (picker.ShowDialog() == true && picker.SelectedTransferId.HasValue)
                    await LoadDocAsync(picker.SelectedTransferId.Value);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void BtnOpenReceipts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new TransferPickerWindow(App.Services, _dbf, _queries, _state, TransferPickerWindow.PickerMode.Receipts)
                {
                    //Owner = this
                };
                if (picker.ShowDialog() == true && picker.SelectedTransferId.HasValue)
                    await LoadDocAsync(picker.SelectedTransferId.Value);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void BindPickerForType(ComboBox typeBox, ComboBox picker)
        {
            var sel = (typeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            bool isWarehouse = string.Equals(sel, "Warehouse", StringComparison.OrdinalIgnoreCase);

            picker.ItemsSource = isWarehouse ? _whs : _outs;
            picker.DisplayMemberPath = "Name";
            picker.SelectedValuePath = "Id";

            if (picker.SelectedIndex < 0 && picker.Items.Count > 0)
                picker.SelectedIndex = 0;
        }

        // NEW: keep row headers in sync with index (1-based)
        private void LinesGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private void LinesGrid_RowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshRowHeaders),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RefreshRowHeaders()
        {
            for (int i = 0; i < LinesGrid.Items.Count; i++)
                if (LinesGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow r)
                    r.Header = (i + 1).ToString();
        }
        

        // NEW: delete a line
        private async void DeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not StockDocLine line) return;

            var status = _doc?.TransferStatus ?? TransferStatus.Draft;
            if (status != TransferStatus.Draft)
            {
                MessageBox.Show("Lines can only be deleted while the transfer is in Draft.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!HasPersistedDoc)
                {
                    // not yet saved → just remove from the staged list
                    _lines.Remove(line);
                    LinesCountText.Text = _lines.Count.ToString();
                }
                else
                {
                    // persisted draft → re-send all remaining lines (replaceAll)
                    var remaining = _lines.Where(l => l != line)
                                          .Select(l => new TransferLineDto
                                          {
                                              ItemId = l.ItemId,
                                              QtyExpected = l.QtyExpected,
                                              Remarks = l.Remarks
                                          })
                                          .ToList();

                    _doc = await _transfer.UpsertLinesAsync(_doc!.Id, remaining, replaceAll: true);
                    await ReloadLinesAsync();
                    
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // NEW: cache for the last shown available
        private decimal _availableOnHand = 0m;

        // NEW: helper – current From location selection
        private (InventoryLocationType type, int id) GetFromLocation()
        {
            var fromType = FromTypeIsWarehouse ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var fromId = (int)(FromPicker.SelectedValue ?? 0);
            return (fromType, fromId);
        }

        //// --- Back-compat wrappers so old call sites keep working ---
        //private Task<decimal> GetOnHandAsync(int itemId)
        //{
        //    var (t, id) = GetFromLocation();
        //    return GetOnHandAsync(itemId, t, id);
        //}

        //private Task<bool> ValidateAllLinesAgainstOnHandAsync()
        //{
        //    var (t, id) = GetFromLocation();
        //    return ValidateAllLinesAgainstOnHandAsync(t, id);
        //}


        // NEW: central on-hand lookup
        // NEW: compute on-hand via ledger (StockEntries) — no ITransferQueries needed
        private async Task<decimal> GetOnHandAsync(int itemId, InventoryLocationType locType, int locId)
        {
            if (locId <= 0) return 0m; // now we never call this with 0 after fix (guard remains harmless)

            using var db = await _dbf.CreateDbContextAsync();

            var onHand = await db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.ItemId == itemId && e.LocationType == locType && e.LocationId == locId)
                .SumAsync(e => (decimal?)e.QtyChange) ?? 0m;

            return Math.Max(onHand, 0m);
        }


        // NEW: update the right-side “Available” box
        private async Task UpdateAvailableBoxForItemAsync(int itemId)
        {
            try
            {
                var (t, id) = GetFromLocation();
                _availableOnHand = await GetOnHandAsync(itemId, t, id);
                AvailableBox.Text = _availableOnHand.ToString("0.####");
            }
            catch
            {
                _availableOnHand = 0m;
                AvailableBox.Text = "";
            }
        }


        // NEW: when grid selection changes, refresh “Available”
        // When selection moves to another row while editing, keep panel visible and refresh
        private async void LinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                await UpdateAvailableBoxForItemAsync(row.ItemId);
                ShowAvailablePanel();
            }
            else
            {
                HideAvailablePanel();
            }
        }

        // Also hide when the grid itself loses focus (e.g., user clicks away)
        private void LinesGrid_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => HideAvailablePanel();


        // NEW: enforce stock availability on QtyExpected edits
        // After cell commit, keep it visible if still on the grid; otherwise hide
        private async void LinesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column != QtyExpectedColumn || e.EditAction != DataGridEditAction.Commit)
                return;

            if (e.Row.Item is not StockDocLine row) return;

            // Parse the new value
            if (e.EditingElement is TextBox tb && decimal.TryParse(tb.Text, out var newQty))
            {
                try
                {
                    var loc = EnsureValidLocationsOrThrow();
                    var fType = loc.fromType;
                    var fId = loc.fromId;
                    // On-hand at From
                    var onHand = await GetOnHandAsync(row.ItemId, fType, fId);
                    // Other staged rows (excluding this row)
                    decimal stagedOther = 0m;
                    if (!HasPersistedDoc)
                        stagedOther = Math.Max(_lines.Where(x => x.ItemId == row.ItemId && !ReferenceEquals(x, row)).Sum(x => x.QtyExpected), 0m);
                    var available = Math.Max(onHand - stagedOther, 0m);
                    if (newQty > available)
                    {
                        row.QtyExpected = available;
                        tb.Text = available.ToString("0.####");
                        MessageBox.Show(
                            $"Only {available:0.####} available to dispatch from selected location.",
                            "Insufficient stock", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                }
            }

            // Keep the Available panel behavior you already added:
            await Dispatcher.InvokeAsync(async () =>
            {
                if (LinesGrid.CurrentItem is StockDocLine cur)
                {
                    await UpdateAvailableBoxForItemAsync(cur.ItemId);
                    ShowAvailablePanel();
                }
                else HideAvailablePanel();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }


        private async Task RefreshAvailableForCurrentAsync()
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
                await UpdateAvailableBoxForItemAsync(row.ItemId);
            else
                AvailableBox.Text = "";
        }

        private async Task<bool> ValidateAllLinesAgainstOnHandAsync(InventoryLocationType fromType, int fromId)
        {
            foreach (var g in _lines.GroupBy(l => l.ItemId))
            {
                // Ledger on-hand at From
                var onHand = await GetOnHandAsync(g.Key, fromType, fromId);

                // If not persisted yet, subtract other staged rows for the same item
                decimal stagedOther = 0m;
                if (!HasPersistedDoc)
                {
                    var totalStagedForItem = _lines.Where(x => x.ItemId == g.Key).Sum(x => x.QtyExpected);
                    var thisGroupQty = g.Sum(x => x.QtyExpected);
                    stagedOther = Math.Max(totalStagedForItem - thisGroupQty, 0m);
                }

                var available = Math.Max(onHand - stagedOther, 0m);
                var requested = g.Sum(x => x.QtyExpected);

                if (requested > available)
                {
                    var any = g.First();
                    MessageBox.Show(
                        $"Insufficient stock:\n{any.ItemNameSnapshot}\n" +
                        $"Requested: {requested:0.####}, Available: {available:0.####}",
                        "Not enough stock", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            return true;
        }

        private (InventoryLocationType fromType, InventoryLocationType toType, int fromId, int toId) EnsureValidLocationsOrThrow()
        {
            var fromType = FromTypeIsWarehouse ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var toType = ToTypeIsWarehouse ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var fromId = (int)(FromPicker.SelectedValue ?? 0);
            var toId = (int)(ToPicker.SelectedValue ?? 0);

            if (fromId <= 0 || toId <= 0)
                throw new InvalidOperationException("Select both From and To locations.");
            if (fromType == toType && fromId == toId)
                throw new InvalidOperationException("From and To cannot be the same location.");

            return (fromType, toType, fromId, toId);
        }


        // Creates a draft if one doesn't exist. Also validates From/To selections.
        private async Task<StockDoc> EnsureDraftAsync()
        {
            if (HasPersistedDoc) return _doc!;

            // Validate FROM / TO selections
            if (FromTypeBox.SelectedItem is not ComboBoxItem fromTypeItem ||
                ToTypeBox.SelectedItem is not ComboBoxItem toTypeItem ||
                FromPicker.SelectedItem is null ||
                ToPicker.SelectedItem is null)
                throw new InvalidOperationException("Select valid FROM and TO locations first.");

            // Resolve FROM type/id
            var fromIsWarehouse = string.Equals(
                (fromTypeItem.Content?.ToString() ?? "").Trim(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);

            InventoryLocationType fromType = fromIsWarehouse
                ? InventoryLocationType.Warehouse
                : InventoryLocationType.Outlet;

            int fromId = fromIsWarehouse
                ? ((Warehouse)FromPicker.SelectedItem).Id
                : ((Outlet)FromPicker.SelectedItem).Id;

            // Resolve TO type/id (this screen is Warehouse -> Outlet; enforce Outlet)
            var toIsWarehouse = string.Equals(
                (toTypeItem.Content?.ToString() ?? "").Trim(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);

            if (toIsWarehouse)
                throw new InvalidOperationException("TO must be an Outlet for this transfer.");

            var toType = InventoryLocationType.Outlet;
            var toId = ((Outlet)ToPicker.SelectedItem).Id;

            // ⬇️ Your service uses simple args; no request objects
            // If your CreateDraftAsync has an overload with EffectiveDateUtc, pass DateTime.UtcNow as last arg.
            var now = DateTime.UtcNow;
            var userId = _state.CurrentUser?.Id ?? 0;
            var draft = await _transfer.CreateDraftAsync(fromType, fromId, toType, toId, now, userId);
            _doc = draft;


            _doc = draft;

            // Lock FROM once a draft exists (as per your UX rule)
            FromTypeBox.IsEnabled = false;
            FromPicker.IsEnabled = false;

            return draft;
        }


        private List<TransferLineDto> BuildUpsertLines()
        {
            // Keep only valid lines: has ItemId and QtyExpected > 0
            return _lines
                .Where(l => l.ItemId > 0 && l.QtyExpected > 0m)
                .Select(l => new TransferLineDto
                {
                    ItemId = l.ItemId,
                    QtyExpected = l.QtyExpected
                    // Add more fields here ONLY if your TransferLineDto actually has them
                    // (e.g., UnitCost, Note, etc.)
                })
                .ToList();
        }


        // Resets the window to a fresh "new transfer" state
        private void ClearFormToNew()
        {
            // forget current doc and staged lines
            _doc = null;

            // clear lines
            _lines.Clear();
            LinesGrid.ItemsSource = _lines;
            LinesCountText.Text = "0";

            // reset pickers and enable header controls
            FromTypeBox.IsEnabled = true;
            FromPicker.IsEnabled = true;
            ToTypeBox.IsEnabled = true;
            ToPicker.IsEnabled = true;

            // default selections: From = Warehouse, To = Outlet
            if (FromTypeBox.SelectedIndex != 0) FromTypeBox.SelectedIndex = 0; // Warehouse
            BindPickerForType(FromTypeBox, FromPicker);

            if (ToTypeBox.SelectedIndex != 1) ToTypeBox.SelectedIndex = 1;     // Outlet
            BindPickerForType(ToTypeBox, ToPicker);

            // reset date and UI bits
            EffectiveDate.SelectedDate = DateTime.Today;
            TransferNoText.Text = "(not saved yet)";
            HideAvailablePanel();
            AvailableBox.Text = "";
            SearchPopup.IsOpen = false;
            SearchBox.Clear();

            // refresh grid visuals
            UpdateUiState();
            RefreshRowHeaders();

            // focus back to search
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        // Toolbar button -> manual clear
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            // Optionally confirm if there are unsaved staged lines
            if (!HasPersistedDoc && _lines.Count > 0)
            {
                var ans = MessageBox.Show("Clear current unsaved lines and start a new transfer?",
                                          "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans != MessageBoxResult.Yes) return;
            }
            ClearFormToNew();
        }

        private void PrefillReceiveAndFocusIfNeeded()
        {
            if (_doc?.TransferStatus != TransferStatus.Dispatched) return;
            if (_lines.Count == 0) return;

            // Prefill only when null (so we don't overwrite any in-progress edits)
            foreach (var l in _lines)
                if (!l.QtyReceived.HasValue)
                    l.QtyReceived = l.QtyExpected;

            LinesGrid.Items.Refresh();

            // auto-focus first row → Qty Received in edit mode
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var first = _lines[0];
                BeginEditOn(first, QtyReceivedColumn);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async void BtnUndoDispatch_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;

            // Safety: block if any line has received qty
            if (_doc.TransferStatus == TransferStatus.Received || _doc.ReceivedAtUtc.HasValue)
            {
                MessageBox.Show("This transfer has been received and cannot be undone.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                "Undo dispatch and return this transfer to Draft? Stock will be restored to the source location.",
                "Confirm Undo", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var reason = ""; // optionally prompt for text; keep blank if not needed
                var when = DateTime.UtcNow;
                _doc = await _transfer.UndoDispatchAsync(_doc.Id, when, _state.CurrentUser.Id, reason);
                await ReloadLinesAsync();
                UpdateUiState();
                MessageBox.Show("Dispatch undone. The transfer is now in Draft and can be edited.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void BtnUndoReceive_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null) return;

            var confirm = MessageBox.Show(
                "Undo receive and return this transfer to Dispatched? Stock will be removed from the destination.",
                "Confirm Undo Receive", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var when = DateTime.UtcNow;
                var reason = ""; // optionally prompt
                _doc = await _transfer.UndoReceiveAsync(_doc.Id, when, _state.CurrentUser.Id, reason);

                await ReloadLinesAsync();
                UpdateUiState();

                // Since we're back to Dispatched, prefill & focus Qty Received again (your helper):
                PrefillReceiveAndFocusIfNeeded();

                MessageBox.Show("Receive undone. The transfer is now Dispatched and can be received again.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }


    }
}
