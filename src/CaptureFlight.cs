using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Clipdrop
{
    /// Works out where on screen a screenshot was taken.
    ///
    /// macOS writes the captured area onto the file; Windows does not. So we
    /// guess the likely spots (the whole monitor for Win+PrtScn, the window
    /// under the pointer for a window snip, and a rectangle ending where the
    /// mouse was let go for a rectangle snip) and keep the one whose pixels
    /// on screen right now match the picture. With no good match the
    /// screenshot simply drops onto the line, as before.
    internal static class CaptureLocator
    {
        private const int Grid = 16;
        private const int Tolerance = 30;
        private const double Accept = 0.72;

        public static Drawing.Rectangle? Find(string path, IEnumerable<Native.POINT> anchors)
        {
            try
            {
                using var image = LoadBitmap(path);
                if (image == null) return null;
                int w = image.Width, h = image.Height;
                if (w < 8 || h < 8) return null;

                var candidates = new List<Drawing.Rectangle>();
                foreach (var s in WinForms.Screen.AllScreens)
                    if (s.Bounds.Width == w && s.Bounds.Height == h) candidates.Add(s.Bounds);
                var virt = WinForms.SystemInformation.VirtualScreen;
                if (virt.Width == w && virt.Height == h) candidates.Add(virt);

                foreach (var p in anchors)
                {
                    foreach (var r in WindowRectsAt(p))
                        if (r.Width == w && r.Height == h) candidates.Add(r);
                    // The pointer sits at one corner of a dragged rectangle.
                    foreach (int x in new[] { p.X - w, p.X - w + 1, p.X - 1, p.X })
                        foreach (int y in new[] { p.Y - h, p.Y - h + 1, p.Y - 1, p.Y })
                            candidates.Add(new Drawing.Rectangle(x, y, w, h));
                }

                candidates.RemoveAll(r => !virt.Contains(r));
                if (candidates.Count == 0) return null;

                using var screen = new Drawing.Bitmap(virt.Width, virt.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Drawing.Graphics.FromImage(screen))
                    g.CopyFromScreen(virt.Left, virt.Top, 0, 0, virt.Size);

                var samples = Sample(image);
                Drawing.Rectangle? best = null;
                double bestScore = 0;
                foreach (var r in candidates)
                {
                    double score = Score(samples, screen, r.Left - virt.Left, r.Top - virt.Top);
                    if (score > bestScore) { bestScore = score; best = r; }
                }
                return bestScore >= Accept ? best : null;
            }
            catch { return null; }
        }

        private static Drawing.Bitmap LoadBitmap(string path)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    using var ms = new MemoryStream(bytes);
                    using var img = Drawing.Image.FromStream(ms);
                    return new Drawing.Bitmap(img);
                }
                catch (IOException) { System.Threading.Thread.Sleep(60); }
            }
            return null;
        }

        private readonly struct Px
        {
            public readonly int X, Y; public readonly Drawing.Color C;
            public Px(int x, int y, Drawing.Color c) { X = x; Y = y; C = c; }
        }

        private static List<Px> Sample(Drawing.Bitmap image)
        {
            var list = new List<Px>(Grid * Grid);
            for (int i = 0; i < Grid; i++)
                for (int j = 0; j < Grid; j++)
                {
                    int x = (int)((i + 0.5) * image.Width / Grid);
                    int y = (int)((j + 0.5) * image.Height / Grid);
                    list.Add(new Px(x, y, image.GetPixel(x, y)));
                }
            return list;
        }

        private static double Score(List<Px> samples, Drawing.Bitmap screen, int ox, int oy)
        {
            int hits = 0;
            foreach (var s in samples)
            {
                var c = screen.GetPixel(ox + s.X, oy + s.Y);
                int d = Math.Max(Math.Abs(c.R - s.C.R), Math.Max(Math.Abs(c.G - s.C.G), Math.Abs(c.B - s.C.B)));
                if (d <= Tolerance) hits++;
            }
            return hits / (double)samples.Count;
        }

        private static IEnumerable<Drawing.Rectangle> WindowRectsAt(Native.POINT p)
        {
            var list = new List<Drawing.Rectangle>();
            IntPtr hw = WindowFromPoint(p);
            if (hw == IntPtr.Zero) return list;
            hw = GetAncestor(hw, 2 /* GA_ROOT */);
            if (DwmGetWindowAttribute(hw, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Native.RECT f, Marshal.SizeOf<Native.RECT>()) == 0)
                list.Add(Drawing.Rectangle.FromLTRB(f.Left, f.Top, f.Right, f.Bottom));
            if (Native.GetWindowRect(hw, out var r))
                list.Add(Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return list;
        }

        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Native.RECT value, int size);
    }

    /// The capture lifting off the screen and flying up to the line. It turns
    /// into the hanging card on the way: it shrinks, arcs, tilts into place
    /// and grows its glass frame, so nothing is left to change on landing.
    internal sealed class CaptureFlight : Window
    {
        private const double Duration = 0.65;
        private const double Arc = 30;

        private readonly Drawing.Rectangle from;
        private readonly Drawing.Rectangle to;
        private readonly double tilt;
        private readonly Action done;
        private readonly Canvas canvas = new Canvas();
        private readonly Border frame;
        private readonly Border photoClip;
        private readonly RotateTransform rotate = new RotateTransform();
        private readonly DropShadowEffect shadow;
        private readonly Stopwatch clock = new Stopwatch();
        private Rect fromDip, toDip;
        private bool finished;

        /// from and to are in screen pixels; to is the card's frame on the line.
        public static void Fly(string path, Drawing.Rectangle from, Drawing.Rectangle to, double tilt, Action done)
        {
            BitmapSource image;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = Math.Min(3000, Math.Max(400, from.Width));
                bmp.EndInit();
                bmp.Freeze();
                image = bmp;
            }
            catch { done(); return; }
            new CaptureFlight(image, from, to, tilt, done).Start();
        }

        private CaptureFlight(BitmapSource image, Drawing.Rectangle from, Drawing.Rectangle to, double tilt, Action done)
        {
            this.from = from; this.to = to; this.tilt = tilt; this.done = done;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            IsHitTestVisible = false;

            var photo = new Image { Source = image, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.HighQuality);
            photoClip = new Border { Child = photo, ClipToBounds = true };
            shadow = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Direction = 270, Opacity = 0, Color = Colors.Black };
            frame = new Border
            {
                Child = photoClip,
                BorderThickness = new Thickness(1),
                Effect = shadow,
                RenderTransform = rotate,
                RenderTransformOrigin = new Point(0.5, 0),
            };
            canvas.Children.Add(frame);
            Content = canvas;
        }

        private void Start()
        {
            // Cover both the line's screen and the screen the capture came from.
            var area = Drawing.Rectangle.Union(
                WinForms.Screen.FromRectangle(from).Bounds,
                WinForms.Screen.FromRectangle(to).Bounds);
            Left = 0; Top = 0; Width = 10; Height = 10;
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
            for (int i = 0; i < 2; i++)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, Native.SWP_NOACTIVATE);
            Show();
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, Native.SWP_NOACTIVATE);
            UpdateLayout();
            fromDip = ToDip(from);
            toDip = ToDip(to);
            Update(0);
            clock.Start();
            CompositionTarget.Rendering += OnFrame;
        }

        private Rect ToDip(Drawing.Rectangle r)
        {
            var a = canvas.PointFromScreen(new Point(r.Left, r.Top));
            var b = canvas.PointFromScreen(new Point(r.Right, r.Bottom));
            return new Rect(a, b);
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (finished) return;
            double k = Math.Min(1, clock.Elapsed.TotalSeconds / Duration);
            Update(k);
            if (k < 1) return;
            finished = true;
            CompositionTarget.Rendering -= OnFrame;
            done();
            // The real card fades in underneath; this one fades out over it.
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(0.16));
            fade.Completed += (s, a) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private static double EaseInOutCubic(double x) => x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;

        private static double Smooth(double x, double a, double b)
        {
            double t = Math.Max(0, Math.Min(1, (x - a) / (b - a)));
            return t * t * (3 - 2 * t);
        }

        private void Update(double raw)
        {
            double k = EaseInOutCubic(raw);
            double chrome = Smooth(k, 0.35, 1);
            double Lerp(double a, double b) => a + (b - a) * k;

            double w = Lerp(fromDip.Width, toDip.Width), h = Lerp(fromDip.Height, toDip.Height);
            double topX = Lerp(fromDip.Left + fromDip.Width / 2, toDip.Left + toDip.Width / 2);
            double topY = Lerp(fromDip.Top, toDip.Top) - Math.Sin(Math.PI * k) * Arc;
            double inset = Layout.FrameInset * k;
            double radius = Layout.FrameRadius * k;

            frame.Width = w;
            frame.Height = h;
            Canvas.SetLeft(frame, topX - w / 2);
            Canvas.SetTop(frame, topY);
            frame.CornerRadius = new CornerRadius(radius);
            frame.Padding = new Thickness(inset);
            frame.Background = new SolidColorBrush(Color.FromArgb((byte)(150 * chrome), 245, 245, 247));
            frame.BorderBrush = new LinearGradientBrush(Color.FromArgb((byte)(150 * chrome), 255, 255, 255), Color.FromArgb((byte)(40 * chrome), 0, 0, 0), 90);
            double inner = Math.Max(0, radius - inset);
            double pw = Math.Max(1, w - 2 * inset - 2), ph = Math.Max(1, h - 2 * inset - 2);
            photoClip.Clip = new RectangleGeometry(new Rect(0, 0, pw, ph), inner, inner);
            rotate.Angle = tilt * k;
            shadow.Opacity = 0.24 * Smooth(k, 0, 0.3);
        }
    }
}
