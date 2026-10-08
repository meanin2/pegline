using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pegline
{
    internal sealed class EditorSurface : FrameworkElement
    {
        readonly EditorDocument document;
        readonly Func<Mark> defaults;
        readonly Func<string, string> prompt;
        Guid? selectedId;
        Mark drawing, moving;
        Point start;
        Rect initial, crop;
        bool dragging, remembered;
        int corner = -1;
        public double Zoom = 1;
        public MarkKind Tool = MarkKind.Select;
        public bool ShowOriginal;
        public bool HasGesture { get { return dragging; } }
        public event Action<Point> PointerMoved;
        public event Action SelectionChanged;
        public Mark Selected { get { return selectedId.HasValue ? document.Layers.FirstOrDefault(m => m.Id == selectedId.Value) : null; } }
        public EditorSurface(EditorDocument document, Func<Mark> defaults, Func<string, string> prompt)
        {
            this.document = document; this.defaults = defaults; this.prompt = prompt;
            Width = document.Width; Height = document.Height; Focusable = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            System.Windows.Automation.AutomationProperties.SetName(this, Ui.L("Image editing canvas. Tools and annotation list are available by keyboard.", "Lienzo de edición. Herramientas y lista de anotaciones disponibles con teclado."));
            document.Changed += delegate { Width = document.Width; Height = document.Height; InvalidateVisual(); };
        }
        public void Select(Mark mark)
        { selectedId = mark == null ? (Guid?)null : mark.Id; InvalidateVisual(); if (SelectionChanged != null) SelectionChanged(); }
        public void NudgeSelected(double x, double y)
        {
            var mark = Selected; if (mark == null) return;
            document.Remember(); var r = mark.Bounds; r.Offset(x, y); mark.Bounds = r; document.Touch();
        }
        public void DeleteSelected()
        {
            Mark mark = Selected; if (mark == null) return;
            document.Remember(); document.Layers.Remove(mark); Select(null); document.Touch();
        }
        public bool CancelGesture()
        {
            if (!dragging) return false;
            dragging = false; if (remembered) document.CancelEdit(); drawing = null; moving = null; crop = Rect.Empty; remembered = false;
            ReleaseMouseCapture(); InvalidateVisual(); return true;
        }
        Point Limited(Point p) { return new Point(ShelfLayout.Clamp(p.X, 0, document.Width), ShelfLayout.Clamp(p.Y, 0, document.Height)); }
        int CornerAt(Point p)
        {
            Mark mark = Selected; if (mark == null) return -1;
            Point[] corners = new Point[] { mark.Bounds.TopLeft, mark.Bounds.TopRight, mark.Bounds.BottomRight, mark.Bounds.BottomLeft };
            for (int i = 0; i < corners.Length; i++) if ((corners[i] - p).Length < 9 / Zoom) return i;
            return -1;
        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || ShowOriginal) return;
            Focus(); start = Limited(e.GetPosition(this)); crop = Rect.Empty; remembered = false;
            if (Tool == MarkKind.Text)
            {
                string text = prompt(""); if (string.IsNullOrWhiteSpace(text)) return;
                Mark m = defaults().NewCopy(); m.Kind = MarkKind.Text; m.Text = text; m.Bounds = new Rect(start.X, start.Y, Math.Min(300, Math.Max(30, document.Width - start.X)), 100);
                document.Remember(); document.Layers.Add(m); Select(m); document.Touch(); e.Handled = true; return;
            }
            if (Tool == MarkKind.Select)
            {
                corner = CornerAt(start); if (corner < 0) Select(document.Hit(start, 6 / Zoom));
                moving = Selected;
                if (moving != null && moving.Kind == MarkKind.Text && e.ClickCount == 2)
                {
                    string text = prompt(moving.Text); if (text != null) { document.Remember(); moving.Text = text; document.Touch(); }
                    e.Handled = true; return;
                }
                if (moving != null) initial = moving.Bounds;
            }
            else if (Tool == MarkKind.Eraser)
            {
                var hit = document.Hit(start, 8 / Zoom); if (hit != null) { document.BeginEdit(); remembered = true; document.Layers.Remove(hit); Select(null); document.Touch(); }
            }
            else if (Tool != MarkKind.Crop)
            {
                drawing = defaults().NewCopy(); drawing.Kind = Tool; drawing.Bounds = new Rect(start, new Size(1, 1));
                drawing.Normalized = false; drawing.Points.Add(new InkPoint(start.X, start.Y, Pressure(e), true));
                document.BeginEdit(); remembered = true; document.Layers.Add(drawing); Select(drawing); document.Touch();
            }
            dragging = true; CaptureMouse(); InvalidateVisual(); e.Handled = true;
        }
        static float Pressure(MouseEventArgs e)
        {
            try { if (e.StylusDevice != null) { var points = e.StylusDevice.GetStylusPoints(null); if (points.Count > 0) return points[points.Count - 1].PressureFactor; } } catch { }
            return 1;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point p = Limited(e.GetPosition(this));
            if (PointerMoved != null) PointerMoved(p);
            if (!dragging) { Cursor = Tool == MarkKind.Select ? ((CornerAt(p) == 0 || CornerAt(p) == 2) ? Cursors.SizeNWSE : CornerAt(p) >= 0 ? Cursors.SizeNESW : Cursors.Arrow) : Cursors.Cross; return; }
            if (Tool == MarkKind.Select && moving != null)
            {
                if ((p - start).Length < .1 && !remembered) return;
                if (!remembered) { document.BeginEdit(); remembered = true; }
                if (corner >= 0)
                {
                    Point opposite = corner == 0 ? initial.BottomRight : corner == 1 ? initial.BottomLeft : corner == 2 ? initial.TopLeft : initial.TopRight;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        double ratio = initial.Width / Math.Max(1, initial.Height), h = Math.Abs(p.Y - opposite.Y);
                        p = new Point(opposite.X + (p.X < opposite.X ? -h : h) * ratio, p.Y);
                    }
                    Rect box = EditorMath.Between(opposite, p); box.Width = Math.Max(2, box.Width); box.Height = Math.Max(2, box.Height); moving.Bounds = box;
                }
                else moving.Bounds = new Rect(initial.X + p.X - start.X, initial.Y + p.Y - start.Y, initial.Width, initial.Height);
                document.Touch();
            }
            else if (Tool == MarkKind.Crop) { crop = EditorMath.Between(start, p); InvalidateVisual(); }
            else if (Tool == MarkKind.Eraser)
            {
                var hit = document.Hit(p, 8 / Zoom);
                if (hit != null) { if (!remembered) { document.BeginEdit(); remembered = true; } document.Layers.Remove(hit); document.Touch(); }
            }
            else if (drawing != null)
            {
                if (Tool == MarkKind.Pen || Tool == MarkKind.Sketch || Tool == MarkKind.Highlighter)
                {
                    var last = drawing.Points[drawing.Points.Count - 1];
                    if (Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) > .4 / Zoom)
                        drawing.Points.Add(new InkPoint(p.X, p.Y, Pressure(e), false));
                    double minX = drawing.Points.Min(q => q.X), minY = drawing.Points.Min(q => q.Y);
                    drawing.Bounds = new Rect(minX, minY, Math.Max(1, drawing.Points.Max(q => q.X) - minX), Math.Max(1, drawing.Points.Max(q => q.Y) - minY));
                }
                else
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    { p = EditorMath.Constrain(start, p, Tool == MarkKind.Line || Tool == MarkKind.Arrow || Tool == MarkKind.DoubleArrow); }
                    Rect b = EditorMath.Between(start, p); b.Width = Math.Max(1, b.Width); b.Height = Math.Max(1, b.Height); drawing.Bounds = b;
                    drawing.Points.Clear(); drawing.Points.Add(new InkPoint(start.X, start.Y, 1, true)); drawing.Points.Add(new InkPoint(p.X, p.Y, 1, false));
                }
                document.Touch();
            }
            e.Handled = true;
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (!dragging || e.ChangedButton != MouseButton.Left) return;
            dragging = false; ReleaseMouseCapture();
            if (Tool == MarkKind.Crop && !crop.IsEmpty && crop.Width >= 2 && crop.Height >= 2) { document.Crop(crop); Select(null); }
            if (drawing != null)
            {
                drawing.NormalizeInk(); if (drawing.Kind == MarkKind.Sketch) EditorMath.Recognize(drawing);
                document.Touch(); if (SelectionChanged != null) SelectionChanged();
            }
            document.CommitEdit(); drawing = null; moving = null; crop = Rect.Empty; remembered = false; InvalidateVisual(); e.Handled = true;
        }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (dragging)
            {
                // A lost capture (Alt+Tab, dialogs, device interruption) cancels rather
                // than committing an incomplete shape or leaving redo polluted.
                dragging = false; if (remembered) document.CancelEdit();
                drawing = null; moving = null; crop = Rect.Empty; remembered = false; InvalidateVisual();
            }
            base.OnLostMouseCapture(e);
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, document.Width, document.Height));
            // Neutral checkerboard indicates real transparency, not image pixels.
            var checker = new DrawingGroup(); using (var g = checker.Open())
            { g.DrawRectangle(Brushes.White, null, new Rect(0, 0, 16, 16)); g.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, 8, 8)); g.DrawRectangle(Brushes.LightGray, null, new Rect(8, 8, 8, 8)); }
            var brush = new DrawingBrush(checker) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16) };
            dc.DrawRectangle(brush, null, new Rect(0, 0, document.Width, document.Height));
            if (ShowOriginal) { dc.DrawImage(document.BaseImage, new Rect(0, 0, document.Width, document.Height)); return; }
            document.Draw(dc, false);
            var pen = new Pen(Ui.Accent, 1.3 / Zoom);
            Mark selected = Selected;
            if (selected != null)
            {
                dc.DrawRectangle(null, pen, selected.Bounds); double size = 7 / Zoom;
                foreach (Point p in new Point[] { selected.Bounds.TopLeft, selected.Bounds.TopRight, selected.Bounds.BottomLeft, selected.Bounds.BottomRight })
                    dc.DrawRectangle(Brushes.White, pen, new Rect(p.X - size / 2, p.Y - size / 2, size, size));
            }
            if (dragging && Tool == MarkKind.Crop && !crop.IsEmpty)
            {
                var mask = new GeometryGroup { FillRule = FillRule.EvenOdd }; mask.Children.Add(new RectangleGeometry(new Rect(0, 0, document.Width, document.Height))); mask.Children.Add(new RectangleGeometry(crop));
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(135, 0, 0, 0)), null, mask); dc.DrawRectangle(null, pen, crop);
                var guide = new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), 1 / Zoom);
                for (int n = 1; n < 3; n++)
                {
                    dc.DrawLine(guide, new Point(crop.X + crop.Width * n / 3, crop.Y), new Point(crop.X + crop.Width * n / 3, crop.Bottom));
                    dc.DrawLine(guide, new Point(crop.X, crop.Y + crop.Height * n / 3), new Point(crop.Right, crop.Y + crop.Height * n / 3));
                }
            }
        }
    }
}
