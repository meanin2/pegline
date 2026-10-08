using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Pegline
{
    internal sealed class ToolChoice
    {
        public MarkKind Kind; public string Label;
        public override string ToString() { return Label; }
    }
    internal sealed class LayerChoice
    {
        public Mark Mark; public string Label;
        public override string ToString() { return Label; }
    }
    internal sealed class EditorWindow : Window
    {
        readonly EditorDocument document;
        readonly EditorSurface surface;
        readonly Store store;
        readonly Mark defaults = new Mark();
        readonly ScrollViewer scroll;
        readonly ListBox layers;
        readonly ComboBox tools, font, alignment;
        readonly TextBox fontSize;
        readonly CheckBox fill, bold, italic, dashed;
        readonly Slider thickness, magnification;
        readonly Button strokeColor, fillColor, undo, redo;
        readonly TextBlock status;
        readonly Dictionary<MarkKind, Button> quickTools = new Dictionary<MarkKind, Button>();
        Button originalButton;
        bool closed;
        string notice = "";
        public int DocumentWidth { get { return document.Width; } }
        public int DocumentHeight { get { return document.Height; } }
        string path, loadedHash, layerIds = "";
        bool updating, finishing, panning;
        Point panStart;
        double panH, panV;
        double zoom = 1;
        public event Action<string, string> Saved;
        public Func<string, bool> CanSaveTarget;
        public EditorWindow(string path, Store store)
        {
            this.path = path; this.store = store;
            byte[] original = Images.ReadBytes(path); loadedHash = SafeFiles.Sha256(original);
            document = new EditorDocument(Images.Decode(original, 0));
            Title = Path.GetFileName(path) + " — " + Ui.L("Pegline Markup", "Marcación de Pegline");
            Width = Math.Min(1180, SystemParameters.WorkArea.Width - 40); Height = Math.Min(820, SystemParameters.WorkArea.Height - 40);
            MinWidth = 720; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Theme(this);
            var root = new DockPanel(); Content = root;
            var heading = new DockPanel { Margin = new Thickness(15, 10, 15, 0) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var done = Ui.PrimaryButton(Ui.L("Done", "Listo"), delegate { if (!document.Dirty || SaveImage(false)) { finishing = true; Close(); } });
            DockPanel.SetDock(done, Dock.Right); heading.Children.Add(done);
            heading.Children.Add(Ui.Heading(Ui.L("Make your point", "Exprésate con claridad")));
            var toolbar = new WrapPanel { Margin = new Thickness(9) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
            undo = Ui.Button(Ui.L("Undo", "Deshacer"), document.Undo); redo = Ui.Button(Ui.L("Redo", "Rehacer"), document.Redo); toolbar.Children.Add(undo); toolbar.Children.Add(redo);
            toolbar.Children.Add(Ui.Button(Ui.L("Rotate left", "Girar izquierda"), delegate { document.Rotate(-90); Fit(); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Rotate right", "Girar derecha"), delegate { document.Rotate(90); Fit(); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Flip H", "Voltear H"), delegate { document.Flip(true); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Flip V", "Voltear V"), delegate { document.Flip(false); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Fit", "Ajustar"), Fit));
            toolbar.Children.Add(Ui.Button("−", delegate { SetZoom(zoom / 1.25); })); toolbar.Children.Add(Ui.Button("+", delegate { SetZoom(zoom * 1.25); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Save as…", "Guardar como…"), delegate { SaveImage(true); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Save", "Guardar"), delegate { SaveImage(false); }));
            toolbar.Children.Add(Ui.Button(Ui.L("Copy result", "Copiar resultado"), CopyResult));
            originalButton = Ui.Button(Ui.L("Base image", "Imagen base"), delegate { SetOriginal(!surface.ShowOriginal); });
            originalButton.ToolTip = Ui.L("Hide annotations without saving. The current crop/rotation is retained. Hold O for a temporary preview.", "Oculta anotaciones sin guardar; conserva el recorte/giro actual. Mantén O para una vista temporal."); toolbar.Children.Add(originalButton);
            var quick = new WrapPanel { Margin = new Thickness(10, 0, 10, 8) }; DockPanel.SetDock(quick, Dock.Top); root.Children.Add(quick);
            AddQuick(quick, MarkKind.Select, "V", "Select", "Seleccionar"); AddQuick(quick, MarkKind.Pen, "P", "Pen", "Pluma");
            AddQuick(quick, MarkKind.Highlighter, "H", "Highlight", "Resaltar"); AddQuick(quick, MarkKind.Rectangle, "R", "Box", "Cuadro");
            AddQuick(quick, MarkKind.Ellipse, "E", "Ellipse", "Elipse"); AddQuick(quick, MarkKind.Arrow, "A", "Arrow", "Flecha");
            AddQuick(quick, MarkKind.Text, "T", "Text", "Texto"); AddQuick(quick, MarkKind.Crop, "C", "Crop", "Recortar");
            AddQuick(quick, MarkKind.Redact, "X", "Redact", "Ocultar");
            status = new TextBlock { Margin = new Thickness(12, 7, 12, 7), TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
            var side = new StackPanel { Margin = new Thickness(12) };
            var sideScroll = new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Width = 244 };
            DockPanel.SetDock(sideScroll, Dock.Right); root.Children.Add(sideScroll);
            side.Children.Add(Ui.Label(Ui.L("Tool", "Herramienta")));
            tools = new ComboBox { Margin = new Thickness(3), Padding = new Thickness(5), ItemsSource = ToolChoices(), SelectedIndex = 0 }; side.Children.Add(tools);
            side.Children.Add(Ui.Button(Ui.L("Signature…", "Firma…"), SignatureMenu));
            side.Children.Add(Ui.Button(Ui.L("Insert image…", "Insertar imagen…"), InsertImage));
            var colors = new StackPanel { Orientation = Orientation.Horizontal }; side.Children.Add(colors);
            strokeColor = Ui.Button(Ui.L("Stroke", "Trazo"), delegate { PickColor(false); }); fillColor = Ui.Button(Ui.L("Fill", "Relleno"), delegate { PickColor(true); });
            colors.Children.Add(strokeColor); colors.Children.Add(fillColor);
            fill = new CheckBox { Content = Ui.L("Fill shape", "Rellenar figura"), Margin = new Thickness(4) }; side.Children.Add(fill);
            dashed = new CheckBox { Content = Ui.L("Dashed outline", "Contorno discontinuo"), Margin = new Thickness(4) }; side.Children.Add(dashed);
            side.Children.Add(Ui.Label(Ui.L("Stroke width", "Grosor de trazo")));
            thickness = new Slider { Minimum = 1, Maximum = 24, Value = 3, TickFrequency = 1, Margin = new Thickness(4), IsSnapToTickEnabled = true }; side.Children.Add(thickness);
            side.Children.Add(Ui.Label(Ui.L("Text font", "Fuente de texto")));
            font = new ComboBox { ItemsSource = new string[] { "Segoe UI", "Arial", "Calibri", "Consolas", "Times New Roman" }, SelectedIndex = 0, Margin = new Thickness(3), IsEditable = true }; side.Children.Add(font);
            fontSize = new TextBox { Text = "24", Margin = new Thickness(3), Padding = new Thickness(5) }; side.Children.Add(fontSize);
            var styles = new StackPanel { Orientation = Orientation.Horizontal }; side.Children.Add(styles);
            bold = new CheckBox { Content = Ui.L("Bold", "Negrita"), Margin = new Thickness(4) }; italic = new CheckBox { Content = Ui.L("Italic", "Cursiva"), Margin = new Thickness(4) }; styles.Children.Add(bold); styles.Children.Add(italic);
            alignment = new ComboBox { ItemsSource = new string[] { Ui.L("Left", "Izquierda"), Ui.L("Center", "Centro"), Ui.L("Right", "Derecha") }, SelectedIndex = 0, Margin = new Thickness(3) }; side.Children.Add(alignment);
            side.Children.Add(Ui.Button(Ui.L("Edit selected text…", "Editar texto seleccionado…"), EditText));
            side.Children.Add(Ui.Label(Ui.L("Magnifier zoom", "Ampliación de lupa")));
            magnification = new Slider { Minimum = 1.25, Maximum = 8, Value = 2, Margin = new Thickness(4) }; side.Children.Add(magnification);
            side.Children.Add(Ui.Label(Ui.L("Annotations", "Anotaciones")));
            layers = new ListBox { Height = 120, Margin = new Thickness(3) }; side.Children.Add(layers);
            var layerButtons = new WrapPanel(); side.Children.Add(layerButtons);
            layerButtons.Children.Add(Ui.Button(Ui.L("Delete", "Eliminar"), delegate { surface.DeleteSelected(); }));
            layerButtons.Children.Add(Ui.Button(Ui.L("Duplicate", "Duplicar"), Duplicate));
            layerButtons.Children.Add(Ui.Button(Ui.L("To front", "Al frente"), delegate { Reorder(true); }));
            layerButtons.Children.Add(Ui.Button(Ui.L("To back", "Al fondo"), delegate { Reorder(false); }));
            side.Children.Add(Ui.Label(Ui.L("Drag corner handles to resize. Shift keeps proportions. Double-click text to edit. Space + drag pans. Ctrl + wheel zooms. Crop is applied when you release; Undo restores it.", "Arrastra las esquinas para cambiar tamaño. Mayús conserva proporciones. Doble clic edita texto. Espacio + arrastrar desplaza. Ctrl + rueda amplía. Recortar se aplica al soltar; Deshacer lo restaura.")));
            surface = new EditorSurface(document, delegate { return defaults.NewCopy(); }, delegate(string initial) { return Ui.TextPrompt(this, Ui.L("Edit text", "Editar texto"), initial); });
            scroll = new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(83, 87, 93)), Padding = new Thickness(16) };
            root.Children.Add(scroll);
            tools.SelectionChanged += delegate
            {
                if (surface == null) return; var t = tools.SelectedItem as ToolChoice; if (t == null) return;
                surface.CancelGesture(); surface.Tool = t.Kind; SetOriginal(false);
                if (t.Kind != MarkKind.Select) surface.Select(null);
                if (t.Kind == MarkKind.Highlighter) defaults.Thickness = 18;
                else if (t.Kind == MarkKind.Pen || t.Kind == MarkKind.Sketch) defaults.Thickness = 3;
                if (t.Kind == MarkKind.Redact) defaults.Stroke = Colors.Black;
                foreach (var pair in quickTools)
                { pair.Value.Background = pair.Key == t.Kind ? Ui.Accent : Ui.Field; pair.Value.Foreground = pair.Key == t.Kind ? Brushes.White : Ui.Text; }
                SelectionChanged(); Refresh();
            };
            surface.SelectionChanged += SelectionChanged;
            layers.SelectionChanged += delegate { if (!updating) { var item = layers.SelectedItem as LayerChoice; surface.Select(item == null ? null : item.Mark); } };
            thickness.ValueChanged += delegate { ApplyStyle(); }; magnification.ValueChanged += delegate { ApplyStyle(); };
            foreach (var check in new CheckBox[] { fill, dashed, bold, italic }) { check.Checked += delegate { ApplyStyle(); }; check.Unchecked += delegate { ApplyStyle(); }; }
            font.SelectionChanged += delegate { ApplyStyle(); }; font.LostKeyboardFocus += delegate { ApplyStyle(); }; fontSize.LostKeyboardFocus += delegate { ApplyStyle(); }; alignment.SelectionChanged += delegate { ApplyStyle(); };
            document.Changed += Refresh;
            PreviewKeyDown += KeyPressed;
            PreviewKeyUp += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.O && surface.ShowOriginal) { SetOriginal(false); e.Handled = true; } };
            Deactivated += delegate { surface.CancelGesture(); if (document.IsEditing) document.CancelEdit(); if (surface.ShowOriginal) SetOriginal(false); };
            scroll.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            { if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { SetZoom(e.Delta > 0 ? zoom * 1.2 : zoom / 1.2, e.GetPosition(scroll)); e.Handled = true; } };
            scroll.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
                { panning = true; panStart = e.GetPosition(scroll); panH = scroll.HorizontalOffset; panV = scroll.VerticalOffset; scroll.CaptureMouse(); e.Handled = true; }
            };
            scroll.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            { if (panning) { Vector delta = e.GetPosition(scroll) - panStart; scroll.ScrollToHorizontalOffset(panH - delta.X); scroll.ScrollToVerticalOffset(panV - delta.Y); e.Handled = true; } };
            scroll.LostMouseCapture += delegate { panning = false; };
            scroll.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs e) { if (panning) { panning = false; scroll.ReleaseMouseCapture(); e.Handled = true; } };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                surface.CancelGesture();
                if (!finishing && document.Dirty)
                {
                    var result = MessageBox.Show(this, Ui.L("Save your changes before closing?", "¿Guardar los cambios antes de cerrar?"), "Pegline", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    e.Cancel = result == MessageBoxResult.Cancel || (result == MessageBoxResult.Yes && !SaveImage(false));
                }
            };
            Closed += delegate { closed = true; };
            foreach (var slider in new Slider[] { thickness, magnification })
            {
                slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate { if (surface.Selected != null) document.BeginEdit(); }));
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate(object sender, DragCompletedEventArgs e)
                { if (e.Canceled) document.CancelEdit(); else document.CommitEdit(); }));
            }
            foreach (var element in new FrameworkElement[] { tools, font, fontSize, alignment, thickness, magnification, layers })
                element.ToolTip = element == layers ? Ui.L("Select an annotation. Delete removes it; arrow keys on the canvas nudge it.", "Selecciona una anotación. Supr la quita; las flechas en el lienzo la mueven.") : null;
            Ui.Name(tools, Ui.L("Annotation tool", "Herramienta de anotación")); Ui.Name(font, Ui.L("Text font", "Fuente"));
            Ui.Name(fontSize, Ui.L("Font size", "Tamaño de letra")); Ui.Name(alignment, Ui.L("Text alignment", "Alineación"));
            Ui.Name(thickness, Ui.L("Stroke width", "Grosor de trazo")); Ui.Name(magnification, Ui.L("Magnifier zoom", "Ampliación de lupa")); Ui.Name(layers, Ui.L("Annotation layers", "Capas de anotación"));
            Loaded += delegate { Fit(); Refresh(); SelectionChanged(); surface.Focus(); };
        }
        static List<ToolChoice> ToolChoices()
        {
            return new List<ToolChoice>
            {
                Choice(MarkKind.Select,"Select / move","Seleccionar / mover"), Choice(MarkKind.Pen,"Pen","Pluma"), Choice(MarkKind.Sketch,"Sketch (recognize shapes)","Boceto (reconocer figuras)"),
                Choice(MarkKind.Highlighter,"Highlighter","Resaltador"), Choice(MarkKind.Eraser,"Erase annotation","Borrar anotación"), Choice(MarkKind.Rectangle,"Rectangle","Rectángulo"),
                Choice(MarkKind.Ellipse,"Ellipse","Elipse"), Choice(MarkKind.Line,"Line","Línea"), Choice(MarkKind.Arrow,"Arrow","Flecha"), Choice(MarkKind.DoubleArrow,"Double arrow","Flecha doble"),
                Choice(MarkKind.Polygon,"Hexagon","Hexágono"), Choice(MarkKind.Star,"Star","Estrella"), Choice(MarkKind.SpeechBubble,"Speech bubble","Bocadillo"), Choice(MarkKind.Text,"Text","Texto"),
                Choice(MarkKind.Spotlight,"Spotlight","Foco"), Choice(MarkKind.Magnifier,"Magnifier","Lupa"), Choice(MarkKind.Crop,"Crop","Recortar"), Choice(MarkKind.Redact,"Solid redaction","Censura opaca")
            };
        }
        static ToolChoice Choice(MarkKind kind, string en, string es) { return new ToolChoice { Kind = kind, Label = Ui.L(en, es) }; }
        string Label(Mark m)
        {
            var choice = ToolChoices().FirstOrDefault(t => t.Kind == m.Kind);
            string label = choice == null ? (m.Kind == MarkKind.Signature ? Ui.L("Signature", "Firma") : Ui.L("Image", "Imagen")) : choice.Label;
            return m.Kind == MarkKind.Text ? label + ": " + (m.Text.Length > 24 ? m.Text.Substring(0, 24) + "…" : m.Text) : label;
        }
        void Refresh()
        {
            undo.IsEnabled = document.CanUndo; redo.IsEnabled = document.CanRedo;
            Title = (document.Dirty ? "● " : "") + Path.GetFileName(path) + " — Pegline Markup";
            string ids = string.Join("|", document.Layers.Select(m => m.Id.ToString() + ":" + m.Kind.ToString() + ":" + m.Text));
            if (ids != layerIds || layers.Items.Cast<LayerChoice>().Any(i => !document.Layers.Contains(i.Mark)))
            {
                updating = true; layers.Items.Clear(); foreach (var mark in document.Layers.AsEnumerable().Reverse()) layers.Items.Add(new LayerChoice { Mark = mark, Label = Label(mark) }); updating = false; layerIds = ids; SelectionChanged();
            }
            status.Text = document.Width + " × " + document.Height + " px   ·   " + Math.Round(zoom * 100) + "%   ·   " + (document.Dirty ? Ui.L("Unsaved changes", "Cambios sin guardar") : Ui.L("Saved", "Guardado"))
                + (surface.ShowOriginal ? Ui.L("   ·   Base image preview", "   ·   Vista de imagen base") : "") + (notice.Length > 0 ? "   ·   " + notice : "");
            if (surface.Tool == MarkKind.Redact || document.Layers.Any(m => m.Kind == MarkKind.Redact))
                status.Text += Ui.L("\nRedaction is flattened in the output. Local backups still contain the original pixels.", "\nLa ocultación se aplica a la imagen exportada. Las copias locales aún contienen los píxeles originales.");
        }
        void SelectionChanged()
        {
            updating = true;
            try
            {
                Mark m = surface.Selected ?? defaults;
                thickness.Value = m.Thickness; fill.IsChecked = m.Filled; dashed.IsChecked = m.Dashed; bold.IsChecked = m.Bold; italic.IsChecked = m.Italic;
                font.Text = m.Font; fontSize.Text = m.FontSize.ToString(CultureInfo.InvariantCulture); magnification.Value = m.Magnification;
                alignment.SelectedIndex = m.Alignment == TextAlignment.Center ? 1 : m.Alignment == TextAlignment.Right ? 2 : 0;
                strokeColor.Background = new SolidColorBrush(m.Stroke); fillColor.Background = new SolidColorBrush(m.Fill);
                layers.SelectedItem = layers.Items.Cast<LayerChoice>().FirstOrDefault(i => surface.Selected != null && i.Mark.Id == surface.Selected.Id);
            }
            finally { updating = false; }
        }
        void ApplyStyle()
        {
            if (updating || surface == null || fontSize == null) return;
            double size; if (!double.TryParse(fontSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out size) || !ShelfLayout.Finite(size) || size < 6 || size > 300) return;
            Mark target = surface.Selected ?? defaults;
            var align = alignment.SelectedIndex == 1 ? TextAlignment.Center : alignment.SelectedIndex == 2 ? TextAlignment.Right : TextAlignment.Left;
            string family = string.IsNullOrWhiteSpace(font.Text) ? "Segoe UI" : font.Text;
            if (target.Thickness == thickness.Value && target.Filled == (fill.IsChecked == true) && target.Dashed == (dashed.IsChecked == true) && target.Bold == (bold.IsChecked == true) && target.Italic == (italic.IsChecked == true) && target.Font == family && target.FontSize == size && target.Magnification == magnification.Value && target.Alignment == align) return;
            if (surface.Selected != null && !document.IsEditing) document.Remember();
            target.Thickness = thickness.Value; target.Filled = fill.IsChecked == true; target.Dashed = dashed.IsChecked == true; target.Bold = bold.IsChecked == true; target.Italic = italic.IsChecked == true;
            target.Font = family; target.FontSize = size; target.Magnification = magnification.Value; target.Alignment = align;
            if (surface.Selected != null) document.Touch();
        }
        void PickColor(bool isFill)
        {
            Mark m = surface.Selected ?? defaults; Color before = isFill ? m.Fill : m.Stroke;
            using (var dialog = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(before.R, before.G, before.B) })
            {
                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
                if (surface.Selected != null) document.Remember();
                Color c = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B); if (isFill) m.Fill = c; else m.Stroke = c;
                if (surface.Selected != null) document.Touch(); SelectionChanged();
            }
        }
        void EditText()
        {
            var selected = surface.Selected; if (selected == null || selected.Kind != MarkKind.Text) return;
            string text = Ui.TextPrompt(this, Ui.L("Edit text", "Editar texto"), selected.Text);
            if (text != null) { document.Remember(); selected.Text = text; document.Touch(); }
        }
        void Duplicate()
        {
            var selected = surface.Selected; if (selected == null) return; var copy = selected.NewCopy(); copy.Bounds.Offset(12, 12);
            document.Remember(); document.Layers.Add(copy); document.Touch(); surface.Select(copy);
        }
        void Reorder(bool front)
        {
            var selected = surface.Selected; if (selected == null) return; document.Remember(); document.Layers.Remove(selected);
            if (front) document.Layers.Add(selected); else document.Layers.Insert(0, selected); layerIds = ""; document.Touch();
        }
        void InsertImage()
        {
            var dialog = new OpenFileDialog { Filter = Ui.L("Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic", "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic") };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var image = Images.Load(dialog.FileName, 0); var m = defaults.NewCopy(); m.Kind = MarkKind.Image; m.Image = image;
                double width = Math.Min(document.Width * .5, image.PixelWidth), height = width * image.PixelHeight / image.PixelWidth;
                m.Bounds = new Rect((document.Width - width) / 2, (document.Height - height) / 2, width, height); Insert(m);
            }
            catch (Exception e) { Ui.Error(e.Message); }
        }
        void Insert(Mark m) { document.Remember(); document.Layers.Add(m); document.Touch(); surface.Select(m); tools.SelectedIndex = 0; }
        void SignatureMenu()
        {
            string file = Path.Combine(store.Root, "signatures.json"); var saved = SafeFiles.ReadJson(file, new List<SavedSignature>()) ?? new List<SavedSignature>();
            var menu = new ContextMenu(); var draw = new MenuItem { Header = Ui.L("Draw a signature…", "Dibujar una firma…") }; menu.Items.Add(draw);
            draw.Click += delegate
            {
                var dialog = new SignatureWindow { Owner = this };
                if (dialog.ShowDialog() == true && dialog.Signature != null)
                {
                    StampSignature(dialog.Signature);
                    if (dialog.RememberSignature)
                    {
                        dialog.Signature.Name = Ui.L("Signature ", "Firma ") + (saved.Count + 1); saved.Add(dialog.Signature);
                        try { SafeFiles.WriteJson(file, saved); } catch (Exception e) { Ui.Error(e.Message); }
                    }
                }
            };
            foreach (var signature in saved.Where(x => x != null && x.Points != null && x.Points.Count > 0 && x.Points.Count <= 50000 && ShelfLayout.Finite(x.Aspect) && x.Aspect > 0 && x.Points.All(p => ShelfLayout.Finite(p.X) && ShelfLayout.Finite(p.Y) && ShelfLayout.Finite(p.Pressure))))
            {
                SavedSignature item = signature;
                var entry = new MenuItem { Header = item.Name };
                var use = new MenuItem { Header = Ui.L("Use", "Usar") }; use.Click += delegate { StampSignature(item); }; entry.Items.Add(use);
                var remove = new MenuItem { Header = Ui.L("Forget this signature", "Olvidar esta firma") };
                remove.Click += delegate { saved.Remove(item); try { SafeFiles.WriteJson(file, saved); } catch (Exception e) { Ui.Error(e.Message); } }; entry.Items.Add(remove); menu.Items.Add(entry);
            }
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; menu.IsOpen = true;
        }
        void StampSignature(SavedSignature signature)
        {
            var m = defaults.NewCopy(); m.Kind = MarkKind.Signature; m.Points = new List<InkPoint>(signature.Points); m.Normalized = true;
            double width = Math.Min(320, document.Width * .55), height = width / Math.Max(.1, signature.Aspect);
            m.Bounds = new Rect((document.Width - width) / 2, (document.Height - height) / 2, width, height); Insert(m);
        }
        public bool SaveImage(bool saveAs)
        {
            surface.CancelGesture();
            if (!saveAs && !document.Dirty) return true;
            string target = path;
            try
            {
                if (!saveAs && File.Exists(path) && SafeFiles.FileHash(path) != loadedHash)
                { Ui.Error(Ui.L("The original changed in another app. Save a separate image to preserve both versions.", "El original cambió en otra app. Guarda una imagen separada para conservar ambas versiones.")); saveAs = true; }
                if (!Images.CanOverwrite(target) || !File.Exists(target)) saveAs = true;
                string expectedHash = loadedHash;
                if (saveAs)
                {
                    var dialog = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|TIFF|*.tiff|Bitmap|*.bmp", FileName = Path.GetFileNameWithoutExtension(path) + "-edited.png",
                        AddExtension = true, DefaultExt = ".png", OverwritePrompt = true };
                    if (dialog.ShowDialog(this) != true) return false; target = dialog.FileName;
                    expectedHash = File.Exists(target) ? SafeFiles.FileHash(target) : null;
                    if (SafeFiles.Same(path, target) && expectedHash != loadedHash)
                    { Ui.Error(Ui.L("Choose a different filename. The original changed since this editor opened.", "Elige otro nombre. El original cambió desde que se abrió el editor.")); return false; }
                }
                if (CanSaveTarget != null && !CanSaveTarget(target))
                { Ui.Error(Ui.L("That image is already open in another Pegline editor. Choose a different filename or close that editor first.", "Esa imagen ya está abierta en otro editor de Pegline. Elige otro nombre o cierra ese editor primero.")); return false; }
                var bytes = Images.Encode(document.Render(0, false), Path.GetExtension(target));
                SafeFiles.WriteChecked(target, bytes, expectedHash, delegate(string file) { store.Backup(file); });
                string old = path; path = target; loadedHash = SafeFiles.Sha256(bytes); document.MarkSaved();
                notice = Ui.L("Saved locally", "Guardado localmente"); Refresh();
                if (Saved != null) Saved(old, path); return true;
            }
            catch (Exception e) { Ui.Error(Ui.L("Could not save. The original was not deliberately removed.\n\n", "No se pudo guardar. El original no se ha eliminado intencionadamente.\n\n") + e.Message); return false; }
        }
        async void CopyResult()
        {
            surface.CancelGesture();
            try
            {
                var image = document.Render(0, false);
                for (int n = 0; n < 6 && !closed; n++)
                {
                    try
                    {
                        // No file-drop reference to the unedited original: consumers receive the edited pixels only.
                        Images.PutClipboard(image, null); notice = Ui.L("Edited image copied", "Imagen editada copiada"); Refresh(); return;
                    }
                    catch (COMException) { if (n == 5) throw; }
                    await Task.Delay(70);
                }
            }
            catch (Exception e) { Ui.Error(e.Message); }
        }
        void AddQuick(Panel panel, MarkKind kind, string shortcut, string en, string es)
        {
            var b = Ui.Button(Ui.L(en, es) + "  " + shortcut, delegate { ChooseTool(kind); });
            b.ToolTip = Ui.L(en, es) + " (" + shortcut + ")"; panel.Children.Add(b); quickTools[kind] = b;
        }
        void ChooseTool(MarkKind kind)
        {
            tools.SelectedItem = tools.Items.Cast<ToolChoice>().FirstOrDefault(t => t.Kind == kind);
            surface.Focus();
        }
        void SetOriginal(bool value)
        {
            if (surface == null) return;
            surface.CancelGesture(); surface.ShowOriginal = value; surface.InvalidateVisual();
            originalButton.Background = value ? Ui.Accent : Ui.Field; originalButton.Foreground = value ? Brushes.White : Ui.Text; Refresh();
        }
        void Fit()
        { if (scroll == null) return; SetZoom(Math.Min(1, Math.Min(Math.Max(100, scroll.ActualWidth - 40) / document.Width, Math.Max(100, scroll.ActualHeight - 40) / document.Height))); }
        void SetZoom(double value) { SetZoom(value, new Point(scroll.ViewportWidth / 2, scroll.ViewportHeight / 2)); }
        void SetZoom(double value, Point anchor)
        {
            if (surface == null) return;
            double old = zoom, next = ShelfLayout.Clamp(value, .025, 8);
            double x = (scroll.HorizontalOffset + anchor.X - scroll.Padding.Left) / old;
            double y = (scroll.VerticalOffset + anchor.Y - scroll.Padding.Top) / old;
            zoom = next; surface.Zoom = next; surface.LayoutTransform = new ScaleTransform(next, next);
            scroll.UpdateLayout();
            scroll.ScrollToHorizontalOffset(Math.Max(0, x * next - anchor.X + scroll.Padding.Left));
            scroll.ScrollToVerticalOffset(Math.Max(0, y * next - anchor.Y + scroll.Padding.Top));
            surface.InvalidateVisual(); Refresh();
        }
        void KeyPressed(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl && e.Key == Key.S) { SaveImage(shift); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox) return;
            if (ctrl && e.Key == Key.Z) { if (!surface.CancelGesture()) { if (shift) document.Redo(); else document.Undo(); } e.Handled = true; }
            else if (ctrl && e.Key == Key.Y) { if (!surface.CancelGesture()) document.Redo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D) { Duplicate(); e.Handled = true; }
            else if (ctrl && e.Key == Key.C) { CopyResult(); e.Handled = true; }
            else if (e.Key == Key.Delete) { surface.DeleteSelected(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { Fit(); e.Handled = true; }
            else if (ctrl && e.Key == Key.D1) { SetZoom(1); e.Handled = true; }
            else if (e.Key == Key.Escape) { if (surface.ShowOriginal) SetOriginal(false); else if (!surface.CancelGesture()) { if (surface.Selected != null) surface.Select(null); else Close(); } e.Handled = true; }
            else if (!ctrl && Keyboard.FocusedElement == surface && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                double step = shift ? 10 : 1;
                surface.NudgeSelected(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0); e.Handled = true;
            }
            else if (!ctrl && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                MarkKind? tool = null;
                switch (e.Key)
                {
                    case Key.V: tool = MarkKind.Select; break; case Key.P: tool = MarkKind.Pen; break; case Key.H: tool = MarkKind.Highlighter; break;
                    case Key.R: tool = MarkKind.Rectangle; break; case Key.E: tool = MarkKind.Ellipse; break; case Key.A: tool = MarkKind.Arrow; break;
                    case Key.T: tool = MarkKind.Text; break; case Key.C: tool = MarkKind.Crop; break; case Key.X: tool = MarkKind.Redact; break;
                    case Key.O: if (!e.IsRepeat) SetOriginal(true); e.Handled = true; return;
                }
                if (tool.HasValue) { ChooseTool(tool.Value); e.Handled = true; }
            }
        }
    }
    [DataContract]
    internal sealed class SavedSignature
    {
        [DataMember] public string Name;
        [DataMember] public double Aspect;
        [DataMember] public List<InkPoint> Points;
    }
    internal sealed class SignatureWindow : Window
    {
        public SavedSignature Signature;
        public bool RememberSignature;
        public SignatureWindow()
        {
            Title = Ui.L("Draw your signature", "Dibuja tu firma"); Width = 650; Height = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner; Ui.Theme(this);
            var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
            var hint = Ui.Label(Ui.L("Use your mouse, pen, or touch screen. A saved signature stays on this PC only.", "Usa el ratón, lápiz o pantalla táctil. Una firma guardada permanece solo en este PC.")); DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
            var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var ink = new InkCanvas { Background = Brushes.White, MinHeight = 160, DefaultDrawingAttributes = new System.Windows.Ink.DrawingAttributes { Color = Colors.Black, Width = 2.5, Height = 2.5, FitToCurve = true } }; root.Children.Add(ink);
            var remember = new CheckBox { Content = Ui.L("Remember locally", "Recordar localmente"), Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Center }; buttons.Children.Add(remember);
            buttons.Children.Add(Ui.Button(Ui.L("Clear", "Limpiar"), delegate { ink.Strokes.Clear(); }));
            buttons.Children.Add(Ui.Button(Ui.L("Cancel", "Cancelar"), delegate { DialogResult = false; }));
            buttons.Children.Add(Ui.Button(Ui.L("Use signature", "Usar firma"), delegate
            {
                if (ink.Strokes.Count == 0) return; Rect bounds = ink.Strokes.GetBounds(); if (bounds.Width < 1 || bounds.Height < 1) return;
                var points = new List<InkPoint>();
                foreach (var stroke in ink.Strokes)
                    for (int i = 0; i < stroke.StylusPoints.Count; i++)
                    { var p = stroke.StylusPoints[i]; points.Add(new InkPoint((p.X - bounds.X) / bounds.Width, (p.Y - bounds.Y) / bounds.Height, p.PressureFactor, i == 0)); }
                Signature = new SavedSignature { Aspect = bounds.Width / bounds.Height, Points = points }; RememberSignature = remember.IsChecked == true; DialogResult = true;
            }));
        }
    }
}
