using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace Pegline
{
    internal enum CaptureMode { Region, Window, Monitor, AllMonitors, Freehand, LastRegion }

    internal static class CaptureService
    {
        public static void Choose(Action<CaptureMode> selected, Preferences preferences)
        {
            var w = new System.Windows.Window { Title = Ui.L("Capture with Pegline", "Capturar con Pegline"), Width = 420, Height = 515, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            Ui.Theme(w); var p = new StackPanel { Margin = new Thickness(18) }; w.Content = p;
            p.Children.Add(Ui.Heading(Ui.L("Capture something", "Captura algo")));
            p.Children.Add(new TextBlock { Text = Ui.L("Precise pixels. No desktop clutter.", "Píxeles precisos. Sin llenar el escritorio."), Foreground = Ui.Muted, Margin = new Thickness(4, 0, 4, 10) });
            AddChoice(p, w, Ui.L("Rectangular region", "Zona rectangular"), CaptureMode.Region, selected);
            AddChoice(p, w, Ui.L("Window", "Ventana"), CaptureMode.Window, selected);
            AddChoice(p, w, Ui.L("This monitor", "Este monitor"), CaptureMode.Monitor, selected);
            AddChoice(p, w, Ui.L("All monitors", "Todos los monitores"), CaptureMode.AllMonitors, selected);
            AddChoice(p, w, Ui.L("Freehand region", "Zona a mano alzada"), CaptureMode.Freehand, selected);
            if (preferences.LastRegion != null) AddChoice(p, w, Ui.L("Repeat last region", "Repetir última zona"), CaptureMode.LastRegion, selected);
            var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 9, 3, 3) };
            options.Children.Add(Ui.Label(Ui.L("Delay", "Retardo")));
            var delay = new ComboBox { ItemsSource = new int[] { 0, 3, 5, 10 }, SelectedItem = preferences.CaptureDelay, Width = 62, Padding = new Thickness(5), Margin = new Thickness(4) };
            if (delay.SelectedIndex < 0) delay.SelectedIndex = 0;
            delay.SelectionChanged += delegate { preferences.CaptureDelay = (int)delay.SelectedItem; }; options.Children.Add(delay);
            options.Children.Add(new TextBlock { Text = Ui.L("seconds", "segundos"), VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Muted }); p.Children.Add(options);
            p.Children.Add(Ui.Label(Ui.L("Escape cancels. Adjust delay and cursor capture in Settings.", "Escape cancela. Ajusta el retardo y el puntero en Ajustes.")));
            w.PreviewKeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Escape) w.Close(); };
            w.Show();
        }
        static void AddChoice(StackPanel p, System.Windows.Window w, string label, CaptureMode mode, Action<CaptureMode> selected)
        { p.Children.Add(Ui.Button(label, delegate { w.Close(); selected(mode); })); }

        public static string DisplaySignature()
        { return string.Join("|", Forms.Screen.AllScreens.OrderBy(s => s.DeviceName).Select(s => s.DeviceName + ":" + s.Bounds.ToString())); }

        public static void Run(CaptureMode mode, Preferences preferences, Action<BitmapSource, Box, bool> completed)
        {
            if (mode == CaptureMode.LastRegion && (preferences.LastRegion == null || !preferences.LastRegion.Valid || preferences.LastRegion.DisplaySignature != DisplaySignature()))
                throw new InvalidOperationException(Ui.L("Select a new region first. Repeat is disabled after the monitor layout changes.", "Selecciona una zona nueva. Repetir se desactiva si cambia la disposición de monitores."));
            if (preferences.CaptureDelay > 0)
                using (var countdown = new CaptureCountdown(preferences.CaptureDelay)) if (countdown.ShowDialog() != Forms.DialogResult.OK) return;
            Rectangle desktop = Forms.SystemInformation.VirtualScreen;
            if ((long)desktop.Width * desktop.Height > Images.MaxPixels) throw new InvalidOperationException(Ui.L("The combined desktop is too large for a safe capture.", "El escritorio combinado es demasiado grande para una captura segura."));
            try { Native.DwmFlush(); } catch { }
            using (var snapshot = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(snapshot))
                {
                    g.CopyFromScreen(desktop.Left, desktop.Top, 0, 0, desktop.Size, CopyPixelOperation.SourceCopy);
                    if (preferences.CaptureCursor) DrawPointer(g, desktop);
                }
                Rectangle selection; Bitmap crop; bool repeatable = false;
                if (mode == CaptureMode.Monitor || mode == CaptureMode.AllMonitors || mode == CaptureMode.LastRegion)
                {
                    selection = mode == CaptureMode.LastRegion ? new Rectangle(preferences.LastRegion.X, preferences.LastRegion.Y, preferences.LastRegion.Width, preferences.LastRegion.Height)
                        : mode == CaptureMode.Monitor ? Native.PointerScreen.Bounds : desktop;
                    if (!desktop.Contains(selection)) throw new InvalidOperationException("The capture region is no longer on this desktop.");
                    repeatable = mode == CaptureMode.LastRegion;
                    crop = snapshot.Clone(new Rectangle(selection.X - desktop.X, selection.Y - desktop.Y, selection.Width, selection.Height), PixelFormat.Format32bppArgb);
                }
                else
                {
                    using (var overlay = new CaptureOverlay(snapshot, desktop, mode, preferences.ShowCaptureLoupe))
                    {
                        if (overlay.ShowDialog() != Forms.DialogResult.OK || overlay.Result == null) return;
                        selection = overlay.Selected; crop = overlay.Result; repeatable = overlay.Repeatable; // Ownership transfers to this scope.
                    }
                }
                using (crop) completed(Images.FromGdi(crop), new Box(selection.X, selection.Y, selection.Width, selection.Height), repeatable);
            }
        }
        static void DrawPointer(Graphics graphics, Rectangle desktop)
        {
            var info = new Native.CURSORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.CURSORINFO)) };
            if (!Native.GetCursorInfo(ref info) || (info.Flags & 1) == 0) return;
            Native.ICONINFO icon; if (!Native.GetIconInfo(info.Cursor, out icon)) return;
            try
            {
                IntPtr dc = graphics.GetHdc();
                try { Native.DrawIconEx(dc, info.Position.X - desktop.X - (int)icon.XHotspot, info.Position.Y - desktop.Y - (int)icon.YHotspot, info.Cursor, 0, 0, 0, IntPtr.Zero, 3); }
                finally { graphics.ReleaseHdc(dc); }
            }
            finally { if (icon.Mask != IntPtr.Zero) Native.DeleteObject(icon.Mask); if (icon.Color != IntPtr.Zero) Native.DeleteObject(icon.Color); }
        }
    }
    internal sealed class CaptureCountdown : Forms.Form
    {
        readonly Forms.Timer timer;
        readonly Forms.Label label;
        readonly DateTime end;
        public CaptureCountdown(int seconds)
        {
            Text = "Pegline"; FormBorderStyle = Forms.FormBorderStyle.FixedToolWindow; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            ClientSize = new Size(320, 95); StartPosition = Forms.FormStartPosition.Manual; var b = Native.PointerScreen.WorkingArea;
            Location = new Point(b.X + (b.Width - Width) / 2, b.Y + 35);
            label = new Forms.Label { Dock = Forms.DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 14) }; Controls.Add(label);
            end = DateTime.UtcNow.AddSeconds(seconds); timer = new Forms.Timer { Interval = 75 };
            timer.Tick += delegate
            {
                double remaining = (end - DateTime.UtcNow).TotalSeconds;
                label.Text = Ui.L("Capture in ", "Captura en ") + Math.Ceiling(Math.Max(0, remaining)) + Ui.L("…\nEscape to cancel", "…\nEscape para cancelar");
                if (remaining <= 0) { timer.Stop(); DialogResult = Forms.DialogResult.OK; }
            };
            Shown += delegate { timer.Start(); };
            FormClosed += delegate { timer.Dispose(); label.Font.Dispose(); };
            KeyDown += delegate(object s, Forms.KeyEventArgs e) { if (e.KeyCode == Forms.Keys.Escape) DialogResult = Forms.DialogResult.Cancel; };
        }
    }
    internal sealed class CaptureOverlay : Forms.Form
    {
        readonly Bitmap snapshot;
        readonly Rectangle desktop;
        CaptureMode mode;
        Point start;
        Rectangle current;
        readonly List<Point> polygon = new List<Point>();
        bool down;
        public Bitmap Result;
        public Rectangle Selected;
        public bool Repeatable;
        bool showLoupe;
        public CaptureOverlay(Bitmap snapshot, Rectangle desktop, CaptureMode mode, bool showLoupe)
        {
            this.snapshot = snapshot; this.desktop = desktop; this.mode = mode; this.showLoupe = showLoupe;
            FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = Forms.FormStartPosition.Manual;
            AutoScaleMode = Forms.AutoScaleMode.None; Bounds = desktop; DoubleBuffered = true; KeyPreview = true; Cursor = Forms.Cursors.Cross;
            Shown += delegate { Bounds = desktop; Activate(); Native.ExcludeFromCapture(Handle); };
        }
        Point Position { get { var p = Native.Cursor; return new Point(p.X, p.Y); } }
        protected override void OnMouseDown(Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Right) { DialogResult = Forms.DialogResult.Cancel; return; }
            if (e.Button != Forms.MouseButtons.Left) return;
            down = true; start = Position; polygon.Clear(); polygon.Add(start); Capture = true;
            if (mode == CaptureMode.Window) current = Native.WindowAt(Native.Cursor);
            else current = Rectangle.Empty;
            Invalidate();
        }
        protected override void OnMouseMove(Forms.MouseEventArgs e)
        {
            Point p = Position;
            if (mode == CaptureMode.Window) current = Native.WindowAt(Native.Cursor);
            else if (down)
            {
                if (mode == CaptureMode.Freehand)
                {
                    if (polygon.Count == 0 || Math.Abs(p.X - polygon[polygon.Count - 1].X) + Math.Abs(p.Y - polygon[polygon.Count - 1].Y) >= 2) polygon.Add(p);
                    current = Rectangle.FromLTRB(polygon.Min(q => q.X), polygon.Min(q => q.Y), polygon.Max(q => q.X) + 1, polygon.Max(q => q.Y) + 1);
                }
                else
                {
                    if ((ModifierKeys & Forms.Keys.Shift) != 0)
                    { int side = Math.Max(Math.Abs(p.X - start.X), Math.Abs(p.Y - start.Y)); p = new Point(start.X + (p.X < start.X ? -side : side), start.Y + (p.Y < start.Y ? -side : side)); }
                    current = Rectangle.FromLTRB(Math.Min(start.X, p.X), Math.Min(start.Y, p.Y), Math.Max(start.X, p.X), Math.Max(start.Y, p.Y));
                }
            }
            Invalidate();
        }
        protected override void OnMouseUp(Forms.MouseEventArgs e)
        {
            if (!down || e.Button != Forms.MouseButtons.Left) return;
            down = false; Capture = false; var clipped = Rectangle.Intersect(current, desktop);
            if (clipped.Width < 2 || clipped.Height < 2) { current = Rectangle.Empty; return; }
            if (mode == CaptureMode.Freehand && polygon.Count < 3) return;
            Selected = clipped; Repeatable = mode != CaptureMode.Freehand; Result = new Bitmap(clipped.Width, clipped.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(Result))
            {
                g.Clear(Color.Transparent);
                if (mode == CaptureMode.Freehand)
                {
                    using (var path = new GraphicsPath()) { path.AddPolygon(polygon.Select(p => new Point(p.X - clipped.X, p.Y - clipped.Y)).ToArray()); g.SetClip(path); }
                }
                g.DrawImage(snapshot, new Rectangle(0, 0, clipped.Width, clipped.Height), new Rectangle(clipped.X - desktop.X, clipped.Y - desktop.Y, clipped.Width, clipped.Height), GraphicsUnit.Pixel);
            }
            DialogResult = Forms.DialogResult.OK;
        }
        protected override bool ProcessCmdKey(ref Forms.Message msg, Forms.Keys keyData)
        {
            if (keyData == Forms.Keys.Escape) { DialogResult = Forms.DialogResult.Cancel; return true; }
            if (keyData == Forms.Keys.L) { showLoupe = !showLoupe; Invalidate(); return true; }
            var key = keyData & Forms.Keys.KeyCode;
            if (key == Forms.Keys.Left || key == Forms.Keys.Right || key == Forms.Keys.Up || key == Forms.Keys.Down)
            {
                var point = Native.Cursor; int step = (keyData & Forms.Keys.Shift) != 0 ? 10 : 1;
                Native.SetCursorPos(Math.Max(desktop.Left, Math.Min(desktop.Right - 1, point.X + (key == Forms.Keys.Left ? -step : key == Forms.Keys.Right ? step : 0))),
                    Math.Max(desktop.Top, Math.Min(desktop.Bottom - 1, point.Y + (key == Forms.Keys.Up ? -step : key == Forms.Keys.Down ? step : 0))));
                Invalidate(); return true;
            }
            if (keyData == Forms.Keys.Space || keyData == Forms.Keys.Tab) { mode = mode == CaptureMode.Window ? CaptureMode.Region : CaptureMode.Window; ResetSelection(); return true; }
            if (keyData == Forms.Keys.R) { mode = CaptureMode.Region; ResetSelection(); return true; }
            if (keyData == Forms.Keys.W) { mode = CaptureMode.Window; ResetSelection(); return true; }
            if (keyData == Forms.Keys.F) { mode = CaptureMode.Freehand; ResetSelection(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        void ResetSelection() { down = false; Capture = false; current = Rectangle.Empty; polygon.Clear(); Invalidate(); }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.DrawImageUnscaled(snapshot, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(105, 0, 0, 0))) g.FillRectangle(dim, ClientRectangle);
            var local = new Rectangle(current.X - desktop.X, current.Y - desktop.Y, current.Width, current.Height);
            if (local.Width > 1 && local.Height > 1)
            {
                var old = g.Save();
                if (mode == CaptureMode.Freehand && polygon.Count >= 3)
                {
                    using (var p = new GraphicsPath()) { p.AddPolygon(polygon.Select(q => new Point(q.X - desktop.X, q.Y - desktop.Y)).ToArray()); g.SetClip(p); }
                }
                else g.SetClip(local);
                g.DrawImageUnscaled(snapshot, 0, 0); g.Restore(old);
                using (var pen = new Pen(Color.White, 1))
                {
                    if (mode == CaptureMode.Freehand && polygon.Count > 1) g.DrawLines(pen, polygon.Select(p => new Point(p.X - desktop.X, p.Y - desktop.Y)).ToArray());
                    else g.DrawRectangle(pen, local);
                }
                using (var font = new Font("Segoe UI", 10))
                using (var brush = new SolidBrush(Color.White))
                using (var dark = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                {
                    string text = current.Width + " × " + current.Height;
                    SizeF size = g.MeasureString(text, font); float y = Math.Max(0, local.Y - size.Height - 10);
                    g.FillRectangle(dark, local.X, y, size.Width + 12, size.Height + 6); g.DrawString(text, font, brush, local.X + 6, y + 3);
                }
            }
            var monitor = Native.PointerScreen.Bounds;
            string modeLabel = mode == CaptureMode.Window ? Ui.L("Click a window", "Haz clic en una ventana") : mode == CaptureMode.Freehand ? Ui.L("Draw around an area", "Dibuja alrededor de una zona") : Ui.L("Drag to select an area", "Arrastra para seleccionar una zona");
            string hint = modeLabel + Ui.L("   ·   Space: window / region   ·   F: freehand   ·   L: loupe   ·   Esc: cancel", "   ·   Espacio: ventana / zona   ·   F: mano alzada   ·   L: lupa   ·   Esc: cancelar");
            using (var font = new Font("Segoe UI", 11))
            using (var brush = new SolidBrush(Color.White))
            using (var dark = new SolidBrush(Color.FromArgb(225, 28, 31, 38)))
            {
                SizeF s = g.MeasureString(hint, font); float x = monitor.Left - desktop.Left + (monitor.Width - s.Width) / 2, y = monitor.Top - desktop.Top + 30;
                g.FillRectangle(dark, x - 12, y - 8, s.Width + 24, s.Height + 16); g.DrawString(hint, font, brush, x, y);
            }
            if (showLoupe && mode != CaptureMode.Window) DrawLoupe(g, monitor);
        }
        void DrawLoupe(Graphics g, Rectangle monitor)
        {
            Point p = Position; const int sample = 15, zoom = 8, size = sample * zoom;
            int x = p.X - desktop.X + 28, y = p.Y - desktop.Y + 28;
            int left = monitor.Left - desktop.Left, top = monitor.Top - desktop.Top;
            if (x + size + 8 > left + monitor.Width) x = p.X - desktop.X - size - 28;
            if (y + size + 30 > top + monitor.Height) y = p.Y - desktop.Y - size - 50;
            x = Math.Max(left + 8, x); y = Math.Max(top + 8, y);
            Rectangle source = new Rectangle(p.X - desktop.X - sample / 2, p.Y - desktop.Y - sample / 2, sample, sample);
            Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, snapshot.Width, snapshot.Height));
            using (var back = new SolidBrush(Color.FromArgb(245, 24, 28, 35))) g.FillRectangle(back, x - 3, y - 3, size + 6, size + 29);
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            if (clipped.Width > 0 && clipped.Height > 0)
                g.DrawImage(snapshot, new Rectangle(x + (clipped.X - source.X) * zoom, y + (clipped.Y - source.Y) * zoom, clipped.Width * zoom, clipped.Height * zoom), clipped, GraphicsUnit.Pixel);
            g.Restore(state);
            using (var grid = new Pen(Color.FromArgb(50, 0, 0, 0)))
                for (int n = 0; n <= sample; n++) { g.DrawLine(grid, x + n * zoom, y, x + n * zoom, y + size); g.DrawLine(grid, x, y + n * zoom, x + size, y + n * zoom); }
            using (var cross = new Pen(Color.FromArgb(255, 44, 209, 210), 2))
                g.DrawRectangle(cross, x + sample / 2 * zoom, y + sample / 2 * zoom, zoom, zoom);
            using (var font = new Font("Segoe UI", 9)) using (var text = new SolidBrush(Color.White))
                g.DrawString(p.X + ", " + p.Y + "  ·  8×", font, text, x + 5, y + size + 5);
        }
    }
}
