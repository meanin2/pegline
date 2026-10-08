// Pegline: Windows adaptation of the Tendedero interaction model.
// C# 5 intentionally: the offline build can use Windows' Framework compiler.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace Pegline
{
    public struct Box
    {
        public double X, Y, Width, Height;
        public Box(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }
        public double CenterX { get { return X + Width / 2; } }
        public double CenterY { get { return Y + Height / 2; } }
        public bool Contains(double x, double y) { return x >= X && x < Right && y >= Y && y < Bottom; }
        public static Box Between(double ax, double ay, double bx, double by)
        { return new Box(Math.Min(ax, bx), Math.Min(ay, by), Math.Abs(bx - ax), Math.Abs(by - ay)); }
        public Box Intersect(Box b)
        {
            double x = Math.Max(X, b.X), y = Math.Max(Y, b.Y);
            return new Box(x, y, Math.Max(0, Math.Min(Right, b.Right) - x), Math.Max(0, Math.Min(Bottom, b.Bottom) - y));
        }
    }

    public static class ShelfLayout
    {
        // Translated from Tendedero's Layout/PeggedView. Units are DIPs, not physical pixels.
        public const double Height = 210, Spacing = 174, CardWidth = 150, RopeTop = 10, PinAbove = 9.5;
        public const double RevealDelay = .25, RetractDelay = .5, PeekDuration = 2.5, HoldDuration = .45;
        public static double Sag(double width) { return Math.Min(30, Math.Max(0, width) * .018); }
        public static double RopeY(double x, double width)
        { double f = width <= 0 ? 0 : x / width; return RopeTop + 4 * Sag(width) * f * (1 - f); }
        public static double X(int index, int count, double width)
        { return width / 2 - Math.Max(0, count - 1) * Spacing / 2 + index * Spacing; }
        public static int Capacity(double width) { return Math.Max(3, Math.Min(12, (int)((width - 200) / Spacing))); }
        public static Box Card(int index, int count, double width, double imageWidth, double imageHeight)
        {
            double s = Math.Min(136 / Math.Max(1, imageWidth), 104 / Math.Max(1, imageHeight));
            double w = imageWidth * s + 8, h = imageHeight * s + 8;
            double x = X(index, count, width);
            return new Box(x - w / 2, RopeY(x, width) - PinAbove + 14, w, h);
        }
        public static double Cubic(double t) { t = Clamp(t, 0, 1); return t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2; }
        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public static double Clamp(double v, double lo, double hi) { return Finite(v) ? Math.Min(hi, Math.Max(lo, v)) : lo; }
        public static Box SquareFrom(double ax, double ay, double bx, double by)
        {
            double size = Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay));
            return Box.Between(ax, ay, ax + (bx < ax ? -size : size), ay + (by < ay ? -size : size));
        }
        public static double Spring(double seconds, double stiffness, double damping)
        {
            // Unit mass, underdamped spring; 1 at t=0, approaches 0 continuously.
            double a = damping / 2, w = Math.Sqrt(Math.Max(.01, stiffness - a * a));
            return Math.Exp(-a * seconds) * (Math.Cos(w * seconds) + a / w * Math.Sin(w * seconds));
        }
    }

    public sealed class VisibilityState
    {
        public bool Revealed { get; private set; }
        public bool Pinned { get; private set; }
        public bool Suppressed { get; private set; }
        double hotSince = -1, awaySince = -1, peekUntil = -1;
        public void Show(double now, bool pin, double peek)
        { Revealed = true; Pinned = pin; peekUntil = now + peek; awaySince = -1; hotSince = -1; }
        public void Hide()
        { Revealed = false; Pinned = false; peekUntil = -1; awaySince = -1; hotSince = -1; }
        public void Tick(double now, bool wanted, bool blocked, bool inBand, bool inside, bool busy, bool topClick)
        {
            if (!inBand) Suppressed = false;
            if (topClick && inBand && !busy) { Suppressed = true; Hide(); }
            if (!wanted || blocked) { Hide(); return; }
            if (!Revealed)
            {
                if (inBand && !Suppressed)
                {
                    if (hotSince < 0) hotSince = now;
                    if (now - hotSince >= ShelfLayout.RevealDelay) Show(now, false, 0);
                }
                else hotSince = -1;
                return;
            }
            if (inside) Pinned = false;
            if (inside || Pinned || busy || now < peekUntil) awaySince = -1;
            else
            {
                if (awaySince < 0) awaySince = now;
                if (now - awaySince >= ShelfLayout.RetractDelay) Hide();
            }
        }
    }

    public enum IngressKind { Folder, Clipboard, Managed, Restored, Manual }
    public sealed class RecentIngress
    {
        sealed class Entry { public string Hash, Path; public IngressKind Kind; public double Time; public bool Paired; }
        readonly List<Entry> entries = new List<Entry>();
        public string FindTwin(string hash, IngressKind kind, double now)
        {
            entries.RemoveAll(e => now - e.Time > 5 || now < e.Time);
            if (string.IsNullOrEmpty(hash) || (kind != IngressKind.Clipboard && kind != IngressKind.Folder)) return null;
            // Only pair opposite channels, once. Repeated identical screenshots remain distinct.
            var twin = entries.FirstOrDefault(e => !e.Paired && e.Hash == hash &&
                ((e.Kind == IngressKind.Folder && kind == IngressKind.Clipboard) ||
                 (e.Kind == IngressKind.Clipboard && kind == IngressKind.Folder)));
            if (twin == null) return null;
            twin.Paired = true;
            return twin.Path;
        }
        public void Remember(string hash, string path, IngressKind kind, double now)
        {
            entries.RemoveAll(e => now - e.Time > 5 || now < e.Time);
            if (kind != IngressKind.Clipboard && kind != IngressKind.Folder) return;
            if (entries.Count >= 128) entries.RemoveAt(0);
            entries.Add(new Entry { Hash = hash, Path = path, Kind = kind, Time = now });
        }
    }

    public static class SafeFiles
    {
        public static string Canonical(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.");
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        public static bool Same(string a, string b)
        { try { return string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase); } catch { return false; } }
        public static bool Inside(string root, string path)
        {
            try { return Canonical(path).StartsWith(Canonical(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public static bool OwnedLocation(string root, string path)
        {
            if (!Inside(root, path)) return false;
            try
            {
                // Do not follow a junction/symlink into unrelated storage for an inbox discard.
                string current = Path.GetFullPath(path);
                while (current != null)
                {
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                    current = Path.GetDirectoryName(current);
                }
                return true;
            }
            catch { return false; }
        }
        public static string Unique(string folder, string name)
        {
            name = Path.GetFileName(name);
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name), target = Path.Combine(folder, name);
            for (int n = 2; File.Exists(target) || Directory.Exists(target); n++) target = Path.Combine(folder, stem + " (" + n + ")" + ext);
            return target;
        }
        public static string Sha256(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        public static string FileHash(string path)
        { using (var s = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", ""); }
        public static void AtomicCreate(string path, byte[] bytes) { WriteAtomic(path, bytes, false); }
        public static void AtomicWrite(string path, byte[] bytes) { WriteAtomic(path, bytes, true); }
        static void WriteAtomic(string path, byte[] bytes, bool replace)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                { s.Write(bytes, 0, bytes.Length); s.Flush(true); }
                // Never delete the destination as a fallback. Failure leaves the original intact.
                if (replace && File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        public static byte[] ReadBounded(string path, long limit)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > limit) throw new IOException("The file exceeds the safety limit.");
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[65536]; int count;
                    while ((count = file.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + count > limit) throw new IOException("The file grew beyond the safety limit.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
        }
        public static void WriteChecked(string path, byte[] bytes, string expectedHash, Action<string> backup)
        {
            // Deny new writers during the ordinary save path. Allow Delete so File.Replace
            // can atomically replace the file while this guard still holds its original handle.
            // This is not a security boundary against a process deliberately racing renames.
            if (expectedHash == null)
            {
                AtomicCreate(path, bytes); // A raced new destination is NEVER overwritten.
                return;
            }
            using (var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(guard)).Replace("-", "");
                if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The destination changed. No file was overwritten; choose Save as.");
                if (backup != null) backup(path);
                // Recheck the name as well as the guarded handle before replacing.
                if (!string.Equals(FileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The destination was replaced by another application. Save as a new file.");
                AtomicWrite(path, bytes);
            }
        }
        public static T ReadJson<T>(string path, T fallback)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    if (f.Length > 8 * 1024 * 1024) return fallback;
                    T value = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);
                    return object.ReferenceEquals(value, null) ? fallback : value;
                }
            }
            catch { return fallback; }
        }
        public static void WriteJson<T>(string path, T value)
        {
            using (var m = new MemoryStream())
            { new DataContractJsonSerializer(typeof(T)).WriteObject(m, value); AtomicWrite(path, m.ToArray()); }
        }
    }

    [DataContract]
    public sealed class Preferences
    {
        [DataMember] public int Version = 2;
        [DataMember] public bool Sound = true;
        [DataMember] public bool ManagedInbox = true;
        [DataMember] public bool ClipboardSnips = true;
        [DataMember] public bool AllClipboardImages;
        [DataMember] public bool CopyCaptures;
        [DataMember] public bool CaptureCursor;
        [DataMember] public bool ReducedMotion;
        [DataMember] public bool Welcomed;
        [DataMember] public bool ShowNewCaptures = true;
        [DataMember] public bool ShowCaptureLoupe = true;
        [DataMember] public string RepeatKey = "Ctrl+Alt+R";
        [DataMember] public string LibraryKey = "Ctrl+Alt+L";
        [DataMember] public CaptureRegion LastRegion;
        [DataMember] public string Language = "auto";
        [DataMember] public string Theme = "auto";
        [DataMember] public string ToggleKey = "Ctrl+Alt+T";
        [DataMember] public string RegionKey = "Ctrl+Alt+4";
        [DataMember] public string ScreenKey = "Ctrl+Alt+3";
        [DataMember] public string CaptureKey = "Ctrl+Alt+5";
        [DataMember] public int CaptureDelay;
        [DataMember] public List<string> Folders = new List<string>();
        [OnDeserializing]
        void BeforeDeserialize(StreamingContext context)
        {
            // DataContractSerializer does not run field initializers for absent fields.
            Version = 2; Sound = true; ManagedInbox = true; ClipboardSnips = true;
            ShowNewCaptures = true; ShowCaptureLoupe = true; RepeatKey = "Ctrl+Alt+R"; LibraryKey = "Ctrl+Alt+L";
            Language = "auto"; Theme = "auto";
            ToggleKey = "Ctrl+Alt+T"; RegionKey = "Ctrl+Alt+4";
            ScreenKey = "Ctrl+Alt+3"; CaptureKey = "Ctrl+Alt+5";
            Folders = new List<string>();
        }
        public void Normalize()
        {
            Version = 2;
            if (Folders == null) Folders = new List<string>();
            if (Language != "en" && Language != "es") Language = "auto";
            if (Theme != "light" && Theme != "dark") Theme = "auto";
            if (RepeatKey == null) RepeatKey = "Ctrl+Alt+R";
            if (LibraryKey == null) LibraryKey = "Ctrl+Alt+L";
            if (LastRegion != null && !LastRegion.Valid) LastRegion = null;
            Folders = Folders.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
            if (ToggleKey == null) ToggleKey = "Ctrl+Alt+T";
            if (RegionKey == null) RegionKey = "Ctrl+Alt+4";
            if (ScreenKey == null) ScreenKey = "Ctrl+Alt+3";
            if (CaptureKey == null) CaptureKey = "Ctrl+Alt+5";
            CaptureDelay = Math.Max(0, Math.Min(10, CaptureDelay));
        }
    }
    [DataContract]
    public sealed class SavedCard
    {
        [DataMember] public string Path;
        [DataMember] public bool Owned;
        [DataMember] public double Tilt;
    }

    [DataContract]
    public sealed class CaptureRegion
    {
        [DataMember] public int X, Y, Width, Height;
        [DataMember] public string DisplaySignature;
        public bool Valid { get { return Width >= 2 && Height >= 2 && (long)Width * Height <= 80000000 && !string.IsNullOrEmpty(DisplaySignature); } }
    }

    [DataContract]
    public sealed class RecentCapture
    {
        [DataMember] public string Path;
        [DataMember] public bool Owned;
        [DataMember] public long AddedUtcTicks;
        [DataMember] public int Width, Height;
    }
    public sealed class CaptureCatalog
    {
        public const int Limit = 200;
        readonly List<RecentCapture> items = new List<RecentCapture>();
        public IEnumerable<RecentCapture> Items { get { return items.ToArray(); } }
        public CaptureCatalog(IEnumerable<RecentCapture> saved)
        {
            foreach (var entry in saved ?? new RecentCapture[0])
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) continue;
                try { SafeFiles.Canonical(entry.Path); } catch { continue; }
                if (!items.Any(i => SafeFiles.Same(i.Path, entry.Path))) items.Add(entry);
                if (items.Count >= Limit) break;
            }
        }
        public void Track(string path, bool owned, int width, int height, long utcTicks)
        {
            items.RemoveAll(i => SafeFiles.Same(i.Path, path));
            items.Insert(0, new RecentCapture { Path = path, Owned = owned, Width = width, Height = height, AddedUtcTicks = utcTicks });
            if (items.Count > Limit) items.RemoveRange(Limit, items.Count - Limit);
        }
        public void Forget(string path) { items.RemoveAll(i => SafeFiles.Same(i.Path, path)); } // References ONLY. No file I/O.
        public void Clear() { items.Clear(); }
    }

    public static class HotkeyChord
    {
        public static string Identity(string text)
        { uint mods, key; return TryParse(text, out mods, out key) && key != 0 ? mods + ":" + key : ""; }
        public static bool Distinct(IEnumerable<string> chords)
        {
            var ids = new HashSet<string>();
            foreach (string chord in chords)
            {
                uint mods, key; if (!TryParse(chord, out mods, out key)) return false;
                if (key != 0 && !ids.Add(mods + ":" + key)) return false;
            }
            return true;
        }
        public static bool TryParse(string text, out uint mods, out uint key)
        {
            mods = 0; key = 0;
            if (string.IsNullOrWhiteSpace(text)) return true;
            foreach (string raw in text.Split('+'))
            {
                string p = raw.Trim().ToUpperInvariant(); uint modifier = 0;
                if (p == "CTRL" || p == "CONTROL") modifier = 2;
                else if (p == "ALT") modifier = 1;
                else if (p == "SHIFT") modifier = 4;
                else if (p == "WIN") modifier = 8;
                if (modifier != 0)
                {
                    if ((mods & modifier) != 0) return false;
                    mods |= modifier; continue;
                }
                if (key != 0) return false;
                int function;
                if (p.Length == 1 && ((p[0] >= 'A' && p[0] <= 'Z') || (p[0] >= '0' && p[0] <= '9'))) key = p[0];
                else if (p.Length >= 2 && p[0] == 'F' && int.TryParse(p.Substring(1), out function) && function >= 1 && function <= 24) key = (uint)(0x6F + function);
                else
                {
                    switch (p)
                    {
                        case "PRTSCN": case "PRINTSCREEN": key = 0x2C; break;
                        case "SPACE": key = 0x20; break; case "TAB": key = 9; break;
                        case "ESC": case "ESCAPE": key = 0x1B; break; case "ENTER": case "RETURN": key = 13; break;
                        case "INSERT": key = 0x2D; break; case "DELETE": key = 0x2E; break;
                        case "HOME": key = 0x24; break; case "END": key = 0x23; break;
                        case "PAGEUP": key = 0x21; break; case "PAGEDOWN": key = 0x22; break;
                        default: return false;
                    }
                }
            }
            return key > 0 && (mods != 0 || key == 0x2C || (key >= 0x70 && key <= 0x87));
        }
    }

    public sealed class History<T>
    {
        readonly List<T> undo = new List<T>(), redo = new List<T>();
        readonly int limit;
        public History(int limit) { this.limit = limit; }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public void Push(T before) { undo.Add(before); if (undo.Count > limit) undo.RemoveAt(0); redo.Clear(); }
        public T Undo(T current) { if (!CanUndo) return current; redo.Add(current); T old = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); return old; }
        public T Redo(T current) { if (!CanRedo) return current; undo.Add(current); T old = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); return old; }
    }
}
