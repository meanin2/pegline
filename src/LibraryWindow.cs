using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Pegline
{
    internal sealed class RecentRow : INotifyPropertyChanged
    {
        public RecentCapture Entry;
        public string Name { get { return Path.GetFileName(Entry.Path); } }
        public string Location { get { return Entry.Path; } }
        string details;
        ImageSource thumbnail;
        public string Details { get { return details; } set { details = value; Notify("Details"); } }
        public ImageSource Thumbnail { get { return thumbnail; } set { thumbnail = value; Notify("Thumbnail"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string property) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(property)); }
    }

    // A keyboard-accessible companion, not a second screenshot storage location.
    // The catalog contains at most 200 local file references. Forget never deletes a file.
    internal sealed class LibraryWindow : Window
    {
        readonly Controller host;
        readonly ObservableCollection<RecentRow> rows = new ObservableCollection<RecentRow>();
        readonly TextBox search;
        readonly ListBox list;
        readonly TextBlock footer, empty;
        readonly DispatcherTimer filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        int generation;
        bool closed;
        public LibraryWindow(Controller host)
        {
            this.host = host;
            Title = Ui.L("Pegline — Screenshot library", "Pegline — Biblioteca de capturas");
            Width = 880; Height = 650; MinWidth = 560; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Theme(this);
            var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
            head.Children.Add(Ui.Heading(Ui.L("Your screenshots, within reach", "Tus capturas, a mano")));
            head.Children.Add(new TextBlock { Text = Ui.L("Find an earlier capture, hang it again, or pick up where you left off.", "Encuentra una captura anterior, vuelve a colgarla o sigue editándola."), Foreground = Ui.Muted, Margin = new Thickness(3, 0, 3, 12) });
            search = new TextBox { Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(3, 0, 3, 10), ToolTip = Ui.L("Search filename or folder · Ctrl+F", "Buscar nombre o carpeta · Ctrl+F") };
            Ui.Name(search, Ui.L("Search screenshots", "Buscar capturas")); head.Children.Add(search);
            var commands = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; head.Children.Add(commands);
            commands.Children.Add(Ui.PrimaryButton(Ui.L("Markup", "Marcación"), delegate { Act("edit"); }));
            commands.Children.Add(Ui.Button(Ui.L("Copy", "Copiar"), delegate { Act("copy"); }));
            commands.Children.Add(Ui.Button(Ui.L("Hang again", "Colgar de nuevo"), delegate { Act("hang"); }));
            commands.Children.Add(Ui.Button(Ui.L("Open", "Abrir"), delegate { Act("open"); }));
            commands.Children.Add(Ui.Button(Ui.L("Show in folder", "Mostrar en carpeta"), delegate { Act("reveal"); }));
            commands.Children.Add(Ui.Button(Ui.L("Add images…", "Añadir imágenes…"), AddImages));
            var forget = Ui.Button(Ui.L("Forget entry", "Olvidar entrada"), delegate { Act("forget"); });
            forget.ToolTip = Ui.L("Removes only this library reference. The image file is not deleted.", "Solo quita la referencia de la biblioteca. No elimina el archivo."); commands.Children.Add(forget);
            var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            footer = new TextBlock { Margin = new Thickness(3, 10, 3, 5), Foreground = Ui.Muted }; bottom.Children.Add(footer);
            bottom.Children.Add(new TextBlock { Text = Ui.L("Enter: edit   ·   Ctrl+C: copy   ·   Shift+Enter: hang   ·   Drop images to add\nLocal references only. Clearing the line never deletes these files.", "Intro: editar   ·   Ctrl+C: copiar   ·   Mayús+Intro: colgar   ·   Suelta imágenes para añadir\nSolo referencias locales. Vaciar el tendedero no elimina estos archivos."), Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) });
            var body = new Grid(); root.Children.Add(body);
            list = new ListBox { ItemsSource = rows, ItemTemplate = RowTemplate(), BorderThickness = new Thickness(1), AllowDrop = true };
            Ui.Name(list, Ui.L("Recent screenshots", "Capturas recientes"));
            VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling); ScrollViewer.SetCanContentScroll(list, true);
            body.Children.Add(list);
            empty = new TextBlock { Text = Ui.L("Nothing here yet. Take a capture or add an image.\nScreenshots you take from now on will appear here.", "Aún no hay nada. Haz una captura o añade una imagen.\nTus próximas capturas aparecerán aquí."), Foreground = Ui.Muted, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            body.Children.Add(empty);
            search.TextChanged += delegate { filterTimer.Stop(); filterTimer.Start(); };
            filterTimer.Tick += delegate { filterTimer.Stop(); Refresh(); };
            list.MouseDoubleClick += delegate(object sender, MouseButtonEventArgs e) { if (list.SelectedItem != null) Act("edit"); };
            list.PreviewDragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            list.Drop += delegate(object sender, DragEventArgs e) { var paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null) host.ImportFiles(paths); e.Handled = true; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrl && e.Key == Key.F) { search.Focus(); search.SelectAll(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (!(Keyboard.FocusedElement is TextBox))
                {
                    if (ctrl && e.Key == Key.C) { Act("copy"); e.Handled = true; }
                    else if (e.Key == Key.Enter) { Act((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "hang" : "edit"); e.Handled = true; }
                    else if (e.Key == Key.Delete) { Act("forget"); e.Handled = true; }
                }
            };
            host.LibraryChanged += Refresh;
            Closed += delegate { closed = true; generation++; filterTimer.Stop(); host.LibraryChanged -= Refresh; };
            Loaded += delegate { Refresh(); search.Focus(); };
        }
        void Act(string action)
        {
            var row = list.SelectedItem as RecentRow; if (row == null) return;
            host.RecentAction(action, row.Entry);
        }
        void AddImages()
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.heif;*.webp" };
            if (dialog.ShowDialog(this) == true) host.ImportFiles(dialog.FileNames);
        }
        async void Refresh()
        {
            if (closed) return;
            int version = ++generation;
            var selected = list.SelectedItem as RecentRow; string selectedPath = selected == null ? null : selected.Entry.Path;
            string query = search.Text.Trim(); rows.Clear();
            foreach (var entry in host.RecentItems.Where(e => query.Length == 0 || e.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string date = "";
                if (entry.AddedUtcTicks >= DateTime.MinValue.Ticks && entry.AddedUtcTicks <= DateTime.MaxValue.Ticks)
                    date = new DateTime(entry.AddedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g");
                rows.Add(new RecentRow { Entry = entry, Details = entry.Width + " × " + entry.Height + " px   ·   " + date + "   ·   " + (entry.Owned ? Ui.L("Private inbox", "Bandeja privada") : Ui.L("External file", "Archivo externo")) });
            }
            empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            footer.Text = rows.Count + Ui.L(" captures shown · up to 200 recent references", " capturas mostradas · hasta 200 referencias recientes");
            list.SelectedItem = rows.FirstOrDefault(r => SafeFiles.Same(r.Entry.Path, selectedPath)) ?? rows.FirstOrDefault();
            // Frozen thumbnails can cross threads; at most one decode per generation is in flight.
            foreach (var row in rows.ToArray())
            {
                if (closed || version != generation) return;
                try
                {
                    var image = await Task.Run(delegate { return Images.Load(row.Entry.Path, 180); });
                    if (closed || version != generation) return; row.Thumbnail = image;
                }
                catch { if (closed || version != generation) return; row.Details = Ui.L("Unavailable or moved — use Show in folder to locate it.", "No disponible o movida — usa Mostrar en carpeta para localizarla."); }
            }
        }
        static DataTemplate RowTemplate()
        {
            var template = new DataTemplate(typeof(RecentRow));
            var dock = new FrameworkElementFactory(typeof(DockPanel)); dock.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
            var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(FrameworkElement.WidthProperty, 100.0); image.SetValue(FrameworkElement.HeightProperty, 66.0);
            image.SetValue(Image.StretchProperty, Stretch.Uniform); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            image.SetValue(DockPanel.DockProperty, Dock.Left); image.SetBinding(Image.SourceProperty, new Binding("Thumbnail")); dock.AppendChild(image);
            var text = new FrameworkElementFactory(typeof(StackPanel)); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            foreach (string property in new string[] { "Name", "Details", "Location" })
            {
                var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(property));
                label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); label.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
                label.SetValue(TextBlock.FontSizeProperty, property == "Name" ? 14.0 : 11.0);
                if (property == "Name") label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
                label.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Location")); text.AppendChild(label);
            }
            dock.AppendChild(text); template.VisualTree = dock; return template;
        }
    }
}
