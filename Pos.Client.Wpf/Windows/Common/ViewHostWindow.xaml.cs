using System.Windows;
using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Common
{
    public partial class ViewHostWindow : Window
    {
        public ViewHostWindow()
        {
            InitializeComponent();
        }
        public void SetView(UserControl view) => ContentHost.Content = view;
    }
}
