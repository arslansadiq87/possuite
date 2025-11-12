// Pos.Client.Wpf/Windows/Shell/LoginWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.Services.Security;

namespace Pos.Client.Wpf.Windows.Shell
{
    public partial class LoginWindow : Window
    {
        private readonly IAuthService _auth;
        private readonly AppState _state;

        public LoginWindow()
        {
            InitializeComponent();
            _auth = App.Services.GetRequiredService<IAuthService>();
            _state = App.Services.GetRequiredService<AppState>();
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

        private void UserBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                var req = new TraversalRequest(FocusNavigationDirection.Next);
                (Keyboard.FocusedElement as UIElement)?.MoveFocus(req);
            }
        }

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
                    MessageBox.Show("Please enter username.", "Login", MessageBoxButton.OK, MessageBoxImage.Information);
                    UserBox.Focus();
                    return;
                }

                var result = await _auth.LoginAsync(username, password);
                if (!result.Ok)
                {
                    MessageBox.Show(result.Error ?? "Login failed.", "Login", MessageBoxButton.OK, MessageBoxImage.Error);
                    PassBox.Clear();
                    PassBox.Focus();
                    return;
                }

                LocalPrefs.SaveLastUsername(username);

                // Use the user returned by AuthService (DTO, not EF entity)
                var user = result.User;
                if (user is null)
                {
                    MessageBox.Show("User record missing after successful login.", "Login",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Populate AppState (no EF entities in UI)
                _state.CurrentUserId = user.Id;
                _state.CurrentUserName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;

                DialogResult = true; // close login window
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
