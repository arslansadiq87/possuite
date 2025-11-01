using System.Windows.Controls;

namespace Pos.Client.Wpf.Windows.Settings;

public partial class PreferencesPage : UserControl
{
    public PreferencesViewModel VM { get; }

    public PreferencesPage(PreferencesViewModel vm)
    {
        InitializeComponent();
        VM = vm;
        DataContext = VM;
        Loaded += async (_, __) => await VM.LoadAsync();
    }
}
