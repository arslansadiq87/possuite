// Pos.Client.Wpf/Windows/Settings/BackupSettingsPage.xaml.cs
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Pos.Client.Wpf.Windows.Settings
{
    public partial class BackupSettingsPage : UserControl
    {
        public BackupSettingsPage()
        {
            InitializeComponent();

            if (DesignerProperties.GetIsInDesignMode(this))
                return;

            if (Application.Current is App && App.Services is not null)
            {
                DataContext = App.Services.GetRequiredService<BackupSettingsViewModel>();
            }
            else
            {
                DataContext = new BackupSettingsViewModelStub();
            }
        }

        // minimal design-time stub
        internal sealed class BackupSettingsViewModelStub
        {
            public bool EnableDailyBackup { get; set; }
            public bool EnableHourlyBackup { get; set; }
            public string? BackupBaseFolder { get; set; }
            public bool UseServerForBackupRestore { get; set; }
        }
    }
}
