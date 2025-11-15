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
//using Pos.Persistence.Services;
using Pos.Domain.Services;
using Pos.Client.Wpf.Security;
using System.Diagnostics.CodeAnalysis;

namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferEditorView : UserControl
    {
        private List<Warehouse> _whs = new();
        private List<Outlet> _outs = new();
        private bool _lookupsReady = false;
        private bool _suppressSameCheck = false;
        private bool _sourceLocked = false;
        //private decimal _availableOnHand = 0m;
        private readonly ITransferService _svc;
        private readonly ITransferQueries _queries;
        private readonly ILookupService _lookups;
        private readonly IInventoryReadService _invRead;
        private readonly AppState _state;
        private StockDoc? _doc;
        private ObservableCollection<StockDocLine> _lines = new();
        public TransferEditorView(ITransferService svc, ITransferQueries queries, ILookupService lookups, IInventoryReadService invRead, AppState state)
        {
            InitializeComponent();
            _svc = svc;
            _queries = queries;
            _lookups = lookups;
            _invRead = invRead;
            _state = state;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            var u = await AuthZ.CurrentUserAsync(); // u may be null; keep it as nullable
            bool canPickSource = (u?.IsGlobalAdmin ?? false) || await AuthZ.IsManagerOrAboveAsync();

            _whs = (await _lookups.GetWarehousesAsync()).ToList();
            _outs = (await _lookups.GetOutletsAsync()).ToList();

            FromTypeBox.SelectedIndex = 0; // Warehouse
            ToTypeBox.SelectedIndex = 1;   // Outlet

            FromPicker.DisplayMemberPath = "Name";
            FromPicker.SelectedValuePath = "Id";
            ToPicker.DisplayMemberPath = "Name";
            ToPicker.SelectedValuePath = "Id";

            ItemSearch.GotFocus += (_, __) => HideAvailableBadge();

            RebindPickerForType(FromTypeBox, FromPicker);
            RebindPickerForType(ToTypeBox, ToPicker);

            if (!canPickSource)
            {
                FromTypeBox.SelectedIndex = 1; // force Outlet for outlet user
                RebindPickerForType(FromTypeBox, FromPicker);

                var myOutId = (u is not null)
                    ? (await _lookups.GetUserOutletIdsAsync(u.Id)).FirstOrDefault()
                    : 0;

                if (myOutId > 0) FromPicker.SelectedValue = myOutId;

                FromTypeBox.IsEnabled = false;
                FromPicker.IsEnabled = false;
            }

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
            if (!_whs.Any() && !_outs.Any()) return;
            var isWarehouse = string.Equals(
                (typeBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                "Warehouse", StringComparison.OrdinalIgnoreCase);
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

            var userId = _state.CurrentUser?.Id
                ?? throw new InvalidOperationException("No current user is set. Please sign in again.");

            _doc = await _svc.CreateDraftAsync(ft, fid, tt, tid, effUtc, userId);
            FromTypeBox.IsEnabled = false;
            FromPicker.IsEnabled = false;
        }

        private void BtnAddLine_Click(object sender, RoutedEventArgs e)
        {
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
                var dtos = _lines.Where(l => l.ItemId > 0 && l.QtyExpected > 0m)
                    .Select(l => new TransferLineDto { ItemId = l.ItemId, QtyExpected = l.QtyExpected, Remarks = l.Remarks })
                    .ToList();
                _doc = await _svc.UpsertLinesAsync(_doc!.Id, dtos, replaceAll: true);

                var dateLocal = (EffectiveDate.SelectedDate ?? DateTime.Today).Date;
                var effUtc = DateTime.SpecifyKind(dateLocal, DateTimeKind.Local).ToUniversalTime();
                bool autoReceive = AutoReceiveCheck.IsChecked == true;

                var userId = _state.CurrentUser?.Id
                    ?? throw new InvalidOperationException("No current user is set. Please sign in again.");

                _doc = await _svc.DispatchAsync(_doc.Id, effUtc, userId, autoReceive);

                MessageBox.Show(autoReceive ? "Transfer dispatched & received." : "Transfer dispatched.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);

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
            try
            {
                var picker = new TransferPickerWindow(_queries, _lookups, _state, TransferPickerWindow.PickerMode.Receipts);
                if (picker.ShowDialog() == true && picker.SelectedTransferId.HasValue)
                {
                    var payload = await _queries.GetWithLinesAsync(picker.SelectedTransferId.Value);
                    if (payload is null) throw new InvalidOperationException("Transfer not found.");
                    _doc = payload.Value.Doc;
                    _lines = new ObservableCollection<StockDocLine>(payload.Value.Lines);
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
                var lines = _lines.Select(l => new ReceiveLineDto
                {
                    LineId = l.Id,
                    QtyReceived = l.QtyReceived ?? l.QtyExpected,
                    VarianceNote = l.VarianceNote
                }).ToList();

                var whenUtc = DateTime.UtcNow;

                var userId = _state.CurrentUser?.Id
                    ?? throw new InvalidOperationException("No current user is set. Please sign in again.");

                _doc = await _svc.ReceiveAsync(_doc.Id, whenUtc, lines, userId);

                MessageBox.Show("Received.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void DeleteLine_Click(object sender, RoutedEventArgs e)
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
                await UpdateAvailableBadgeAsync(added.ItemId);
                ShowAvailableBadge();
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
                    await UpdateAvailableBadgeAsync(line.ItemId);
                    ShowAvailableBadge();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool TryGetCurrentCellEditor([NotNullWhen(true)] out TextBox? editor)
        {
            editor = Keyboard.FocusedElement as TextBox;
            return editor is not null;
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
                tb.KeyDown -= GridEditor_KeyDown;   // avoid duplicate subscriptions
                tb.KeyDown += GridEditor_KeyDown;
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

                // keep the same “effective day end” display rule
                var cutoffLocal = (EffectiveDate.SelectedDate ?? DateTime.Today).AddDays(1);
                var cutoffUtc = DateTime.SpecifyKind(cutoffLocal, DateTimeKind.Local).ToUniversalTime();

                // how much of THIS item is staged in the grid (purely for showing available)
                var staged = _lines
                    .Where(x => x.ItemId == itemId)
                    .Sum(x => x.QtyExpected);

                // Centralized availability (read-only)
                var available = await _invRead.GetAvailableForIssueAsync(itemId, fromType, fromId, cutoffUtc, staged);

                AvailableText.Text = $"Available: {available:0.####}";
            }
            catch
            {
                AvailableText.Text = "Available: —";
            }
        }



        private async void LinesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.Column == QtyColumn && e.EditAction == DataGridEditAction.Commit && e.Row.Item is StockDocLine row)
            {
                if (e.EditingElement is TextBox tb && decimal.TryParse(tb.Text, out var newQty))
                {
                    // accept user input as-is; guard is enforced in service layer
                    row.QtyExpected = newQty;
                }
            }

            // refresh badge for the current row (pure display)
            await Dispatcher.InvokeAsync(async () =>
            {
                if (LinesGrid.CurrentItem is StockDocLine cur)
                {
                    await UpdateAvailableBadgeAsync(cur.ItemId);
                    ShowAvailableBadge();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }


        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearFormToNew();
        }

        private void ClearFormToNew()
        {
            _doc = null;
            _lines.Clear();
            LinesGrid.ItemsSource = _lines;
            LinesGrid.Items.Refresh();
            LinesCountText.Text = "0";
            FromTypeBox.IsEnabled = true;
            FromPicker.IsEnabled = true;
            _sourceLocked = false;
            if (FromTypeBox.SelectedIndex != 0) FromTypeBox.SelectedIndex = 0;
            RebindPickerForType(FromTypeBox, FromPicker);
            if (ToTypeBox.SelectedIndex != 1) ToTypeBox.SelectedIndex = 1;
            RebindPickerForType(ToTypeBox, ToPicker);
            EffectiveDate.SelectedDate = DateTime.Today;
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
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var tb = FindDescendant<TextBox>(ItemSearch);
            if (tb != null)
            {
                try { tb.Focus(); tb.SelectAll(); }
                catch { /* ignore */ }
            }
            else
            {
                try { ItemSearch.Focus(); } catch { /* ignore */ }
            }
            HideAvailableBadge(); // hide badge once we’re back to search
        }
    }
}