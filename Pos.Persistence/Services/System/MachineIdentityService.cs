using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Pos.Domain.Services.System;

namespace Pos.Persistence.Services.Systems
{
    /// <summary>
    /// Machine identity provider backed by a file in %ProgramData%\PosSuite (Windows) or the OS common app-data dir.
    /// No UI calls, fully async, cancellation-aware.
    /// </summary>
    public sealed class MachineIdentityService : IMachineIdentityService
    {
        private const string FileName = "pos_machine_id.txt";
        private static readonly string PathDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PosSuite");
        private static readonly string PathFile = Path.Combine(PathDir, FileName);

        private static readonly SemaphoreSlim Gate = new(1, 1);

        public async Task<string> GetMachineIdAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Serialize access to avoid races creating the ID file.
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Fast path: file exists
                if (File.Exists(PathFile))
                {
                    // Use async read to honor CT
                    var existing = await File.ReadAllTextAsync(PathFile, ct).ConfigureAwait(false);
                    var trimmed = existing.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        return trimmed;
                }

                // Ensure directory exists
                Directory.CreateDirectory(PathDir);

                // Create new 32-char GUID (no hyphens)
                var id = Guid.NewGuid().ToString("N");

                // Write atomically: write to temp, then move
                var tmp = PathFile + ".tmp";
                await File.WriteAllTextAsync(tmp, id, ct).ConfigureAwait(false);

                // Replace/commit
                if (File.Exists(PathFile))
                    File.Delete(PathFile);
                File.Move(tmp, PathFile);

                return id;
            }
            catch
            {
                // Ultra-fallback: volatile ID (won’t survive restart)
                return "VOLATILE-" + Guid.NewGuid().ToString("N");
            }
            finally
            {
                Gate.Release();
            }
        }

        public Task<string> GetMachineNameAsync(CancellationToken ct = default)
        {
            // Environment.MachineName is synchronous and cheap; still expose async signature.
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Environment.MachineName);
        }
    }
}
