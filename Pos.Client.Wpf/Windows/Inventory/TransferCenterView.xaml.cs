// Pos.Client.Wpf/Windows/Inventory/TransferCenterView.xaml.cs
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Infrastructure;
using Pos.Client.Wpf.Services;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Persistence.Features.Transfers;
using Pos.Domain.Models.Inventory; // ReceiveLineDto

namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferCenterView : UserControl, IRefreshOnActivate
    {
        private readonly ITransferQueries _queries;
        private readonly ITransferService _svc;
        private readonly ILookupService _lookups;
        private readonly AppState _state;
        private readonly ObservableCollection<UiTransferRow> _rows = new();
        private readonly ObservableCollection<UiLineRow> _lines = new();
        private TransferStatus[]? _lastStatuses = new[]
            {
                TransferStatus.Draft,
                TransferStatus.Dispatched,
                TransferStatus.Received,
                //TransferStatus.Voided
            };

        public class UiTransferRow
        {
            public int Id { get; set; }
            public string TransferNo { get; set; } = "";
            public TransferStatus Status { get; set; }
            public string FromDisplay { get; set; } = "";
            public string ToDisplay { get; set; } = "";
            public string EffectiveLocal { get; set; } = "";
            public string? ReceivedLocal { get; set; }
            public int LineCount { get; set; }
            public decimal QtyExpectedSum { get; set; }
            public decimal QtyReceivedSum { get; set; }
            public string? FirstItem { get; set; }
            public string FromTypeLabel { get; set; } = "";   // "Outlet" or "Warehouse"
            public string ToTypeLabel { get; set; } = "";   // "Outlet" or "Warehouse"
            public string RouteType => $"{FromTypeLabel} \u2192 {ToTypeLabel}";
            public string RouteNames => $"{FromDisplay} \u2192 {ToDisplay}";
        }

        public class UiLineRow
        {
            public int ItemId { get; set; }
            public string Sku { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public decimal QtyExpected { get; set; }
            public decimal QtyReceived { get; set; }
            public decimal ShortQty { get; set; }
            public decimal OverQty { get; set; }
        }

        public TransferCenterView()
        {
            InitializeComponent();
            _queries = App.Services.GetRequiredService<ITransferQueries>();
            _svc = App.Services.GetRequiredService<ITransferService>();
            _lookups = App.Services.GetRequiredService<ILookupService>();
            _state = App.Services.GetRequiredService<AppState>();
            TransfersGrid.ItemsSource = _rows;
            LinesGrid.ItemsSource = _lines;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                FromTypeBox.SelectedIndex = 0; // Any
                ToTypeBox.SelectedIndex = 0;   // Any
                FromDate.SelectedDate = DateTime.Today.AddDays(-30);
                ToDate.SelectedDate = DateTime.Today;
                FromTypeBox.SelectionChanged += async (_, __) => await ReloadFromPickerAsync();
                ToTypeBox.SelectionChanged += async (_, __) => await ReloadToPickerAsync();
                await ReloadPickersAsync();
                await LoadTransfersAsync();
                // default status checkboxes
                if (ChkDraft != null) ChkDraft.IsChecked = true;
                if (ChkDispatched != null) ChkDispatched.IsChecked = true;
                if (ChkReceived != null) ChkReceived.IsChecked = true;
                if (ChkVoided != null) ChkVoided.IsChecked = false; // Voided OFF by default

                // also set the server-side statuses for the initial query
                _lastStatuses = new[]
                {
                    TransferStatus.Draft,
                    TransferStatus.Dispatched,
                    TransferStatus.Received
                };

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task RefreshAsync() => await LoadTransfersAsync();

        public async void OnActivated()
        {
            await RefreshAsync();
        }

        private async Task ReloadPickersAsync()
        {
            await ReloadFromPickerAsync();
            await ReloadToPickerAsync();
        }

        private static string LabelFromType(InventoryLocationType? t)
    => t == InventoryLocationType.Warehouse ? "Warehouse"
     : t == InventoryLocationType.Outlet ? "Outlet"
     : "";

        private static string GuessTypeFromName(string name)
        {
            var n = name?.ToLowerInvariant() ?? "";
            if (n.Contains("warehouse") || n.StartsWith("wh") || n.Contains("[w]")) return "Warehouse";
            if (n.Contains("outlet") || n.StartsWith("out") || n.Contains("[o]")) return "Outlet";
            return "";
        }


        private async Task ReloadFromPickerAsync()
        {
            try
            {
                var sel = (FromTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (sel == "Warehouse")
                {
                    FromPicker.ItemsSource = await _lookups.GetWarehousesAsync();
                    FromPicker.SelectedIndex = -1;
                    FromPicker.IsEnabled = true;
                }
                else if (sel == "Outlet")
                {
                    FromPicker.ItemsSource = await _lookups.GetOutletsAsync();
                    FromPicker.SelectedIndex = -1;
                    FromPicker.IsEnabled = true;
                }
                else
                {
                    FromPicker.ItemsSource = null;
                    FromPicker.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ReloadToPickerAsync()
        {
            try
            {
                var sel = (ToTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (sel == "Warehouse")
                {
                    ToPicker.ItemsSource = await _lookups.GetWarehousesAsync();
                    ToPicker.SelectedIndex = -1;
                    ToPicker.IsEnabled = true;
                }
                else if (sel == "Outlet")
                {
                    ToPicker.ItemsSource = await _lookups.GetOutletsAsync();
                    ToPicker.SelectedIndex = -1;
                    ToPicker.IsEnabled = true;
                }
                else
                {
                    ToPicker.ItemsSource = null;
                    ToPicker.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        
        private static InventoryLocationType? TypeFromUi(ComboBox box)
        {
            var s = (box.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return s switch
            {
                "Warehouse" => InventoryLocationType.Warehouse,
                "Outlet" => InventoryLocationType.Outlet,
                _ => null
            };
        }

        private async Task LoadTransfersAsync()
        {
            try
            {
                DateTime? fromUtc = FromDate.SelectedDate.HasValue
                    ? DateTime.SpecifyKind(FromDate.SelectedDate.Value, DateTimeKind.Local).ToUniversalTime()
                    : (DateTime?)null;
                DateTime? toUtc = ToDate.SelectedDate.HasValue
                    ? DateTime.SpecifyKind(ToDate.SelectedDate.Value.AddDays(1), DateTimeKind.Local).ToUniversalTime()
                    : (DateTime?)null;

                var filter = new TransferSearchFilter
                {
                    FromType = TypeFromUi(FromTypeBox),
                    FromId = FromPicker.IsEnabled ? (int?)FromPicker.SelectedValue : null,
                    ToType = TypeFromUi(ToTypeBox),
                    ToId = ToPicker.IsEnabled ? (int?)ToPicker.SelectedValue : null,
                    DateFromUtc = fromUtc,
                    DateToUtc = toUtc,
                    Search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                    Statuses = _lastStatuses is { Length: > 0 } ? _lastStatuses : new[] {
                    TransferStatus.Draft, TransferStatus.Dispatched, TransferStatus.Received, TransferStatus.Voided
                },
                                Skip = 0,
                    Take = 400
                };

                var (rows, total) = await _queries.SearchAsync(filter);
                bool wantDraft = ChkDraft?.IsChecked == true;
                bool wantDispatched = ChkDispatched?.IsChecked == true;
                bool wantReceived = ChkReceived?.IsChecked == true;
                bool wantVoided = ChkVoided?.IsChecked == true;   // include Voided when checked

                _rows.Clear();
                foreach (var r in rows)
                {
                    bool include = r.Status switch
                    {
                        TransferStatus.Draft => wantDraft,
                        TransferStatus.Dispatched => wantDispatched,
                        TransferStatus.Received => wantReceived,
                        TransferStatus.Voided => wantVoided,
                        _ => true
                    };
                    if (!include)
                        continue;
                    _rows.Add(new UiTransferRow
                    {
                        Id = r.Id,
                        TransferNo = r.TransferNo,
                        Status = r.Status,
                        FromDisplay = r.FromDisplay,
                        ToDisplay = r.ToDisplay,
                        EffectiveLocal = r.EffectiveDateUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                        ReceivedLocal = r.ReceivedAtUtc.HasValue
                            ? r.ReceivedAtUtc.Value.ToLocalTime().ToString("dd-MMM-yyyy HH:mm")
                            : null,
                        LineCount = r.LineCount,
                        QtyExpectedSum = r.QtyExpectedSum,
                        QtyReceivedSum = r.QtyReceivedSum,
                        FirstItem = r.FirstItem,
                        FromTypeLabel = LabelFromType(r.FromType) switch { "" => GuessTypeFromName(r.FromDisplay), var s => s },
                        ToTypeLabel = LabelFromType(r.ToType) switch { "" => GuessTypeFromName(r.ToDisplay), var s => s },
                    });
                }

                TotalText.Text = $"{_rows.Count} of {total} transfers";
                if (_rows.Count > 0)
                    TransfersGrid.SelectedIndex = 0;
                else
                    ClearLines();

                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ApplyStatusFilter_Click(object sender, RoutedEventArgs e)
        {
            PopupFilter.IsOpen = false;
            var list = new System.Collections.Generic.List<TransferStatus>();
            if (ChkDraft.IsChecked == true) list.Add(TransferStatus.Draft);
            if (ChkDispatched.IsChecked == true) list.Add(TransferStatus.Dispatched);
            if (ChkReceived.IsChecked == true) list.Add(TransferStatus.Received);
            if (ChkVoided.IsChecked == true) list.Add(TransferStatus.Voided);
            _lastStatuses = list.ToArray(); // store to a field; define: private TransferStatus[]? _lastStatuses;
            await LoadTransfersAsync();
        }

        private void ClearStatusFilter_Click(object sender, RoutedEventArgs e)
        {
            ChkDraft.IsChecked = true;
            ChkDispatched.IsChecked = true;
            ChkReceived.IsChecked = true;
            ChkVoided.IsChecked = false;
        }

        private UiTransferRow? Pick() => TransfersGrid.SelectedItem as UiTransferRow;

        private async void TransfersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sel = Pick();
            await LoadLinesForSelectionAsync(sel);
            UpdateActionButtons();
        }

        private async Task LoadLinesForSelectionAsync(UiTransferRow? sel)
        {
            _lines.Clear();
            HeaderText.Text = "";
            if (sel == null)
                return;
            try
            {
                var payload = await _queries.GetWithLinesAsync(sel.Id);
                if (payload == null)
                    return;
                var (doc, lines) = payload.Value;
                HeaderText.Text =
                    $"Transfer {doc.TransferNo ?? doc.Id.ToString()}  " +
                    $"Status: {doc.TransferStatus}  " +
                    $"From: {sel.FromDisplay}  To: {sel.ToDisplay}";
                foreach (var l in lines)
                {
                    _lines.Add(new UiLineRow
                    {
                        ItemId = l.ItemId,
                        Sku = l.SkuSnapshot,
                        DisplayName = l.ItemNameSnapshot,
                        QtyExpected = l.QtyExpected,
                        QtyReceived = l.QtyReceived ?? 0m,
                        ShortQty = l.ShortQty,
                        OverQty = l.OverQty
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Load lines failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearLines()
        {
            _lines.Clear();
            HeaderText.Text = "";
        }

        private void UpdateActionButtons()
        {
            var sel = Pick();

            if (sel == null)
            {
                if (BtnOpen != null) BtnOpen.IsEnabled = false;
                if (BtnReceive != null) BtnReceive.IsEnabled = false;
                if (BtnVoid != null) BtnVoid.IsEnabled = false;
                return;
            }

            var isVoided = sel.Status == TransferStatus.Voided;

       
            // Receive: only Dispatched and not Voided
            if (BtnReceive != null)
                BtnReceive.IsEnabled = !isVoided && sel.Status == TransferStatus.Dispatched;

            // Void: allowed unless already Voided; allow for Draft/Dispatched/Received (your existing rule)
            if (BtnVoid != null)
                BtnVoid.IsEnabled =
                    !isVoided &&
                    (sel.Status == TransferStatus.Draft ||
                     sel.Status == TransferStatus.Dispatched ||
                     sel.Status == TransferStatus.Received);

            // Amend (if you have a button): allowed for Draft or Dispatched, but not Voided
            if (BtnOpen != null)
                BtnOpen.IsEnabled =
                    !isVoided &&
                    (sel.Status == TransferStatus.Draft || sel.Status == TransferStatus.Dispatched);
        }


        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadTransfersAsync();

        private async void Search_Click(object sender, RoutedEventArgs e) => await LoadTransfersAsync();

        private async void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            FromTypeBox.SelectedIndex = 0;
            FromPicker.SelectedIndex = -1;
            ToTypeBox.SelectedIndex = 0;
            ToPicker.SelectedIndex = -1;
            FromDate.SelectedDate = DateTime.Today.AddDays(-30);
            ToDate.SelectedDate = DateTime.Today;
            SearchBox.Text = string.Empty;
            if (ChkDraft != null) ChkDraft.IsChecked = true;
            if (ChkDispatched != null) ChkDispatched.IsChecked = true;
            if (ChkReceived != null) ChkReceived.IsChecked = true;
            if (ChkVoided != null) ChkVoided.IsChecked = false;
            await LoadTransfersAsync();
        }


        private void TransfersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Pick() != null) Open_Click(sender, e);
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {

            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a transfer first."); return; }
            if (sel.Status == TransferStatus.Voided)
            {
                MessageBox.Show("This transfer is voided and cannot be amended.");
                return;
            }

            // your existing amend flow (usually open editor in amend mode)
            var win = new EditTransferWindow(sel.Id) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && win.Confirmed)
                await LoadTransfersAsync();
        }
       


        private async void Receive_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null) { MessageBox.Show("Select a transfer to receive."); return; }
            if (sel.Status != TransferStatus.Dispatched) { MessageBox.Show("Only dispatched transfers can be received."); return; }

            try
            {
                var confirm = MessageBox.Show(
                    "Receive all expected quantities now?\n\nTip: use Open if you need to enter variances.",
                    "Receive Transfer", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;

                var payload = await _queries.GetWithLinesAsync(sel.Id);
                if (payload is null) throw new InvalidOperationException("Transfer not found.");

                var (doc, lines) = payload.Value;

                // ✅ IEnumerable-safe emptiness check
                if (!lines.Any() || lines.All(l => l.QtyExpected <= 0m))
                    throw new InvalidOperationException("This transfer has no quantities to receive.");

                // ✅ Build receive lines and actually materialize with ToList()
                var receiveLines = lines
                    .Where(l => l.QtyExpected > 0m)
                    .Select(l => new ReceiveLineDto
                    {
                        LineId = l.Id,
                        QtyReceived = l.QtyExpected,
                        VarianceNote = null
                    })
                    .ToList(); // <-- () required

                var userId = (_state.CurrentUser?.Id > 0) ? _state.CurrentUser.Id : _state.CurrentUserId;
                if (userId <= 0) throw new InvalidOperationException("No current user is set. Please sign in again.");

                await _svc.ReceiveAsync(doc.Id, DateTime.UtcNow, receiveLines, userId); // sig: (int, DateTime, IEnumerable<ReceiveLineDto>, int)

                MessageBox.Show("Transfer received.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadTransfersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Receive failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void Void_Click(object sender, RoutedEventArgs e)
        {
            var sel = Pick();
            if (sel == null)
            {
                MessageBox.Show("Select a transfer first.");
                return;
            }
            if (sel.Status == TransferStatus.Voided)
            {
                MessageBox.Show("This transfer is already voided.");
                return;
            }

            var warn = sel.Status switch
            {
                TransferStatus.Draft =>
                    "This will mark the draft as Voided. No stock postings will be affected.",
                TransferStatus.Dispatched =>
                    "This will undo the dispatch (stock will return to the source) and mark the transfer as Voided.",
                TransferStatus.Received =>
                    "This will attempt to undo the receive (remove stock from destination) and undo the dispatch. " +
                    "If stock at the destination has already moved or been used, the operation may be blocked.",
                _ => "This will void the transfer."
            };
            var transferLabel = !string.IsNullOrWhiteSpace(sel.TransferNo) ? sel.TransferNo : sel.Id.ToString();
            var result = MessageBox.Show(
        $"Void transfer #{transferLabel}?\n\n{warn}\n\nAre you sure?",
        "Confirm Void",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No);

            if (result != MessageBoxResult.Yes) return;

            // Prevent double-click re-entry
            var oldEnabled = BtnVoid.IsEnabled;
            BtnVoid.IsEnabled = false;

            try
            {
                var userId = (_state.CurrentUser?.Id > 0)
                    ? _state.CurrentUser.Id
                    : _state.CurrentUserId;
                if (userId <= 0)
                    throw new InvalidOperationException("No current user is set. Please sign in again.");
                await _svc.VoidAsync(sel.Id, userId, "Voided from Transfer Center");
                await LoadTransfersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Void failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnVoid.IsEnabled = oldEnabled;
            }
        }

    }
}
