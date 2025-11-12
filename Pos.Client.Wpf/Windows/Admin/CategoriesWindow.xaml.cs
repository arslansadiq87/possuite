using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Services;   // CategoryService
using Pos.Domain.Models.Catalog; // ICategoryService
namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class CategoriesWindow : Window
    {
        private ICategoryService? _svc;
        private Func<EditCategoryWindow>? _editCategoryFactory;
        private readonly bool _design;
        private bool _queuedVisibilityCheck;
        private bool _inVisibilityUpdate;
        private System.Windows.Threading.DispatcherOperation? _visOp;

        public CategoriesWindow()
        {
            InitializeComponent();
            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;
            _svc = App.Services.GetRequiredService<ICategoryService>();
            _editCategoryFactory = () => App.Services.GetRequiredService<EditCategoryWindow>();
            Loaded += async (_, __) =>
            {
                await LoadRowsAsync();
                UpdateSearchVisibilitySoon();
            };
            SizeChanged += (_, __) => UpdateSearchVisibilitySoon();
        }

        private async Task LoadRowsAsync()
        {
            if (_design || _svc == null) return;
            try
            {
                var term = (SearchBox.Text ?? "").Trim();
                var includeInactive = ShowInactive.IsChecked == true;
                var rows = await _svc.SearchAsync(term, includeInactive);
                CategoriesList.ItemsSource = rows;
                UpdateActionButtons();
                UpdateSearchVisibilitySoon();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load categories: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private CategoryRowDto? Selected()
            => CategoriesList.SelectedItem as CategoryRowDto;

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

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateActionButtons();

        private void List_MouseDoubleClick(object s, MouseButtonEventArgs e)
            => Edit_Click(s, e);

        private async void SearchBox_TextChanged(object s, TextChangedEventArgs e)
            => await LoadRowsAsync();

        private async void FilterChanged(object s, RoutedEventArgs e)
            => await LoadRowsAsync();

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_design) return;
            var dlg = _editCategoryFactory!();
            dlg.Owner = this;
            dlg.EditId = null;
            if (dlg.ShowDialog() == true)
                _ = LoadRowsAsync();
        }

        private void Edit_Click(object? sender, RoutedEventArgs e)
        {
            if (_design) return;
            var row = Selected(); if (row == null) return;

            var dlg = _editCategoryFactory!();
            dlg.Owner = this;
            dlg.EditId = row.Id;
            if (dlg.ShowDialog() == true)
                _ = LoadRowsAsync();
        }

        private async void Disable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row == null || _svc == null) return;
            if (MessageBox.Show($"Disable category “{row.Name}”?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await _svc.SetActiveAsync(row.Id, false);
            await LoadRowsAsync();
        }

        private async void Enable_Click(object sender, RoutedEventArgs e)
        {
            var row = Selected(); if (row == null || _svc == null) return;
            await _svc.SetActiveAsync(row.Id, true);
            await LoadRowsAsync();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) Edit_Click(sender, e);
        }

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
                var sv = FindDescendantIter<ScrollViewer>(CategoriesList);
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
