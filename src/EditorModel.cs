using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pegline
{
    internal enum MarkKind { Select, Pen, Sketch, Highlighter, Eraser, Rectangle, Ellipse, Line, Arrow, DoubleArrow, Polygon, Star, SpeechBubble, Text, Spotlight, Magnifier, Signature, Image, Crop, Redact }
    [DataContract]
    internal struct InkPoint
    {
        [DataMember] public double X;
        [DataMember] public double Y;
        [DataMember] public float Pressure;
        [DataMember] public bool Start;
        public InkPoint(double x, double y, float pressure, bool start) { X = x; Y = y; Pressure = pressure; Start = start; }
    }
    internal sealed class Mark
    {
        public Guid Id = Guid.NewGuid();
        public MarkKind Kind;
        public Rect Bounds;
        public Color Stroke = Color.FromRgb(219, 52, 64), Fill = Color.FromArgb(90, 255, 214, 69);
        public bool Filled, Dashed, Bold, Italic;
        public double Thickness = 3, FontSize = 24, Magnification = 2;
        public string Text = "", Font = "Segoe UI";
        public TextAlignment Alignment;
        public int Sides = 6;
        public bool Normalized = true;
        public List<InkPoint> Points = new List<InkPoint>();
        public BitmapSource Image;
        // History snapshots preserve identity. New annotations MUST request a fresh identity.
        public Mark Clone() { var c = (Mark)MemberwiseClone(); c.Points = new List<InkPoint>(Points); return c; }
        public Mark NewCopy() { var c = Clone(); c.Id = Guid.NewGuid(); return c; }
        public Point PointAt(int n)
        { var p = Points[n]; return Normalized ? new Point(Bounds.X + p.X * Bounds.Width, Bounds.Y + p.Y * Bounds.Height) : new Point(p.X, p.Y); }
        public void NormalizeInk()
        {
            if (Normalized || Points.Count == 0) return;
            double x = Points.Min(p => p.X), y = Points.Min(p => p.Y), w = Math.Max(1, Points.Max(p => p.X) - x), h = Math.Max(1, Points.Max(p => p.Y) - y);
            Bounds = new Rect(x, y, w, h);
            Points = Points.Select(p => new InkPoint((p.X - x) / w, (p.Y - y) / h, p.Pressure, p.Start)).ToList(); Normalized = true;
        }
        public bool Hit(Point p, double tolerance)
        {
            Rect r = Bounds; r.Inflate(Math.Max(tolerance, Thickness), Math.Max(tolerance, Thickness));
            if (!r.Contains(p)) return false;
            if (Kind == MarkKind.Line || Kind == MarkKind.Arrow || Kind == MarkKind.DoubleArrow)
                return EditorMath.DistanceToSegment(p, Points.Count >= 2 ? PointAt(0) : Bounds.TopLeft,
                    Points.Count >= 2 ? PointAt(1) : Bounds.BottomRight) <= Math.Max(Thickness / 2, tolerance) + 3;
            if (!Filled && Kind == MarkKind.Rectangle)
                return Math.Min(Math.Min(Math.Abs(p.X - Bounds.Left), Math.Abs(p.X - Bounds.Right)),
                    Math.Min(Math.Abs(p.Y - Bounds.Top), Math.Abs(p.Y - Bounds.Bottom))) <= Math.Max(Thickness / 2, tolerance);
            if (!Filled && Kind == MarkKind.Ellipse && Bounds.Width > 0 && Bounds.Height > 0)
            {
                double dx = (p.X - Bounds.X - Bounds.Width / 2) / (Bounds.Width / 2), dy = (p.Y - Bounds.Y - Bounds.Height / 2) / (Bounds.Height / 2);
                return Math.Abs(Math.Sqrt(dx * dx + dy * dy) - 1) <= Math.Max(Thickness / 2, tolerance) / (Math.Min(Bounds.Width, Bounds.Height) / 2);
            }
            if (Kind == MarkKind.Pen || Kind == MarkKind.Sketch || Kind == MarkKind.Signature || Kind == MarkKind.Highlighter || Kind == MarkKind.Line || Kind == MarkKind.Arrow || Kind == MarkKind.DoubleArrow)
            {
                for (int i = 1; i < Points.Count; i++)
                    if (!Points[i].Start && EditorMath.DistanceToSegment(p, PointAt(i - 1), PointAt(i)) <= Math.Max(Thickness, tolerance) + 3) return true;
                return Points.Count == 1 && (p - PointAt(0)).Length <= Thickness + tolerance;
            }
            return true;
        }
    }
    internal static class EditorMath
    {
        public static double DistanceToSegment(Point p, Point a, Point b)
        {
            Vector ab = b - a; double t = ab.LengthSquared == 0 ? 0 : Vector.Multiply(p - a, ab) / ab.LengthSquared;
            return (p - (a + ab * ShelfLayout.Clamp(t, 0, 1))).Length;
        }
        public static Point Constrain(Point start, Point end, bool line)
        {
            Vector delta = end - start;
            if (line)
            {
                double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * (Math.PI / 4);
                return start + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
            }
            double size = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            return new Point(start.X + (delta.X < 0 ? -size : size), start.Y + (delta.Y < 0 ? -size : size));
        }
        public static Rect Between(Point a, Point b) { return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)); }
        public static void Recognize(Mark m)
        {
            if (m.Points.Count < 4) return;
            m.NormalizeInk();
            double size = Math.Max(m.Bounds.Width, m.Bounds.Height);
            if (size < 12) return;
            Point first = m.PointAt(0), last = m.PointAt(m.Points.Count - 1);
            bool closed = (first - last).Length < size * .22;
            if (!closed)
            {
                double error = Enumerable.Range(0, m.Points.Count).Average(i => DistanceToSegment(m.PointAt(i), first, last));
                if (error < size * .045)
                {
                    var a = m.Points[0]; var b = m.Points[m.Points.Count - 1]; m.Kind = MarkKind.Line; m.Points = new List<InkPoint> { a, b };
                }
                return;
            }
            double ellipseError = m.Points.Average(p => Math.Abs(Math.Sqrt(Math.Pow((p.X - .5) * 2, 2) + Math.Pow((p.Y - .5) * 2, 2)) - 1));
            double rectangleError = m.Points.Average(p => Math.Min(Math.Min(p.X, 1 - p.X), Math.Min(p.Y, 1 - p.Y)));
            if (ellipseError < .15 && ellipseError < rectangleError * 2.5) m.Kind = MarkKind.Ellipse;
            else if (rectangleError < .12) m.Kind = MarkKind.Rectangle;
            // Recognition is undoable; imperfect sketches remain untouched instead of forcing a shape.
        }
    }

    internal sealed class EditorSnapshot
    {
        public BitmapSource Base;
        public List<Mark> Layers;
        public long Revision;
    }
    internal sealed class EditorDocument
    {
        public BitmapSource BaseImage { get; private set; }
        public List<Mark> Layers { get; private set; }
        public bool Dirty { get { return revision != savedRevision; } }
        public int Version { get; private set; }
        readonly List<EditorSnapshot> undo = new List<EditorSnapshot>(), redo = new List<EditorSnapshot>();
        long revision, savedRevision, nextRevision;
        EditorSnapshot transaction;
        List<EditorSnapshot> redoBeforeTransaction;
        BitmapSource magnifierPreview;
        int magnifierVersion = -1;
        public bool IsEditing { get { return transaction != null; } }
        public event Action Changed;
        public int Width { get { return BaseImage.PixelWidth; } }
        public int Height { get { return BaseImage.PixelHeight; } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public EditorDocument(BitmapSource image)
        { BaseImage = image; if (!image.IsFrozen) image.Freeze(); Layers = new List<Mark>(); }
        EditorSnapshot Snapshot() { return new EditorSnapshot { Base = BaseImage, Layers = Layers.Select(m => m.Clone()).ToList(), Revision = revision }; }
        void Restore(EditorSnapshot state)
        { BaseImage = state.Base; Layers = state.Layers.Select(m => m.Clone()).ToList(); revision = state.Revision; Notify(); }
        void Notify() { Version++; magnifierPreview = null; if (Changed != null) Changed(); }
        public void BeginEdit()
        {
            if (transaction != null) return;
            transaction = Snapshot(); redoBeforeTransaction = new List<EditorSnapshot>(redo);
            // Do not evict committed history for a gesture that may be canceled.
            undo.Add(transaction); redo.Clear();
        }
        public void CommitEdit()
        {
            if (transaction == null) return;
            if (revision == transaction.Revision)
            {
                undo.Remove(transaction); redo.Clear(); redo.AddRange(redoBeforeTransaction);
            }
            transaction = null; redoBeforeTransaction = null; TrimHistory();
        }
        public void CancelEdit()
        {
            if (transaction == null) return;
            var before = transaction; undo.Remove(before); redo.Clear(); redo.AddRange(redoBeforeTransaction);
            transaction = null; redoBeforeTransaction = null; Restore(before);
        }
        public void Remember()
        {
            CommitEdit(); undo.Add(Snapshot()); redo.Clear(); TrimHistory();
        }
        void TrimHistory()
        {
            while (undo.Count > 32) undo.RemoveAt(0);
            // Bound retained raster history (shared immutable sources count only once).
            while (undo.Count > 1 && undo.Select(s => s.Base).Distinct().Sum(b => (long)b.PixelWidth * b.PixelHeight) > 96000000) undo.RemoveAt(0);
        }
        public void Undo()
        { if (IsEditing) { CancelEdit(); return; } if (!CanUndo) return; redo.Add(Snapshot()); var s = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); Restore(s); }
        public void Redo()
        { if (IsEditing) { CancelEdit(); return; } if (!CanRedo) return; undo.Add(Snapshot()); var s = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); Restore(s); TrimHistory(); }
        public void Touch() { revision = ++nextRevision; Notify(); }
        public void MarkSaved() { CommitEdit(); savedRevision = revision; if (Changed != null) Changed(); }
        public Mark Hit(Point point, double tolerance)
        { return Layers.AsEnumerable().Reverse().FirstOrDefault(m => m.Hit(point, tolerance)); }
        public BitmapSource Render(int maxDimension, bool omitMagnifiers)
        {
            double scale = maxDimension > 0 ? Math.Min(1, (double)maxDimension / Math.Max(Width, Height)) : 1;
            int w = Math.Max(1, (int)Math.Round(Width * scale)), h = Math.Max(1, (int)Math.Round(Height * scale));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform((double)w / Width, (double)h / Height));
                DrawInternal(dc, omitMagnifiers, maxDimension == 0); dc.Pop();
            }
            var result = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
        }
        public void Draw(DrawingContext dc, bool omitMagnifiers) { DrawInternal(dc, omitMagnifiers, false); }
        void DrawInternal(DrawingContext dc, bool omitMagnifiers, bool fullResolution)
        {
            dc.DrawImage(BaseImage, new Rect(0, 0, Width, Height));
            BitmapSource magnified = BaseImage;
            if (!omitMagnifiers && Layers.Any(m => m.Kind == MarkKind.Magnifier))
            {
                if (fullResolution) magnified = Render(0, true);
                else
                {
                    // Repaint/hover/zoom must not re-render an entire document each frame.
                    if (magnifierPreview == null || magnifierVersion != Version)
                    { magnifierPreview = Render(2048, true); magnifierVersion = Version; }
                    magnified = magnifierPreview;
                }
            }
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Width, Height)));
            foreach (var m in Layers) if (!omitMagnifiers || m.Kind != MarkKind.Magnifier) DrawMark(dc, m, magnified, Width, Height);
            dc.Pop();
        }
        public void Crop(Rect selection)
        {
            selection.Intersect(new Rect(0, 0, Width, Height));
            if (selection.IsEmpty || selection.Width < 1 || selection.Height < 1) return;
            int x = Math.Max(0, (int)Math.Floor(selection.X)), y = Math.Max(0, (int)Math.Floor(selection.Y));
            int right = Math.Min(Width, (int)Math.Ceiling(selection.Right)), bottom = Math.Min(Height, (int)Math.Ceiling(selection.Bottom));
            if (right - x < 1 || bottom - y < 1) return;
            // Crop the original raster, but keep editable annotations and their identities.
            Remember(); BaseImage = new CroppedBitmap(BaseImage, new Int32Rect(x, y, right - x, bottom - y)); BaseImage.Freeze();
            foreach (var layer in Layers)
            {
                layer.NormalizeInk(); var bounds = layer.Bounds; bounds.Offset(-x, -y); layer.Bounds = bounds;
            }
            Touch();
        }
        public void Rotate(double angle)
        {
            var flat = Render(0, false); Remember(); BaseImage = new TransformedBitmap(flat, new RotateTransform(angle)); BaseImage.Freeze(); Layers.Clear(); Touch();
        }
        public void Flip(bool horizontal)
        {
            var flat = Render(0, false); Remember(); BaseImage = new TransformedBitmap(flat, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1)); BaseImage.Freeze(); Layers.Clear(); Touch();
        }
        static Pen PenFor(Mark m)
        {
            var pen = new Pen(new SolidColorBrush(m.Stroke), m.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (m.Dashed) pen.DashStyle = DashStyles.Dash; return pen;
        }
        static Geometry Polygon(Rect r, int count, bool star)
        {
            var path = new StreamGeometry();
            using (var g = path.Open())
            {
                int n = star ? count * 2 : count;
                for (int i = 0; i < n; i++)
                {
                    double angle = -Math.PI / 2 + 2 * Math.PI * i / n, s = star && i % 2 != 0 ? .45 : 1;
                    var p = new Point(r.X + r.Width / 2 + Math.Cos(angle) * r.Width / 2 * s, r.Y + r.Height / 2 + Math.Sin(angle) * r.Height / 2 * s);
                    if (i == 0) g.BeginFigure(p, true, true); else g.LineTo(p, true, false);
                }
            }
            return path;
        }
        internal static void DrawMark(DrawingContext dc, Mark m, BitmapSource magnified, int docWidth, int docHeight)
        {
            Rect r = m.Bounds; if (r.IsEmpty || !ShelfLayout.Finite(r.X) || !ShelfLayout.Finite(r.Y) || !ShelfLayout.Finite(r.Width) || !ShelfLayout.Finite(r.Height) || r.Width <= 0 || r.Height <= 0) return;
            Pen pen = PenFor(m); Brush fill = m.Filled ? new SolidColorBrush(m.Fill) : null;
            switch (m.Kind)
            {
                case MarkKind.Pen: case MarkKind.Sketch: case MarkKind.Highlighter: case MarkKind.Signature:
                    if (m.Kind == MarkKind.Highlighter) dc.PushOpacity(.35);
                    if (m.Points.Count == 1) dc.DrawEllipse(pen.Brush, null, m.PointAt(0), m.Thickness / 2, m.Thickness / 2);
                    for (int i = 1; i < m.Points.Count; i++)
                    {
                        if (m.Points[i].Start) continue;
                        var pressure = new Pen(pen.Brush, m.Thickness * Math.Max(.2, m.Points[i].Pressure)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                        dc.DrawLine(pressure, m.PointAt(i - 1), m.PointAt(i));
                    }
                    if (m.Kind == MarkKind.Highlighter) dc.Pop(); break;
                case MarkKind.Rectangle: dc.DrawRoundedRectangle(fill, pen, r, 0, 0); break;
                case MarkKind.Redact:
                    // Expand outward to physical image pixels: a fractional edge must not
                    // blend original pixels back into an exported redaction.
                    Rect opaque = new Rect(Math.Floor(r.Left), Math.Floor(r.Top), Math.Ceiling(r.Right) - Math.Floor(r.Left), Math.Ceiling(r.Bottom) - Math.Floor(r.Top));
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(m.Stroke.R, m.Stroke.G, m.Stroke.B)), null, opaque); break;
                case MarkKind.Ellipse: dc.DrawEllipse(fill, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case MarkKind.Line: case MarkKind.Arrow: case MarkKind.DoubleArrow:
                    Point a = m.Points.Count >= 2 ? m.PointAt(0) : r.TopLeft, b = m.Points.Count >= 2 ? m.PointAt(1) : r.BottomRight;
                    dc.DrawLine(pen, a, b); if (m.Kind != MarkKind.Line) ArrowHead(dc, pen, a, b); if (m.Kind == MarkKind.DoubleArrow) ArrowHead(dc, pen, b, a); break;
                case MarkKind.Polygon: case MarkKind.Star: dc.DrawGeometry(fill, pen, Polygon(r, m.Kind == MarkKind.Star ? 5 : m.Sides, m.Kind == MarkKind.Star)); break;
                case MarkKind.SpeechBubble:
                    var bubble = new StreamGeometry(); using (var g = bubble.Open())
                    {
                        g.BeginFigure(r.TopLeft, true, true); g.LineTo(r.TopRight, true, false); g.LineTo(new Point(r.Right, r.Y + r.Height * .78), true, false);
                        g.LineTo(new Point(r.X + r.Width * .45, r.Y + r.Height * .78), true, false); g.LineTo(new Point(r.X + r.Width * .20, r.Bottom), true, false);
                        g.LineTo(new Point(r.X + r.Width * .25, r.Y + r.Height * .78), true, false); g.LineTo(new Point(r.X, r.Y + r.Height * .78), true, false);
                    }
                    dc.DrawGeometry(fill, pen, bubble); break;
                case MarkKind.Text:
                    var direction = (m.Text ?? "").Any(c => c >= 0x590 && c <= 0x8FF) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                    var face = new Typeface(new FontFamily(m.Font), m.Italic ? FontStyles.Italic : FontStyles.Normal, m.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                    var text = new FormattedText(m.Text ?? "", CultureInfo.CurrentUICulture, direction, face, m.FontSize, pen.Brush) { MaxTextWidth = Math.Max(1, r.Width), TextAlignment = m.Alignment };
                    dc.DrawText(text, r.TopLeft); break;
                case MarkKind.Spotlight:
                    var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
                    mask.Children.Add(new RectangleGeometry(new Rect(0, 0, docWidth, docHeight))); mask.Children.Add(new EllipseGeometry(r));
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, mask); dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case MarkKind.Magnifier:
                    double mag = ShelfLayout.Clamp(m.Magnification, 1, 8);
                    var zoom = new ImageBrush(magnified) { ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Viewbox = new Rect((r.X + r.Width / 2 - r.Width / mag / 2) / docWidth, (r.Y + r.Height / 2 - r.Height / mag / 2) / docHeight, r.Width / mag / docWidth, r.Height / mag / docHeight), Stretch = Stretch.Fill };
                    dc.DrawEllipse(zoom, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2); break;
                case MarkKind.Image: if (m.Image != null) dc.DrawImage(m.Image, r); break;
            }
        }
        static void ArrowHead(DrawingContext dc, Pen pen, Point from, Point to)
        {
            Vector v = from - to; if (v.Length < .01) return; v.Normalize(); Vector n = new Vector(-v.Y, v.X);
            double len = Math.Max(10, pen.Thickness * 4); dc.DrawLine(pen, to, to + v * len + n * len * .45); dc.DrawLine(pen, to, to + v * len - n * len * .45);
        }
    }
}
