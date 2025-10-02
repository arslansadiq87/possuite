using System;
using System.IO;
using System.Text.Json;
using Pos.Domain.Entities; // for BarcodeSymbology

namespace Pos.Client.Wpf.Services
{
    public sealed class UserPrefs
    {
        public BarcodeSymbology LastSymbology { get; set; } = BarcodeSymbology.Ean13;
        public string LastSkuPrefix { get; set; } = "ITEM";
        public int LastSkuStart { get; set; } = 1;
        public int LastSkuNext { get; set; } = 1;           // 👈 new

        public string LastBarcodePrefix { get; set; } = "978000";
        public int LastBarcodeStart { get; set; } = 1;
        public int LastBarcodeNext { get; set; } = 1;       // 👈 new

    }

    public static class UserPrefsService
    {
        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PosSuite");
        private static readonly string FilePath = Path.Combine(Dir, "userprefs.json");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
        private static UserPrefs? _cache;

        public static UserPrefs Load()
        {
            if (_cache is not null) return _cache;

            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _cache = JsonSerializer.Deserialize<UserPrefs>(json) ?? new UserPrefs();
                }
                else
                {
                    _cache = new UserPrefs();
                }
            }
            catch
            {
                _cache = new UserPrefs(); // be defensive
            }

            return _cache!;
        }

        public static void Save(Action<UserPrefs> mutate)
        {
            var prefs = Load();
            mutate?.Invoke(prefs);

            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(prefs, JsonOpts);
                File.WriteAllText(FilePath, json);
                _cache = prefs;
            }
            catch
            {
                // never block UX on prefs I/O
            }
        }
    }
}
