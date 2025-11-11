using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Services;   // BrandService
using System.Threading.Tasks;
using Pos.Domain.Models.Catalog; // BrandRowDto

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class BrandsWindow : Window
    {
        private IBrandService? _svc;
        private Func<EditBrandWindow>? _editBrandFactory;
        private readonly bool _design;
        private bool _queuedVisibilityCheck;
        private bool _inVisibilityUpdate;
        private System.Windows.Threading.DispatcherOperation? _visOp;

        public BrandsWindow()
        {
            InitializeComponent();

            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;

            _svc = App.Services.GetRequiredService<IBrandService>();
            _editBrandFactory = () => App.Services.GetRequiredService<EditBrandWindow>();

            Loaded += async (_, __) =>
            {
                await LoadRowsAsync();
                UpdateSearchVisibilitySoon();
            };
            SizeChanged += (_, __) => UpdateSearchVisibilitySoon();
        }

        // Row DTO for UI binding (mapped from BrandService.BrandRowDto)
        private sealed class BrandRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public bool IsActive { get; set; }
            public int ItemCount { get; set; }
            public DateTime? CreatedAtUtc { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
        }

        private async Task LoadRowsAsync()
        {
            if (_design || _svc == null) return;

            try
            {
                var term = (SearchBox.Text ?? "").Trim();
                var includeInactive = ShowInactive.IsChecked == true;
                var rows = await _svc.SearchAsync(term, includeInactive);
                BrandsList.ItemsSource = rows;
                UpdateActionButtons();
                UpdateSearchVisibilitySoon();
                System.Diagnostics.Debug.WriteLine($"[BrandsWindow] rows={rows.Count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load brands: " + ex.Message, "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
        }

        private void List_MouseDoubleClick(object s, MouseButtonEventArgs e)
        {
            Edit_Click(s, e);
        }

        private async void SearchBox_TextChanged(object s, TextChangedEventArgs e)
        {
            await LoadRowsAsync();
        }

        private async void FilterChanged(object s, RoutedEventArgs e)
        {
            await LoadRowsAsync();
        }

        private BrandRowDto? Selected()
            => BrandsList.SelectedItem as BrandRowDto;

        private void UpdateActionButtons()
        {
            if (EditBtn == null || EnableBtn == null || DisableBtn == null)
                return;

            var row = Selected();
            if (row == null)
            {
                EditBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Collapsed;
                DisableBtn.Visibility = Visibility.Collapsed;
                return;
            }

            EditBtn.Visibility = Visibility.Visible;
            if (row.IsActive)
            {
                DisableBtn.Visibility = Visibility.Visible;
                EnableBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                DisableBtn.Visibility = Visibility.Collapsed;
                EnableBtn.Visibility = Visibility.Visible;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_design) return;
            var dlg = _editBrandFactory!();
            dlg.Owner = this;
            dlg.EditId = null;
            if (dlg.ShowDialog() == true)
                _ = LoadRowsAsync();
        }

        private void Edit_Click(object? sender, RoutedEventArgs e)
        {
            if (_design) return;
            var row = Selected(); if (row is null) return;

            var dlg = _editBrandFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true)
                _ = LoadRowsAsync();
        }

        private async void Disable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row is null || _svc == null) return;

            if (MessageBox.Show($"Disable brand “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            await _svc.SetActiveAsync(row.Id, false);
            await LoadRowsAsync();
        }

        private async void Enable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row is null || _svc == null) return;
            await _svc.SetActiveAsync(row.Id, true);
            await LoadRowsAsync();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Edit_Click(sender, e);
        }

        // --- Search visibility (only when scrolling) -------------------------

        private void UpdateSearchVisibilitySoon()
        {
            if (_queuedVisibilityCheck) return;
            _queuedVisibilityCheck = true;

            _visOp = Dispatcher.InvokeAsync(() =>
            {
                _queuedVisibilityCheck = false;
                UpdateSearchVisibility();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateSearchVisibility()
        {
            if (_inVisibilityUpdate) return;
            _inVisibilityUpdate = true;

            try
            {
                var sv = FindDescendantIter<ScrollViewer>(BrandsList);
                var shouldShow = sv != null &&
                                 (sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ||
                                  sv.ScrollableHeight > 0);

                var target = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                if (SearchPanel.Visibility != target)
                    SearchPanel.Visibility = target;
            }
            finally
            {
                _inVisibilityUpdate = false;
            }
        }

        private static T? FindDescendantIter<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var q = new System.Collections.Generic.Queue<DependencyObject>();
            q.Enqueue(root);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur is T hit) return hit;

                int count = VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < count; i++)
                    q.Enqueue(VisualTreeHelper.GetChild(cur, i));
            }
            return null;
        }
    }
}
