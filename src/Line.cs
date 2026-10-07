using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;

namespace Clipdrop
{
    /// One screenshot hanging on the line.
    public sealed class Pegged
    {
        private static readonly Random Rng = new Random();

        public Guid Id { get; } = Guid.NewGuid();
        public string Path { get; }
        public BitmapSource Thumb { get; set; }
        public DateTime Stamp { get; set; }
        /// Every photo hangs a little crooked, like on a real line.
        public double Tilt { get; } = Rng.NextDouble() * 5 - 2.5;
        public bool Falling { get; set; }

        public Pegged(string path, BitmapSource thumb)
        {
            Path = path;
            Thumb = thumb;
            Stamp = SafeWriteTime(path);
        }

        public static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }
    }

    /// The line itself: what hangs on it and what you can do with each item.
    /// The files never move. The line is only a view onto them.
    public sealed class Line
    {
        private readonly Settings settings;
        private readonly Dispatcher ui;
        private readonly Random rng = new Random();
        private readonly DispatcherTimer gustTimer;

        public List<Pegged> Items { get; } = new List<Pegged>();
        public int MaxItems { get; set; } = 8;
        public Guid? DraggingId { get; set; }
        public Guid? PressedId { get; set; }

        public event Action<Pegged> Hung;
        public event Action<Pegged> Fell;
        public event Action<Pegged> Copied;
        public event Action<Pegged> ThumbChanged;
        public event Action Gust;
        public event Action Changed;

        public int LiveCount => Items.Count(i => !i.Falling);
        public IEnumerable<Pegged> Live => Items.Where(i => !i.Falling);

        /// Set while Clipdrop itself writes to the clipboard, so the clipboard
        /// listener does not hang our own copy again.
        public bool SuppressClipboard { get; private set; }

        public Line(Settings settings, Dispatcher ui)
        {
            this.settings = settings;
            this.ui = ui;
            foreach (var p in settings.Pegged.ToList())
                if (File.Exists(p)) Hang(p, quietly: true);

            // Every so often a little wind moves the line. It is the detail
            // that makes it feel like an object and not a widget.
            gustTimer = new DispatcherTimer(DispatcherPriority.Background, ui);
            gustTimer.Tick += (s, e) =>
            {
                gustTimer.Interval = TimeSpan.FromSeconds(7 + rng.NextDouble() * 9);
                if (Items.Count > 0 && DraggingId == null) Gust?.Invoke();
            };
            gustTimer.Interval = TimeSpan.FromSeconds(9);
            gustTimer.Start();
        }

        // ---- Hanging and dropping ----

        public Pegged Hang(string path, bool quietly = false)
        {
            if (Items.Any(i => !i.Falling && SamePath(i.Path, path))) return null;
            var thumb = Thumbnails.Make(path, 480);
            if (thumb == null) return null;
            var item = new Pegged(path, thumb);
            Items.Add(item);
            // A full line lets the oldest photo fall off the far end.
            while (LiveCount > MaxItems)
            {
                var oldest = Items.First(i => !i.Falling);
                Drop(oldest.Id, quietly: true);
            }
            Save();
            if (!quietly) Play("Windows Navigation Start.wav");
            Hung?.Invoke(item);
            Changed?.Invoke();
            return item;
        }

        public void Drop(Guid id, bool quietly = false)
        {
            var item = Find(id);
            if (item == null || item.Falling) return;
            item.Falling = true;
            Save();
            if (!quietly) Play("Windows Pop-up Blocked.wav");
            Fell?.Invoke(item);
            Changed?.Invoke();
            After(0.6, () => { Items.Remove(item); Changed?.Invoke(); });
        }

        public void Clear()
        {
            var live = Live.ToList();
            for (int n = 0; n < live.Count; n++)
            {
                var item = live[n];
                bool quiet = n > 0;
                After(0.06 * n, () => Drop(item.Id, quiet));
            }
        }

        /// Photos whose file was deleted or moved away fall off by themselves.
        public void Prune()
        {
            foreach (var item in Live.ToList())
                if (!File.Exists(item.Path)) Drop(item.Id, quietly: true);
        }

        /// After editing in Paint, the photo on the line shows the new version.
        public void RefreshEdited()
        {
            foreach (var item in Live.ToList())
            {
                var stamp = Pegged.SafeWriteTime(item.Path);
                if (stamp > item.Stamp)
                {
                    var thumb = Thumbnails.Make(item.Path, 480);
                    if (thumb == null) continue;
                    item.Thumb = thumb;
                    item.Stamp = stamp;
                    ThumbChanged?.Invoke(item);
                }
            }
        }

        // ---- Actions on one photo ----

        public void Copy(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            try
            {
                var data = new DataObject();
                var full = Thumbnails.LoadFull(item.Path);
                if (full != null) data.SetImage(full);
                var files = new StringCollection { item.Path };
                data.SetFileDropList(files);
                SuppressClipboard = true;
                Clipboard.SetDataObject(data, true);
                Copied?.Invoke(item);
            }
            catch { SystemSounds.Beep.Play(); }
            finally { After(0.5, () => SuppressClipboard = false); }
        }

        public void Open(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            Shell(item.Path);
        }

        /// Long press: open the photo in Paint to mark it up.
        public void Markup(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            try { Process.Start(new ProcessStartInfo("mspaint.exe", "\"" + item.Path + "\"") { UseShellExecute = true }); }
            catch { Shell(item.Path); }
        }

        public void Reveal(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + item.Path + "\""); } catch { }
        }

        /// Sends the file to the Recycle Bin and takes the photo off the line.
        public void Trash(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            try
            {
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Play("Windows Recycle.wav");
                Drop(id, quietly: true);
            }
            catch { SystemSounds.Beep.Play(); }
        }

        /// Whether the file lives in Clipdrop's own folder. Those are discarded
        /// to the Recycle Bin; files anywhere else stay where they are.
        public bool IsInInbox(Guid id)
        {
            var item = Find(id);
            if (item == null) return false;
            var inbox = System.IO.Path.GetFullPath(Settings.InboxDir).TrimEnd('\\') + "\\";
            return System.IO.Path.GetFullPath(item.Path).StartsWith(inbox, StringComparison.OrdinalIgnoreCase);
        }

        /// The corner cross and "Take down" both end up here.
        public void Discard(Guid id)
        {
            if (IsInInbox(id)) Trash(id); else Drop(id);
        }

        public void SaveToDesktop(Guid id)
        {
            var item = Find(id);
            if (item == null) return;
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var target = UniquePath(desktop, System.IO.Path.GetFileName(item.Path));
                File.Move(item.Path, target);
                Drop(id, quietly: true);
            }
            catch { SystemSounds.Beep.Play(); }
        }

        // ---- Helpers ----

        public Pegged Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);

        public static bool SamePath(string a, string b) =>
            string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private void Save()
        {
            settings.Pegged = Live.Select(i => i.Path).ToList();
            settings.Save();
        }

        public static string UniquePath(string folder, string name)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(name);
            string ext = System.IO.Path.GetExtension(name);
            string candidate = System.IO.Path.Combine(folder, name);
            int n = 2;
            while (File.Exists(candidate))
                candidate = System.IO.Path.Combine(folder, $"{stem} ({n++}){ext}");
            return candidate;
        }

        private static void Shell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { SystemSounds.Beep.Play(); }
        }

        private void Play(string file)
        {
            if (!settings.SoundOn) return;
            try
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", file);
                if (File.Exists(path)) new SoundPlayer(path).Play();
            }
            catch { }
        }

        public void After(double seconds, Action action)
        {
            var t = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromSeconds(Math.Max(0.001, seconds)) };
            t.Tick += (s, e) => { t.Stop(); action(); };
            t.Start();
        }
    }

    internal static class Thumbnails
    {
        /// Reads the image without locking the file, retrying for a moment
        /// because a screenshot may still be being written.
        public static BitmapSource Make(string path, int maxPixels)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    using var ms = new MemoryStream(bytes);
                    var probe = BitmapFrame.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    int w = probe.PixelWidth, h = probe.PixelHeight;
                    ms.Position = 0;
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    if (w >= h) img.DecodePixelWidth = Math.Min(maxPixels, w);
                    else img.DecodePixelHeight = Math.Min(maxPixels, h);
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    return img;
                }
                catch (IOException) { System.Threading.Thread.Sleep(150); }
                catch { return null; }
            }
            return null;
        }

        public static BitmapSource LoadFull(string path)
        {
            try
            {
                using var ms = new MemoryStream(File.ReadAllBytes(path));
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }
    }
}
