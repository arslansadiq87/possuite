// Pos.Client.Wpf/Windows/Shell/LoginWindow.cs
using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class LoginWindow : Window
    {
        private readonly AuthService _auth;
        private readonly AppState _state;
        private readonly IUserReadService _users;

        public LoginWindow()
        {
            InitializeComponent();
            _auth = App.Services.GetRequiredService<AuthService>();
            _state = App.Services.GetRequiredService<AppState>();
            _users = App.Services.GetRequiredService<IUserReadService>();
        }

        private void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            var last = LocalPrefs.LoadLastUsername();
            if (!string.IsNullOrWhiteSpace(last))
            {
                UserBox.Text = last;
                PassBox.Focus();
            }
            else
            {
                UserBox.Focus();
            }
        }

        // Enter in username → move focus to password
        private void UserBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
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
            try
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

                LocalPrefs.SaveLastUsername(username);

                // fetch user via read service (no EF here)
                var user = await _users.GetByUsernameAsync(username);
                if (user is null)
                {
                    MessageBox.Show("User record not found after successful login.", "Login",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _state.CurrentUser = user;

                // Success → let App proceed
                DialogResult = true; // closes this window
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
