// Real controller/watchers, isolated synthetic files. No capture, clipboard reads,
// registered hotkeys, startup changes, Shell transfers or permanent user-file changes.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pegline.Tests
{
    internal static class IntegrationSmoke
    {
        static int checks;
        static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame); Thread.Sleep(20);
        }
        static void Until(Func<bool> ready, string label)
        {
            var time = Stopwatch.StartNew(); while (!ready() && time.Elapsed.TotalSeconds < 15) Pump();
            Check(ready(), label);
        }
        static void Settle(int milliseconds) { var t = Stopwatch.StartNew(); while (t.ElapsedMilliseconds < milliseconds) Pump(); }
        static void Write(string path, int width)
        {
            byte[] pixels = new byte[width * 80 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 90; pixels[i+1] = 160; pixels[i+2] = (byte)width; pixels[i+3] = 255; }
            var image = BitmapSource.Create(width, 80, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze();
            File.WriteAllBytes(path, Images.Encode(image, ".png"));
        }
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "Pegline-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Native.InitializeDpi();
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                string folder = Path.Combine(root, "watched"); Directory.CreateDirectory(folder);
                string baseline = Path.Combine(folder, "existing.png"); Write(baseline, 110);
                var store = new Store(Path.Combine(root, "state"), folder);
                var p = store.Preferences; p.ClipboardSnips = p.AllClipboardImages = p.CopyCaptures = p.Sound = p.ShowNewCaptures = false;
                p.Welcomed = p.ReducedMotion = true;
                p.ToggleKey = p.RegionKey = p.ScreenKey = p.CaptureKey = p.RepeatKey = p.LibraryKey = "";
                store.SavePreferences();
                string fresh = Path.Combine(folder, "new.png"), imported = Path.Combine(root, "manual.png");
                using (var controller = new Controller(app.Dispatcher, store))
                {
                    controller.Start(); Settle(1200);
                    Check(controller.Cards.Count == 0, "watcher baseline does not import old screenshots");
                    Write(fresh, 120);
                    Until(delegate { return controller.Cards.Any(c => c.Path == fresh); }, "new watched image reaches native shelf");
                    Write(fresh, 160);
                    Until(delegate { return controller.Cards.Any(c => c.Path == fresh && c.PixelWidth == 160); }, "changed image refreshes shelf pixels");
                    Check(store.Catalog.Items.Any(c => c.Path == fresh), "collection updates persisted library");
                    controller.Discard(controller.Cards.First(c => c.Path == fresh));
                    Until(delegate { return controller.Cards.Count == 0; }, "external discard removes card");
                    Check(File.Exists(fresh), "external discard preserves original file");
                    controller.RecentAction("hang", store.Catalog.Items.First(c => c.Path == fresh));
                    Until(delegate { return controller.Cards.Any(c => c.Path == fresh); }, "library rehang restores card");
                    controller.RecentAction("forget", store.Catalog.Items.First(c => c.Path == fresh));
                    Check(File.Exists(fresh) && !store.Catalog.Items.Any(c => c.Path == fresh), "library forget preserves file");
                    Write(imported, 180); controller.ImportFiles(new string[] { imported });
                    Until(delegate { return controller.Cards.Any(c => c.Path == imported); }, "manual import reaches shelf");
                    File.Delete(fresh);
                    Until(delegate { return !controller.Cards.Any(c => c.Path == fresh); }, "deleted file pruned from shelf");
                }
                using (var controller = new Controller(app.Dispatcher, new Store(store.Root, folder)))
                {
                    controller.Start();
                    Check(controller.Cards.Any(c => c.Path == imported), "session restores after controller restart");
                    typeof(Controller).GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
                    Until(delegate { return controller.Cards.Count == 0; }, "clear removes all cards");
                    Check(File.Exists(imported) && File.Exists(baseline), "clear preserves images");
                }
                Console.WriteLine(checks + " native integration checks passed."); return 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
    }
}
