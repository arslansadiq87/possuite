using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// WPF
using System.Windows;
using System.Windows.Controls;              // PasswordBox

// WinForms (only for FolderBrowserDialog)
using WF = System.Windows.Forms;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Pos.Client.Wpf.Security;              // AuthZ (admin checks)
using Pos.Domain.Services;                  // IServerSettingsService, IBackupService, ITerminalContext
using Pos.Domain.Settings;                  // ServerSettings

namespace Pos.Client.Wpf.Windows.Settings
{
    public sealed partial class BackupSettingsViewModel : ObservableObject
    {
        // ───────────────── Services (UI never touches EF) ─────────────────
        private readonly IServerSettingsService _serverSvc;
        private readonly Pos.Client.Wpf.Services.IBackupService _backupSvc;
        private readonly ITerminalContext _terminal;

        // ───────────────── Admin gate ─────────────────
        [ObservableProperty] private bool isAdmin;

        // ───────────────── Server settings ─────────────────
        [ObservableProperty] private string? baseUrl;
        [ObservableProperty] private string? apiKey;
        [ObservableProperty] private string? outletCode;      // self-filled (read-only in UI)
        [ObservableProperty] private string? counterCode;     // self-filled (read-only in UI)
        [ObservableProperty] private bool autoSyncEnabled;
        [ObservableProperty] private int pushIntervalSec;
        [ObservableProperty] private int pullIntervalSec;

        // ───────────────── Backup settings (moved to ServerSettings) ─────────────────
        [ObservableProperty] private bool enableDailyBackup;
        [ObservableProperty] private bool enableHourlyBackup;
        [ObservableProperty] private string? backupBaseFolder;
        [ObservableProperty] private bool useServerForBackupRestore;

        public bool HasBackupFolder => !string.IsNullOrWhiteSpace(BackupBaseFolder);
        partial void OnBackupBaseFolderChanged(string? value) => OnPropertyChanged(nameof(HasBackupFolder));

        // Radio helpers
        public bool IsOfflineMode
        {
            get => !UseServerForBackupRestore;
            set { UseServerForBackupRestore = !value; OnPropertyChanged(nameof(IsOfflineMode)); OnPropertyChanged(nameof(IsServerMode)); }
        }
        public bool IsServerMode
        {
            get => UseServerForBackupRestore;
            set { UseServerForBackupRestore = value; OnPropertyChanged(nameof(IsOfflineMode)); OnPropertyChanged(nameof(IsServerMode)); }
        }

        // Keep the loaded row
        private ServerSettings? _loaded;

        public BackupSettingsViewModel(
            IServerSettingsService serverSvc,
            Services.IBackupService backupSvc,
            ITerminalContext terminal)
        {
            _serverSvc = serverSvc;
            _backupSvc = backupSvc;
            _terminal = terminal;

            _ = InitAsync();
        }

        // ───────────────── Lifecycle ─────────────────
        private async Task InitAsync()
        {
            try
            {
                var ct = CancellationToken.None;

                // Admin gate
                IsAdmin = await AuthZ.IsAdminAsync(ct);
                OnPropertyChanged(nameof(IsAdmin));

                var s = await _serverSvc.GetAsync(ct);
                _loaded = s;

                // Server fields
                BaseUrl = s.BaseUrl;
                ApiKey = s.ApiKey;
                AutoSyncEnabled = s.AutoSyncEnabled;
                PushIntervalSec = s.PushIntervalSec;
                PullIntervalSec = s.PullIntervalSec;

                // Self-filled from terminal scope (ids → string; no EF in VM)
                OutletCode = _terminal.OutletId > 0 ? _terminal.OutletId.ToString() : null;
                CounterCode = _terminal.CounterId > 0 ? _terminal.CounterId.ToString() : null;

                // Backup fields
                EnableDailyBackup = s.EnableDailyBackup;
                EnableHourlyBackup = s.EnableHourlyBackup;
                BackupBaseFolder = s.BackupBaseFolder;
                UseServerForBackupRestore = s.UseServerForBackupRestore;

                OnPropertyChanged(nameof(IsOfflineMode));
                OnPropertyChanged(nameof(IsServerMode));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to load settings:\n\n" + ex.Message,
                    "Server, Backup & Restore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────────────── Commands ─────────────────

        // PasswordBox → VM (safe bridge for API key)
        [RelayCommand]
        private void SetApiKeyFromPasswordBox(PasswordBox? box)
        {
            if (box is null) return;
            ApiKey = box.Password;
        }

        [RelayCommand]
        private void BrowseForFolder()
        {
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "Select a folder to store backups (Daily/Hourly subfolders will be created automatically)"
            };

            if (!string.IsNullOrWhiteSpace(BackupBaseFolder) && Directory.Exists(BackupBaseFolder))
                dlg.SelectedPath = BackupBaseFolder;

            var result = dlg.ShowDialog();
            if (result == WF.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                BackupBaseFolder = dlg.SelectedPath;
                EnsureSub("Daily");
                EnsureSub("Hourly");
            }

            static void EnsureSub(string name)
            {
                try
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        // BackupBaseFolder! is safe after assignment above
                        var path = Path.Combine(Environment.CurrentDirectory, name); // placeholder; will not be used because we pass full combine below
                    }
                }
                catch { /* ignore */ }
            }

