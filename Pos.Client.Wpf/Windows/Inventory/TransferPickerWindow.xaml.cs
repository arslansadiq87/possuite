// Pos.Client.Wpf/Windows/Inventory/TransferPickerWindow.xaml.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence;
using Pos.Persistence.Features.Transfers;
using System.Collections.Generic;
using Pos.Client.Wpf.Services;   // for AppState


namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferPickerWindow : Window
    {
        public enum PickerMode { Drafts, Receipts }

        private readonly IServiceProvider _sp;
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        private readonly ITransferQueries _queries;
        private readonly AppState _state;
        private readonly PickerMode _mode;

        private List<int> _allowedToOutletIds = new(); // for non-global in Receipts mode
        
        public int? SelectedTransferId { get; private set; }

        public TransferPickerWindow(IServiceProvider sp,
                            IDbContextFactory<PosClientDbContext> dbf,
                            ITransferQueries queries,
                            AppState state,
                            PickerMode mode)
        {
            InitializeComponent();
            _sp = sp;
            _dbf = dbf;
            _queries = queries;
            _state = state;
            _mode = mode;
            Loaded += OnLoaded;
        }


        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Dynamic title by mode
            this.Title = _mode == PickerMode.Drafts ? "Find Transfers — Drafts (Sending)"
                                                    : "Find Transfers — Receipts (Pending / Received)";

            // Populate From/To pickers when type changes
            FromTypeBox.SelectionChanged += async (_, __) => await ReloadFromPickerAsync();
            ToTypeBox.SelectionChanged += async (_, __) => await ReloadToPickerAsync();

            // Lock the Status selector by mode
            if (_mode == PickerMode.Drafts)
            {
                // Only Drafts
                StatusBox.SelectedIndex = 1; // "Draft"
                StatusBox.IsEnabled = false;

                // Default: show transfers from anywhere (user may filter further)
            }
            else // Receipts
            {
                // We’ll post-filter to Dispatched + Received; keep UI status as Any but disable
                StatusBox.SelectedIndex = 0; // "Any"
                StatusBox.IsEnabled = false;

                // Permission: if not global admin, restrict to "To = user's assigned outlets"
                var isGlobal = _state.CurrentUser?.IsGlobalAdmin == true;
                if (!isGlobal)
                {
                    try
                    {
                        using var db = await _dbf.CreateDbContextAsync();
                        _allowedToOutletIds = await db.Set<UserOutlet>()
                            .AsNoTracking()
                            .Where(uo => uo.UserId == _state.CurrentUser!.Id)
                            .Select(uo => uo.OutletId)
                            .ToListAsync();

                        // Pre-populate To filter to Outlet + the first assigned outlet (optional convenience)
                        if (_allowedToOutletIds.Count > 0)
                        {
                            // Force ToType = Outlet and disable changing it (receipts flow is to assigned store)
                            ToTypeBox.SelectedIndex = 2; // "Outlet"
                            ToTypeBox.IsEnabled = false;

                            await ReloadToPickerAsync(); // load outlets
                            ToPicker.SelectedValue = _allowedToOutletIds[0];
                            // You can allow changing among assigned outlets; keep ToPicker enabled
                        }
                        else
                        {
                            // No assigned outlets: show nothing
                            ToTypeBox.SelectedIndex = 2; // Outlet
                            ToTypeBox.IsEnabled = false;
                            await ReloadToPickerAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error loading user outlets", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            // Quick enter-to-search
            SearchBox.KeyDown += (s, ke) => { if (ke.Key == Key.Enter) BtnSearch_Click(s!, new RoutedEventArgs()); };

            await ReloadFromPickerAsync();
            await ReloadToPickerAsync();

            await SearchAndBindAsync();
        }


        private async Task ReloadFromPickerAsync()
        {
            try
            {
                using var db = await _dbf.CreateDbContextAsync();
                var sel = (FromTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (sel == "Warehouse")
                {
                    FromPicker.ItemsSource = await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
                    FromPicker.SelectedIndex = -1;
                    FromPicker.IsEnabled = true;
                }
                else if (sel == "Outlet")
                {
                    FromPicker.ItemsSource = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
                    FromPicker.SelectedIndex = -1;
                    FromPicker.IsEnabled = true;
                }
                else
                {
                    FromPicker.ItemsSource = null;
                    FromPicker.IsEnabled = false;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task ReloadToPickerAsync()
        {
            try
            {
                using var db = await _dbf.CreateDbContextAsync();
                var sel = (ToTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (sel == "Warehouse")
                {
                    ToPicker.ItemsSource = await db.Warehouses.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
                    ToPicker.SelectedIndex = -1;
                    ToPicker.IsEnabled = true;
                }
                else if (sel == "Outlet")
                {
                    ToPicker.ItemsSource = await db.Outlets.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
                    ToPicker.SelectedIndex = -1;
                    ToPicker.IsEnabled = true;
                }
                else
                {
                    ToPicker.ItemsSource = null;
                    ToPicker.IsEnabled = false;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task SearchAndBindAsync()
        {
            try
            {
                // Build base filter from UI
                var f = new TransferSearchFilter
                {
                    Status = _mode == PickerMode.Drafts ? TransferStatus.Draft : (TransferStatus?)null, // Receipts => we’ll post-filter
                    FromType = TypeFromUi(FromTypeBox),
                    FromId = (FromPicker.IsEnabled ? (int?)FromPicker.SelectedValue : null),
                    ToType = TypeFromUi(ToTypeBox),
                    ToId = (ToPicker.IsEnabled ? (int?)ToPicker.SelectedValue : null),
                    DateFromUtc = FromDate.SelectedDate.HasValue
                        ? DateTime.SpecifyKind(FromDate.SelectedDate.Value, DateTimeKind.Local).ToUniversalTime() : null,
                    DateToUtc = ToDate.SelectedDate.HasValue
                        ? DateTime.SpecifyKind(ToDate.SelectedDate.Value.AddDays(1), DateTimeKind.Local).ToUniversalTime() : null,
                    Search = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                    Skip = 0,
                    Take = 400
                };

                var (rows, total) = await _queries.SearchAsync(f);

                // Mode filter
                if (_mode == PickerMode.Receipts)
                    rows = rows.Where(r => r.Status == TransferStatus.Dispatched || r.Status == TransferStatus.Received).ToList();

                // Permission filter (non-global admin in Receipts mode: only To in assigned outlets)
                var isGlobal = _state.CurrentUser?.IsGlobalAdmin == true;
                if (_mode == PickerMode.Receipts && !isGlobal)
                {
                    if (_allowedToOutletIds.Count == 0)
                    {
                        rows = new List<TransferListRow>(); // nothing to show
                    }
                    else
                    {
                        rows = rows.Where(r => r.ToType == InventoryLocationType.Outlet
                                              && _allowedToOutletIds.Contains(r.ToId))
                                   .ToList();
                    }
                }

                Grid.ItemsSource = rows;
                TotalText.Text = $"{rows.Count} of {total} transfers";
                if (rows.Count > 0) Grid.SelectedIndex = 0;
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

        private TransferStatus? StatusFromUi()
        {
            var s = (StatusBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return s switch
            {
                "Draft" => TransferStatus.Draft,
                "Dispatched" => TransferStatus.Dispatched,
                "Received" => TransferStatus.Received,
                _ => (TransferStatus?)null
            };
        }


        private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await SearchAndBindAsync();

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Grid.SelectedItem is TransferListRow row) { SelectedTransferId = row.Id; DialogResult = true; }
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Grid.SelectedItem is TransferListRow row)
            {
                SelectedTransferId = row.Id; DialogResult = true;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is TransferListRow row)
            {
                SelectedTransferId = row.Id; DialogResult = true;
            }
            else
            {
                MessageBox.Show("Select a transfer first.");
            }
        }
    }
}
