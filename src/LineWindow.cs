using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Clipdrop
{
    /// The transparent strip along the top of a screen. It hangs above the
    /// top edge, tucked away, and slides down when the pointer rests against
    /// the top of the screen, the way an auto-hiding taskbar does.
    internal sealed class LineWindow : Window
    {
        public Line Line { get; }
        public Settings Settings { get; }
        public ScreenshotWatcher Watcher { get; private set; }

        private IntPtr hwnd;
        private HwndSource source;
        private readonly Canvas stage = new Canvas();
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly System.Windows.Shapes.Path ropeShadow = new System.Windows.Shapes.Path();
        private readonly System.Windows.Shapes.Path rope = new System.Windows.Shapes.Path();
        private readonly System.Windows.Shapes.Path ropeShine = new System.Windows.Shapes.Path();
        private readonly Border hint;
        private readonly Dictionary<Guid, CardView> cards = new Dictionary<Guid, CardView>();
        private readonly DispatcherTimer mouseTimer;
        private WinForms.Screen currentScreen;

        private bool isPresent, isRevealed, pinned, wanted, keepOpen;
        private DateTime peekUntil = DateTime.MinValue;
        private DateTime? hotZoneSince, awaySince;
        /// After a click at the top of the screen the line stays hidden until
        /// the pointer leaves that band, so it never drops over a tab you
        /// were reaching for.
        private bool topClickSuppressed;
        private bool clickWasDown;
        private int lastLiveCount;
        private int tickCount;
        private DateTime lastClipboardCapture = DateTime.MinValue;
        private Pegged lastClipboardItem;
        private DateTime lastFolderCapture = DateTime.MinValue;

        private const int HotKeyId = 0x5754;
        private static readonly TimeSpan RevealDelay = TimeSpan.FromSeconds(0.3);
        private static readonly TimeSpan RetractDelay = TimeSpan.FromSeconds(0.5);
        /// The band at the top of the screen that counts as "up there".
        private const int TopBandPx = 2;
        private const double ClickGuardDip = 44;

        public event Action ToggledVisibility;

        public LineWindow(Settings settings)
        {
            Settings = settings;
            Title = "Clipdrop";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Left = 0; Top = 0; Width = 800; Height = Layout.PanelHeight;

            stage.RenderTransform = slide;
            slide.Y = -(Layout.PanelHeight + 12);
            ropeShadow.Stroke = new SolidColorBrush(Color.FromArgb(56, 0, 0, 0));
            ropeShadow.StrokeThickness = 1.4;
            ropeShadow.Effect = new BlurEffect { Radius = 2.4 };
            ropeShadow.RenderTransform = new TranslateTransform(0, 1.2);
            rope.Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140));
            rope.StrokeThickness = 1.2;
            ropeShine.Stroke = new SolidColorBrush(Color.FromArgb(115, 255, 255, 255));
            ropeShine.StrokeThickness = 0.4;
            ropeShine.RenderTransform = new TranslateTransform(0, -0.35);
            foreach (var p in new[] { ropeShadow, rope, ropeShine })
            {
                p.IsHitTestVisible = false;
                p.OpacityMask = RopeFade();
                stage.Children.Add(p);
            }

            hint = new Border
            {
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromArgb(225, 248, 248, 248)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(0.5),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Direction = 270, Opacity = 0.15 },
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = "Take a screenshot and it will hang here",
                    FontSize = 12, FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                },
            };
            stage.Children.Add(hint);
            Content = stage;
            stage.SizeChanged += (s, e) => { DrawRope(); LayoutCards(animated: false); };

            Line = new Line(settings, Dispatcher);
            Line.Hung += OnHung;
            Line.Fell += OnFell;
            Line.Copied += item => { if (cards.TryGetValue(item.Id, out var c)) c.ShowCopied(); };
            Line.ThumbChanged += item => { if (cards.TryGetValue(item.Id, out var c)) { c.SetThumb(); c.Nudge(2); } };
            Line.Gust += () =>
            {
                var rnd = new Random();
                foreach (var c in cards.Values.Where(c => !c.Item.Falling))
                {
                    var card = c;
                    Line.After(rnd.NextDouble() * 0.35, () => card.Nudge(1.6 + rnd.NextDouble() * 1.8));
                }
            };
            Line.Changed += ItemsChanged;
            foreach (var item in Line.Live) AddCard(item, arrive: false);
            lastLiveCount = Line.LiveCount;

            mouseTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) };
            mouseTimer.Tick += (s, e) => Tick();

            hwnd = new WindowInteropHelper(this).EnsureHandle();
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT);
            source = HwndSource.FromHwnd(hwnd);
            source.AddHook(WndProc);

            if (!Native.RegisterHotKey(hwnd, HotKeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x54 /* T */))
                System.Diagnostics.Debug.WriteLine("Clipdrop: Ctrl+Alt+T is taken by another app.");
            Native.AddClipboardFormatListener(hwnd);

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s, e) =>
                Dispatcher.BeginInvoke(new Action(() => PlaceOnScreen(currentScreen)));

            PlaceOnScreen(ScreenUnderPointer());
            StartWatcher();
            Directory.CreateDirectory(Settings.InboxDir);
        }

        // ---- Start up ----

        public void Welcome()
        {
            if (Settings.Welcomed) return;
            Settings.Welcomed = true;
            Settings.Save();
            keepOpen = true;
            wanted = true;
            Refresh();
            Reveal(pin: true);
            Line.After(5, () =>
            {
                if (Line.LiveCount > 0) return;
                keepOpen = false;
                wanted = false;
                Refresh();
            });
        }

        private void StartWatcher()
        {
            Watcher?.Dispose();
            Watcher = new ScreenshotWatcher(ScreenshotWatcher.DefaultFolder(), Dispatcher, OnFolderCapture, () => Line.Prune());
        }

        public void Shutdown()
        {
            Native.UnregisterHotKey(hwnd, HotKeyId);
            Native.RemoveClipboardFormatListener(hwnd);
            Watcher?.Dispose();
        }

        // ---- New captures ----

        private void OnFolderCapture(string path)
        {
            // The Snipping Tool both copies and saves. If the clipboard copy
            // was just hung, the saved file replaces it.
            if (lastClipboardItem != null && DateTime.Now - lastClipboardCapture < TimeSpan.FromSeconds(4))
            {
                var stale = lastClipboardItem;
                lastClipboardItem = null;
                if (!stale.Falling)
                {
                    Line.Drop(stale.Id, quietly: true);
                    try { File.Delete(stale.Path); } catch { }
                }
                Hang(path, quietly: true);
            }
            else Hang(path);
            lastFolderCapture = DateTime.Now;
        }

        private void OnClipboardChanged()
        {
            if (!Settings.CatchClipboard || Line.SuppressClipboard) return;
            if (DateTime.Now - lastFolderCapture < TimeSpan.FromSeconds(4)) return;
            try
            {
                if (!Clipboard.ContainsImage() || Clipboard.ContainsFileDropList()) return;
                var image = Clipboard.GetImage();
                if (image == null) return;
                Directory.CreateDirectory(Settings.InboxDir);
                var path = Line.UniquePath(Settings.InboxDir, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
                using (var fs = File.Create(path))
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(image));
                    enc.Save(fs);
                }
                lastClipboardCapture = DateTime.Now;
                lastClipboardItem = Hang(path);
            }
            catch { }
        }

        private Pegged Hang(string path, bool quietly = false)
        {
            // The line goes to the screen you were working on.
            var screen = ScreenUnderPointer();
            if (!isRevealed && screen.DeviceName != currentScreen?.DeviceName) PlaceOnScreen(screen);
            return Line.Hang(path, quietly);
        }

        // ---- Cards ----

        private void AddCard(Pegged item, bool arrive)
        {
            var card = new CardView(item, Line);
            cards[item.Id] = card;
            stage.Children.Add(card);
            LayoutCards(animated: false, only: card);
            if (arrive) card.Arrive(); else card.ShowAt();
        }

        private void OnHung(Pegged item)
        {
            AddCard(item, arrive: true);
            LayoutCards(animated: true);
        }

        private void OnFell(Pegged item)
        {
            if (!cards.TryGetValue(item.Id, out var card)) return;
            card.Fall(() =>
            {
                stage.Children.Remove(card);
                cards.Remove(item.Id);
                LayoutCards(animated: true);
            });
        }

        private void LayoutCards(bool animated, CardView only = null)
        {
            double width = stage.ActualWidth > 0 ? stage.ActualWidth : ActualWidth;
            var list = Line.Items.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!cards.TryGetValue(list[i].Id, out var card)) continue;
                if (only != null && card != only) continue;
                double x = Layout.X(i, list.Count, width);
                double left = x - Layout.CardWidth / 2;
                double top = Layout.RopeY(x, width) - Layout.PinAbove;
                if (animated && !double.IsNaN(Canvas.GetLeft(card)))
                {
                    var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
                    card.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, TimeSpan.FromSeconds(0.55)) { EasingFunction = ease });
                    card.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, TimeSpan.FromSeconds(0.55)) { EasingFunction = ease });
                }
                else
                {
                    card.BeginAnimation(Canvas.LeftProperty, null);
                    card.BeginAnimation(Canvas.TopProperty, null);
                    Canvas.SetLeft(card, left);
                    Canvas.SetTop(card, top);
                }
            }
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hint, width / 2 - hint.DesiredSize.Width / 2);
            Canvas.SetTop(hint, Layout.RopeY(width / 2, width) + 20);
            hint.BeginAnimation(OpacityProperty, new DoubleAnimation(Line.Items.Count == 0 ? 1 : 0, TimeSpan.FromSeconds(0.3)));
        }

        // ---- The rope ----

        private void DrawRope()
        {
            double w = stage.ActualWidth;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(-20, Layout.RopeTop) };
            fig.Segments.Add(new QuadraticBezierSegment(new Point(w / 2, Layout.RopeTop + 2 * Layout.Sag(w)), new Point(w + 20, Layout.RopeTop), true));
            geo.Figures.Add(fig);
            geo.Freeze();
            ropeShadow.Data = rope.Data = ropeShine.Data = geo;
        }

        /// The line fades out at both ends so it seems to come from beyond
        /// the screen.
        private static Brush RopeFade()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            b.GradientStops.Add(new GradientStop(Colors.Black, 0.08));
            b.GradientStops.Add(new GradientStop(Colors.Black, 0.92));
            b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            return b;
        }

        // ---- Placement ----

        private static WinForms.Screen ScreenUnderPointer()
        {
            Native.GetCursorPos(out var p);
            return WinForms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
        }

        /// Stretches the strip across the top of the given screen, sized in
        /// that screen's own pixels.
        private void PlaceOnScreen(WinForms.Screen screen)
        {
            screen ??= WinForms.Screen.PrimaryScreen;
            currentScreen = screen;
            var b = screen.Bounds;
            double scale = Native.DpiScaleAt(b.Left + 1, b.Top + 1);
            int h = (int)Math.Ceiling(Layout.PanelHeight * scale);
            // Twice: the first move may change the DPI and with it the size.
            for (int i = 0; i < 2; i++)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, h, Native.SWP_NOACTIVATE);
            double widthDip = b.Width / scale;
            Line.MaxItems = Math.Max(3, Math.Min(12, (int)((widthDip - 200) / Layout.Spacing)));
            LayoutCards(animated: false);
        }

        // ---- Showing and hiding ----

        private void ItemsChanged()
        {
            int live = Line.LiveCount;
            if (live > lastLiveCount)
            {
                wanted = true;
                Refresh();
                Reveal(peekSeconds: 2.5);
            }
            else if (live == 0 && !keepOpen)
            {
                Line.After(0.7, () =>
                {
                    if (Line.LiveCount != 0 || keepOpen) return;
                    wanted = false;
                    Refresh();
                });
            }
            lastLiveCount = live;
        }

        /// Whether the strip is shown at all: something to show, and no full
        /// screen app on that screen.
        private void Refresh()
        {
            bool blocked = currentScreen != null && Native.IsFullScreenOn(currentScreen.Bounds);
            if (wanted && !blocked) Present(); else Dismiss();
            // The pointer is watched while there is a line, even tucked away,
            // to notice it resting against the top edge.
            if (wanted) mouseTimer.Start(); else { mouseTimer.Stop(); Native.SetClickThrough(hwnd, true); }
        }

        private void Present()
        {
            if (isPresent) return;
            isPresent = true;
            Show();
            PlaceOnScreen(currentScreen);
        }

        private void Dismiss()
        {
            if (!isPresent) return;
            isPresent = false;
            SetRevealed(false);
            Line.After(0.4, () => { if (!isPresent) Hide(); });
        }

        private void Reveal(bool pin = false, double peekSeconds = 0)
        {
            if (!isPresent) return;
            if (pin) pinned = true;
            if (peekSeconds > 0) peekUntil = DateTime.Now.AddSeconds(peekSeconds);
            awaySince = null;
            SetRevealed(true);
        }

        private void SetRevealed(bool on)
        {
            if (on == isRevealed) return;
            isRevealed = on;
            if (on)
            {
                Line.RefreshEdited();
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | Native.SWP_NOACTIVATE);
                slide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromSeconds(0.45)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                pinned = false;
                peekUntil = DateTime.MinValue;
                Native.SetClickThrough(hwnd, true);
                slide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-(Layout.PanelHeight + 12), TimeSpan.FromSeconds(0.22)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            }
            ToggledVisibility?.Invoke();
        }

        public bool IsRevealed => isRevealed;

        public void Toggle()
        {
            if (isRevealed)
            {
                SetRevealed(false);
                if (Line.LiveCount == 0)
                {
                    keepOpen = false;
                    wanted = false;
                    Refresh();
                }
            }
            else
            {
                keepOpen = true;
                wanted = true;
                PlaceOnScreen(ScreenUnderPointer());
                Refresh();
                Reveal(pin: true);
            }
        }

        private void Tick()
        {
            Native.GetCursorPos(out var p);
            var now = DateTime.Now;
            var screen = WinForms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
            double scale = Native.DpiScaleAt(p.X, p.Y);
            bool inTopBand = p.Y <= screen.Bounds.Top + TopBandPx - 1;
            bool inClickGuard = p.Y <= screen.Bounds.Top + ClickGuardDip * scale;

            // A click near the top of the screen (a tab, a title bar) puts the
            // line away and keeps it away until the pointer leaves.
            bool down = (Native.GetAsyncKeyState(Native.VK_LBUTTON) & 0x8000) != 0 || (Native.GetAsyncKeyState(Native.VK_RBUTTON) & 0x8000) != 0;
            bool overCard = isRevealed && OverCard(p);
            if (down && !clickWasDown && inClickGuard && !overCard && !CardView.IsDragging && Line.PressedId == null)
            {
                topClickSuppressed = true;
                hotZoneSince = null;
                if (isRevealed) { pinned = false; SetRevealed(false); }
            }
            clickWasDown = down;
            if (!inClickGuard) topClickSuppressed = false;

            // Full screen apps come and go; check now and then.
            if (++tickCount % 15 == 0)
            {
                bool blocked = currentScreen != null && Native.IsFullScreenOn(currentScreen.Bounds);
                if (blocked && isPresent) Dismiss();
                else if (!blocked && !isPresent && wanted) Present();
            }

            if (!isRevealed)
            {
                if (inTopBand && !topClickSuppressed && !down && !Native.IsFullScreenOn(screen.Bounds))
                {
                    hotZoneSince ??= now;
                    if (now - hotZoneSince.Value >= RevealDelay)
                    {
                        hotZoneSince = null;
                        if (screen.DeviceName != currentScreen?.DeviceName) PlaceOnScreen(screen);
                        if (!isPresent) Refresh();
                        Reveal();
                    }
                }
                else hotZoneSince = null;
                return;
            }

            // Catch clicks only over a photo; everywhere else they go through.
            if (!CardView.IsDragging) Native.SetClickThrough(hwnd, !overCard);

            Native.GetWindowRect(hwnd, out var r);
            int lowest = r.Top + (int)(LowestCardBottom() * scale) + 8;
            bool inside = p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y <= Math.Min(r.Bottom, lowest);
            if (inside && pinned) pinned = false;

            bool busy = pinned || CardView.IsDragging || Line.PressedId != null || now < peekUntil || ContextMenuOpen();
            if (inside || busy) awaySince = null;
            else
            {
                awaySince ??= now;
                if (now - awaySince.Value >= RetractDelay)
                {
                    awaySince = null;
                    SetRevealed(false);
                }
            }
        }

        private bool OverCard(Native.POINT p)
        {
            Point local;
            try { local = stage.PointFromScreen(new Point(p.X, p.Y)); } catch { return false; }
            foreach (var c in cards.Values)
            {
                var rect = c.CardRect(stage);
                if (rect == null) continue;
                var r = rect.Value;
                r.Inflate(4, 4);
                if (r.Contains(local)) return true;
            }
            return false;
        }

        private double LowestCardBottom()
        {
            double lowest = 60;
            foreach (var c in cards.Values)
            {
                var r = c.CardRect(stage);
                if (r != null) lowest = Math.Max(lowest, r.Value.Bottom + 24);
            }
            return Math.Min(lowest, Layout.PanelHeight);
        }

        private bool ContextMenuOpen() => cards.Values.Any(c => c.ContextMenuIsOpen());

        // ---- Messages ----

        private IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case Native.WM_HOTKEY when wParam.ToInt32() == HotKeyId:
                    Toggle();
                    handled = true;
                    break;
                case Native.WM_CLIPBOARDUPDATE:
                    Dispatcher.BeginInvoke(new Action(OnClipboardChanged), DispatcherPriority.Background);
                    break;
            }
            return IntPtr.Zero;
        }
    }
}
