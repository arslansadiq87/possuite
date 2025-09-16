// Pos.Persistence/DbPath.cs
using System;
using System.IO;

namespace Pos.Persistence
{
    public static class DbPath
    {
        public static string Get()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PosSuite");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "posclient.db");
        }

        public static string ConnectionString => $"Data Source={Get()}";
    }
}
