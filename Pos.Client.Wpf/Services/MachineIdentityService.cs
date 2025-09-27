using System;
using System.IO;

//Pos.Client.Wpf/Services/MachineIdentityService.cs
namespace Pos.Client.Wpf.Services
{
    public interface IMachineIdentityService
    {
        string GetMachineId();
        string GetMachineName();
    }

    public sealed class MachineIdentityService : IMachineIdentityService
    {
        private const string FileName = "pos_machine_id.txt";
        private static readonly string PathDir =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PosSuite"); // %ProgramData%\PosSuite
        private static readonly string PathFile = System.IO.Path.Combine(PathDir, FileName);

        public string GetMachineId()
        {
            try
            {
                if (File.Exists(PathFile))
                    return File.ReadAllText(PathFile).Trim();

                Directory.CreateDirectory(PathDir);
                var id = Guid.NewGuid().ToString("N");   // 32 chars, no hyphens
                File.WriteAllText(PathFile, id);
                return id;
            }
            catch
            {
                // ultra-fallback: volatile id (won’t survive app restart if file write failed)
                return "VOLATILE-" + Guid.NewGuid().ToString("N");
            }
        }

        public string GetMachineName() => Environment.MachineName;
    }
}
