using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Pegline
{
    internal static class Ui
    {
        public static Preferences Preferences = new Preferences();
        public static bool Spanish { get { return Preferences.Language == "es" || (Preferences.Language != "en" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"); } }
        public static string L(string en, string es) { return Spanish ? es : en; }
        static DateTime darkChecked = DateTime.MinValue;
        static bool cachedDark;
        public static bool Dark
        {
            get
            {
                if (Preferences.Theme == "dark") return true; if (Preferences.Theme == "light") return false;
                if ((DateTime.UtcNow - darkChecked).TotalSeconds < 1) return cachedDark;
                darkChecked = DateTime.UtcNow;
                try { cachedDark = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0; }
                catch { cachedDark = false; }
                return cachedDark;
            }
        }
        public static Brush Text { get { return SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Dark ? Brushes.WhiteSmoke : ColorBrush(32, 35, 42); } }
        public static Brush Panel { get { return SystemParameters.HighContrast ? SystemColors.WindowBrush : Dark ? ColorBrush(27, 30, 36) : ColorBrush(247, 248, 250); } }
        public static Brush Accent { get { return SystemParameters.HighContrast ? SystemColors.HighlightBrush : ColorBrush(26, 118, 139); } }
        public static Brush Muted { get { return SystemParameters.HighContrast ? Text : Dark ? ColorBrush(169, 177, 190) : ColorBrush(93, 105, 122); } }
        public static Brush Field { get { return SystemParameters.HighContrast ? Panel : Dark ? ColorBrush(38, 43, 51) : Brushes.White; } }
        public static Brush Line { get { return SystemParameters.HighContrast ? Text : Dark ? ColorBrush(62, 70, 81) : ColorBrush(215, 222, 230); } }
        public static SolidColorBrush ColorBrush(byte r, byte g, byte b)
        { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
        static event Action AppearanceChanged;
        public static void RefreshTheme() { darkChecked = DateTime.MinValue; if (AppearanceChanged != null) AppearanceChanged(); }
        public static void Theme(Window window)
        {
            bool closed = false;
            Action apply = delegate
            {
                if (closed) return;
                window.Background = Panel; window.Foreground = Text; window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 13;
                window.UseLayoutRounding = true;
                window.Resources[SystemColors.WindowBrushKey] = Field;
                window.Resources[SystemColors.WindowTextBrushKey] = Text;
                window.Resources[SystemColors.ControlBrushKey] = Panel;
                window.Resources[SystemColors.ControlTextBrushKey] = Text;
                window.Resources[SystemColors.HighlightBrushKey] = Accent;
                window.Resources[SystemColors.HighlightTextBrushKey] = Brushes.White;
                if (SystemParameters.HighContrast)
                {
                    // Remove stale dark/light templates after a live contrast switch.
                    foreach (Type type in new Type[] { typeof(Button), typeof(TextBox), typeof(ListBox), typeof(ListBoxItem), typeof(ComboBox), typeof(ComboBoxItem) }) window.Resources.Remove(type);
                    window.Resources[SystemColors.HighlightTextBrushKey] = SystemColors.HighlightTextBrush;
                    return;
                }
                var button = new Style(typeof(Button));
                button.Setters.Add(new Setter(Control.BackgroundProperty, Field)); button.Setters.Add(new Setter(Control.ForegroundProperty, Text));
                button.Setters.Add(new Setter(Control.BorderBrushProperty, Line)); button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                button.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
                var template = new ControlTemplate(typeof(Button));
                var border = new FrameworkElementFactory(typeof(Border), "Chrome"); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
                border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
                border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
                border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
                border.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
                var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
                content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                border.AppendChild(content); template.VisualTree = border;
                var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty, .84, "Chrome")); template.Triggers.Add(hover);
                var press = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true }; press.Setters.Add(new Setter(UIElement.OpacityProperty, .65, "Chrome")); template.Triggers.Add(press);
                var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Chrome")); template.Triggers.Add(focus);
                var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .42, "Chrome")); template.Triggers.Add(disabled);
                button.Setters.Add(new Setter(Control.TemplateProperty, template)); window.Resources[typeof(Button)] = button;
                foreach (Type type in new Type[] { typeof(TextBox), typeof(ListBox) })
                {
                    var style = new Style(type); style.Setters.Add(new Setter(Control.BackgroundProperty, Field)); style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
                    style.Setters.Add(new Setter(Control.BorderBrushProperty, Line)); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    if (type == typeof(TextBox)) { style.Setters.Add(new Setter(TextBox.CaretBrushProperty, Text)); style.Setters.Add(new Setter(TextBox.SelectionBrushProperty, Accent)); }
                    window.Resources[type] = style;
                }
                // The framework ComboBox uses light system chrome even in our dark window.
                foreach (Type type in new Type[] { typeof(ComboBox), typeof(ComboBoxItem) })
                {
                    var combo = new Style(type); combo.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
                    combo.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); window.Resources[type] = combo;
                }
                var item = new Style(typeof(ListBoxItem)); item.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
                var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
                var itemBorder = new FrameworkElementFactory(typeof(Border));
                itemBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
                itemBorder.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
                itemBorder.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                itemTemplate.VisualTree = itemBorder; item.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));
                item.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); item.Setters.Add(new Setter(Control.ForegroundProperty, Text));
                var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, Accent)); selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White)); item.Triggers.Add(selected);
                window.Resources[typeof(ListBoxItem)] = item;
            };
            apply(); AppearanceChanged += apply;
            Microsoft.Win32.UserPreferenceChangedEventHandler system = delegate
            {
                if (!window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(delegate { darkChecked = DateTime.MinValue; apply(); }));
            };
            SystemEvents.UserPreferenceChanged += system;
            window.Closed += delegate { closed = true; AppearanceChanged -= apply; SystemEvents.UserPreferenceChanged -= system; };
        }
        public static Button PrimaryButton(string title, Action action)
        {
            var b = Button(title, action);
            b.SetResourceReference(Control.BackgroundProperty, SystemColors.HighlightBrushKey);
            b.SetResourceReference(Control.ForegroundProperty, SystemColors.HighlightTextBrushKey);
            b.SetResourceReference(Control.BorderBrushProperty, SystemColors.HighlightBrushKey);
            return b;
        }
        public static TextBlock Heading(string title)
        { return new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 4, 3, 9) }; }
        public static void Name(DependencyObject control, string text)
        { System.Windows.Automation.AutomationProperties.SetName(control, text); }
        public static Button Button(string title, Action action)
        {
            var b = new Button { Content = title, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 6), MinHeight = 30 };
            System.Windows.Automation.AutomationProperties.SetName(b, title);
            b.Click += delegate { action(); }; return b;
        }
        public static TextBlock Label(string text)
        { return new TextBlock { Text = text, Margin = new Thickness(5, 7, 5, 5), TextWrapping = TextWrapping.Wrap }; }
        public static void Error(string message)
        { MessageBox.Show(message, "Pegline", MessageBoxButton.OK, MessageBoxImage.Warning); }
        public static string TextPrompt(Window owner, string title, string initial)
        {
            var w = new Window { Title = title, Owner = owner, Width = 440, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
            Theme(w); var root = new DockPanel { Margin = new Thickness(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var input = new TextBox { Text = initial ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8) };
            buttons.Children.Add(Button(L("Cancel", "Cancelar"), delegate { w.DialogResult = false; }));
            buttons.Children.Add(Button(L("OK", "Aceptar"), delegate { w.DialogResult = true; })); root.Children.Add(input); w.Content = root;
            w.Loaded += delegate { input.Focus(); input.SelectAll(); };
            w.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { w.DialogResult = false; e.Handled = true; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { w.DialogResult = true; e.Handled = true; }
            };
            return w.ShowDialog() == true ? input.Text : null;
        }
    }

    internal sealed class SettingsWindow : Window
    {
        readonly Preferences source;
        readonly CheckBox sound, managed, snips, allImages, copy, cursor, motion, startup, peek, loupe;
        readonly TextBox toggle, region, screen, capture, repeat, library;
        readonly ComboBox language, theme, delay;
        readonly ListBox folders;
        readonly Func<string, bool> validateHotkeys;
        readonly Action<bool> setStartup;
        public SettingsWindow(Preferences source, bool startsWithWindows, Func<string, bool> validateHotkeys, Action<bool> setStartup)
        {
            this.source = source; this.validateHotkeys = validateHotkeys; this.setStartup = setStartup;
            Title = Ui.L("Pegline settings", "Ajustes de Pegline"); Width = 660; Height = 720; MinWidth = 530; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen; Ui.Theme(this);
            var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            buttons.Children.Add(Ui.Button(Ui.L("Cancel", "Cancelar"), delegate { DialogResult = false; }));
            buttons.Children.Add(Ui.PrimaryButton(Ui.L("Save", "Guardar"), Save));
            var stack = new StackPanel(); root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            stack.Children.Add(Ui.Heading(Ui.L("Make it yours", "A tu manera")));
            stack.Children.Add(Ui.Label(Ui.L("Local only. No account, telemetry, analytics, updater, or network requests.", "Solo local. Sin cuenta, telemetría, análisis, actualizador ni solicitudes de red.")));
            managed = Check(stack, Ui.L("Keep Pegline captures in its inbox until saved or discarded", "Guardar las capturas de Pegline en su bandeja hasta conservarlas o descartarlas"), source.ManagedInbox);
            snips = Check(stack, Ui.L("Collect images copied by Snipping Tool", "Recoger imágenes copiadas por Recortes"), source.ClipboardSnips);
            allImages = Check(stack, Ui.L("Also collect clipboard images from other apps (opt-in)", "Recoger también imágenes del portapapeles de otras apps (opcional)"), source.AllClipboardImages);
            copy = Check(stack, Ui.L("Copy my Pegline captures to the clipboard immediately", "Copiar inmediatamente mis capturas de Pegline al portapapeles"), source.CopyCaptures);
            cursor = Check(stack, Ui.L("Include the pointer in Pegline captures", "Incluir el puntero en las capturas de Pegline"), source.CaptureCursor);
            sound = Check(stack, Ui.L("Sounds", "Sonidos"), source.Sound);
            peek = Check(stack, Ui.L("Show the line briefly when a capture arrives", "Mostrar brevemente el tendedero al llegar una captura"), source.ShowNewCaptures);
            loupe = Check(stack, Ui.L("Show a pixel magnifier while selecting a capture", "Mostrar una lupa de píxeles al seleccionar la captura"), source.ShowCaptureLoupe);
            motion = Check(stack, Ui.L("Reduce motion", "Reducir movimiento"), source.ReducedMotion);
            startup = Check(stack, Ui.L("Start with Windows (this executable's current location)", "Iniciar con Windows (ubicación actual de este ejecutable)"), startsWithWindows);
            stack.Children.Add(Ui.Label(Ui.L("Shortcuts — Windows-reserved combinations cannot be taken over. Blank disables a shortcut.", "Atajos — no se pueden sustituir combinaciones reservadas de Windows. Vacío desactiva el atajo.")));
            toggle = Text(stack, Ui.L("Show / hide line", "Mostrar / ocultar"), source.ToggleKey);
            region = Text(stack, Ui.L("Capture a region", "Capturar una zona"), source.RegionKey);
            screen = Text(stack, Ui.L("Capture this monitor", "Capturar este monitor"), source.ScreenKey);
            capture = Text(stack, Ui.L("Capture chooser", "Selector de captura"), source.CaptureKey);
            repeat = Text(stack, Ui.L("Repeat last region", "Repetir última zona"), source.RepeatKey);
            library = Text(stack, Ui.L("Screenshot library", "Biblioteca de capturas"), source.LibraryKey);
            delay = Choice(stack, Ui.L("Capture delay (seconds)", "Retardo de captura (segundos)"), new string[] { "0", "3", "5", "10" }, source.CaptureDelay.ToString());
            language = Choice(stack, Ui.L("Language", "Idioma"), new string[] { "auto", "en", "es" }, source.Language ?? "auto");
            theme = Choice(stack, Ui.L("Appearance", "Apariencia"), new string[] { "auto", "light", "dark" }, source.Theme ?? "auto");
            stack.Children.Add(Ui.Label(Ui.L("Additional screenshot folders (every supported image added here is collected):", "Carpetas adicionales de capturas (se recogerán todas las imágenes compatibles añadidas):")));
            folders = new ListBox { Height = 100, Margin = new Thickness(5) };
            foreach (string f in source.Folders) folders.Items.Add(f); stack.Children.Add(folders);
            var row = new StackPanel { Orientation = Orientation.Horizontal }; stack.Children.Add(row);
            row.Children.Add(Ui.Button(Ui.L("Add folder…", "Añadir carpeta…"), delegate
            {
                using (var dialog = new Forms.FolderBrowserDialog()) if (dialog.ShowDialog() == Forms.DialogResult.OK && !folders.Items.Contains(dialog.SelectedPath)) folders.Items.Add(dialog.SelectedPath);
            }));
            row.Children.Add(Ui.Button(Ui.L("Remove", "Quitar"), delegate { if (folders.SelectedItem != null) folders.Items.Remove(folders.SelectedItem); }));
            stack.Children.Add(Ui.Label(Ui.L("Windows' screenshot folder is always watched. Pegline does not change Snipping Tool settings or redirect other applications. WebP/HEIF decoding depends on locally installed Windows codecs.", "Siempre se vigila la carpeta de capturas de Windows. Pegline no cambia los ajustes de Recortes ni redirige otras aplicaciones. WebP/HEIF dependen de los códecs locales de Windows.")));
        }
        static CheckBox Check(Panel panel, string label, bool value)
        { var c = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(5, 7, 5, 7) }; panel.Children.Add(c); return c; }
        static TextBox Text(Panel panel, string label, string value)
        { panel.Children.Add(Ui.Label(label)); var t = new TextBox { Text = value, Margin = new Thickness(5), Padding = new Thickness(6) }; panel.Children.Add(t); return t; }
        static ComboBox Choice(Panel panel, string label, string[] choices, string value)
        { panel.Children.Add(Ui.Label(label)); var c = new ComboBox { ItemsSource = choices, SelectedItem = value, Margin = new Thickness(5), Padding = new Thickness(4) }; if (c.SelectedIndex < 0) c.SelectedIndex = 0; panel.Children.Add(c); return c; }
        void Save()
        {
            string[] keys = new string[] { toggle.Text.Trim(), region.Text.Trim(), screen.Text.Trim(), capture.Text.Trim(), repeat.Text.Trim(), library.Text.Trim() };
            if (!HotkeyChord.Distinct(keys) || keys.Any(k => !validateHotkeys(k)))
            { Ui.Error(Ui.L("Use different valid shortcuts, for example Ctrl+Alt+4 or PrintScreen.", "Utiliza atajos válidos y diferentes, como Ctrl+Alt+4 o PrintScreen.")); return; }
            try { setStartup(startup.IsChecked == true); } catch (Exception e) { Ui.Error(e.Message); return; }
            source.Sound = sound.IsChecked == true; source.ManagedInbox = managed.IsChecked == true; source.ClipboardSnips = snips.IsChecked == true;
            source.AllClipboardImages = allImages.IsChecked == true; source.CopyCaptures = copy.IsChecked == true; source.CaptureCursor = cursor.IsChecked == true;
            source.ReducedMotion = motion.IsChecked == true; source.ToggleKey = keys[0]; source.RegionKey = keys[1]; source.ScreenKey = keys[2]; source.CaptureKey = keys[3];
            source.RepeatKey = keys[4]; source.LibraryKey = keys[5]; source.ShowNewCaptures = peek.IsChecked == true; source.ShowCaptureLoupe = loupe.IsChecked == true;
            source.CaptureDelay = int.Parse((string)delay.SelectedItem); source.Theme = (string)theme.SelectedItem; source.Language = (string)language.SelectedItem;
            source.Folders = folders.Items.Cast<string>().ToList(); DialogResult = true;
        }
    }
}
