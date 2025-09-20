using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Common
{
    public partial class SimplePromptWindow : Window
    {
        private readonly Dictionary<string, FrameworkElement> _controls = new();

        public SimplePromptWindow(string title, params (string key, object value)[] fields)
        {
            InitializeComponent();
            Title = title;
            foreach (var (key, value) in fields)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(new TextBlock { Text = key + ":", Width = 220, VerticalAlignment = VerticalAlignment.Center });
                FrameworkElement input = value is bool b ? new CheckBox { IsChecked = b, Width = 120 }
                                                         : new TextBox { Text = value?.ToString() ?? "", Width = 160 };
                _controls[key] = input;
                row.Children.Add(input);
                FormHost.Children.Add(row);
            }
        }

        public string GetText(string key) => _controls[key] is TextBox t ? t.Text : "";
        public bool GetBool(string key) => _controls[key] is CheckBox c && (c.IsChecked ?? false);

        private void Ok_Click(object? s, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object? s, RoutedEventArgs e) => DialogResult = false;
    }
}