            // actually ensure the created subfolders under the selected base
            try { Directory.CreateDirectory(Path.Combine(BackupBaseFolder!, "Daily")); } catch { }
            try { Directory.CreateDirectory(Path.Combine(BackupBaseFolder!, "Hourly")); } catch { }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (!IsAdmin)
            {
                System.Windows.MessageBox.Show("Only admins can change these settings.",
                    "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if ((EnableDailyBackup || EnableHourlyBackup) && string.IsNullOrWhiteSpace(BackupBaseFolder))
            {
                System.Windows.MessageBox.Show("Please select a Backup Location before enabling Daily/Hourly backup.",
                    "Server, Backup & Restore", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var ct = CancellationToken.None;
                var s = _loaded ?? new ServerSettings { Id = 1 };

                // Server fields
                s.BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl.Trim();
                s.ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
                s.AutoSyncEnabled = AutoSyncEnabled;
                s.PushIntervalSec = PushIntervalSec <= 0 ? 15 : PushIntervalSec;
                s.PullIntervalSec = PullIntervalSec <= 0 ? 15 : PullIntervalSec;

                // Self-filled identifiers (no UI editing)
                s.OutletCode = _terminal.OutletId > 0 ? _terminal.OutletId.ToString() : null;
                s.CounterCode = _terminal.CounterId > 0 ? _terminal.CounterId.ToString() : null;

                // Backup fields
                s.EnableDailyBackup = EnableDailyBackup;
                s.EnableHourlyBackup = EnableHourlyBackup;
                s.BackupBaseFolder = string.IsNullOrWhiteSpace(BackupBaseFolder) ? null : BackupBaseFolder.Trim();
                s.UseServerForBackupRestore = UseServerForBackupRestore;

                await _serverSvc.UpsertAsync(s, ct);
                _loaded = s;

                System.Windows.MessageBox.Show("Settings saved.", "Server, Backup & Restore",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to save settings:\n\n" + ex.Message,
                    "Server, Backup & Restore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand] private Task RestoreFromDailyAsync() => RestoreFromLocalAsync("Daily");
        [RelayCommand] private Task RestoreFromHourlyAsync() => RestoreFromLocalAsync("Hourly");

        private async Task RestoreFromLocalAsync(string subFolder)
        {
            if (UseServerForBackupRestore)
            {
                System.Windows.MessageBox.Show("Server-based restore is not implemented yet.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite backups|*.db;*.sqlite;*.bak|All files|*.*",
                Title = $"Select {subFolder.ToLower()} backup file"
            };

            if (!string.IsNullOrWhiteSpace(BackupBaseFolder))
            {
                var initial = Path.Combine(BackupBaseFolder, subFolder);
                if (Directory.Exists(initial))
                    dlg.InitialDirectory = initial;
            }

            if (dlg.ShowDialog() != true) return;

            var confirm = System.Windows.MessageBox.Show(
                "This will overwrite the local POS database with the selected backup.\n\n" +
                "The application will close after restore. Continue?",
                "Restore Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await _backupSvc.RestoreFromLocalBackupAsync(dlg.FileName, CancellationToken.None);
                System.Windows.MessageBox.Show("Restore completed. The application will now close. Please restart.",
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Restore failed:\n\n" + ex.Message,
                    "Restore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
