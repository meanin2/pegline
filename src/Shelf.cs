using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Pegline
{
    internal sealed class ShelfWindow : Window
    {
        public readonly ShelfView View;
        public Forms.Screen Screen { get; private set; }
        public IntPtr Handle { get { return new WindowInteropHelper(this).EnsureHandle(); } }
        public double Scale
        {
            get
            {
                var source = PresentationSource.FromVisual(View);
                if (source != null && source.CompositionTarget != null) return source.CompositionTarget.TransformToDevice.M11;
                try { uint dpi = Native.GetDpiForWindow(Handle); return dpi > 0 ? dpi / 96.0 : 1; }
                catch { return 1; }
            }
        }
        bool clickThrough = true;
        readonly ShelfDecorationWindow back, front;
        public ShelfWindow(Controller host)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; Height = ShelfLayout.Height; Focusable = false;
            View = new ShelfView(host); Content = View;
            back = new ShelfDecorationWindow(this, false); front = new ShelfDecorationWindow(this, true);
            View.DecorationsChanged += delegate { back.Refresh(); front.Refresh(); };
            View.Retracted += delegate { if (!View.TargetVisible) { back.Hide(); front.Hide(); Hide(); } };
            SourceInitialized += delegate
            {
                Native.SetOverlay(Handle, true); Native.ExcludeFromCapture(Handle);
                HwndSource.FromHwnd(Handle).AddHook(Hook);
            };
        }
        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            if (msg == Native.WM_DPICHANGED)
                Dispatcher.BeginInvoke(new Action(delegate { if (IsVisible) Place(Screen); }));
            return IntPtr.Zero;
        }
        public void Place(Forms.Screen screen)
        {
            Screen = screen ?? Native.PointerScreen;
            var b = Screen.WorkingArea;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, (int)Math.Round(ShelfLayout.Height * Scale), Native.SWP_NOACTIVATE);
            double actualScale = Scale;
            back.Place(b, actualScale);
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.Left, b.Top, b.Width, (int)Math.Round(ShelfLayout.Height * actualScale), Native.SWP_NOACTIVATE);
            front.Place(b, actualScale);
            View.Kick();
        }
        public void SetRevealed(bool visible)
        {
            if (visible && !IsVisible) { back.Show(); Show(); front.Show(); Place(Screen); FollowDesktop(); }
            View.Reveal(visible, false); SetClickThrough(!visible);
        }
        public void ForceHide() { View.Reveal(false, true); SetClickThrough(true); back.Hide(); front.Hide(); Hide(); }
        public void SetClickThrough(bool value)
        {
            // The interactive HWND paints ONLY cards. Its alpha-zero pixels are
            // mouse-through at the compositor; do not toggle its input based on a
            // 30 Hz pointer sample (which could swallow fast first clicks).
            if (View.TargetVisible) value = false;
            if (value != clickThrough) { clickThrough = value; Native.SetOverlay(Handle, value); }
        }
        public void FollowDesktop() { Native.FollowDesktop(back.Handle); Native.FollowDesktop(Handle); Native.FollowDesktop(front.Handle); }
        public Point ToLocal(Native.POINT p)
        {
            // A restored shelf may not have been shown/layout-attached yet. Physical
            // HWND coordinates work even then; Visual.PointFromScreen would throw.
            Native.RECT r; if (!Native.GetWindowRect(Handle, out r)) return new Point(-10000, -10000);
            double scale = Scale; return new Point((p.X - r.Left) / scale, (p.Y - r.Top) / scale);
        }
        public System.Drawing.RectangleF ToScreen(Box r)
        {
            Native.RECT frame; if (!Native.GetWindowRect(Handle, out frame)) throw new InvalidOperationException("Shelf window coordinates are unavailable.");
            double scale = Scale;
            return new System.Drawing.RectangleF((float)(frame.Left + r.X * scale), (float)(frame.Top + r.Y * scale), (float)(r.Width * scale), (float)(r.Height * scale));
        }
        protected override void OnClosed(EventArgs e) { View.Dispose(); back.Close(); front.Close(); base.OnClosed(e); }
    }

    // Rope and shadows below the cards; clips and status pills above them.
    // Both decoration HWNDs always ignore mouse input, including their painted pixels.
    internal sealed class ShelfDecorationWindow : Window
    {
        readonly DecorationView view;
        public IntPtr Handle { get { return new WindowInteropHelper(this).EnsureHandle(); } }
        public ShelfDecorationWindow(ShelfWindow owner, bool front)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
            Content = view = new DecorationView(owner.View, front);
            SourceInitialized += delegate
            {
                Native.SetOverlay(Handle, true); Native.ExcludeFromCapture(Handle);
                HwndSource.FromHwnd(Handle).AddHook(delegate(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
                { if (message == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
            };
        }
        public void Place(System.Drawing.Rectangle area, double scale)
        { Native.SetWindowPos(Handle, Native.HWND_TOPMOST, area.X, area.Y, area.Width, (int)Math.Round(ShelfLayout.Height * scale), Native.SWP_NOACTIVATE); }
        public void Refresh() { view.InvalidateVisual(); }
        sealed class DecorationView : FrameworkElement
        {
            readonly ShelfView shelf; readonly bool front;
            public DecorationView(ShelfView shelf, bool front) { this.shelf = shelf; this.front = front; IsHitTestVisible = false; }
            protected override void OnRender(DrawingContext dc) { shelf.DrawDecorations(dc, front); }
        }
    }

    internal sealed class ShelfView : FrameworkElement, IDisposable
    {
        sealed class Motion { public Box From, Target, Current; public double Since, Hover, Press; public Matrix Matrix; }
        internal Controller Host { get { return host; } }
        public event Action DecorationsChanged;
        double lastFrameAt;
        ShelfAutomationPeer automation;
        bool disposed;
        readonly Controller host;
        readonly Dictionary<Guid, Motion> motion = new Dictionary<Guid, Motion>();
        readonly DispatcherTimer hold;
        Card hovered, pressed;
        Point pressedPoint;
        bool longPress, rendering;
        double slide = 0, slideFrom = 0, slideAt = -100;
        public bool TargetVisible { get; private set; }
        public bool MenuOpen { get; private set; }
        public bool IsPressed { get { return pressed != null; } }
        public event Action Retracted;
        public ShelfView(Controller host)
        {
            this.host = host; Focusable = false; ClipToBounds = false;
            System.Windows.Automation.AutomationProperties.SetName(this, "Pegline screenshot shelf");
            hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ShelfLayout.HoldDuration) };
            hold.Tick += delegate
            {
                hold.Stop(); if (pressed == null) return; var item = pressed; longPress = true; pressed = null; ReleaseMouseCapture(); Kick(); host.Edit(item);
            };
        }
        public void Reveal(bool value, bool instant)
        {
            if (TargetVisible == value && !instant) return;
            TargetVisible = value; slideFrom = slide; slideAt = host.Now;
            if (instant || host.ReducedMotion) slide = value ? 1 : 0;
            Kick();
        }
        public void Kick()
        {
            if (disposed) return;
            InvalidateVisual();
            if (automation != null) automation.InvalidatePeer();
            if (!rendering) { rendering = true; CompositionTarget.Rendering += RenderFrame; }
        }
        void RenderFrame(object sender, EventArgs args)
        {
            double t = host.Now - slideAt;
            double elapsed = ShelfLayout.Clamp(host.Now - lastFrameAt, .001, .05); lastFrameAt = host.Now;
            foreach (var pair in motion)
            {
                Motion m = pair.Value; double targetHover = hovered != null && hovered.Id == pair.Key ? 1 : 0;
                double targetPress = pressed != null && pressed.Id == pair.Key ? 1 : 0;
                m.Hover += (targetHover - m.Hover) * (1 - Math.Exp(-elapsed * 20));
                m.Press += (targetPress - m.Press) * (1 - Math.Exp(-elapsed * (targetPress > 0 ? 9 : 24)));
                if (Math.Abs(targetHover - m.Hover) < .001) m.Hover = targetHover;
                if (Math.Abs(targetPress - m.Press) < .001) m.Press = targetPress;
            }
            if (host.ReducedMotion) slide = TargetVisible ? 1 : 0;
            else if (TargetVisible) slide = 1 - (1 - slideFrom) * ShelfLayout.Spring(t, 220, 24);
            else slide = slideFrom * (1 - Math.Pow(ShelfLayout.Clamp(t / .22, 0, 1), 3));
            if (t > 1) slide = TargetVisible ? 1 : 0;
            if (!TargetVisible && (t >= .22 || host.ReducedMotion))
            {
                slide = 0; CompositionTarget.Rendering -= RenderFrame; rendering = false;
                if (Retracted != null) Retracted();
            }
            InvalidateVisual();
            if (TargetVisible && t > 1 && !host.Cards.Any(c => host.Now - c.AddedAt < 6 || host.Now - c.NudgeAt < 6 || c.CopiedUntil > host.Now) &&
                !motion.Any(p => host.Now - p.Value.Since < 1 || Math.Abs(p.Value.Hover - (hovered != null && hovered.Id == p.Key ? 1 : 0)) > .001 ||
                    Math.Abs(p.Value.Press - (pressed != null && pressed.Id == p.Key ? 1 : 0)) > .001))
            { CompositionTarget.Rendering -= RenderFrame; rendering = false; }
        }
        public Box RestingBox(Card item)
        {
            var cards = host.Cards.Where(c => !c.Falling).ToList(); int index = cards.IndexOf(item);
            return ShelfLayout.Card(Math.Max(0, index), cards.Count, ActualWidth, item.Thumbnail.PixelWidth, item.Thumbnail.PixelHeight);
        }
        public Card Hit(Point point)
        {
            foreach (Card card in host.Cards.AsEnumerable().Reverse())
            {
                Motion m;
                if (card.Falling || card.Flying || !motion.TryGetValue(card.Id, out m) || !m.Matrix.HasInverse) continue;
                Matrix inverse = m.Matrix; inverse.Invert(); Point p = inverse.Transform(point);
                var r = new Rect(m.Current.X - 4, m.Current.Y - 4, m.Current.Width + 8, m.Current.Height + 8);
                if (r.Contains(p)) return card;
            }
            return null;
        }
        bool Cross(Card card, Point point)
        {
            Motion m; if (!motion.TryGetValue(card.Id, out m)) return false;
            Matrix inverse = m.Matrix; if (!inverse.HasInverse) return false; inverse.Invert(); Point p = inverse.Transform(point);
            return new Rect(m.Current.X, m.Current.Y, 26, 26).Contains(p);
        }
        public void SetPointer(Point p)
        { Card next = Hit(p); if (next != hovered) { hovered = next; Cursor = next == null ? Cursors.Arrow : Cursors.Hand; Kick(); } }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(this); Card card = Hit(p); if (card == null) return;
            if (e.ChangedButton == MouseButton.Right)
            {
                var menu = host.MenuFor(card); MenuOpen = true; menu.PlacementTarget = this; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.Closed += delegate { MenuOpen = false; Kick(); }; menu.IsOpen = true; e.Handled = true; return;
            }
            if (e.ChangedButton != MouseButton.Left) return;
            if (Cross(card, p)) { host.Discard(card); e.Handled = true; return; }
            if (e.ClickCount == 2) { CancelPress(); host.Open(card); e.Handled = true; return; }
            pressed = card; pressedPoint = p; longPress = false; CaptureMouse(); hold.Start(); Kick(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = e.GetPosition(this); SetPointer(p);
            if (pressed != null && !longPress && (p - pressedPoint).Length > 4 && e.LeftButton == MouseButtonState.Pressed)
            {
                Card item = pressed; CancelPress(); host.Drag(item); e.Handled = true;
            }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            Card item = pressed; bool copy = item != null && !longPress && e.ChangedButton == MouseButton.Left;
            CancelPress(); if (copy) host.Copy(item); e.Handled = copy;
        }
        protected override void OnLostMouseCapture(MouseEventArgs e) { if (pressed != null) CancelPress(); base.OnLostMouseCapture(e); }
        void CancelPress() { hold.Stop(); pressed = null; if (IsMouseCaptured) ReleaseMouseCapture(); Kick(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); double width = ActualWidth, now = host.Now;
            double offset = -(ShelfLayout.Height + 12) * (1 - slide);
            dc.PushTransform(new TranslateTransform(0, offset));
            var cards = host.Cards.Where(c => !c.Falling).ToList();
            foreach (Guid dead in motion.Keys.Where(k => !cards.Any(c => c.Id == k)).ToArray()) motion.Remove(dead);
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i]; Box target = ShelfLayout.Card(i, cards.Count, width, card.Thumbnail.PixelWidth, card.Thumbnail.PixelHeight); Motion m;
                if (!motion.TryGetValue(card.Id, out m)) { m = new Motion { From = target, Target = target, Current = target, Since = now }; motion[card.Id] = m; }
                if (Math.Abs(m.Target.X - target.X) > .1 || Math.Abs(m.Target.Y - target.Y) > .1 || Math.Abs(m.Target.Width - target.Width) > .1)
                { m.From = m.Current; m.Target = target; m.Since = now; }
                double travel = host.ReducedMotion ? 1 : 1 - ShelfLayout.Spring(now - m.Since, 150, 20);
                var r = new Box(m.From.X + (target.X - m.From.X) * travel, m.From.Y + (target.Y - m.From.Y) * travel, target.Width, target.Height);
                double age = now - card.AddedAt;
                if (!host.ReducedMotion && !card.HasFlown) r.Y -= 46 * ShelfLayout.Spring(age, 180, 20);
                m.Current = r;
                double swing = card.Tilt;
                if (!host.ReducedMotion)
                {
                    if (!card.HasFlown) swing += 16 * ShelfLayout.Spring(age, 46, 2.6);
                    if (now >= card.NudgeAt) swing += card.NudgeDegrees * ShelfLayout.Spring(now - card.NudgeAt, 38, 2.4);
                }
                double scale = host.ReducedMotion ? (pressed == card ? .95 : hovered == card ? 1.035 : 1) : 1 + .035 * m.Hover - .05 * m.Press;
                var anchor = new Point(r.CenterX, r.Y - 14);
                Matrix matrix = new ScaleTransform(scale, scale, anchor.X, anchor.Y).Value;
                matrix.Append(new RotateTransform(swing, anchor.X, anchor.Y).Value); matrix.Translate(0, offset); m.Matrix = matrix;
                card.LastBox = new Box(r.X, r.Y + offset, r.Width, r.Height);
                if (card.Flying) continue;
                dc.PushTransform(new RotateTransform(swing, anchor.X, anchor.Y)); dc.PushTransform(new ScaleTransform(scale, scale, anchor.X, anchor.Y));
                dc.PushOpacity(host.Dragging == card ? .45 : 1);
                Rect frame = new Rect(r.X, r.Y, r.Width, r.Height);
                Glass(dc, frame, 16, Ui.Dark);
                var imageRect = new Rect(frame.X + 4, frame.Y + 4, frame.Width - 8, frame.Height - 8);
                dc.PushClip(new RectangleGeometry(imageRect, 12, 12)); dc.DrawImage(card.Thumbnail, imageRect); dc.Pop();
                if (hovered == card && host.Dragging != card)
                {
                    Rect cross = new Rect(r.X + 3, r.Y + 3, 20, 20); Glass(dc, cross, 10, Ui.Dark);
                    var pen = new Pen(Ui.Text, 1.4); dc.DrawLine(pen, new Point(cross.X + 7, cross.Y + 7), new Point(cross.X + 13, cross.Y + 13));
                    dc.DrawLine(pen, new Point(cross.X + 13, cross.Y + 7), new Point(cross.X + 7, cross.Y + 13));
                }
                dc.Pop(); dc.Pop(); dc.Pop();
            }
            dc.Pop();
            if (DecorationsChanged != null) DecorationsChanged();
        }
        internal void DrawDecorations(DrawingContext dc, bool front)
        {
            double offset = -(ShelfLayout.Height + 12) * (1 - slide), width = ActualWidth, now = host.Now;
            if (!front)
            {
                dc.PushTransform(new TranslateTransform(0, offset)); DrawRope(dc, width);
                if (!host.Cards.Any(c => !c.Falling))
                {
                    var text = Text(Ui.L("Take a screenshot and it will hang here", "Haz una captura y se quedará colgada aquí"), 12, Ui.Text);
                    var r = new Rect((width - text.Width - 24) / 2, ShelfLayout.RopeY(width / 2, width) + 20, text.Width + 24, 30);
                    Glass(dc, r, 15, Ui.Dark); dc.DrawText(text, new Point(r.X + 12, r.Y + 7));
                }
                dc.Pop();
            }
            foreach (var card in host.Cards.Where(c => !c.Falling && !c.Flying))
            {
                Motion m; if (!motion.TryGetValue(card.Id, out m)) continue;
                Box r = m.Current; dc.PushTransform(new MatrixTransform(m.Matrix)); dc.PushOpacity(host.Dragging == card ? .45 : 1);
                if (!front)
                {
                    for (int shadow = 5; shadow >= 1; shadow--)
                        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(hovered == card ? 8 : 5), 0, 0, 0)), null,
                            new Rect(r.X - shadow, r.Y + 5 - shadow, r.Width + shadow * 2, r.Height + shadow * 2), 16 + shadow, 16 + shadow);
                }
                else
                {
                    DrawClip(dc, r.CenterX, r.Y - 14);
                    if (card.CopiedUntil > now)
                    {
                        double alpha = host.ReducedMotion ? 1 : ShelfLayout.Clamp((card.CopiedUntil - now) / .18, 0, 1);
                        dc.PushOpacity(alpha); var text = Text(Ui.L("✓ Copied", "✓ Copiado"), 11, Ui.Text);
                        Rect pill = new Rect(r.CenterX - (text.Width + 20) / 2, r.Bottom + 2, text.Width + 20, 25);
                        Glass(dc, pill, 12.5, Ui.Dark); dc.DrawText(text, new Point(pill.X + 10, pill.Y + 5)); dc.Pop();
                    }
                }
                dc.Pop(); dc.Pop();
            }
        }
        internal Rect CardBounds(Card card)
        {
            Motion m; if (!IsVisible || !motion.TryGetValue(card.Id, out m) || PresentationSource.FromVisual(this) == null) return Rect.Empty;
            var transformed = new MatrixTransform(m.Matrix).TransformBounds(new Rect(m.Current.X, m.Current.Y, m.Current.Width, m.Current.Height));
            return new Rect(PointToScreen(transformed.TopLeft), PointToScreen(transformed.BottomRight));
        }
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() { return automation = new ShelfAutomationPeer(this); }
        static FormattedText Text(string text, double size, Brush color)
        { return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color); }
        internal static void Glass(DrawingContext dc, Rect rect, double radius, bool dark)
        {
            if (SystemParameters.HighContrast) { dc.DrawRoundedRectangle(SystemColors.WindowBrush, new Pen(SystemColors.WindowTextBrush, 1.5), rect, radius, radius); return; }
            // Self-rendered translucent material; no undocumented DWM/Acrylic API.
            var fill = new LinearGradientBrush(dark ? Color.FromArgb(225, 45, 49, 57) : Color.FromArgb(225, 252, 252, 253), dark ? Color.FromArgb(200, 24, 27, 33) : Color.FromArgb(200, 227, 231, 235), 90);
            dc.DrawRoundedRectangle(fill, new Pen(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), .5), rect, radius, radius);
            var light = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(35, 255, 255, 255), 90);
            dc.DrawRoundedRectangle(null, new Pen(light, .75), rect, radius, radius);
        }
        static void DrawClip(DrawingContext dc, double x, double top)
        {
            var metal = new LinearGradientBrush(); metal.StartPoint = new Point(0, .5); metal.EndPoint = new Point(1, .5);
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(178, 178, 178), 0)); metal.GradientStops.Add(new GradientStop(Color.FromRgb(237, 237, 237), .35));
            metal.GradientStops.Add(new GradientStop(Color.FromRgb(209, 209, 209), .65)); metal.GradientStops.Add(new GradientStop(Color.FromRgb(158, 158, 158), 1));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)), null, new Rect(x - 5.5, top + 1, 11, 28), 4, 4);
            dc.DrawRoundedRectangle(metal, new Pen(new SolidColorBrush(Color.FromArgb(200, 245, 245, 245)), .6), new Rect(x - 4.5, top, 9, 26), 3.5, 3.5);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)), null, new Rect(x - 2.5, top + 8.5, 5, 1.4), .7, .7);
        }
        static void DrawRope(DrawingContext dc, double width)
        {
            var rope = new StreamGeometry(); using (var c = rope.Open())
            { c.BeginFigure(new Point(-20, 10), false, false); c.QuadraticBezierTo(new Point(width / 2, 10 + 2 * ShelfLayout.Sag(width)), new Point(width + 20, 10), true, false); } rope.Freeze();
            var mask = new LinearGradientBrush(); mask.StartPoint = new Point(0, 0); mask.EndPoint = new Point(1, 0);
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0)); mask.GradientStops.Add(new GradientStop(Colors.Black, .08));
            mask.GradientStops.Add(new GradientStop(Colors.Black, .92)); mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            dc.PushOpacityMask(mask); dc.PushTransform(new TranslateTransform(0, 1.2)); dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)), 2.5), rope); dc.Pop();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 140)), 1.2), rope);
            dc.PushTransform(new TranslateTransform(0, -.35)); dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)), .4), rope); dc.Pop(); dc.Pop();
        }
        public void Dispose() { disposed = true; hold.Stop(); if (rendering) CompositionTarget.Rendering -= RenderFrame; rendering = false; }
    }
}
