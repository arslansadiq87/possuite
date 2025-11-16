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
using Microsoft.Extensions.DependencyInjection;
using static System.Windows.Forms.AxHost;
using Pos.Domain.Utils;


namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferEditorView : UserControl
    {
        private bool _initializedOnce;
        private bool _canPickSource;
        private int _myOutletId;

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
        
        public TransferEditorView()
        {
            InitializeComponent();

            var sp = App.Services;
            _svc = sp.GetRequiredService<ITransferService>();
            _queries = sp.GetRequiredService<ITransferQueries>();
            _lookups = sp.GetRequiredService<ILookupService>();
            _invRead = sp.GetRequiredService<IInventoryReadService>();
            _state = sp.GetRequiredService<AppState>();
            IsVisibleChanged += TransferEditorView_IsVisibleChanged;

            Loaded += OnLoaded;
            

        }

        private static readonly string[] _typeChoices = { "Warehouse", "Outlet" };

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initializedOnce) return;

            // populate type boxes once
            FromTypeBox.ItemsSource = _typeChoices;
            ToTypeBox.ItemsSource = _typeChoices;

            // defaults (only if not chosen)
            if (FromTypeBox.SelectedIndex < 0) FromTypeBox.SelectedIndex = 0; // Warehouse
            if (ToTypeBox.SelectedIndex < 0) ToTypeBox.SelectedIndex = 1; // Outlet

            // load lookups
            var u = await AuthZ.CurrentUserAsync();
            _canPickSource = (u?.IsGlobalAdmin ?? false) || await AuthZ.IsManagerOrAboveAsync();
            _myOutletId = (u is not null) ? (await _lookups.GetUserOutletIdsAsync(u.Id)).FirstOrDefault() : 0;
            _whs = (await _lookups.GetWarehousesAsync()).ToList();
            _outs = (await _lookups.GetOutletsAsync()).ToList();

            // pickers initial bind
            RebindPickerForType(FromTypeBox, FromPicker, true);
            RebindPickerForType(ToTypeBox, ToPicker, true);

            if (!_canPickSource)
            {
                FromTypeBox.SelectedIndex = 1; // Outlet
                RebindPickerForType(FromTypeBox, FromPicker, true);
                if (_myOutletId > 0) FromPicker.SelectedValue = _myOutletId;
                FromTypeBox.IsEnabled = false;
                FromPicker.IsEnabled = false;
            }

            // rest of your existing OnLoaded...
            FromTypeBox.SelectionChanged += FromTypeBox_SelectionChanged;
            ToTypeBox.SelectionChanged += ToTypeBox_SelectionChanged;
            FromPicker.SelectionChanged += AnyPicker_SelectionChanged;
            ToPicker.SelectionChanged += AnyPicker_SelectionChanged;

            _lookupsReady = true;
            EffectiveDate.SelectedDate = DateTime.Today;

            LinesGrid.ItemsSource = _lines;
            LinesCountText.Text = "0";
            if (_doc == null)
            {
                AutoReceiveCheck.IsChecked = true;
                AutoReceiveCheck.IsEnabled = true;
            }
            _initializedOnce = true;

            ItemSearch.Focus();
        }


        private static void EnsureTypeBoxItems(ComboBox box)
        {
            if (box.Items.Count > 0) return; // already populated

            box.Items.Add(new ComboBoxItem { Content = "Warehouse" });
            box.Items.Add(new ComboBoxItem { Content = "Outlet" });
        }


        private void TransferEditorView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible != true) return;

            // Ensure type lists are still attached (prevents blanks)
            if (FromTypeBox.ItemsSource == null || FromTypeBox.Items.Count == 0)
                FromTypeBox.ItemsSource = _typeChoices;
            if (ToTypeBox.ItemsSource == null || ToTypeBox.Items.Count == 0)
                ToTypeBox.ItemsSource = _typeChoices;

            // Do NOT call RebindPickerForType here (we don't want to reset locations).
            // Only refresh the badge if needed:
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                SetAvailBadgeFor(row.ItemId);
                ShowAvailableBadge();
            }
            else
            {
                HideAvailableBadge();
            }
        }





        public async Task LoadTransferAsync(int stockDocId)
        {
            try
            {
                var payload = await _queries.GetWithLinesAsync(stockDocId);
                if (payload is null)
                    throw new InvalidOperationException("Transfer not found.");

                _doc = payload.Value.Doc;
                _lines = new ObservableCollection<StockDocLine>(payload.Value.Lines);
                LinesGrid.ItemsSource = _lines;
                LinesGrid.Items.Refresh();
                LinesCountText.Text = _lines.Count.ToString();

                // lock source when editing existing transfer
                FromTypeBox.IsEnabled = false;
                FromPicker.IsEnabled = false;

                _sourceLocked = true;
                // reflect/editability based on status; default to checked for draft
                if (_doc.TransferStatus == TransferStatus.Draft)
                {
                    // For new/ongoing drafts, default ON (or use the saved flag if you prefer)
                    AutoReceiveCheck.IsChecked = true;            // <- was: _doc.AutoReceiveOnDispatch ?? true
                    AutoReceiveCheck.IsEnabled = true;
                }
                else
                {
                    // Once dispatched/received, user shouldn't toggle this
                    AutoReceiveCheck.IsChecked = _doc.AutoReceiveOnDispatch; // <- was: _doc.AutoReceiveOnDispatch ?? false
                    AutoReceiveCheck.IsEnabled = false;
                }


            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Load transfer failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }






        private void RebindPickerForType(ComboBox typeBox, ComboBox picker, bool allowDefaultSelect)
        {
            if (!_whs.Any() && !_outs.Any()) return;

            var prevId = (picker.SelectedValue is int v && v > 0) ? v : 0;
            bool isWarehouse = IsWarehouseSelected(typeBox);

            if (isWarehouse)
            {
                picker.ItemsSource = _whs;
                if (_whs.Count == 0) { picker.SelectedIndex = -1; return; }

                if (prevId > 0 && _whs.Any(w => w.Id == prevId))
                    picker.SelectedValue = prevId;
                else if (allowDefaultSelect && picker.SelectedIndex < 0)
                    picker.SelectedIndex = 0;
            }
            else
            {
                picker.ItemsSource = _outs;
                if (_outs.Count == 0) { picker.SelectedIndex = -1; return; }

                if (prevId > 0 && _outs.Any(o => o.Id == prevId))
                    picker.SelectedValue = prevId;
                else if (allowDefaultSelect && picker.SelectedIndex < 0)
                    picker.SelectedIndex = 0;
            }
        }


        private static bool IsWarehouseSelected(ComboBox typeBox)
        {
            var sel = typeBox.SelectedItem;
            if (sel is string s)
                return s.Equals("Warehouse", StringComparison.OrdinalIgnoreCase);

            if (sel is ComboBoxItem cbi)
                return string.Equals(cbi.Content?.ToString(), "Warehouse", StringComparison.OrdinalIgnoreCase);

            // Fallback: treat index 0 as Warehouse if nothing else is known
            return typeBox.SelectedIndex == 0;
        }



        private void EnforceNotSameLocation()
        {
            if (!_lookupsReady || _suppressSameCheck) return;

            var fromIsWh = IsWarehouseSelected(FromTypeBox);
            var toIsWh = IsWarehouseSelected(ToTypeBox);

            var fromType = fromIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;
            var toType = toIsWh ? InventoryLocationType.Warehouse : InventoryLocationType.Outlet;

            var fromId = (int)(FromPicker.SelectedValue ?? 0);
            var toId = (int)(ToPicker.SelectedValue ?? 0);

            // nothing to enforce if either side not chosen yet
            if (fromId <= 0 || toId <= 0) return;

            // Only enforce when types are actually the same AND ids are the same
            if (fromType != toType || fromId != toId) return;

            _suppressSameCheck = true;
            try
            {
                if (toIsWh)
                {
                    // Try to pick a different warehouse if at least two exist
                    var alt = _whs.FirstOrDefault(w => w.Id != fromId);
                    if (alt != null)
                        ToPicker.SelectedValue = alt.Id;
                    // else: leave as-is (don’t blank out)
                }
                else
                {
                    // Try to pick a different outlet if at least two exist
                    var alt = _outs.FirstOrDefault(o => o.Id != fromId);
                    if (alt != null)
                        ToPicker.SelectedValue = alt.Id;
                    // else: leave as-is (don’t blank out)
                }
            }
            finally
            {
                _suppressSameCheck = false;
            }
        }


        private void FromTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RebindPickerForType(FromTypeBox, FromPicker, true);
            EnforceNotSameLocation();
        }

        private void ToTypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RebindPickerForType(ToTypeBox, ToPicker, true);
            EnforceNotSameLocation();
        }


        private void AnyPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_lookupsReady) return;
            EnforceNotSameLocation();
        }

        private (InventoryLocationType fromType, int fromId, InventoryLocationType toType, int toId) GetHeader()
        {
            var fromIsWh = IsWarehouseSelected(FromTypeBox);
            var toIsWh = IsWarehouseSelected(ToTypeBox);
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

            // date -> local -> UTC
            // Combine the picked day with *current* local time (HH:mm:ss)
            //var d = (EffectiveDate.SelectedDate ?? DateTime.Today);
            //var now = DateTime.Now; // local
            //var effLocal = new DateTime(d.Year, d.Month, d.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Local);
            var effUtc = EffectiveTime.ComposeUtcFromDateAndNowTime(EffectiveDate.SelectedDate ?? DateTime.Today);



            // IMPORTANT: CurrentUser may be null; fall back to CurrentUserId
            var userId = (_state.CurrentUser?.Id > 0)
                ? _state.CurrentUser.Id
                : _state.CurrentUserId;

            if (userId <= 0)
                throw new InvalidOperationException("No current user is set. Please sign in again.");

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

                // Combine the picked day with *current* local time (HH:mm:ss)
               
                var effUtc = EffectiveTime.ComposeUtcFromDateAndNowTime(EffectiveDate.SelectedDate ?? DateTime.Today);

                bool autoReceive = AutoReceiveCheck.IsChecked == true;

                // FIX: use CurrentUserId fallback
                var userId = (_state.CurrentUser?.Id > 0)
                    ? _state.CurrentUser.Id
                    : _state.CurrentUserId;

                if (userId <= 0)
                    throw new InvalidOperationException("No current user is set. Please sign in again.");

                _doc = await _svc.DispatchAsync(_doc.Id, effUtc, userId, autoReceive);

                var msg = autoReceive
                    ? "Transfer posted and delivered (auto-received at destination)."
                    : "Transfer posted. Destination can receive it from Transfer Center.";

                MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                _doc = null;
                _lines.Clear();
                LinesGrid.Items.Refresh();
                LinesCountText.Text = "0";
                FromTypeBox.IsEnabled = true;
                FromPicker.IsEnabled = true;
                ItemSearch.Clear();
                ItemSearch.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Post failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

                var userId = (_state.CurrentUser?.Id > 0)
                    ? _state.CurrentUser.Id
                    : _state.CurrentUserId;

                if (userId <= 0)
                    throw new InvalidOperationException("No current user is set. Please sign in again.");


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
                SetAvailBadgeFor(added.ItemId);
                ShowAvailableBadge();
                BeginEditOn(added, QtyColumn);
            }
            else
            {
                existing.QtyExpected += 1m;
                LinesGrid.Items.Refresh();
                SetAvailBadgeFor(existing.ItemId);
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
                    SetAvailBadgeFor(line.ItemId);
                    ShowAvailableBadge();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private bool TryGetCurrentCellEditor([NotNullWhen(true)] out TextBox? editor)
        {
            editor = Keyboard.FocusedElement as TextBox;
            return editor is not null;
        }


        private void LinesGrid_PreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Row?.Item is StockDocLine row)
            {
                SetAvailBadgeFor(row.ItemId);
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

        private void QtyEditor_GotKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e)
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                SetAvailBadgeFor(row.ItemId);
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

        private void LinesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LinesGrid.CurrentItem is StockDocLine row)
            {
                SetAvailBadgeFor(row.ItemId);
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

        private void ShowAvailBadge() => AvailBadge.Visibility = Visibility.Visible;
        private void HideAvailBadge() => AvailBadge.Visibility = Visibility.Collapsed;

        // How much of THIS item is staged in the grid (for "AvailableForIssue" mode)
        private decimal GetStagedQtyFor(int itemId) =>
            _lines.Where(x => x.ItemId == itemId).Sum(x => x.QtyExpected);

        // Set all inputs the badge needs; it will auto-refresh itself
        private void SetAvailBadgeFor(int itemId)
        {
            // Guard: header must be valid (both source & destination selected)
            try
            {
                var (fromType, fromId, _, _) = GetHeader();

                AvailBadge.ItemId = itemId;
                AvailBadge.LocationType = fromType;
                AvailBadge.LocationId = fromId;
                AvailBadge.EffectiveDate = EffectiveDate.SelectedDate ?? DateTime.Today;
                AvailBadge.StagedQty = GetStagedQtyFor(itemId);   // optional; 0 is fine too
            }
            catch
            {
                // If header isn't complete yet, just hide/reset
                HideAvailBadge();
            }
        }


        private void ShowAvailableBadge() => ShowAvailBadge();
        private void HideAvailableBadge() => HideAvailBadge();


        //private async Task UpdateAvailableBadgeAsync(int itemId)
        //{
        //    try
        //    {
        //        var (fromType, fromId, _, _) = GetHeader();

        //        var cutoffUtc = EffectiveTime.ComposeUtcFromDateAndNowTime(EffectiveDate.SelectedDate ?? DateTime.Today);

        //        // how much of THIS item is staged in the grid (purely for showing available)
        //        var staged = _lines
        //            .Where(x => x.ItemId == itemId)
        //            .Sum(x => x.QtyExpected);

        //        // Centralized availability (read-only)
        //        var available = await _invRead.GetAvailableForIssueAsync(itemId, fromType, fromId, cutoffUtc, staged);

        //        // push the numeric value into the universal badge
        //        AvailableBadge.Quantity = available;
        //    }
        //    catch
        //    {
        //        // null => badge shows "Available: —"
        //        AvailableBadge.Quantity = null;
        //    }
        //}




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
                    SetAvailBadgeFor(cur.ItemId);
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
            LinesCountText.Text = "0";
            // ItemsSource is already set once in OnLoaded; no need to reassign each time.
            LinesGrid.Items.Refresh();

            _sourceLocked = false;

            // Reset header to “new” respecting permission
            if (_canPickSource)
            {
                // Admin/manager: let them pick source; default Warehouse
                FromTypeBox.IsEnabled = true;
                FromPicker.IsEnabled = true;

                // Clear any previous selection explicitly, then rebind with default allowed
                FromPicker.SelectedValue = null;
                FromTypeBox.SelectedIndex = 0; // Warehouse
                RebindPickerForType(FromTypeBox, FromPicker, true);
            }
            else
            {
                // Outlet user: force Outlet and the user's outlet id; keep locked
                FromTypeBox.SelectedIndex = 1; // Outlet
                FromPicker.SelectedValue = null;
                RebindPickerForType(FromTypeBox, FromPicker, true);

                if (_myOutletId > 0)
                    FromPicker.SelectedValue = _myOutletId;

                FromTypeBox.IsEnabled = false;
                FromPicker.IsEnabled = false;
            }

            // Destination defaults to Outlet (index 1). Clear value before rebind so we don't “restore” old id
            ToPicker.SelectedValue = null;
            ToTypeBox.SelectedIndex = 1; // Outlet
            RebindPickerForType(ToTypeBox, ToPicker, true);

            EffectiveDate.SelectedDate = DateTime.Today;
            AutoReceiveCheck.IsChecked = true;
            AutoReceiveCheck.IsEnabled = true;

            HideAvailableBadge();
            try { ItemSearch.Clear(); } catch { /* ignore */ }
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