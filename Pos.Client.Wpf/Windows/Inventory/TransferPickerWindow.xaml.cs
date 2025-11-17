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
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Inventory
{
    public partial class TransferPickerWindow : Window
    {
        public enum PickerMode { Drafts, Receipts }
        private readonly ITransferQueries _queries;
        private readonly ILookupService _lookups;
        private readonly AppState _state;
        private readonly PickerMode _mode;
        private List<int> _allowedToOutletIds = new(); // for non-global in Receipts mode
        
        public int? SelectedTransferId { get; private set; }

        public TransferPickerWindow(ITransferQueries queries, ILookupService lookups, AppState state, PickerMode mode)
        {
            InitializeComponent();
            _queries = queries;
            _lookups = lookups;
            _state = state;
            _mode = mode;
            Loaded += OnLoaded;
        }
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.Title = _mode == PickerMode.Drafts ? "Find Transfers — Drafts (Sending)"
                                                    : "Find Transfers — Receipts (Pending / Received)";

            FromTypeBox.SelectionChanged += async (_, __) => await ReloadFromPickerAsync();
            ToTypeBox.SelectionChanged += async (_, __) => await ReloadToPickerAsync();

            if (_mode == PickerMode.Drafts)
            {
                StatusBox.SelectedIndex = 1; // "Draft"
                StatusBox.IsEnabled = false;
            }
            else // Receipts
            {
                StatusBox.SelectedIndex = 0; // "Any"
                StatusBox.IsEnabled = false;
                var isGlobal = _state.CurrentUser?.IsGlobalAdmin == true;
                if (!isGlobal)
                {
                    try
                    {
                        _allowedToOutletIds = (await _lookups.GetUserOutletIdsAsync(_state.CurrentUserId)).ToList();
                        if (_allowedToOutletIds.Count > 0)
                        {
                            ToTypeBox.SelectedIndex = 2; // "Outlet"
                            ToTypeBox.IsEnabled = false;
                            await ReloadToPickerAsync(); // load outlets
                            ToPicker.SelectedValue = _allowedToOutletIds[0];
                        }
                        else
                        {
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
            SearchBox.KeyDown += (s, ke) => { if (ke.Key == Key.Enter) BtnSearch_Click(s!, new RoutedEventArgs()); };
            await ReloadFromPickerAsync();
            await ReloadToPickerAsync();
            await SearchAndBindAsync();
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
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
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
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task SearchAndBindAsync()
        {
            try
            {
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
                if (_mode == PickerMode.Receipts)
                    rows = rows.Where(r => r.Status == TransferStatus.Dispatched || r.Status == TransferStatus.Received).ToList();
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