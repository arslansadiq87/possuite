namespace Pos.Domain.Settings;

public sealed class ServerSettings
{
    public int Id { get; set; }                  // single-row table (Id = 1)
    public string? BaseUrl { get; set; }         // e.g., https://api.example.com
    public string? ApiKey { get; set; }          // X-Api-Key
    public string? OutletCode { get; set; }      // optional scoping
    public string? CounterCode { get; set; }     // optional scoping
    public bool AutoSyncEnabled { get; set; } = true;
    public int PushIntervalSec { get; set; } = 15;
    public int PullIntervalSec { get; set; } = 15;

    // ===== Moved here from InvoiceSettingsScoped =====
    public bool EnableDailyBackup { get; set; }
    public bool EnableHourlyBackup { get; set; }
    public string? BackupBaseFolder { get; set; }
    public bool UseServerForBackupRestore { get; set; }
}
