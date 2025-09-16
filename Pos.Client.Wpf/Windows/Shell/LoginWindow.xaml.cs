//Pos.Client.Wpf/Windows/Shell/LoginWindow.cs
using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Persistence;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _auth;
        private readonly AppState _state;
        private readonly IDbContextFactory<PosClientDbContext> _dbf;

        public LoginWindow()
        {
            InitializeComponent();
            _auth = App.Services.GetRequiredService<AuthService>();
            _state = App.Services.GetRequiredService<AppState>();
            _dbf = App.Services.GetRequiredService<IDbContextFactory<PosClientDbContext>>();
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            var last = LocalPrefs.LoadLastUsername();   // from the small helper class I shared earlier
            if (!string.IsNullOrWhiteSpace(last))
            {
                UserBox.Text = last;
                PassBox.Focus();                        // prefilled → focus password
            }
            else
            {
                UserBox.Focus();                        // empty → focus username
            }
        }

        // Enter in username → move focus to next control (PasswordBox)
        private void UserBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // prevent default button from firing
                var req = new TraversalRequest(FocusNavigationDirection.Next);
                (Keyboard.FocusedElement as UIElement)?.MoveFocus(req);
            }
        }

        // Enter in password → click Login
        private void PassBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                LoginBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var username = UserBox.Text.Trim();
            var password = PassBox.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter username.", "Login",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                UserBox.Focus();
                return;
            }

            var (ok, error) = await _auth.LoginAsync(username, password);
            if (!ok)
            {
                MessageBox.Show(error, "Login", MessageBoxButton.OK, MessageBoxImage.Error);
                PassBox.Clear();
                PassBox.Focus();
                return;
            }

            // remember last successful username on this PC
            LocalPrefs.SaveLastUsername(username);

            using var db = await _dbf.CreateDbContextAsync();
            var user = await db.Users.FirstAsync(u => u.Username == username);
            _state.CurrentUser = user;

            var dash = App.Services.GetRequiredService<DashboardWindow>();
            dash.Show();
            Close();
        }
    }
}
