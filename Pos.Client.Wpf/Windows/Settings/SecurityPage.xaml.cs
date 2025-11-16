using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Pos.Client.Wpf.Services;
using Pos.Domain.Services;

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class SecurityPage : UserControl
    {
        private readonly IUserAdminService _userAdmin;

        public SecurityPage()
        {
            InitializeComponent();
            _userAdmin = App.Services.GetRequiredService<IUserAdminService>();
        }

        private async void SavePin_Click(object sender, RoutedEventArgs e)
        {
            var userId = AppState.Current.CurrentUserId;
            if (userId <= 0)
            {
                MessageBox.Show("No logged-in user.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var currentPwd = PinCurrentPasswordBox.Password;
            var newPin = NewPinBox.Password;
            var confirmPin = ConfirmPinBox.Password;

            if (string.IsNullOrWhiteSpace(currentPwd))
            {
                MessageBox.Show("Enter your current password.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPin))
            {
                MessageBox.Show("Enter a new PIN.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPin != confirmPin)
            {
                MessageBox.Show("PIN and confirmation do not match.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPin.Length < 4 || newPin.Length > 6 || !newPin.All(char.IsDigit))
            {
                MessageBox.Show("PIN must be 4–6 digits.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _userAdmin.ChangeOwnPinAsync(userId, currentPwd, newPin);
                // Update in-memory current user so unlock screens see the PIN immediately
                var state = AppState.Current;
                if (state.CurrentUser != null && state.CurrentUser.Id == userId)
                {
                    // We don't know the DB's exact hash (service hashed it with its own salt),
                    // but any valid BCrypt hash of this PIN will verify correctly.
                    state.CurrentUser.PinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
                }

                PinCurrentPasswordBox.Password = "";
                NewPinBox.Password = "";
                ConfirmPinBox.Password = "";

                MessageBox.Show("PIN updated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Failed to update PIN",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SavePassword_Click(object sender, RoutedEventArgs e)
        {
            var userId = AppState.Current.CurrentUserId;
            if (userId <= 0)
            {
                MessageBox.Show("No logged-in user.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var currentPwd = CurrentPasswordBox.Password;
            var newPwd = NewPasswordBox.Password;
            var confirmPwd = ConfirmPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(currentPwd))
            {
                MessageBox.Show("Enter your current password.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(newPwd))
            {
                MessageBox.Show("Enter a new password.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPwd != confirmPwd)
            {
                MessageBox.Show("Password and confirmation do not match.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (newPwd.Length < 4)
            {
                MessageBox.Show("Password must be at least 4 characters.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await _userAdmin.ChangeOwnPasswordAsync(userId, currentPwd, newPwd);

                CurrentPasswordBox.Password = "";
                NewPasswordBox.Password = "";
                ConfirmPasswordBox.Password = "";

                MessageBox.Show("Password updated successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Failed to update password",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
