// Pos.Client.Wpf/Windows/Common/ConfirmDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using Pos.Client.Wpf.Services;

namespace Pos.Client.Wpf.Windows.Common
{
    public partial class ConfirmDialog : UserControl
    {
        public event Action<DialogResult>? OnResult;

        public ConfirmDialog() : this("Are you sure?", "Confirm", DialogButtons.YesNo) { }

        public ConfirmDialog(string message, string? title, DialogButtons buttons)
        {
            InitializeComponent();

            TitleBlock.Text = string.IsNullOrWhiteSpace(title) ? "Confirm" : title!;
            MessageBlock.Text = message;

            // Show/hide buttons based on requested set
            OkBtn.Visibility = Visibility.Collapsed;
            CancelBtn.Visibility = Visibility.Collapsed;
            YesBtn.Visibility = Visibility.Collapsed;
            NoBtn.Visibility = Visibility.Collapsed;

            switch (buttons)
            {
                case DialogButtons.OK:
                    OkBtn.Visibility = Visibility.Visible;
                    break;
                case DialogButtons.OKCancel:
                    OkBtn.Visibility = Visibility.Visible;
                    CancelBtn.Visibility = Visibility.Visible;
                    break;
                case DialogButtons.YesNo:
                    YesBtn.Visibility = Visibility.Visible;
                    NoBtn.Visibility = Visibility.Visible;
                    break;
                case DialogButtons.YesNoCancel:
                    YesBtn.Visibility = Visibility.Visible;
                    NoBtn.Visibility = Visibility.Visible;
                    CancelBtn.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Ok_Click(object s, RoutedEventArgs e) => OnResult?.Invoke(DialogResult.OK);
        private void Cancel_Click(object s, RoutedEventArgs e) => OnResult?.Invoke(DialogResult.Cancel);
        private void Yes_Click(object s, RoutedEventArgs e) => OnResult?.Invoke(DialogResult.Yes);
        private void No_Click(object s, RoutedEventArgs e) => OnResult?.Invoke(DialogResult.No);
    }
}
