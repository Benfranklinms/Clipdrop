using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace Clipdrop
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            using var single = new Mutex(true, "Clipdrop.SingleInstance", out bool first);
            if (!first) return;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var settings = Settings.Load();
            var window = new LineWindow(settings);
            using var tray = new Tray(window, app);
            app.Startup += (s, e) => window.Welcome();
            app.Exit += (s, e) => window.Shutdown();
            app.Run();
        }
    }

    /// The notification area icon and its menu, standing in for the macOS
    /// menu bar item.
    internal sealed class Tray : IDisposable
    {
        private readonly WinForms.NotifyIcon icon;
        private readonly LineWindow window;
        private readonly Application app;

        public Tray(LineWindow window, Application app)
        {
            this.window = window;
            this.app = app;
            icon = new WinForms.NotifyIcon
            {
                Icon = DrawIcon(),
                Text = "Clipdrop: rest the pointer at the top edge (Ctrl+Alt+T)",
                Visible = true,
                ContextMenuStrip = new WinForms.ContextMenuStrip(),
            };
            icon.ContextMenuStrip.Opening += (s, e) => { e.Cancel = false; Fill(); };
            icon.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) window.Toggle(); };
            Fill();

            if (!window.Settings.Welcomed)
                icon.ShowBalloonTip(6000, "Clipdrop is running",
                    "Every screenshot now hangs on a line at the top of your screen. Rest the pointer against the top edge to bring it down, or press Ctrl+Alt+T.",
                    WinForms.ToolTipIcon.None);
        }

        private void Fill()
        {
            var m = icon.ContextMenuStrip;
            m.Items.Clear();
            var line = window.Line;
            var settings = window.Settings;

            m.Items.Add(new WinForms.ToolStripMenuItem(window.IsRevealed ? "Hide line" : "Show line", null, (s, e) => window.Toggle())
            { ShortcutKeyDisplayString = "Ctrl+Alt+T" });
            var clear = new WinForms.ToolStripMenuItem("Take everything down", null, (s, e) => line.Clear()) { Enabled = line.LiveCount > 0 };
            m.Items.Add(clear);
            m.Items.Add(new WinForms.ToolStripMenuItem("Also catch screenshots copied to the clipboard", null, (s, e) =>
            {
                settings.CatchClipboard = !settings.CatchClipboard;
                settings.Save();
            })
            { Checked = settings.CatchClipboard, ToolTipText = "Plain PrtScn and Alt+PrtScn copy without saving. With this on, they hang too." });
            m.Items.Add(new WinForms.ToolStripMenuItem("Open screenshots folder", null, (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + window.Watcher.Folder + "\""); } catch { }
            }));
            m.Items.Add(new WinForms.ToolStripSeparator());
            m.Items.Add(new WinForms.ToolStripMenuItem("Sounds", null, (s, e) =>
            {
                settings.SoundOn = !settings.SoundOn;
                settings.Save();
            })
            { Checked = settings.SoundOn });
            m.Items.Add(new WinForms.ToolStripMenuItem("Open at sign-in", null, (s, e) =>
            {
                try { Settings.LaunchAtLogin = !Settings.LaunchAtLogin; } catch { }
            })
            { Checked = SafeLaunchAtLogin() });
            m.Items.Add(new WinForms.ToolStripSeparator());
            m.Items.Add(new WinForms.ToolStripMenuItem("Quit Clipdrop", null, (s, e) =>
            {
                icon.Visible = false;
                app.Shutdown();
            }));
        }

        private static bool SafeLaunchAtLogin()
        {
            try { return Settings.LaunchAtLogin; } catch { return false; }
        }

        /// A little line with two photos pegged to it, drawn in code.
        private static Icon DrawIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var rope = new Pen(Color.FromArgb(235, 235, 235), 2f);
                g.DrawBezier(rope, 1, 6, 10, 12, 22, 12, 31, 6);
                using var frame = new SolidBrush(Color.White);
                using var edge = new Pen(Color.FromArgb(120, 120, 120), 1f);
                g.TranslateTransform(9, 9);
                g.RotateTransform(-6);
                g.FillRectangle(frame, -5, 0, 10, 13);
                g.DrawRectangle(edge, -5, 0, 10, 13);
                g.ResetTransform();
                g.TranslateTransform(22, 9);
                g.RotateTransform(5);
                g.FillRectangle(frame, -5, 0, 10, 9);
                g.DrawRectangle(edge, -5, 0, 10, 9);
            }
            IntPtr h = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return icon;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
