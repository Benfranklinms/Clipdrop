using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace Washline
{
    /// Watches the folder Windows saves screenshots to (Win+PrtScn, and the
    /// Snipping Tool's auto-save) and reports new ones. Washline never takes
    /// screenshots itself: you keep your usual shortcut and the line picks
    /// them up.
    public sealed class ScreenshotWatcher : IDisposable
    {
        private static readonly HashSet<string> ImageExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

        public string Folder { get; }
        private readonly FileSystemWatcher fsw;
        private readonly Dispatcher ui;
        private readonly Action<string> onNew;
        private readonly Action onChange;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ScreenshotWatcher(string folder, Dispatcher ui, Action<string> onNew, Action onChange)
        {
            Folder = folder;
            this.ui = ui;
            this.onNew = onNew;
            this.onChange = onChange;
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.EnumerateFiles(folder)) seen.Add(f);

            fsw = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            fsw.Created += (s, e) => Candidate(e.FullPath);
            fsw.Renamed += (s, e) => { Candidate(e.FullPath); Changed(); };
            fsw.Deleted += (s, e) => Changed();
        }

        public static string DefaultFolder()
        {
            var known = Native.KnownFolder(Native.ScreenshotsFolderId);
            if (!string.IsNullOrEmpty(known)) return known;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        private void Candidate(string path)
        {
            if (!ImageExtensions.Contains(Path.GetExtension(path))) return;
            lock (seen) { if (!seen.Add(path)) return; }
            // Give the writer a moment to finish the file.
            ui.BeginInvoke(new Action(() =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                t.Tick += (s, e) => { t.Stop(); if (File.Exists(path)) onNew(path); };
                t.Start();
            }));
        }

        private void Changed() => ui.BeginInvoke(onChange);

        public void Dispose() => fsw.Dispose();
    }
}
