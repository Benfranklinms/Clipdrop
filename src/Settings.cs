using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Washline
{
    /// Everything Washline remembers, kept as JSON in %AppData%\Washline.
    public sealed class Settings
    {
        public bool SoundOn { get; set; } = true;
        /// Also hang images copied to the clipboard (plain PrtScn, Alt+PrtScn).
        public bool CatchClipboard { get; set; } = false;
        public bool Welcomed { get; set; } = false;
        public List<string> Pegged { get; set; } = new List<string>();

        public static string AppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Washline");

        /// Washline's own folder, for images caught from the clipboard. Files
        /// here are discarded to the Recycle Bin when taken down, so it never
        /// fills up with forgotten captures.
        public static string InboxDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Washline", "Captures");

        private static string FilePath => Path.Combine(AppDataDir, "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool LaunchAtLogin
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("Washline") != null;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue("Washline", "\"" + Environment.ProcessPath + "\"");
                else key.DeleteValue("Washline", false);
            }
        }
    }
}
