using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Domain.Hr;
using Pos.Persistence.Services;

namespace Pos.Client.Wpf.Windows.Admin
{
    public partial class StaffView : UserControl
    {
        private readonly StaffService _svc;

        public StaffView()
        {
            InitializeComponent();
            _svc = App.Services.GetRequiredService<StaffService>();
            Loaded += async (_, __) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            Grid.ItemsSource = await _svc.GetAllAsync();
        }

        private async void New_Click(object sender, RoutedEventArgs e)
        {
            var dlg = App.Services.GetRequiredService<StaffDialog>();
            dlg.Configure(null); // New mode

            var owner = Window.GetWindow(this)
                       ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;
            if (owner != null) dlg.Owner = owner;

            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not Staff s)
            {
                MessageBox.Show("Select a staff row first.");
                return;
            }

            var dlg = App.Services.GetRequiredService<StaffDialog>();
            dlg.Configure(s.Id); // Edit mode

            var owner = Window.GetWindow(this)
                       ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;
            if (owner != null) dlg.Owner = owner;

            if (dlg.ShowDialog() == true)
                await RefreshAsync();
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (Grid.SelectedItem is not Staff s)
            {
                MessageBox.Show("Select a staff row first."); return;
            }

            if (MessageBox.Show($"Delete {s.FullName}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            try
            {
                await _svc.DeleteAsync(s.Id);
                await RefreshAsync();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Failed to delete staff:\n\n" + ex.Message, "Staff",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    }
}
