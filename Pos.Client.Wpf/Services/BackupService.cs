// Pos.Client.Wpf/Services/BackupService.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Domain.Services;
using Pos.Domain.Settings;
using Pos.Persistence;

namespace Pos.Client.Wpf.Services
{
    public interface IBackupService
    {
        Task RunDailyBackupIfNeededAsync(CancellationToken ct);
        Task RunHourlyBackupIfDueAsync(CancellationToken ct);
        Task RestoreFromLocalBackupAsync(string backupFilePath, CancellationToken ct);
        Task CreateManualBackupAsync(string backupFilePath, CancellationToken ct);

    }

    public sealed class BackupService : IBackupService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbFactory;
        private readonly IServerSettingsService _serverSvc;
        private readonly AppState _state;
        private readonly ILogger<BackupService> _log;

        public BackupService(
            IDbContextFactory<PosClientDbContext> dbFactory,
             IServerSettingsService serverSvc,
            AppState state,
            ILogger<BackupService> log)
        {
            _dbFactory = dbFactory;
            _serverSvc = serverSvc;
            _state = state;
            _log = log;
        }

        public async Task RunDailyBackupIfNeededAsync(CancellationToken ct)
        {
            var settings = await _serverSvc.GetAsync(ct);
            if (!settings.EnableDailyBackup) return;
            var baseFolder = GetBaseFolder(settings.BackupBaseFolder);
            if (baseFolder is null) return;

            var dailyFolder = Path.Combine(baseFolder, "Daily");
            Directory.CreateDirectory(dailyFolder);

            var todayPrefix = DateTime.Today.ToString("yyyyMMdd");
            var already = Directory.EnumerateFiles(dailyFolder, todayPrefix + "_daily.db").Any();
            if (already) return;

            var destFile = Path.Combine(dailyFolder, todayPrefix + "_daily.db");
            await CreateSqliteBackupAsync(destFile, ct);
            TrimOldBackups(dailyFolder, 15);
        }

        public async Task RunHourlyBackupIfDueAsync(CancellationToken ct)
        {
            var settings = await _serverSvc.GetAsync(ct);
            if (!settings.EnableDailyBackup || !settings.EnableHourlyBackup) return;

            var baseFolder = GetBaseFolder(settings.BackupBaseFolder);
            if (baseFolder is null) return;

            var hourlyFolder = Path.Combine(baseFolder, "Hourly");
            Directory.CreateDirectory(hourlyFolder);

            var now = DateTime.Now;
            var fileName = now.ToString("yyyyMMdd_HH00") + "_hourly.db";
            var destFile = Path.Combine(hourlyFolder, fileName);

            await CreateSqliteBackupAsync(destFile, ct);
            TrimOldBackups(hourlyFolder, 24);
        }

        public async Task CreateManualBackupAsync(string backupFilePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentException("Backup file path is required.", nameof(backupFilePath));

            await CreateSqliteBackupAsync(backupFilePath, ct);
        }



        // Pos.Client.Wpf/Services/BackupService.cs

        public Task RestoreFromLocalBackupAsync(string backupFilePath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
                throw new ArgumentException("Backup file path is required.", nameof(backupFilePath));

            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("Backup file not found.", backupFilePath);

            var target = DbPath.Get();                         // %LOCALAPPDATA%\PosSuite\posclient.db
            var dir = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(dir);

            void TryDelete(string path)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // ignore – last resort is manual delete
                }
            }

            // IMPORTANT: remove any previous DB and WAL/SHM
            TryDelete(target);
            TryDelete(target + "-wal");
            TryDelete(target + "-shm");

            // Copy backup file to be the new main DB
            File.Copy(backupFilePath, target, overwrite: true);

            return Task.CompletedTask;
        }



        private static string? GetBaseFolder(string? baseFolder)
        {
            if (string.IsNullOrWhiteSpace(baseFolder)) return null;
            try { Directory.CreateDirectory(baseFolder); return baseFolder; }
            catch { return null; }
        }

        private async Task CreateSqliteBackupAsync(string destFile, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var escaped = destFile.Replace("'", "''");
            var sql = $"VACUUM INTO '{escaped}'";

            _log.LogInformation("Creating SQLite backup at {DestFile}", destFile);
            await db.Database.ExecuteSqlRawAsync(sql, ct);

            // sanity check – SQLite DB won't be tiny
            var fi = new FileInfo(destFile);
            if (!fi.Exists || fi.Length < 4096)
            {
                throw new InvalidOperationException(
                    $"Backup file '{destFile}' looks invalid (size={fi.Length} bytes).");
            }
        }


        private static void TrimOldBackups(string folder, int maxFiles)
        {
            try
            {
                var files = Directory.EnumerateFiles(folder, "*.db")
                    .Select(path => new FileInfo(path))
                    .OrderBy(f => f.CreationTimeUtc)
                    .ToList();

                while (files.Count > maxFiles)
                {
                    var oldest = files[0];
                    try { File.Delete(oldest.FullName); } catch { /* ignore */ }
                    files.RemoveAt(0);
                }
            }
            catch
            {
                // ignore rotation errors
            }
        }
    }
}
