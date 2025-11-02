using System.Windows;

namespace Pos.Client.Wpf.Windows.Common
{
    public partial class InputDialog : Window
    {
        public string TitleText { get; }
        public string Message { get; }
        public string Text { get; set; }

        public InputDialog(string title, string message, string defaultText)
        {
            InitializeComponent();
            TitleText = title;
            Title = title;
            Message = message;
            Text = defaultText;
            DataContext = this;
            Loaded += (_, __) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        public static string? Show(Window? owner, string title, string message, string defaultText)
        {
            var dlg = new InputDialog(title, message, defaultText) { Owner = owner };
            var result = dlg.ShowDialog();
            return result == true ? dlg.Text : null;
        }
    }
}
