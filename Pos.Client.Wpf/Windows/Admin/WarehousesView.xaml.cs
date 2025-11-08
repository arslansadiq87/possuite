using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Entities;
using Pos.Persistence.Services;
using Pos.Client.Wpf.Services;   // AuthZ, IViewNavigator, etc.

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class WarehousesView : UserControl
    {
        private WarehouseService? _svc;
        private Func<EditWarehouseWindow>? _editFactory;
        private readonly bool _design;

        public WarehousesView()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<WarehouseService>();
            _editFactory = () => App.Services.GetRequiredService<EditWarehouseWindow>();
            Loaded += async (_, __) => await LoadRowsAsync();
        }

        private bool Ready => !_design && _svc != null;

        // ---------------- LOAD ----------------
        private async Task LoadRowsAsync()
        {
            if (!Ready) return;
            try
            {
                var term = (SearchBox.Text ?? "").Trim();
                var showInactive = ShowInactive.IsChecked == true;

                var rows = await _svc!.SearchAsync(term, showInactive);
                WarehousesGrid.ItemsSource = rows;

                UpdateActionButtons();
                UpdateSearchRowVisibility();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load warehouses: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Warehouse? Selected() => WarehousesGrid.SelectedItem as Warehouse;

        // ---------------- BUTTONS ----------------
        private void UpdateActionButtons()
        {
            var row = Selected();
            EditBtn.Visibility =
                OpeningStockBtn.Visibility =
                (row != null ? Visibility.Visible : Visibility.Collapsed);

            if (row == null)
            {
                EnableBtn.Visibility = DisableBtn.Visibility = Visibility.Collapsed;
                return;
            }

            EnableBtn.Visibility = row.IsActive ? Visibility.Collapsed : Visibility.Visible;
            DisableBtn.Visibility = row.IsActive ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------------- EVENTS ----------------
        private async void SearchBox_TextChanged(object s, TextChangedEventArgs e) => await LoadRowsAsync();
        private async void FilterChanged(object s, RoutedEventArgs e) => await LoadRowsAsync();
        private void Grid_SelectionChanged(object s, SelectionChangedEventArgs e) => UpdateActionButtons();
        private void Grid_MouseDoubleClick(object s, MouseButtonEventArgs e) => Edit_Click(s, e);

        private void Add_Click(object s, RoutedEventArgs e)
        {
            var dlg = _editFactory!();
            dlg.Owner = Window.GetWindow(this);
            dlg.EditId = null;
            if (dlg.ShowDialog() == true)
                _ = LoadRowsAsync();
        }

        private async void Edit_Click(object? s, RoutedEventArgs e)
        {
            var row = Selected();
            if (row is null) return;

            // use service to ensure latest data
            var wh = await _svc!.GetWarehouseAsync(row.Id);
            if (wh == null)
            {
                MessageBox.Show("Warehouse not found or deleted.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = _editFactory!();
            dlg.Owner = Window.GetWindow(this);
            dlg.EditId = wh.Id;
            if (dlg.ShowDialog() == true)
                await LoadRowsAsync();
        }

        private async void Disable_Click(object s, RoutedEventArgs e)
        {
            var row = Selected();
            if (row is null) return;

            if (MessageBox.Show($"Disable warehouse “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await _svc!.SetActiveAsync(row.Id, false);
            await LoadRowsAsync();
        }

        private async void Enable_Click(object s, RoutedEventArgs e)
        {
            var row = Selected();
            if (row is null) return;

            await _svc!.SetActiveAsync(row.Id, true);
            await LoadRowsAsync();
        }

        // ---------------- SEARCH ROW VISIBILITY ----------------
        private void UpdateSearchRowVisibility()
        {
            var sv = FindDescendant<ScrollViewer>(WarehousesGrid);
            if (sv == null)
            {
                SearchRow.Visibility = Visibility.Collapsed;
                return;
            }

            SearchRow.Visibility =
                sv.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var sub = FindDescendant<T>(child);
                if (sub != null) return sub;
            }
            return null;
        }

        // ---------------- KEY SHORTCUTS ----------------
        public event EventHandler? CloseRequested;
        private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Edit_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        // ---------------- OPENING STOCK ----------------
        private void OpeningStock_Click(object s, RoutedEventArgs e)
        {
            var wh = Selected();
            if (wh == null) return;

            if (!AuthZ.IsAdmin())
            {
                MessageBox.Show("Only Admin can create or edit Opening Stock.", "Not allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var nav = App.Services.GetRequiredService<IViewNavigator>();
            var make = App.Services.GetRequiredService<Func<InventoryLocationType, int, string, OpeningStockView>>();

            string ctx = $"Opening:{InventoryLocationType.Warehouse}:{wh.Id}";
            string label = string.IsNullOrWhiteSpace(wh.Code)
                ? wh.Name
                : $"{wh.Code} - {wh.Name}";

            if ((nav as ViewNavigator)?.TryActivateByContext(ctx) == true)
                return;

            var view = make(InventoryLocationType.Warehouse, wh.Id, label);
            var tab = nav.OpenTab(view, $"Opening Stock – {label}", ctx);
            view.CloseRequested += (_, __) => nav.CloseTab(tab);
        }
    }
}
