using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Pos.Domain;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Features.Transfers;
using Pos.Client.Wpf.Services; // AppState
using Pos.Domain.Formatting;
using System.Windows.Media;

namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferEditorView : UserControl
    {
        // at top of TransferEditorView class
        private List<Warehouse> _whs = new();
        private List<Outlet> _outs = new();
        private bool _lookupsReady = false;
        private bool _suppressSameCheck = false;
        
        private bool _sourceLocked = false;
        private decimal _availableOnHand = 0m;


        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ITransferService _svc;
        private readonly AppState _state;

        private StockDoc? _doc;
        private ObservableCollection<StockDocLine> _lines = new();

        public TransferEditorView(
            IDbContextFactory<PosClientDbContext> dbf,
            ITransferService svc,
            AppState state)
        {
            InitializeComponent();
            _dbf = dbf;
            _svc = svc;
            _state = state;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            using var db = await _dbf.CreateDbContextAsync();

            var u = _state.CurrentUser;
            bool canPickSource = u.IsGlobalAdmin || u.Role == UserRole.Admin || u.Role == UserRole.Manager;

            // load into fields (not locals)
            _whs = await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            _outs = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();

            // defaults
            FromTypeBox.SelectedIndex = 0; // Warehouse
            ToTypeBox.SelectedIndex = 1; // Outlet

            FromPicker.DisplayMemberPath = "Name";
            FromPicker.SelectedValuePath = "Id";
            ToPicker.DisplayMemberPath = "Name";
            ToPicker.SelectedValuePath = "Id";
            ItemSearch.GotFocus += (_, __) => HideAvailableBadge();

            // initial bind based on type selections
            RebindPickerForType(FromTypeBox, FromPicker);
            RebindPickerForType(ToTypeBox, ToPicker);

            if (!canPickSource)
            {
                FromTypeBox.SelectedIndex = 1; // force Outlet for outlet user
                RebindPickerForType(FromTypeBox, FromPicker);

                var myOutId = await db.Set<UserOutlet>().AsNoTracking()
                    .Where(x => x.UserId == u.Id)
                    .Select(x => x.OutletId)
                    .FirstOrDefaultAsync();

                if (myOutId > 0) FromPicker.SelectedValue = myOutId;
                FromTypeBox.IsEnabled = false;
                FromPicker.IsEnabled = false;
            }

            // now that everything is set, wire change handlers
            FromTypeBox.SelectionChanged += FromTypeBox_SelectionChanged;
            ToTypeBox.SelectionChanged += ToTypeBox_SelectionChanged;
            FromPicker.SelectionChanged += AnyPicker_SelectionChanged;
            ToPicker.SelectionChanged += AnyPicker_SelectionChanged;

            _lookupsReady = true;

            EffectiveDate.SelectedDate = DateTime.Today;
            LinesGrid.ItemsSource = _lines;
            LinesCountText.Text = "0";
            ItemSearch.Focus();
        }

        private void RebindPickerForType(ComboBox typeBox, ComboBox picker)
        {
            // ignore during initialization
            if (!_whs.Any() && !_outs.Any()) return;

            var isWarehouse = string.Equals(
                (typeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);

            // preserve previous selected id if it exists in the new list
            var prevId = (int)(picker.SelectedValue ?? 0);

            if (isWarehouse)
            {
                picker.ItemsSource = _whs;
                if (_whs.Count == 0) { picker.SelectedIndex = -1; return; }
                if (!_whs.Any(w => w.Id == prevId)) picker.SelectedIndex = 0;
            }
            else
            {
                picker.ItemsSource = _outs;
                if (_outs.Count == 0) { picker.SelectedIndex = -1; return; }
                if (!_outs.Any(o => o.Id == prevId)) picker.SelectedIndex = 0;
            }
        }

        private void EnforceNotSameLocation()
        {
            if (!_lookupsReady || _suppressSameCheck) return;

            var fromIsWh = string.Equals(
                (FromTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);
            var toIsWh = string.Equals(
                (ToTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);

            var fromType = fromIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var toType = toIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;

            var fromId = (int)(FromPicker.SelectedValue ?? 0);
            var toId = (int)(ToPicker.SelectedValue ?? 0);

            if (fromId <= 0 || toId <= 0) return;

            if (fromType == toType && fromId == toId)
            {
                _suppressSameCheck = true;
                try
                {
                    // Nudge destination to a different option if possible
                    if (toIsWh)
                    {
                        var next = _whs.FirstOrDefault(w => w.Id != fromId);
                        if (next != null) ToPicker.SelectedValue = next.Id;
                    }
                    else
                    {
                        var next = _outs.FirstOrDefault(o => o.Id != fromId);
                        if (next != null) ToPicker.SelectedValue = next.Id;
                    }
                }
                finally { _suppressSameCheck = false; }
            }
        }

        private void FromTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // If From is locked (outlet user), this won't fire because box is disabled
            RebindPickerForType(FromTypeBox, FromPicker);
            EnforceNotSameLocation();
        }

        private void ToTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RebindPickerForType(ToTypeBox, ToPicker);
            EnforceNotSameLocation();
        }

        private void AnyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_lookupsReady) return;
            EnforceNotSameLocation();
        }


        private (InventoryLocationType fromType, int fromId, InventoryLocationType toType, int toId) GetHeader()
        {
            var fromIsWh = ((ComboBoxItem)FromTypeBox.SelectedItem)?.Content?.ToString() == "Warehouse";
            var toIsWh = ((ComboBoxItem)ToTypeBox.SelectedItem)?.Content?.ToString() == "Warehouse";

            var fromType = fromIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var toType = toIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;

            var fromId = (int)(FromPicker.SelectedValue ?? 0);
            var toId = (int)(ToPicker.SelectedValue ?? 0);

            if (fromId <= 0 || toId <= 0) throw new InvalidOperationException("Select both Source and Destination.");
            if (fromType == toType && fromId == toId) throw new InvalidOperationException("Source and Destination cannot be the same.");

            return (fromType, fromId, toType, toId);
        }

        private async Task EnsureDraftAsync()
        {
            if (_doc != null && _doc.Id > 0) return;

            var (ft, fid, tt, tid) = GetHeader();
            var effUtc = DateTime.SpecifyKind((EffectiveDate.SelectedDate ?? DateTime.Today), DateTimeKind.Local).ToUniversalTime();
            _doc = await _svc.CreateDraftAsync(ft, fid, tt, tid, effUtc, _state.CurrentUser.Id);

            // lock picking after draft
            FromTypeBox.IsEnabled = false;
            FromPicker.IsEnabled = false;
        }

        private async void BtnAddLine_Click(object sender, RoutedEventArgs e)
        {
            //var term = (SearchBox.Text ?? "").Trim();
            //if (string.IsNullOrWhiteSpace(term)) return;

            //try
            //{
            //    using var db = await _dbf.CreateDbContextAsync();

            //    // Search by barcode/sku/name
            //    int itemId = 0;
            //    itemId = await db.ItemBarcodes.AsNoTracking().Where(b => b.Code == term).Select(b => b.ItemId).FirstOrDefaultAsync();
            //    if (itemId == 0)
            //        itemId = await db.Items.AsNoTracking().Where(i => i.Sku == term).Select(i => i.Id).FirstOrDefaultAsync();
            //    if (itemId == 0)
            //        itemId = await db.Items.AsNoTracking()
            //                   .Where(i => EF.Functions.Like(i.Name, $"{term}%") || EF.Functions.Like(i.Name, $"%{term}%"))
            //                   .OrderBy(i => i.Name).Select(i => i.Id).FirstOrDefaultAsync();

            //    if (itemId == 0) throw new InvalidOperationException("Item not found.");

            //    var it = await db.Items.AsNoTracking()
            //        .Where(x => x.Id == itemId)
            //        .Select(x => new {
            //            x.Id,
            //            x.Sku,
            //            x.Name,
            //            Product = x.Product != null ? x.Product.Name : null,
            //            x.Variant1Name,
            //            x.Variant1Value,
            //            x.Variant2Name,
            //            x.Variant2Value
            //        })
            //        .FirstAsync();

            //    var name = ProductNameComposer.Compose(it.Product, it.Name, it.Variant1Name, it.Variant1Value, it.Variant2Name, it.Variant2Value);

            //    var existing = _lines.FirstOrDefault(l => l.ItemId == itemId && l.Id == 0);
            //    if (existing == null)
            //    {
            //        _lines.Add(new StockDocLine
            //        {
            //            ItemId = it.Id,
            //            SkuSnapshot = it.Sku ?? "",
            //            ItemNameSnapshot = name,
            //            QtyExpected = 1m,
            //            Remarks = ""
            //        });
            //    }
            //    else
            //    {
            //        existing.QtyExpected += 1m;
            //    }

            //    LinesGrid.Items.Refresh();
            //    LinesCountText.Text = _lines.Count.ToString();
            //    SearchBox.SelectAll();
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show(ex.Message, "Add failed", MessageBoxButton.OK, MessageBoxImage.Error);
            //}
        }

        private async void BtnSaveDraft_Click(object sender, RoutedEventArgs e)
        {
            if (_lines.Count == 0) { MessageBox.Show("No lines."); return; }
            try
            {
                await EnsureDraftAsync();

                var dtos = _lines.Where(l => l.ItemId > 0 && l.QtyExpected > 0m)
                    .Select(l => new TransferLineDto { ItemId = l.ItemId, QtyExpected = l.QtyExpected, Remarks = l.Remarks })
                    .ToList();

                _doc = await _svc.UpsertLinesAsync(_doc!.Id, dtos, replaceAll: true);
                MessageBox.Show("Draft saved.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnDispatch_Click(object sender, RoutedEventArgs e)
        {
            if (_lines.Count == 0) { MessageBox.Show("No lines."); return; }
            try
            {
                await EnsureDraftAsync();

                // Save the on-screen changes first
                var dtos = _lines.Where(l => l.ItemId > 0 && l.QtyExpected > 0m)
                    .Select(l => new TransferLineDto { ItemId = l.ItemId, QtyExpected = l.QtyExpected, Remarks = l.Remarks })
                    .ToList();

                _doc = await _svc.UpsertLinesAsync(_doc!.Id, dtos, replaceAll: true);

                var dateLocal = (EffectiveDate.SelectedDate ?? DateTime.Today).Date;
                var effUtc = DateTime.SpecifyKind(dateLocal, DateTimeKind.Local).ToUniversalTime();

                bool autoReceive = AutoReceiveCheck.IsChecked == true;

                _doc = await _svc.DispatchAsync(_doc.Id, effUtc, _state.CurrentUser.Id, autoReceive);

                MessageBox.Show(autoReceive ? "Transfer dispatched & received." : "Transfer dispatched.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Reset
                _doc = null;
                _lines.Clear();
                LinesGrid.Items.Refresh();
                LinesCountText.Text = "0";
                FromTypeBox.IsEnabled = true;
                FromPicker.IsEnabled = true;
                ItemSearch.Clear();
                ItemSearch.Focus();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Dispatch failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnOpenReceipts_Click(object sender, RoutedEventArgs e)
        {
            // Reuse your existing TransferPickerWindow in Receipts mode
            try
            {
                var picker = new TransferPickerWindow(App.Services, _dbf, new TransferQueries(_dbf), _state, TransferPickerWindow.PickerMode.Receipts);
                if (picker.ShowDialog() == true && picker.SelectedTransferId.HasValue)
                {
                    using var db = await _dbf.CreateDbContextAsync();
                    _doc = await db.StockDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == picker.SelectedTransferId.Value);
                    if (_doc == null) throw new InvalidOperationException("Transfer not found.");
                    // load lines to grid for review
                    var rows = await db.StockDocLines.AsNoTracking().Where(l => l.StockDocId == _doc.Id).OrderBy(l => l.Id).ToListAsync();
                    _lines = new ObservableCollection<StockDocLine>(rows);
                    LinesGrid.ItemsSource = _lines;
                    LinesCountText.Text = _lines.Count.ToString();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void BtnReceive_Click(object sender, RoutedEventArgs e)
        {
            if (_doc == null || _doc.TransferStatus != TransferStatus.Dispatched)
            {
                MessageBox.Show("Open a dispatched transfer to receive.");
                return;
            }

            try
            {
                // Prefill receive = expected (editable in grid if you want)
                var lines = _lines.Select(l => new ReceiveLineDto
                {
                    LineId = l.Id,
                    QtyReceived = l.QtyReceived ?? l.QtyExpected,
                    VarianceNote = l.VarianceNote
                }).ToList();

                var whenUtc = DateTime.UtcNow;
                _doc = await _svc.ReceiveAsync(_doc.Id, whenUtc, lines, _state.CurrentUser.Id);

                MessageBox.Show("Received.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // clear
                _doc = null;
                _lines.Clear();
                LinesGrid.Items.Refresh();
                LinesCountText.Text = "0";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Receive failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAddLine_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void DeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not StockDocLine line) return;

            if (_doc == null || _doc.TransferStatus == TransferStatus.Draft)
            {
                _lines.Remove(line);
                LinesGrid.Items.Refresh();
                LinesCountText.Text = _lines.Count.ToString();
                return;
            }

            MessageBox.Show("Lines can only be deleted while in Draft. Undo Dispatch to edit.");
        }

        private async void ItemSearch_ItemPicked(object sender, RoutedEventArgs e)
        {
            var pick = ((Pos.Client.Wpf.Controls.ItemSearchBox)sender).SelectedItem;
            if (pick is null) return;

            var existing = _lines.FirstOrDefault(l => l.ItemId == pick.Id && l.Id == 0);
            if (existing is null)
            {
                var added = new StockDocLine
                {
                    ItemId = pick.Id,
                    SkuSnapshot = pick.Sku ?? "",
                    ItemNameSnapshot = pick.DisplayName ?? pick.Name,
                    QtyExpected = 1m,
                    Remarks = ""
                };
                _lines.Add(added);
                LinesGrid.Items.Refresh();
                LinesCountText.Text = _lines.Count.ToString();

                if (!_sourceLocked && _lines.Count == 1 && FromTypeBox.IsEnabled)
                {
                    FromTypeBox.IsEnabled = false;
                    FromPicker.IsEnabled = false;
                    _sourceLocked = true;
                }

                // ⬇️ Show badge for this item immediately
                await UpdateAvailableBadgeAsync(added.ItemId);
                ShowAvailableBadge();

                // focus Qty
                BeginEditOn(added, QtyColumn);
            }
            else
            {
                existing.QtyExpected += 1m;
                LinesGrid.Items.Refresh();

                await UpdateAvailableBadgeAsync(existing.ItemId);
                ShowAvailableBadge();

                BeginEditOn(existing, QtyColumn);
            }
        }

        private void BeginEditOn(StockDocLine line, DataGridColumn column)
        {
            if (line is null || column is null) return;

            LinesGrid.CommitEdit();
            LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            LinesGrid.SelectedItem = line;
            LinesGrid.ScrollIntoView(line, column);
            LinesGrid.CurrentCell = new DataGridCellInfo(line, column);

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                LinesGrid.BeginEdit();
                if (TryGetCurrentCellEditor(out var tb))
                {
                    tb.SelectAll();
                    tb.Focus();

                    // ⬇️ ensure badge is visible immediately on entering edit
                    await UpdateAvailableBadgeAsync(line.ItemId);
                    ShowAvailableBadge();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }


        private bool TryGetCurrentCellEditor(out TextBox editor)
        {
            editor = Keyboard.FocusedElement as TextBox;
            return editor != null;
        }

        private async void LinesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Row?.Item is StockDocLine row)
            {
                await UpdateAvailableBadgeAsync(row.ItemId);
                ShowAvailableBadge();
            }

            if (e.EditingElement is TextBox tb)
            {
                // 1) Make sure our Enter navigation handler is attached
                tb.KeyDown -= GridEditor_KeyDown;   // avoid duplicate subscriptions
                tb.KeyDown += GridEditor_KeyDown;

                // 2) For Qty editor, also ensure badge shows the instant the box gets focus
                if (e.Column == QtyColumn)
                {
                    tb.GotKeyboardFocus -= QtyEditor_GotKeyboardFocus;
                    tb.GotKeyboardFocus += QtyEditor_GotKeyboardFocus;
                }
            }
        }


        private async void QtyEditor_GotKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e)
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                await UpdateAvailableBadgeAsync(row.ItemId);
                ShowAvailableBadge();
            }
        }

        private async void GridEditor_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;

            var row = LinesGrid.CurrentItem as StockDocLine;
            if (row == null) return;

            var col = LinesGrid.CurrentColumn;

            if (col == QtyColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                BeginEditOn(row, NoteColumn);          // move to Note
                return;
            }

            if (col == NoteColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                LinesGrid.CurrentCell = new DataGridCellInfo();  // stop grid from stealing focus back
                await FocusItemSearchTextBoxAsync();             // jump to search box (inner TextBox)
                return;
            }
        }

        private async void LinesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (LinesGrid.CurrentItem is not StockDocLine row) return;

            // If an editor already handled Enter, do nothing
            // (We marked e.Handled in GridEditor_KeyDown. But Preview fires first, so just own it.)
            e.Handled = true;

            var col = LinesGrid.CurrentColumn;

            if (col == QtyColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                BeginEditOn(row, NoteColumn);
                return;
            }

            if (col == NoteColumn)
            {
                LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
                LinesGrid.CurrentCell = new DataGridCellInfo();
                await FocusItemSearchTextBoxAsync();
                return;
            }

            // Not Qty/Note? Ignore; let default behavior proceed on other keys/columns.
            e.Handled = false;
        }


        private async void LinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                await UpdateAvailableBadgeAsync(row.ItemId);
                ShowAvailableBadge();
            }
            else
            {
                HideAvailableBadge();
            }
        }

        private void LinesGrid_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!LinesGrid.IsKeyboardFocusWithin) HideAvailableBadge();
        }

        private void ItemSearch_GotFocus(object sender, RoutedEventArgs e) => HideAvailableBadge();


        private void LinesGrid_KeyDown(object sender, KeyEventArgs e)
        {
            // Any navigation inside the grid should keep badge visible;
            // we don’t need special handling here beyond SelectionChanged.
        }

        private void ShowAvailableBadge()
        {
            AvailableBadge.Visibility = Visibility.Visible;
        }

        private void HideAvailableBadge()
        {
            AvailableBadge.Visibility = Visibility.Collapsed;
            AvailableText.Text = "";
        }

        private async Task UpdateAvailableBadgeAsync(int itemId)
        {
            try
            {
                var (fromType, fromId, _, _) = GetHeader();
                _availableOnHand = await GetOnHandAsync(itemId, fromType, fromId);
                AvailableText.Text = $"Available: {_availableOnHand:0.####}";
            }
            catch
            {
                _availableOnHand = 0m;
                AvailableText.Text = "Available: —";
            }
        }

        // On-hand at the Source (up to selected EffectiveDate end-of-day)
        private async Task<decimal> GetOnHandAsync(int itemId, InventoryLocationType locType, int locId)
        {
            if (locId <= 0) return 0m;

            using var db = await _dbf.CreateDbContextAsync();

            var cutoffLocal = (EffectiveDate.SelectedDate ?? DateTime.Today).AddDays(1);
            var cutoffUtc = DateTime.SpecifyKind(cutoffLocal, DateTimeKind.Local).ToUniversalTime();

            var onHand = await db.Set<StockEntry>()
                .AsNoTracking()
                .Where(e => e.ItemId == itemId
                         && e.LocationType == locType
                         && e.LocationId == locId
                         && e.Ts < cutoffUtc) // exclusive
                .SumAsync(e => (decimal?)e.QtyChange) ?? 0m;

            return Math.Max(onHand, 0m);
        }

        private async void LinesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // React only when user commits the Qty cell
            if (e.Column == QtyColumn && e.EditAction == DataGridEditAction.Commit && e.Row.Item is StockDocLine row)
            {
                if (e.EditingElement is TextBox tb && decimal.TryParse(tb.Text, out var newQty))
                {
                    try
                    {
                        var (fromType, fromId, _, _) = GetHeader();

                        // On-hand at source (up to end-of-day of EffectiveDate)
                        var onHand = await GetOnHandAsync(row.ItemId, fromType, fromId);

                        // Other staged rows for the same item (exclude the row being edited)
                        var stagedOther = _lines
                            .Where(x => x.ItemId == row.ItemId && !ReferenceEquals(x, row))
                            .Sum(x => x.QtyExpected);

                        var available = Math.Max(onHand - stagedOther, 0m);

                        if (newQty > available)
                        {
                            row.QtyExpected = available;
                            tb.Text = available.ToString("0.####"); // reflect clamp in the editor
                            MessageBox.Show(
                                $"Only {available:0.####} available at the selected Source.",
                                "Insufficient stock", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch
                    {
                        // UI clamp best-effort; server/service will still hard-guard on dispatch
                    }
                }
            }

            // Refresh the availability badge AFTER the commit finalizes
            await Dispatcher.InvokeAsync(async () =>
            {
                if (LinesGrid.CurrentItem is StockDocLine cur)
                {
                    await UpdateAvailableBadgeAsync(cur.ItemId);
                    ShowAvailableBadge();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }


        private async Task FocusItemSearchAsync()
        {
            // First hop: let DataGrid finish committing
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            // Second hop: ensure editors are closed and focus changes stick
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    ItemSearch.Focus();
                    Keyboard.Focus(ItemSearch);
                    HideAvailableBadge(); // badge must be hidden as we left the grid
                }
                catch { /* ignore */ }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearFormToNew();
        }

        private void ClearFormToNew()
        {
            // forget current doc and staged lines
            _doc = null;
            _lines.Clear();
            LinesGrid.ItemsSource = _lines;
            LinesGrid.Items.Refresh();
            LinesCountText.Text = "0";

            // unlock & reset Source/Destination
            FromTypeBox.IsEnabled = true;
            FromPicker.IsEnabled = true;
            _sourceLocked = false;

            // Default types: From=Warehouse, To=Outlet (same as OnLoaded)
            if (FromTypeBox.SelectedIndex != 0) FromTypeBox.SelectedIndex = 0;
            RebindPickerForType(FromTypeBox, FromPicker);

            if (ToTypeBox.SelectedIndex != 1) ToTypeBox.SelectedIndex = 1;
            RebindPickerForType(ToTypeBox, ToPicker);

            // Reset date
            EffectiveDate.SelectedDate = DateTime.Today;

            // Hide badge, clear search, focus back
            HideAvailableBadge();
            try { ItemSearch.Clear(); } catch { }
            ItemSearch.Focus();
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private async Task FocusItemSearchTextBoxAsync()
        {
            // Let the DataGrid finish its commit/layout
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // Try to focus the actual TextBox inside ItemSearch
            var tb = FindDescendant<TextBox>(ItemSearch);
            if (tb != null)
            {
                try { tb.Focus(); tb.SelectAll(); }
                catch { /* ignore */ }
            }
            else
            {
                // Fallback: focus the control itself
                try { ItemSearch.Focus(); } catch { /* ignore */ }
            }

            HideAvailableBadge(); // hide badge once we’re back to search
        }


    }
}
