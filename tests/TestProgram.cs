using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pegline.Tests
{
    internal static class TestProgram
    {
        static int passed, failed;
        static string root;
        static void Test(string name, Action run)
        {
            try { run(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + " -- " + e); }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Near(double actual, double expected) { Assert(Math.Abs(actual - expected) < .00001, actual + " != " + expected); }
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--render-smoke") return VisualSmoke.Run(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--readme-media") return VisualSmoke.Run(Path.GetFullPath(args[1]), true);
            if (args.Length == 1 && args[0] == "--integration-smoke") return IntegrationSmoke.Run();
            root = Path.Combine(Path.GetTempPath(), "Pegline-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                Test("layout/original-constants", delegate { Near(ShelfLayout.Height, 210); Near(ShelfLayout.Spacing, 174); Near(ShelfLayout.CardWidth, 150); Near(ShelfLayout.PinAbove, 9.5); });
                Test("layout/rope-symmetry", delegate { Near(ShelfLayout.RopeY(300, 1440), ShelfLayout.RopeY(1140, 1440)); Near(ShelfLayout.RopeY(0, 1440), 10); });
                Test("layout/sag-capped", delegate { Near(ShelfLayout.Sag(5000), 30); Near(ShelfLayout.Sag(1000), 18); });
                Test("layout/centering", delegate { Near(ShelfLayout.X(0, 1, 1440), 720); Near(ShelfLayout.X(0, 3, 1440) + ShelfLayout.X(2, 3, 1440), 1440); });
                Test("layout/capacity-clamps", delegate { Assert(ShelfLayout.Capacity(300) == 3, "minimum"); Assert(ShelfLayout.Capacity(10000) == 12, "maximum"); Assert(ShelfLayout.Capacity(1920) == 9, "1920 DIP capacity"); });
                Test("layout/card-wide", delegate { Box b = ShelfLayout.Card(0, 1, 1440, 1920, 1080); Near(b.Width, 144); Assert(b.Height <= 112, "height"); Near(b.CenterX, 720); });
                Test("layout/card-tall", delegate { Box b = ShelfLayout.Card(0, 1, 1440, 900, 1800); Near(b.Height, 112); Near(b.Width, 60); });
                Test("animation/cubic-endpoints", delegate { Near(ShelfLayout.Cubic(0), 0); Near(ShelfLayout.Cubic(1), 1); Near(ShelfLayout.Cubic(.5), .5); });
                Test("animation/spring-endpoints", delegate { Near(ShelfLayout.Spring(0, 46, 2.6), 1); Assert(Math.Abs(ShelfLayout.Spring(20, 46, 2.6)) < .00001, "settles"); });
                Test("visibility/dwell", delegate { var s = new VisibilityState(); s.Tick(1, true, false, true, false, false, false); s.Tick(1.24, true, false, true, false, false, false); Assert(!s.Revealed, "too early"); s.Tick(1.26, true, false, true, false, false, false); Assert(s.Revealed, "did not reveal"); });
                Test("visibility/retract-delay", delegate { var s = new VisibilityState(); s.Show(0, false, 0); s.Tick(1, true, false, false, false, false, false); s.Tick(1.4, true, false, false, false, false, false); Assert(s.Revealed, "too early"); s.Tick(1.6, true, false, false, false, false, false); Assert(!s.Revealed, "not retracted"); });
                Test("visibility/peek-hold", delegate { var s = new VisibilityState(); s.Show(0, false, 2.5); s.Tick(2, true, false, false, false, false, false); Assert(s.Revealed, "peek lost"); });
                Test("visibility/drag-hold", delegate { var s = new VisibilityState(); s.Show(0, false, 0); s.Tick(10, true, false, false, false, true, false); Assert(s.Revealed, "hidden during drag"); });
                Test("visibility/pinned-until-visited", delegate { var s = new VisibilityState(); s.Show(0, true, 0); s.Tick(5, true, false, false, false, false, false); Assert(s.Pinned, "pin lost"); s.Tick(6, true, false, false, true, false, false); Assert(!s.Pinned, "pin not released"); });
                Test("visibility/fullscreen", delegate { var s = new VisibilityState(); s.Show(0, true, 3); s.Tick(1, true, true, true, true, false, false); Assert(!s.Revealed, "fullscreen overlay"); });
                Test("visibility/click-suppresses", delegate { var s = new VisibilityState(); s.Show(0, false, 0); s.Tick(1, true, false, true, true, false, true); s.Tick(2, true, false, true, true, false, false); Assert(!s.Revealed, "reopened over top click"); s.Tick(3, true, false, false, false, false, false); s.Tick(4, true, false, true, false, false, false); s.Tick(4.3, true, false, true, false, false, false); Assert(s.Revealed, "suppression did not reset"); });
                Test("visibility/no-items", delegate { var s = new VisibilityState(); s.Show(0, true, 5); s.Tick(1, false, false, true, true, false, false); Assert(!s.Revealed, "empty unwanted line"); });
                Test("dedup/opposite-channel", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Folder, 0); Assert(d.FindTwin("A", IngressKind.Clipboard, 1) == "one", "no twin"); });
                Test("dedup/same-channel-is-new", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Folder, 0); Assert(d.FindTwin("A", IngressKind.Folder, 1) == null, "lost a genuine repeat"); });
                Test("dedup/pair-only-once", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Clipboard, 0); d.FindTwin("A", IngressKind.Folder, 1); Assert(d.FindTwin("A", IngressKind.Folder, 2) == null, "paired twice"); });
                Test("dedup/expiry", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Clipboard, 0); Assert(d.FindTwin("A", IngressKind.Folder, 6) == null, "stale twin"); });
                Test("dedup/different-content", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Folder, 0); Assert(d.FindTwin("B", IngressKind.Clipboard, 1) == null, "content mismatch"); });
                Test("dedup/managed-never-paired", delegate { var d = new RecentIngress(); d.Remember("A", "one", IngressKind.Managed, 0); Assert(d.FindTwin("A", IngressKind.Clipboard, 1) == null, "managed paired"); });
                Test("files/containment-boundary", delegate { string inbox = Path.Combine(root, "Inbox"); Assert(SafeFiles.Inside(inbox, Path.Combine(inbox, "a.png")), "child"); Assert(!SafeFiles.Inside(inbox, Path.Combine(root, "Inbox-other", "a.png")), "prefix confusion"); });
                Test("files/parent-traversal", delegate { string inbox = Path.Combine(root, "Inbox"); Assert(!SafeFiles.Inside(inbox, Path.Combine(inbox, "..", "outside.png")), "escaped inbox"); });
                Test("files/unique-names", delegate { string a = Path.Combine(root, "same.png"); File.WriteAllText(a, "original"); Assert(Path.GetFileName(SafeFiles.Unique(root, "same.png")) == "same (2).png", "name collision"); });
                Test("files/atomic-replacement", delegate { string a = Path.Combine(root, "atomic.json"); SafeFiles.AtomicWrite(a, new byte[] { 1 }); SafeFiles.AtomicWrite(a, new byte[] { 2, 3 }); Assert(File.ReadAllBytes(a).SequenceEqual(new byte[] { 2, 3 }), "replacement"); Assert(!Directory.GetFiles(root, "*.tmp").Any(), "temporary leaked"); });
                Test("files/json-round-trip", delegate { string p = Path.Combine(root, "prefs.json"); var prefs = new Preferences { Language = "es", ManagedInbox = false }; SafeFiles.WriteJson(p, prefs); var loaded = SafeFiles.ReadJson(p, new Preferences()); Assert(loaded.Language == "es" && !loaded.ManagedInbox, "prefs"); });
                Test("files/corrupt-json-fallback", delegate { string p = Path.Combine(root, "bad.json"); File.WriteAllText(p, "{broken"); var fallback = new Preferences(); Assert(object.ReferenceEquals(fallback, SafeFiles.ReadJson(p, fallback)), "fallback"); });
                Test("files/null-json-fallback", delegate { string p = Path.Combine(root, "null.json"); File.WriteAllText(p, "null"); var fallback = new Preferences(); Assert(object.ReferenceEquals(fallback, SafeFiles.ReadJson(p, fallback)), "null fallback"); });
                Test("files/missing-preferences-defaults", delegate { string p = Path.Combine(root, "partial.json"); File.WriteAllText(p, "{\"Language\":\"es\"}"); var loaded = SafeFiles.ReadJson(p, new Preferences()); Assert(loaded.Language == "es" && loaded.Sound && loaded.ManagedInbox && loaded.ClipboardSnips && loaded.RegionKey == "Ctrl+Alt+4", "field defaults lost"); });
                Test("history/undo-redo", delegate { var h = new History<int>(3); h.Push(1); Assert(h.Undo(2) == 1, "undo"); Assert(h.Redo(1) == 2, "redo"); });
                Test("history/new-action-clears-redo", delegate { var h = new History<int>(3); h.Push(1); h.Undo(2); h.Push(3); Assert(!h.CanRedo, "stale redo"); });
                Test("shortcuts/defaults", delegate { uint m, k; Assert(Controller.ParseShortcut("Ctrl+Alt+4", out m, out k) && m == 3 && k == 52, "default region"); });
                Test("shortcuts/printscreen", delegate { uint m, k; Assert(Controller.ParseShortcut("PrintScreen", out m, out k) && m == 0 && k == 44, "printscreen"); });
                Test("shortcuts/no-global-typing-hijack", delegate { uint m, k; Assert(!Controller.ParseShortcut("Q", out m, out k), "bare typing key allowed"); });
                Test("shortcuts/invalid-modifier-only", delegate { uint m, k; Assert(!Controller.ParseShortcut("Ctrl+Alt", out m, out k), "modifier only"); });
                Test("shortcuts/disabled", delegate { uint m, k; Assert(Controller.ParseShortcut("", out m, out k) && k == 0, "blank disabled"); });
                Test("editor/clone-independent-ink", delegate { var m = new Mark(); m.Points.Add(new InkPoint(0, 0, 1, true)); var copy = m.Clone(); copy.Points.Add(new InkPoint(1, 1, 1, false)); Assert(m.Points.Count == 1, "shared mutable points"); });
                Test("editor/point-segment-distance", delegate { Near(EditorMath.DistanceToSegment(new Point(5, 3), new Point(0, 0), new Point(10, 0)), 3); });
                Test("editor/sketch-line", delegate { var m = new Mark { Kind = MarkKind.Sketch, Normalized = false }; for (int i = 0; i < 20; i++) m.Points.Add(new InkPoint(i * 5, i * 5, 1, i == 0)); EditorMath.Recognize(m); Assert(m.Kind == MarkKind.Line, "line recognition"); });
                Test("editor/sketch-ellipse", delegate { var m = new Mark { Kind = MarkKind.Sketch, Bounds = new Rect(0, 0, 100, 100) }; for (int i = 0; i <= 64; i++) m.Points.Add(new InkPoint(.5 + .5 * Math.Cos(i * Math.PI / 32), .5 + .5 * Math.Sin(i * Math.PI / 32), 1, i == 0)); EditorMath.Recognize(m); Assert(m.Kind == MarkKind.Ellipse, "ellipse recognition"); });
                Test("image/png-round-trip", delegate { var image = Solid(40, 20, Colors.CornflowerBlue); var decoded = Images.Decode(Images.Encode(image, ".png"), 0); Assert(decoded.PixelWidth == 40 && decoded.PixelHeight == 20 && Images.Fingerprint(image) == Images.Fingerprint(decoded), "PNG pixel mismatch"); });
                Test("image/unsupported-overwrite-blocked", delegate { Assert(!Images.CanOverwrite("photo.heic") && !Images.CanOverwrite("animation.gif") && Images.CanOverwrite("capture.png"), "format protection"); });
                Test("editor/redaction-renders-opaque", delegate { var doc = new EditorDocument(Solid(40, 20, Colors.White)); doc.Layers.Add(new Mark { Kind = MarkKind.Redact, Stroke = Colors.Black, Bounds = new Rect(5, 5, 10, 10) }); var image = doc.Render(0, false); int stride; byte[] p = Images.Pixels(image, out stride); int n = 7 * stride + 7 * 4; Assert(p[n] == 0 && p[n + 1] == 0 && p[n + 2] == 0 && p[n + 3] == 255, "redaction not opaque"); });
                Test("editor/crop-dimensions-and-undo", delegate { var doc = new EditorDocument(Solid(40, 20, Colors.White)); doc.Crop(new Rect(5, 2, 10, 8)); Assert(doc.Width == 10 && doc.Height == 8, "crop dimensions"); doc.Undo(); Assert(doc.Width == 40 && doc.Height == 20, "crop undo"); });
                Test("editor/rotation-dimensions", delegate { var doc = new EditorDocument(Solid(40, 20, Colors.White)); doc.Rotate(90); Assert(doc.Width == 20 && doc.Height == 40, "rotation"); });
                Test("editor/all-vector-tools-render", delegate
                {
                    var kinds = new MarkKind[] { MarkKind.Rectangle, MarkKind.Ellipse, MarkKind.Line, MarkKind.Arrow, MarkKind.DoubleArrow, MarkKind.Polygon, MarkKind.Star, MarkKind.SpeechBubble, MarkKind.Text, MarkKind.Spotlight, MarkKind.Magnifier };
                    foreach (var kind in kinds)
                    {
                        var doc = new EditorDocument(Solid(120, 90, Colors.White)); doc.Layers.Add(new Mark { Kind = kind, Bounds = new Rect(15, 15, 70, 50), Text = "Test", Filled = true });
                        var image = doc.Render(0, false); Assert(image.PixelWidth == 120, kind.ToString());
                    }
                });
                Test("editor/save-flag", delegate { var doc = new EditorDocument(Solid(4, 4, Colors.White)); doc.Touch(); Assert(doc.Dirty, "dirty missing"); doc.MarkSaved(); Assert(!doc.Dirty, "saved flag"); });

                // Regression cases for the 0.2 review. These are executed by build.cmd
                // on Windows; source authorship is not a passing test result.
                Test("editor/new-annotations-have-distinct-identities", delegate
                {
                    var style = new Mark(); var a = style.NewCopy(); var b = style.NewCopy();
                    Assert(a.Id != b.Id && a.Id != style.Id && b.Id != style.Id, "New annotations inherited the style ID");
                });
                Test("editor/history-clone-preserves-identity", delegate { var mark = new Mark(); Assert(mark.Id == mark.Clone().Id, "Undo snapshots must preserve identity"); });
                Test("editor/new-copy-owns-point-list", delegate
                {
                    var style = new Mark(); style.Points.Add(new InkPoint(.2, .3, 1, true)); var copy = style.NewCopy(); copy.Points.Clear();
                    Assert(style.Points.Count == 1, "New mark shared mutable stroke points");
                });
                Test("editor/undo-to-original-is-clean", delegate
                {
                    var doc = Document(); AddMark(doc); Assert(doc.Dirty, "Edit not dirty"); doc.Undo(); Assert(!doc.Dirty && doc.Layers.Count == 0, "Undo to load should be clean");
                });
                Test("editor/redo-to-saved-revision-is-clean", delegate
                {
                    var doc = Document(); AddMark(doc); doc.MarkSaved(); doc.Undo(); Assert(doc.Dirty, "Undo away from saved revision must be dirty");
                    doc.Redo(); Assert(!doc.Dirty && doc.Layers.Count == 1, "Redo to save point should be clean");
                });
                Test("editor/cancel-restores-layers-and-clean-state", delegate
                {
                    var doc = Document(); doc.BeginEdit(); doc.Layers.Add(new Mark()); doc.Touch(); doc.CancelEdit();
                    Assert(doc.Layers.Count == 0 && !doc.Dirty && !doc.CanUndo && !doc.CanRedo, "Cancelled gesture left history or pixels");
                });
                Test("editor/cancel-preserves-previous-redo", delegate
                {
                    var doc = Document(); AddMark(doc); Guid id = doc.Layers[0].Id; doc.Undo(); doc.BeginEdit(); doc.Layers.Add(new Mark()); doc.Touch(); doc.CancelEdit(); doc.Redo();
                    Assert(doc.Layers.Count == 1 && doc.Layers[0].Id == id, "Cancelled gesture erased prior redo");
                });
                Test("editor/no-op-gesture-does-not-add-history", delegate
                { var doc = Document(); doc.BeginEdit(); doc.CommitEdit(); Assert(!doc.CanUndo && !doc.Dirty, "No-op gesture changed history"); });
                Test("editor/no-op-gesture-preserves-redo", delegate
                { var doc = Document(); AddMark(doc); doc.Undo(); doc.BeginEdit(); doc.CommitEdit(); Assert(doc.CanRedo, "No-op cleared redo"); });
                Test("editor/gesture-is-one-undo-step", delegate
                {
                    var doc = Document(); doc.BeginEdit(); doc.Layers.Add(new Mark()); for (int n = 0; n < 15; n++) doc.Touch(); doc.CommitEdit(); doc.Undo();
                    Assert(doc.Layers.Count == 0 && !doc.CanUndo, "Gesture produced multiple undo steps");
                });
                Test("editor/undo-during-gesture-cancels-it", delegate
                {
                    var doc = Document(); AddMark(doc); doc.BeginEdit(); doc.Layers.Add(new Mark()); doc.Touch(); doc.Undo();
                    Assert(!doc.IsEditing && doc.Layers.Count == 1 && !doc.CanRedo, "Undo committed unfinished gesture");
                });
                Test("editor/branch-does-not-reuse-saved-revision", delegate
                {
                    var doc = Document(); AddMark(doc); doc.MarkSaved(); doc.Undo(); AddMark(doc);
                    Assert(doc.Dirty && !doc.CanRedo, "New branch incorrectly matches saved revision");
                });
                Test("editor/crop-preserves-editable-layer", delegate
                {
                    var doc = Document(); AddMark(doc); var id = doc.Layers[0].Id; doc.Crop(new Rect(3, 4, 20, 12));
                    Assert(doc.Layers.Count == 1 && doc.Layers[0].Id == id, "Crop flattened or replaced layer"); Near(doc.Layers[0].Bounds.X, 2); Near(doc.Layers[0].Bounds.Y, 1);
                });
                Test("editor/crop-undo-restores-layer-coordinates", delegate
                {
                    var doc = Document(); AddMark(doc); doc.Crop(new Rect(3, 4, 20, 12)); doc.Undo(); Near(doc.Layers[0].Bounds.X, 5); Near(doc.Layers[0].Bounds.Y, 5);
                    Assert(doc.Width == 40 && doc.Height == 20, "Original crop size not restored");
                });
                Test("editor/crop-outside-is-no-op", delegate
                { var doc = Document(); doc.Crop(new Rect(100, 100, 3, 3)); Assert(!doc.Dirty && doc.Width == 40 && !doc.CanUndo, "Outside crop changed document"); });
                Test("editor/crop-subpixel-is-no-op", delegate
                { var doc = Document(); doc.Crop(new Rect(2, 2, .2, .3)); Assert(!doc.Dirty && !doc.CanUndo, "Tiny crop changed document"); });
                Test("editor/crop-clamps-to-image", delegate
                { var doc = Document(); doc.Crop(new Rect(-10, -10, 20, 20)); Assert(doc.Width == 10 && doc.Height == 10, "Crop did not intersect image bounds"); });
                Test("editor/fractional-redaction-covers-whole-edge-pixels", delegate
                {
                    var doc = Document(); doc.Layers.Add(new Mark { Kind = MarkKind.Redact, Stroke = Color.FromArgb(1, 0, 0, 0), Bounds = new Rect(5.3, 5.3, 2.4, 2.4) });
                    var image = doc.Render(0, false); for (int y = 5; y < 8; y++) for (int x = 5; x < 8; x++) Pixel(image, x, y, 0, 0, 0, 255);
                    Pixel(image, 4, 4, 255, 255, 255, 255);
                });
                Test("editor/redaction-does-not-change-base", delegate
                {
                    var doc = Document(); doc.Layers.Add(new Mark { Kind = MarkKind.Redact, Bounds = new Rect(0, 0, 30, 15) }); doc.Render(0, false);
                    Pixel(doc.BaseImage, 2, 2, 255, 255, 255, 255);
                });
                Test("editor/outline-hit-does-not-block-inner-mark", delegate
                {
                    var doc = Document(); var small = new Mark { Kind = MarkKind.Rectangle, Filled = true, Bounds = new Rect(14, 6, 8, 8) };
                    doc.Layers.Add(small); doc.Layers.Add(new Mark { Kind = MarkKind.Rectangle, Filled = false, Bounds = new Rect(0, 0, 39, 19), Thickness = 1 });
                    Assert(doc.Hit(new Point(18, 10), 1) == small, "Hollow shape stole interior selection");
                });
                Test("editor/magnifier-keeps-output-dimensions", delegate
                { var doc = Document(); doc.Layers.Add(new Mark { Kind = MarkKind.Magnifier, Bounds = new Rect(4, 2, 14, 14) }); var output = doc.Render(0, false); Assert(output.PixelWidth == 40 && output.PixelHeight == 20, "Magnifier changed output dimensions"); });
                Test("image/transparent-png-remains-transparent", delegate
                { var decoded = Images.Decode(Images.Encode(Solid(4, 4, Colors.Transparent), ".png"), 0); int stride; var bytes = Images.Pixels(decoded, out stride); Assert(bytes[stride + 7] == 0, "PNG alpha was repaired as if it were a DIB"); });
                Test("image/dib-zero-alpha-keeps-rgb", delegate
                { byte[] bytes = { 12, 34, 56, 0, 78, 90, 123, 0 }; Assert(Images.RepairDibAlpha(bytes, false), "No DIB repair"); Assert(bytes.SequenceEqual(new byte[] { 12, 34, 56, 255, 78, 90, 123, 255 }), "DIB RGB was lost"); });
                Test("image/dib-partial-alpha-is-preserved", delegate
                { byte[] bytes = { 2, 4, 6, 0, 5, 7, 9, 128 }; byte[] expected = (byte[])bytes.Clone(); Assert(!Images.RepairDibAlpha(bytes, false) && bytes.SequenceEqual(expected), "Legitimate alpha changed"); });
                Test("image/dib-rgb-padding-is-not-alpha", delegate
                { byte[] bytes = { 2, 4, 6, 19 }; Images.RepairDibAlpha(bytes, true); Assert(bytes[3] == 255 && bytes[0] == 2, "RGB padding treated as alpha"); });
                Test("image/dib-invalid-buffer-rejected", delegate
                { Throws<ArgumentException>(delegate { Images.RepairDibAlpha(new byte[3], false); }); });
                Test("image/exif-five-is-transpose", delegate
                {
                    var output = Images.ApplyOrientation(Swatch(), 5); Assert(output.PixelWidth == 2 && output.PixelHeight == 3, "Transpose dimensions");
                    Pixel(output, 0, 0, 1, 0, 0, 255); Pixel(output, 1, 0, 4, 0, 0, 255); Pixel(output, 0, 2, 3, 0, 0, 255); Pixel(output, 1, 2, 6, 0, 0, 255);
                });
                Test("image/exif-seven-is-transverse", delegate
                {
                    var output = Images.ApplyOrientation(Swatch(), 7); Assert(output.PixelWidth == 2 && output.PixelHeight == 3, "Transverse dimensions");
                    Pixel(output, 0, 0, 6, 0, 0, 255); Pixel(output, 1, 0, 3, 0, 0, 255); Pixel(output, 0, 2, 4, 0, 0, 255); Pixel(output, 1, 2, 1, 0, 0, 255);
                });
                Test("image/exif-orientation-one-is-unchanged", delegate
                { var source = Swatch(); Assert(Images.Fingerprint(source) == Images.Fingerprint(Images.ApplyOrientation(source, 1)), "Default EXIF changed pixels"); });
                Test("image/thumbnail-does-not-enlarge", delegate
                { var image = Images.Thumbnail(Solid(7, 4, Colors.Red), 480); Assert(image.PixelWidth == 7 && image.PixelHeight == 4 && image.IsFrozen, "Small image upscaled or not frozen"); });
                Test("image/thumbnail-bounds", delegate
                { var image = Images.Thumbnail(Solid(800, 400, Colors.Red), 100); Assert(image.PixelWidth <= 100 && image.PixelHeight <= 50, "Thumbnail limit ignored"); });
                Test("files/create-must-not-overwrite", delegate
                {
                    string file = Path.Combine(root, "create-collision.txt"); SafeFiles.AtomicCreate(file, new byte[] { 3 });
                    Throws<IOException>(delegate { SafeFiles.AtomicCreate(file, new byte[] { 4 }); }); Assert(File.ReadAllBytes(file)[0] == 3, "Atomic create overwrote destination");
                });
                Test("files/checked-write-rejects-external-edit", delegate
                {
                    string file = Path.Combine(root, "external.txt"); File.WriteAllBytes(file, new byte[] { 1 }); string hash = SafeFiles.FileHash(file); File.WriteAllBytes(file, new byte[] { 2 });
                    Throws<IOException>(delegate { SafeFiles.WriteChecked(file, new byte[] { 3 }, hash, null); }); Assert(File.ReadAllBytes(file)[0] == 2, "External edit overwritten");
                });
                Test("files/checked-write-preserves-backup", delegate
                {
                    string file = Path.Combine(root, "checked.txt"), backup = file + ".backup"; File.WriteAllBytes(file, new byte[] { 1 });
                    SafeFiles.WriteChecked(file, new byte[] { 2 }, SafeFiles.FileHash(file), delegate(string original) { File.Copy(original, backup); });
                    Assert(File.ReadAllBytes(file)[0] == 2 && File.ReadAllBytes(backup)[0] == 1, "Checked write or backup incorrect");
                });
                Test("files/failed-backup-prevents-overwrite", delegate
                {
                    string file = Path.Combine(root, "no-backup.txt"); File.WriteAllBytes(file, new byte[] { 1 });
                    Throws<IOException>(delegate { SafeFiles.WriteChecked(file, new byte[] { 2 }, SafeFiles.FileHash(file), delegate { throw new IOException("Backup unavailable"); }); });
                    Assert(File.ReadAllBytes(file)[0] == 1, "Overwrite proceeded without backup");
                });
                Test("files/checked-new-file-rejects-name-race", delegate
                {
                    string file = Path.Combine(root, "name-race.txt"); File.WriteAllBytes(file, new byte[] { 9 });
                    Throws<IOException>(delegate { SafeFiles.WriteChecked(file, new byte[] { 2 }, null, null); }); Assert(File.ReadAllBytes(file)[0] == 9, "New-file path clobbered a file");
                });
                Test("files/read-size-cap", delegate
                {
                    string file = Path.Combine(root, "bounded.txt"); File.WriteAllBytes(file, new byte[11]);
                    Throws<IOException>(delegate { SafeFiles.ReadBounded(file, 10); }); Assert(SafeFiles.ReadBounded(file, 11).Length == 11, "Inclusive read cap");
                });
                Test("files/empty-canonical-path-rejected", delegate
                { Throws<ArgumentException>(delegate { SafeFiles.Canonical(" "); }); });
                Test("files/no-temporary-files-after-failure", delegate
                { Assert(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Failed atomic write leaked temporary data"); });
                Test("shortcuts/canonical-order-and-synonyms", delegate
                { Assert(HotkeyChord.Identity("Control+Alt+T") == HotkeyChord.Identity("alt + ctrl + t"), "Equivalent chords differ"); });
                Test("shortcuts/equivalent-duplicates-rejected", delegate
                { Assert(!HotkeyChord.Distinct(new string[] { "Ctrl+Alt+T", "Alt+Control+T" }), "Equivalent duplicate shortcuts allowed"); });
                Test("shortcuts/disabled-slots-can-repeat", delegate
                { Assert(HotkeyChord.Distinct(new string[] { "", " ", "Ctrl+Alt+T" }), "Disabled bindings must not conflict"); });
                Test("shortcuts/repeated-modifier-rejected", delegate
                { uint mods, key; Assert(!HotkeyChord.TryParse("Ctrl+Control+T", out mods, out key), "Duplicate modifier accepted"); });
                Test("shortcuts/non-ascii-key-rejected", delegate
                { uint mods, key; Assert(!HotkeyChord.TryParse("Ctrl+é", out mods, out key), "Unicode codepoint accepted as virtual key"); });
                Test("shortcuts/function-range", delegate
                { uint mods, key; Assert(HotkeyChord.TryParse("F24", out mods, out key) && key == 135, "F24 rejected"); Assert(!HotkeyChord.TryParse("F25", out mods, out key), "F25 accepted"); });
                Test("catalog/bounded-reference-history", delegate
                {
                    var catalog = new CaptureCatalog(null); for (int n = 0; n < 240; n++) catalog.Track(Path.Combine(root, n + ".png"), false, 10, 10, n);
                    Assert(catalog.Items.Count() == 200 && Path.GetFileName(catalog.Items.First().Path) == "239.png", "History size/order");
                });
                Test("catalog/reimport-promotes-not-duplicates", delegate
                {
                    var catalog = new CaptureCatalog(null); string a = Path.Combine(root, "a.png"), b = Path.Combine(root, "b.png");
                    catalog.Track(a, true, 10, 20, 1); catalog.Track(b, false, 10, 20, 2); catalog.Track(a, true, 30, 40, 3);
                    Assert(catalog.Items.Count() == 2 && catalog.Items.First().Path == a && catalog.Items.First().Width == 30, "History promotion");
                });
                Test("catalog/forget-leaves-file", delegate
                {
                    string file = Path.Combine(root, "keep.png"); File.WriteAllBytes(file, new byte[] { 7 }); var catalog = new CaptureCatalog(null); catalog.Track(file, true, 1, 1, 1); catalog.Forget(file);
                    Assert(!catalog.Items.Any() && File.ReadAllBytes(file)[0] == 7, "Forget deleted image");
                });
                Test("catalog/invalid-entries-skipped", delegate
                {
                    string file = Path.Combine(root, "entry.png"); var catalog = new CaptureCatalog(new RecentCapture[] { null, new RecentCapture { Path = "" }, new RecentCapture { Path = file }, new RecentCapture { Path = file } });
                    Assert(catalog.Items.Count() == 1, "Invalid/duplicate catalog entries retained");
                });
                Test("preferences/new-features-default-on-upgrade", delegate
                {
                    string file = Path.Combine(root, "upgrade.json"); File.WriteAllText(file, "{\"Version\":1}"); var prefs = SafeFiles.ReadJson(file, new Preferences()); prefs.Normalize();
                    Assert(prefs.ShowNewCaptures && prefs.ShowCaptureLoupe && prefs.RepeatKey == "Ctrl+Alt+R" && prefs.LibraryKey == "Ctrl+Alt+L", "Upgrade lost new defaults");
                });
                Test("preferences/normalization", delegate
                {
                    var prefs = new Preferences { Theme = "bad", Language = "xx", CaptureDelay = 999, Folders = new List<string> { null, "", " ", root, root } }; prefs.Normalize();
                    Assert(prefs.Theme == "auto" && prefs.Language == "auto" && prefs.CaptureDelay == 10 && prefs.Folders.Count == 1, "Invalid preferences survived");
                });
                Test("capture/repeat-region-validation", delegate
                {
                    Assert(!new CaptureRegion { Width = 40000, Height = 40000, DisplaySignature = "display" }.Valid, "Oversize repeat accepted");
                    Assert(new CaptureRegion { X = -1920, Y = -500, Width = 600, Height = 400, DisplaySignature = "display" }.Valid, "Negative-origin monitor region rejected");
                });
                Test("capture/repeat-region-without-topology-rejected", delegate
                { var prefs = new Preferences { LastRegion = new CaptureRegion { Width = 20, Height = 20 } }; prefs.Normalize(); Assert(prefs.LastRegion == null, "Unknown-topology region retained"); });
                Test("geometry/clamp-nonfinite", delegate
                { Near(ShelfLayout.Clamp(double.NaN, 2, 8), 2); Near(ShelfLayout.Clamp(double.PositiveInfinity, 2, 8), 2); });
                Test("geometry/square-horizontal-drag", delegate
                { var b = ShelfLayout.SquareFrom(4, 8, 20, 8); Near(b.Width, 16); Near(b.Height, 16); });
                Test("geometry/square-negative-drag", delegate
                { var b = ShelfLayout.SquareFrom(10, 20, -5, 14); Near(b.X, -5); Near(b.Y, 5); Near(b.Width, 15); Near(b.Height, 15); });
                Test("dedup/interleaved-identical-captures", delegate
                {
                    var d = new RecentIngress(); d.Remember("X", "first", IngressKind.Clipboard, 0); d.Remember("X", "second", IngressKind.Clipboard, .1);
                    Assert(d.FindTwin("X", IngressKind.Folder, .2) == "first" && d.FindTwin("X", IngressKind.Folder, .3) == "second", "Identical captures paired incorrectly");
                });
                Test("dedup/clock-reset-clears-twins", delegate
                { var d = new RecentIngress(); d.Remember("X", "stale", IngressKind.Folder, 20); Assert(d.FindTwin("X", IngressKind.Clipboard, 1) == null, "Clock reset kept stale twin"); });
                Test("dedup/null-content-never-matches", delegate
                { var d = new RecentIngress(); d.Remember(null, "null", IngressKind.Clipboard, 0); Assert(d.FindTwin(null, IngressKind.Folder, 1) == null, "Unknown pixels deduplicated"); });
                Test("store/injected-root-does-not-touch-user-state", delegate
                {
                    string directory = Path.Combine(root, "isolated-store"); var store = new Store(directory); store.Preferences.Language = "es"; store.SavePreferences();
                    Assert(store.Root == directory && File.Exists(Path.Combine(directory, "preferences.json")), "Store escaped isolated test root");
                });
                Test("editor/shift-line-snaps-horizontal", delegate
                { var p = EditorMath.Constrain(new Point(5, 5), new Point(25, 6), true); Near(p.Y, 5); Assert(p.X > 24, "Horizontal line collapsed"); });
                Test("editor/shift-shape-stays-square", delegate
                { var p = EditorMath.Constrain(new Point(5, 5), new Point(25, 5), false); Near(p.X, 25); Near(p.Y, 25); });
                Test("editor/canceled-gesture-preserves-full-history", delegate
                {
                    var d = Document(); for (int i = 0; i < 32; i++) AddMark(d);
                    d.BeginEdit(); d.Touch(); d.CancelEdit();
                    int steps = 0; while (d.CanUndo) { d.Undo(); steps++; }
                    Assert(steps == 32 && d.Layers.Count == 0, "Cancel evicted a committed undo step");
                });
                Test("editor/no-op-gesture-preserves-full-history", delegate
                {
                    var d = Document(); for (int i = 0; i < 32; i++) AddMark(d);
                    d.BeginEdit(); d.CommitEdit();
                    int steps = 0; while (d.CanUndo) { d.Undo(); steps++; }
                    Assert(steps == 32, "No-op evicted a committed undo step");
                });
                Console.WriteLine("\n" + passed + " passed, " + failed + " failed.");
                Console.WriteLine("This suite does NOT verify real clipboard targets, focus, monitor DPI transitions, fullscreen, or capture overlays. See docs/WINDOWS_ACCEPTANCE.md.");
                return failed == 0 ? 0 : 1;
            }
            finally { try { Directory.Delete(root, true); } catch { } } // Only the GUID-named test folder created above.
        }

        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        static EditorDocument Document() { return new EditorDocument(Solid(40, 20, Colors.White)); }
        static void AddMark(EditorDocument doc)
        { doc.Remember(); doc.Layers.Add(new Mark { Kind = MarkKind.Rectangle, Bounds = new Rect(5, 5, 10, 10), Filled = true }); doc.Touch(); }
        static void Pixel(BitmapSource image, int x, int y, byte b, byte g, byte r, byte a)
        {
            int stride; byte[] pixels = Images.Pixels(image, out stride); int index = y * stride + x * 4;
            Assert(pixels[index] == b && pixels[index + 1] == g && pixels[index + 2] == r && pixels[index + 3] == a,
                "Pixel " + x + "," + y + ": expected BGRA " + b + "," + g + "," + r + "," + a + "; actual " + string.Join(",", pixels.Skip(index).Take(4)));
        }
        static BitmapSource Swatch()
        {
            byte[] pixels = { 1, 0, 0, 255, 2, 0, 0, 255, 3, 0, 0, 255, 4, 0, 0, 255, 5, 0, 0, 255, 6, 0, 0, 255 };
            var image = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12); image.Freeze(); return image;
        }
        static BitmapSource Solid(int width, int height, Color c)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A; }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
        }
    }
}
