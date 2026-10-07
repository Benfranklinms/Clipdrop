using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Washline
{
    internal static class Layout
    {
        public const double PanelHeight = 210;
        public const double RopeTop = 10;
        public const double Spacing = 174;
        public const double CardWidth = 150;
        public const double PinAbove = 9.5;
        public const double PinHeight = 26;
        public const double PinOverlap = 12;
        public const double FrameRadius = 16;
        public const double FrameInset = 4;

        /// The rope hangs as a parabola from edge to edge of the screen.
        public static double Sag(double width) => Math.Min(30, width * 0.018);

        public static double RopeY(double x, double width)
        {
            if (width <= 0) return RopeTop;
            double f = x / width;
            return RopeTop + 4 * Sag(width) * f * (1 - f);
        }

        public static double X(int index, int count, double width)
        {
            double total = Math.Max(count - 1, 0) * Spacing;
            return width / 2 - total / 2 + index * Spacing;
        }

        /// The photo fits inside the card keeping its proportions.
        public static Size PhotoSize(double w, double h)
        {
            double maxW = CardWidth - 14, maxH = 104;
            if (w <= 0 || h <= 0) return new Size(maxW, maxH);
            double s = Math.Min(maxW / w, maxH / h);
            return new Size(w * s, h * s);
        }
    }

    /// One photo with its clip. It drops onto the line, swings, sways with
    /// the breeze and falls when you take it down.
    ///
    /// Click copies. Press and hold opens Paint. Double click opens it.
    /// Drag into an app sends a copy; drag into a folder moves it there and
    /// it leaves the line. The corner cross lets it go.
    internal sealed class CardView : Grid
    {
        public static bool IsDragging;

        public Pegged Item { get; }
        private readonly Line line;

        private readonly RotateTransform tilt = new RotateTransform();
        private readonly RotateTransform swing = new RotateTransform();
        private readonly TranslateTransform drop = new TranslateTransform();
        private readonly ScaleTransform cardScale = new ScaleTransform(1, 1);
        private readonly Border card;
        private readonly Image photo;
        private readonly Border cross;
        private readonly Border copiedBadge;

        private Point? downPoint;
        private bool startedDrag;
        private bool didLongPress;
        private DispatcherTimer holdTimer;
        private bool hovering;

        public CardView(Pegged item, Line line)
        {
            Item = item;
            this.line = line;
            Width = Layout.CardWidth;
            VerticalAlignment = VerticalAlignment.Top;

            tilt.Angle = item.Tilt;
            var group = new TransformGroup();
            group.Children.Add(tilt);
            group.Children.Add(swing);
            group.Children.Add(drop);
            RenderTransform = group;
            RenderTransformOrigin = new Point(0.5, 0);

            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };

            // The photo in its glass frame.
            photo = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(photo, BitmapScalingMode.HighQuality);
            card = new Border
            {
                CornerRadius = new CornerRadius(Layout.FrameRadius),
                Padding = new Thickness(Layout.FrameInset),
                Background = new SolidColorBrush(Color.FromArgb(150, 245, 245, 247)),
                BorderThickness = new Thickness(1),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(40, 0, 0, 0), 90),
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Direction = 270, Opacity = 0.22, Color = Colors.Black },
                Child = photo,
                Margin = new Thickness(0, -Layout.PinOverlap, 0, 0),
                RenderTransform = cardScale,
                RenderTransformOrigin = new Point(0.5, 0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            SetThumb();

            var cardHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            cardHost.Children.Add(card);

            // The discard cross, shown on hover.
            cross = new Border
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(225, 250, 250, 250)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                BorderThickness = new Thickness(0.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 3 - Layout.PinOverlap, 0, 0),
                Opacity = 0,
                Cursor = Cursors.Arrow,
                Child = new TextBlock
                {
                    Text = "\u2715", FontSize = 9, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            cross.MouseLeftButtonDown += (s, e) => { e.Handled = true; line.Discard(Item.Id); };
            cardHost.Children.Add(cross);

            // "Copied" confirmation under the card.
            copiedBadge = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 4, 10, 4),
                Background = new SolidColorBrush(Color.FromArgb(235, 250, 250, 250)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                BorderThickness = new Thickness(0.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -16),
                Opacity = 0,
                IsHitTestVisible = false,
                Child = new TextBlock { Text = "\u2713  Copied", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)) },
            };
            cardHost.Children.Add(copiedBadge);

            stack.Children.Add(MakeClip());
            stack.Children.Add(cardHost);
            Children.Add(stack);

            cardHost.MouseEnter += (s, e) => SetHover(true);
            cardHost.MouseLeave += (s, e) => SetHover(false);
            card.MouseLeftButtonDown += OnDown;
            card.MouseMove += OnMove;
            card.MouseLeftButtonUp += OnUp;
            card.ContextMenu = new ContextMenu();
            FillMenu(card.ContextMenu);
            card.ContextMenuOpening += (s, e) => FillMenu(card.ContextMenu);

            Opacity = 0;
        }

        public void SetThumb()
        {
            photo.Source = Item.Thumb;
            var size = Layout.PhotoSize(Item.Thumb.PixelWidth, Item.Thumb.PixelHeight);
            photo.Width = size.Width;
            photo.Height = size.Height;
            double r = Layout.FrameRadius - Layout.FrameInset;
            photo.Clip = new RectangleGeometry(new Rect(size), r, r);
        }

        /// A minimal aluminium clip, with a slot where it grips the line.
        private static FrameworkElement MakeClip()
        {
            var metal = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(178, 178, 178), 0));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(237, 237, 237), 0.35));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(209, 209, 209), 0.65));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(158, 158, 158), 1));
            var pin = new Border
            {
                Width = 9, Height = Layout.PinHeight,
                CornerRadius = new CornerRadius(3.5),
                Background = metal,
                BorderThickness = new Thickness(0.6),
                BorderBrush = new LinearGradientBrush(Color.FromArgb(230, 255, 255, 255), Color.FromArgb(46, 0, 0, 0), 90),
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1.5, Direction = 270, Opacity = 0.3 },
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Border
                {
                    Width = 5, Height = 1.4, CornerRadius = new CornerRadius(0.7),
                    Background = new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)),
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8.5, 0, 0),
                },
            };
            Panel.SetZIndex(pin, 1);
            return pin;
        }

        /// The card's frame in the given ancestor's coordinates, for the
        /// window to know where clicks should be caught.
        public Rect? CardRect(Visual ancestor)
        {
            if (Item.Falling || !IsVisible || card.ActualWidth <= 0) return null;
            try { return card.TransformToAncestor(ancestor).TransformBounds(new Rect(card.RenderSize)); }
            catch { return null; }
        }

        // ---- Motion ----

        public void Arrive()
        {
            Animate(this, OpacityProperty, 0, 1, 0.25, null);
            Animate(drop, TranslateTransform.YProperty, -46, 0, 0.45, new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
            Animate(swing, RotateTransform.AngleProperty, 16, 0, 1.8, new ElasticEase { Oscillations = 4, Springiness = 3, EasingMode = EasingMode.EaseOut });
        }

        public void ShowAt() { Opacity = 1; }

        public void Nudge(double degrees)
        {
            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(degrees, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.9)), new ElasticEase { Oscillations = 3, Springiness = 3, EasingMode = EasingMode.EaseOut }));
            swing.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        public void Fall(Action done)
        {
            IsHitTestVisible = false;
            var rnd = new Random();
            double spin = (rnd.NextDouble() < 0.5 ? -1 : 1) * (14 + rnd.NextDouble() * 16);
            Animate(drop, TranslateTransform.YProperty, drop.Y, 260, 0.6, new QuadraticEase { EasingMode = EasingMode.EaseIn });
            Animate(swing, RotateTransform.AngleProperty, swing.Angle, spin, 0.6, new QuadraticEase { EasingMode = EasingMode.EaseIn });
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromSeconds(0.55)) { BeginTime = TimeSpan.FromSeconds(0.1) };
            fade.Completed += (s, e) => done();
            BeginAnimation(OpacityProperty, fade);
        }

        public void ShowCopied()
        {
            Nudge(3);
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.18))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.4))));
            copiedBadge.BeginAnimation(OpacityProperty, a);
        }

        private void SetHover(bool on)
        {
            hovering = on;
            UpdateScale();
            cross.BeginAnimation(OpacityProperty, new DoubleAnimation(on && !IsDragging ? 1 : 0, TimeSpan.FromSeconds(0.18)));
            if (card.Effect is DropShadowEffect fx)
            {
                fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(on ? 26 : 18, TimeSpan.FromSeconds(0.18)));
                fx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(on ? 0.3 : 0.22, TimeSpan.FromSeconds(0.18)));
            }
        }

        private void UpdateScale()
        {
            bool pressed = line.PressedId == Item.Id;
            double target = pressed ? 0.95 : (hovering ? 1.035 : 1);
            var dur = TimeSpan.FromSeconds(pressed ? 0.45 : 0.22);
            IEasingFunction ease = pressed ? new SineEase { EasingMode = EasingMode.EaseInOut } : new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            cardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, dur) { EasingFunction = ease });
            cardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, dur) { EasingFunction = ease });
        }

        private static void Animate(IAnimatable target, DependencyProperty prop, double from, double to, double seconds, IEasingFunction ease)
        {
            target.BeginAnimation(prop, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease });
        }

        // ---- Mouse ----

        /// How long you hold before Paint opens. Long enough not to fire on a
        /// slow click, short enough to feel deliberate.
        private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(0.45);

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                downPoint = null;
                EndPress();
                line.Open(Item.Id);
                e.Handled = true;
                return;
            }
            downPoint = e.GetPosition(card);
            startedDrag = false;
            didLongPress = false;
            card.CaptureMouse();
            SetPressed(true);
            holdTimer?.Stop();
            holdTimer = new DispatcherTimer { Interval = HoldDuration };
            holdTimer.Tick += (s, a) =>
            {
                holdTimer.Stop();
                if (downPoint == null || startedDrag) return;
                didLongPress = true;
                SetPressed(false);
                line.Markup(Item.Id);
            };
            holdTimer.Start();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (downPoint == null || startedDrag || didLongPress || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(card);
            if ((p - downPoint.Value).Length <= 4) return;
            startedDrag = true;
            EndPress();
            card.ReleaseMouseCapture();
            BeginDrag();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            bool click = downPoint != null && !startedDrag && !didLongPress;
            EndPress();
            card.ReleaseMouseCapture();
            downPoint = null;
            didLongPress = false;
            if (click) line.Copy(Item.Id);
            e.Handled = true;
        }

        private void EndPress()
        {
            holdTimer?.Stop();
            holdTimer = null;
            SetPressed(false);
        }

        private void SetPressed(bool on)
        {
            if (on) line.PressedId = Item.Id;
            else if (line.PressedId == Item.Id) line.PressedId = null;
            UpdateScale();
        }

        private void BeginDrag()
        {
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { Item.Path });
            IsDragging = true;
            line.DraggingId = Item.Id;
            card.Opacity = 0.45;
            DragDropEffects result = DragDropEffects.None;
            try
            {
                // Apps take a copy. Explorer takes a move, so a folder or the
                // Recycle Bin keeps the file and the photo leaves the line.
                result = DragDrop.DoDragDrop(card, data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            catch { }
            finally
            {
                IsDragging = false;
                line.DraggingId = null;
                card.Opacity = 1;
                startedDrag = false;
                downPoint = null;
            }
            line.Prune();
            // Explorer may finish a move a moment later.
            if (result.HasFlag(DragDropEffects.Move) || result == DragDropEffects.None)
                line.After(0.8, line.Prune);
        }

        public bool ContextMenuIsOpen() => card.ContextMenu?.IsOpen == true;

        private void FillMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            void Add(string title, Action a)
            {
                var mi = new MenuItem { Header = title };
                mi.Click += (s, e) => a();
                menu.Items.Add(mi);
            }
            var id = Item.Id;
            Add("Copy", () => line.Copy(id));
            Add("Open", () => line.Open(id));
            Add("Mark up in Paint", () => line.Markup(id));
            Add("Show in Explorer", () => line.Reveal(id));
            bool inInbox = line.IsInInbox(id);
            if (inInbox) Add("Save to Desktop", () => line.SaveToDesktop(id));
            menu.Items.Add(new Separator());
            if (inInbox) Add("Discard", () => line.Discard(id));
            else
            {
                Add("Take down", () => line.Discard(id));
                Add("Move to Recycle Bin", () => line.Trash(id));
            }
        }
    }
}
