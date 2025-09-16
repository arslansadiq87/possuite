//Pos.Client.Wpf/Services/LocalPrefs.cs
using System;
using System.IO;
using System.Text.Json;

namespace Pos.Client.Wpf.Services
{
    public static class LocalPrefs
    {
        private const string AppFolder = "PosSuite";
        private const string FileName = "prefs.json";

        private class Data
        {
            public string? LastUsername { get; set; }
        }

        private static string GetPath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public static string? LoadLastUsername()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Data>(json);
                return data?.LastUsername;
            }
            catch { return null; }
        }

        public static void SaveLastUsername(string username)
        {
            try
            {
                var path = GetPath();
                var data = new Data { LastUsername = username };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { /* ignore */ }
        }
    }
}
