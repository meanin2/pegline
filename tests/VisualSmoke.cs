// Optional Windows-only real WPF render harness. No real screenshots, clipboard
// reads, watchers, hotkeys, startup registration, or user settings are used.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pegline.Tests
{
    internal static class VisualSmoke
    {
        public static int Run(string destination, bool readmeMedia = false)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "Pegline-visual-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch); Directory.CreateDirectory(destination);
            try
            {
                Native.InitializeDpi();
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var store = new Store(scratch); store.Preferences.ReducedMotion = true; store.Preferences.Welcomed = true;
                using (var controller = new Controller(app.Dispatcher, store))
                {
                    for (int n = 0; n < 3; n++)
                    {
                        var sample = Sample(720, n == 1 ? 940 : 460, n);
                        string path = Path.Combine(scratch, "Example " + (n + 1) + ".png"); SafeFiles.AtomicCreate(path, Images.Encode(sample, ".png"));
                        var card = new Card(path, false, Images.Thumbnail(sample, 480), -100) { Tilt = (n - 1) * 1.6, HasFlown = true };
                        controller.Cards.Add(card); store.Catalog.Track(path, false, sample.PixelWidth, sample.PixelHeight, DateTime.UtcNow.Ticks);
                    }
                    var nativeShelf = new ShelfWindow(controller);
                    try
                    {
                        nativeShelf.Place(Native.PointerScreen); nativeShelf.SetRevealed(true); Pump();
                        long styles = Native.GetWindowLongPtr(nativeShelf.Handle, Native.GWL_EXSTYLE).ToInt64();
                        if (!nativeShelf.IsVisible || (styles & Native.WS_EX_NOACTIVATE) == 0 || (styles & Native.WS_EX_TRANSPARENT) != 0)
                            throw new InvalidOperationException("Native shelf visibility/input styles are incorrect.");
                        nativeShelf.ForceHide(); Pump();
                        if (nativeShelf.IsVisible) throw new InvalidOperationException("Shelf failed to hide.");
                        Console.WriteLine("PASS native shelf create/show/style/hide lifecycle (three WPF windows).");
                    }
                    finally { nativeShelf.Close(); }
                    foreach (string theme in new string[] { "light", "dark" })
                    {
                        store.Preferences.Theme = theme; Ui.RefreshTheme();
                        using (var shelf = new ShelfView(controller))
                        {
                            shelf.Reveal(true, true); shelf.Measure(new Size(1440, 210)); shelf.Arrange(new Rect(0, 0, 1440, 210)); shelf.UpdateLayout();
                            var interactive = Raster(shelf, 1440, 210);
                            var composition = new DrawingVisual();
                            using (var dc = composition.RenderOpen())
                            {
                                dc.DrawRectangle(Ui.Panel, null, new Rect(0, 0, 1440, 210));
                                shelf.DrawDecorations(dc, false); dc.DrawImage(interactive, new Rect(0, 0, 1440, 210)); shelf.DrawDecorations(dc, true);
                            }
                            Save(composition, 1440, 210, Path.Combine(destination, "shelf-" + theme + ".png"));
                            if (readmeMedia && theme == "light")
                            {
                                var cards = controller.Cards.ToArray();
                                for (int count = 0; count <= cards.Length; count++)
                                {
                                    controller.Cards.Clear(); controller.Cards.AddRange(cards.Take(count));
                                    shelf.InvalidateVisual(); Pump();
                                    var frame = new DrawingVisual();
                                    using (var dc = frame.RenderOpen())
                                    {
                                        dc.DrawRectangle(Ui.Panel, null, new Rect(0, 0, 1440, 210));
                                        shelf.DrawDecorations(dc, false); dc.DrawImage(Raster(shelf, 1440, 210), new Rect(0, 0, 1440, 210)); shelf.DrawDecorations(dc, true);
                                    }
                                    Save(frame, 1440, 210, Path.Combine(destination, "shelf-step-" + count + ".png"));
                                }
                            }
                        }
                        var editor = new EditorWindow(controller.Cards[0].Path, store) { ShowActivated = false, Width = 1180, Height = 760 };
                        try
                        {
                            // Test-only access keeps production API free of demo mutations.
                            var document = (EditorDocument)typeof(EditorWindow).GetField("document", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
                            document.Layers.Add(new Mark { Kind = MarkKind.Arrow, Bounds = new Rect(80, 150, 170, 100), Stroke = Colors.OrangeRed, Thickness = 5 });
                            document.Layers.Add(new Mark { Kind = MarkKind.Text, Bounds = new Rect(290, 175, 320, 100), Text = "A clear point.\nA clean screenshot.", FontSize = 26, Stroke = Colors.DarkSlateGray });
                            document.Touch(); document.MarkSaved();
                            editor.Show(); Pump(); SnapshotWindow(editor, Path.Combine(destination, "editor-" + theme + ".png"));
                            if (readmeMedia && theme == "light")
                            {
                                document.Layers.Clear(); document.Touch(); Pump();
                                SnapshotWindow(editor, Path.Combine(destination, "markup-step-0.png"));
                                document.BeginEdit();
                                document.Layers.Add(new Mark { Kind = MarkKind.Arrow, Bounds = new Rect(80, 150, 170, 100), Stroke = Colors.OrangeRed, Thickness = 5 });
                                document.Touch(); document.CommitEdit(); Pump();
                                SnapshotWindow(editor, Path.Combine(destination, "markup-step-1.png"));
                                document.BeginEdit();
                                document.Layers.Add(new Mark { Kind = MarkKind.Text, Bounds = new Rect(290, 175, 320, 100), Text = "A clear point.\nA clean screenshot.", FontSize = 26, Stroke = Colors.DarkSlateGray });
                                document.Touch(); document.CommitEdit(); Pump();
                                SnapshotWindow(editor, Path.Combine(destination, "markup-step-2.png"));
                                document.Undo(); Pump(); SnapshotWindow(editor, Path.Combine(destination, "markup-step-3.png"));
                                document.MarkSaved();
                            }
                        }
                        finally { editor.Close(); }
                        var library = new LibraryWindow(controller) { ShowActivated = false };
                        try
                        {
                            library.Show(); Pump();
                            var rows = (System.Collections.Generic.IEnumerable<RecentRow>)typeof(LibraryWindow).GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(library);
                            WaitFor(delegate { return rows.All(row => row.Thumbnail != null); });
                            SnapshotWindow(library, Path.Combine(destination, "library-" + theme + ".png"));
                        }
                        finally { library.Close(); }
                    }
                }
                string report = "Real WPF visual smoke run completed at " + DateTime.UtcNow.ToString("O") + "\n" +
                    "OS: " + Environment.OSVersion + "; CLR: " + Environment.Version + "\n" +
                    "Six renderer snapshots generated from synthetic images and an isolated temporary store.\n" +
                    "This does not certify visual parity, Shell dragging, clipboard targets, monitor changes, or click-through.\n";
                File.WriteAllText(Path.Combine(destination, "visual-smoke.txt"), report); Console.WriteLine(report); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { try { Directory.Delete(scratch, true); } catch { } }
        }
        static void WaitFor(Func<bool> ready)
        {
            if (ready()) return;
            var clock = System.Diagnostics.Stopwatch.StartNew(); var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += delegate { if (ready() || clock.Elapsed.TotalSeconds >= 8) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!ready()) throw new TimeoutException("Synthetic thumbnails did not finish loading.");
        }
        static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
        static void SnapshotWindow(Window window, string path)
        {
            window.UpdateLayout(); var content = window.Content as FrameworkElement;
            if (content == null || content.ActualWidth < 1 || content.ActualHeight < 1) throw new InvalidOperationException("Window did not lay out.");
            int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
            var composition = new DrawingVisual();
            using (var dc = composition.RenderOpen())
            {
                // Content-only rendering omits Window.Background and produces misleading transparent chrome.
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
            }
            Save(composition, width, height, path);
        }
        static RenderTargetBitmap Raster(Visual visual, int width, int height)
        { var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image; }
        static void Save(Visual visual, int width, int height, string path)
        { SafeFiles.AtomicWrite(path, Images.Encode(Raster(visual, width, height), ".png")); }
        static BitmapSource Sample(int width, int height, int index)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(240, 246, 249)), null, new Rect(0, 0, width, height));
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(25, 103, 119)), null, new Rect(30, 30, width - 60, 80), 12, 12);
                var title = new FormattedText("EXAMPLE " + (index + 1), System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 28, Brushes.White);
                dc.DrawText(title, new Point(54, 48));
                for (int n = 0; n < 4; n++) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb((byte)(190 + n * 8), (byte)(214 + n * 4), 220)),
                    null, new Rect(45, 150 + n * 54, width - 90 - (n % 2) * 140, 24), 5, 5);
            }
            return Raster(visual, width, height);
        }
    }
}
