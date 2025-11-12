using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Persistence.Services;
using Pos.Domain.Models.Parties;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class PartiesView : UserControl
    {
        private IPartyService? _svc;
        private Func<EditPartyWindow>? _editFactory;
        private readonly bool _design;
        private ObservableCollection<PartyRowDto> _rows = new();

        public PartiesView()
        {
            InitializeComponent();
            _design = DesignerProperties.GetIsInDesignMode(this);
            if (_design) return;
            _svc = App.Services.GetRequiredService<IPartyService>();
            _editFactory = () => App.Services.GetRequiredService<EditPartyWindow>();
            Loaded += async (_, __) => await RefreshRowsAsync();
            Grid.ItemsSource = _rows;
        }

        private async Task RefreshRowsAsync()
        {
            if (_design || _svc == null) return;

            try
            {
                var term = (SearchText.Text ?? "").Trim();
                var onlyActive = OnlyActiveCheck.IsChecked == true;
                var wantCust = RoleCustomerCheck.IsChecked == true;
                var wantSupp = RoleSupplierCheck.IsChecked == true;

                var list = await _svc.SearchAsync(term, onlyActive, wantCust, wantSupp);

                _rows.Clear();
                foreach (var r in list)
                    _rows.Add(r);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load parties: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SearchText_TextChanged(object sender, TextChangedEventArgs e)
            => await RefreshRowsAsync();

        private async void FilterChanged(object sender, RoutedEventArgs e)
            => await RefreshRowsAsync();

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var w = _editFactory!();
            if (w.ShowDialog() == true)
                _ = RefreshRowsAsync();
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not PartyRowDto row) return;

            var w = _editFactory!();
            w.LoadParty(row.Id);
            if (w.ShowDialog() == true)
                _ = RefreshRowsAsync();
        }

        private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => Edit_Click(sender, e);

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                New_Click(sender, e);
            if (e.Key == Key.Enter)
                Edit_Click(sender, e);
        }
    }
}
