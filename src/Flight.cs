using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Forms = System.Windows.Forms;

namespace Pegline
{
    // Small, moving layered window in PHYSICAL screen coordinates. This avoids
    // clipping falls to the shelf strip and allows a flight to cross DPI boundaries.
    internal sealed class Flight : Forms.Form
    {
        enum Kind { Arrival, Fall, Return }
        static readonly HashSet<Flight> active = new HashSet<Flight>();
        readonly Bitmap image;
        readonly RectangleF from, to;
        readonly double tilt, scale, duration;
        readonly Kind kind;
        readonly Action complete;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Forms.Timer timer;
        bool completed, released;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override Forms.CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= (int)(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE); return p; }
        }
        Flight(Bitmap image, RectangleF from, RectangleF to, double tilt, double scale, Kind kind, Action complete)
        {
            this.image = image; this.from = from; this.to = to; this.tilt = tilt; this.scale = scale; this.kind = kind; this.complete = complete;
            duration = kind == Kind.Arrival ? .65 : kind == Kind.Fall ? .55 : .28;
            FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = Forms.FormStartPosition.Manual;
            AutoScaleMode = Forms.AutoScaleMode.None; Bounds = new Rectangle((int)to.X, (int)to.Y, 1, 1);
            timer = new Forms.Timer { Interval = 16 }; timer.Tick += delegate { TickFrame(); };

        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == Native.WM_MOUSEACTIVATE) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        void Finish() { if (completed) return; completed = true; if (complete != null) complete(); }
        void Run()
        {
            active.Add(this);
            try { Show(); Native.ExcludeFromCapture(Handle); clock.Restart(); TickFrame(); if (!IsDisposed && !completed) timer.Start(); }
            catch { Dispose(); throw; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !released) { released = true; if (timer != null) timer.Dispose(); if (image != null) image.Dispose(); active.Remove(this); Finish(); }
            base.Dispose(disposing);
        }
        public static void Arrive(Bitmap image, RectangleF from, RectangleF to, double tilt, double scale, Action done)
        { new Flight(image, from, to, tilt, scale, Kind.Arrival, done).Run(); }
        public static void Fall(Bitmap image, RectangleF card, double tilt, double scale)
        { new Flight(image, card, card, tilt, scale, Kind.Fall, null).Run(); }
        public static void Return(Bitmap image, RectangleF from, RectangleF to, double tilt, double scale, Action done)
        { new Flight(image, from, to, tilt, scale, Kind.Return, done).Run(); }
        public static void CloseAll() { foreach (var f in new List<Flight>(active)) f.Close(); }
        void TickFrame()
        {
            double raw = Math.Min(1, clock.Elapsed.TotalSeconds / duration), t = ShelfLayout.Cubic(raw);
            RectangleF rect; double angle, opacity = 1, chrome = 1;
            if (kind == Kind.Fall)
            {
                double e = raw * raw * raw; rect = new RectangleF(to.X, to.Y + (float)(520 * scale * e), to.Width, to.Height);
                angle = tilt + (tilt * 7 + 20) * e; opacity = 1 - e;
            }
            else
            {
                rect = new RectangleF((float)(from.X + (to.X - from.X) * t), (float)(from.Y + (to.Y - from.Y) * t - Math.Sin(Math.PI * t) * 30 * scale),
                    (float)(from.Width + (to.Width - from.Width) * t), (float)(from.Height + (to.Height - from.Height) * t));
                angle = tilt * t; if (kind == Kind.Arrival) chrome = ShelfLayout.Clamp((t - .35) / .65, 0, 1);
            }
            try { PaintFrame(rect, angle, opacity, chrome); }
            catch { timer.Stop(); Close(); return; }
            if (raw >= 1) { timer.Stop(); Close(); }
        }
        void PaintFrame(RectangleF r, double angle, double opacity, double chrome)
        {
            double rad = angle * Math.PI / 180;
            float padding = (float)(30 * scale), rotation = (float)(Math.Abs(Math.Sin(rad)) * (r.Width + r.Height));
            int left = (int)Math.Floor(r.X - padding - rotation), top = (int)Math.Floor(r.Y - padding - rotation);
            int width = Math.Max(1, (int)Math.Ceiling(r.Width + padding * 2 + rotation * 2)), height = Math.Max(1, (int)Math.Ceiling(r.Height + padding * 2 + rotation * 2));
            if ((long)width * height > 16000000) throw new InvalidOperationException("Flight surface too large.");
            using (var frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(r.X + r.Width / 2 - left, r.Y - top); g.RotateTransform((float)angle); g.TranslateTransform(-r.Width / 2, 0);
                float inset = (float)(4 * scale * chrome), radius = (float)(16 * scale * chrome);
                using (var path = Round(new RectangleF(0, 0, r.Width, r.Height), radius))
                {
                    using (var brush = new SolidBrush(Color.FromArgb((int)(220 * chrome), 245, 246, 248))) g.FillPath(brush, path);
                    using (var pen = new Pen(Color.FromArgb((int)(180 * chrome), 255, 255, 255), (float)Math.Max(1, scale))) g.DrawPath(pen, path);
                }
                RectangleF photo = new RectangleF(inset, inset, Math.Max(1, r.Width - 2 * inset), Math.Max(1, r.Height - 2 * inset));
                var state = g.Save(); using (var clip = Round(photo, Math.Max(0, radius - inset))) g.SetClip(clip);
                g.DrawImage(image, photo); g.Restore(state);
                if (chrome > 0)
                {
                    RectangleF clip = new RectangleF(r.Width / 2 - (float)(4.5 * scale), (float)(-14 * scale), (float)(9 * scale), (float)(26 * scale));
                    using (var shape = Round(clip, (float)(3.5 * scale)))
                    using (var metal = new LinearGradientBrush(clip, Color.FromArgb((int)(255 * chrome), 170, 170, 170), Color.FromArgb((int)(255 * chrome), 235, 235, 235), 0f)) g.FillPath(metal, shape);
                }
                IntPtr dc = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
                try
                {
                    dc = Native.GetDC(IntPtr.Zero); if (dc == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                    memory = Native.CreateCompatibleDC(dc); if (memory == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                    bitmap = frame.GetHbitmap(Color.FromArgb(0));
                    previous = Native.SelectObject(memory, bitmap);
                    if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
                    var dst = new Native.POINT(left, top); var src = new Native.POINT(0, 0); var size = new Native.SIZE(width, height);
                    var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = (byte)(255 * opacity), AlphaFormat = 1 };
                    if (!Native.UpdateLayeredWindow(Handle, dc, ref dst, ref size, memory, ref src, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception();
                }
                finally
                {
                    if (memory != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) Native.SelectObject(memory, previous);
                    if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                    if (memory != IntPtr.Zero) Native.DeleteDC(memory);
                    if (dc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, dc);
                }
            }
        }
        internal static GraphicsPath Round(RectangleF rect, float radius)
        {
            var p = new GraphicsPath(); radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
            if (radius < .1) { p.AddRectangle(rect); return p; }
            float d = radius * 2; p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
    }
}
