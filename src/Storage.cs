using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pegline
{
    internal sealed class Card
    {
        public Guid Id = Guid.NewGuid();
        public string Path, Hash;
        public bool Owned, Falling, Flying, HasFlown;
        public bool Reloading; public int PixelWidth, PixelHeight;
        public double Tilt, AddedAt, NudgeAt = -100, NudgeDegrees, CopiedUntil, FallAt;
        public BitmapSource Thumbnail;
        public DateTime Stamp;
        public Box LastBox;
        public Card(string path, bool owned, BitmapSource thumb, double now)
        { Path = path; Owned = owned; Thumbnail = thumb; AddedAt = now; Tilt = (Guid.NewGuid().GetHashCode() % 2500) / 1000.0; Stamp = File.GetLastWriteTimeUtc(path); }
    }
    internal sealed class Store
    {
        public readonly string Root, Inbox, Recovery, PreferencesPath, SessionPath, CatalogPath;
        public readonly CaptureCatalog Catalog;
        public Preferences Preferences;
        internal readonly string StandardScreenshotsFolder;
        public Store() : this(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pegline")) { }
        internal Store(string root) : this(root, null) { }
        internal Store(string root, string screenshotsFolder)
        {
            StandardScreenshotsFolder = screenshotsFolder == null ? Native.ScreenshotsFolder() : Path.GetFullPath(screenshotsFolder);
            Root = Path.GetFullPath(root);
            Inbox = System.IO.Path.Combine(Root, "Inbox"); Recovery = System.IO.Path.Combine(Root, "Recovery");
            PreferencesPath = System.IO.Path.Combine(Root, "preferences.json"); SessionPath = System.IO.Path.Combine(Root, "session.json");
            Directory.CreateDirectory(Root); Directory.CreateDirectory(Inbox); Directory.CreateDirectory(Recovery);
            Preferences = SafeFiles.ReadJson(PreferencesPath, new Preferences()); Preferences.Normalize();
            CatalogPath = Path.Combine(Root, "recent.json");
            Catalog = new CaptureCatalog(SafeFiles.ReadJson(CatalogPath, new List<RecentCapture>()));
        }
        public List<SavedCard> Restore()
        {
            var saved = SafeFiles.ReadJson(SessionPath, new List<SavedCard>()) ?? new List<SavedCard>();
            // A normal session has at most 12 cards. Do not decode an unbounded
            // edited/corrupt session before applying the screen capacity limit.
            return saved.Skip(Math.Max(0, saved.Count - 12)).ToList();
        }
        public void Save(IEnumerable<Card> items)
        { SafeFiles.WriteJson(SessionPath, items.Where(i => !i.Falling).Select(i => new SavedCard { Path = i.Path, Owned = i.Owned, Tilt = i.Tilt }).ToList()); }
        public void SaveCatalog() { SafeFiles.WriteJson(CatalogPath, Catalog.Items.ToList()); }
        public void SavePreferences() { SafeFiles.WriteJson(PreferencesPath, Preferences); }
        public bool IsOwned(Card card) { return card.Owned && SafeFiles.OwnedLocation(Inbox, card.Path); }
        public string SaveCapture(BitmapSource image)
        {
            string folder = Preferences.ManagedInbox ? Inbox : StandardScreenshotsFolder; Directory.CreateDirectory(folder);
            string path = SafeFiles.Unique(folder, "Capture " + DateTime.Now.ToString("yyyy-MM-dd HHmmss-fff") + ".png");
            SafeFiles.AtomicCreate(path, Images.Encode(image, ".png")); return path;
        }
        public string SaveClipboard(BitmapSource image)
        {
            string path = SafeFiles.Unique(Inbox, "Clipboard " + DateTime.Now.ToString("yyyy-MM-dd HHmmss-fff") + ".png");
            SafeFiles.AtomicCreate(path, Images.Encode(image, ".png")); return path;
        }
        public string Backup(string path)
        {
            string backup = System.IO.Path.Combine(Recovery, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(path));
            File.Copy(path, backup, false); return backup;
        }
    }

    // A watcher is a hint, not a guarantee. Stable reads, limited concurrency,
    // a startup baseline and periodic reconciliation cover rename/burst/overflow cases.
    internal sealed class ScreenshotWatcher : IDisposable
    {
        sealed class Stamp
        {
            public long Length; public DateTime Written, Created;
            public static Stamp Read(string path)
            {
                var f = new FileInfo(path); if (!f.Exists) return null;
                return new Stamp { Length = f.Length, Written = f.LastWriteTimeUtc, Created = f.CreationTimeUtc };
            }
            public bool Same(Stamp b) { return b != null && Length == b.Length && Written == b.Written; }
        }
        readonly string folder;
        readonly Dispatcher dispatcher;
        readonly Func<string, bool, Task> onFile;
        readonly Action onDelete;
        readonly Action<string> onError;
        readonly Dictionary<string, Stamp> known = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> beforeReady = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly System.Threading.SemaphoreSlim workers = new System.Threading.SemaphoreSlim(2);
        readonly System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource();
        readonly DateTime launched = DateTime.UtcNow;
        FileSystemWatcher watcher;
        DispatcherTimer fallback;
        bool disposed, scanning, errorReported, ready;

        public ScreenshotWatcher(string folder, Dispatcher dispatcher, Func<string, bool, Task> onFile, Action onDelete, Action<string> onError)
        {
            this.folder = Path.GetFullPath(folder); this.dispatcher = dispatcher;
            this.onFile = onFile; this.onDelete = onDelete; this.onError = onError;
            TryWatch();
            fallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            fallback.Tick += delegate { Scan(); }; fallback.Start();
            Initialize();
        }
        Dictionary<string, Stamp> Listing()
        {
            var result = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folder)) return result;
            foreach (string path in Directory.EnumerateFiles(folder).Where(Images.SupportedPath))
            {
                if (stop.IsCancellationRequested) break;
                try { var stamp = Stamp.Read(path); if (stamp != null) result[path] = stamp; }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            return result;
        }
        async void Initialize()
        {
            try
            {
                var baseline = await Task.Run(new Func<Dictionary<string, Stamp>>(Listing));
                if (disposed) return;
                foreach (var pair in baseline)
                    if (pair.Value.Created < launched) known[pair.Key] = pair.Value;
                ready = true;
                foreach (string path in beforeReady.Concat(baseline.Where(p => !known.ContainsKey(p.Key)).Select(p => p.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()) Check(path);
                beforeReady.Clear();
            }
            catch (Exception e) { if (!disposed) { ready = true; Report(e); } }
        }
        void Post(Action callback)
        {
            if (disposed || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            try { dispatcher.BeginInvoke(new Action(delegate { if (!disposed) callback(); })); }
            catch (InvalidOperationException) { } // Dispatcher is closing.
        }
        void TryWatch()
        {
            if (disposed || watcher != null || !Directory.Exists(folder)) return;
            try
            {
                watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false, InternalBufferSize = 16384 };
                watcher.Created += delegate(object s, FileSystemEventArgs e) { Queue(e.FullPath); };
                watcher.Changed += delegate(object s, FileSystemEventArgs e) { Queue(e.FullPath); };
                watcher.Renamed += delegate(object s, RenamedEventArgs e) { Queue(e.FullPath); Post(onDelete); };
                watcher.Deleted += delegate { Post(onDelete); };
                watcher.Error += delegate { Post(delegate { ResetWatch(); Scan(); }); };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { ResetWatch(); Report(e); }
        }
        void ResetWatch() { if (watcher != null) watcher.Dispose(); watcher = null; }
        void Report(Exception e)
        {
            if (!errorReported && !disposed)
            { errorReported = true; onError(Ui.L("A screenshot folder could not be read; it will be retried. ", "No se pudo leer una carpeta de capturas; se reintentará. ") + e.Message); }
        }
        void Queue(string path)
        {
            if (!Images.SupportedPath(path)) return;
            Post(delegate { if (!ready) { if (beforeReady.Count < 512) beforeReady.Add(path); return; } Check(path); });
        }
        async void Check(string path)
        {
            if (disposed || pending.Count >= 512 || !pending.Add(path)) return;
            bool acquired = false;
            try
            {
                await workers.WaitAsync(stop.Token); acquired = true;
                for (int attempt = 0; attempt < 12 && !disposed; attempt++)
                {
                    Stamp before = Stamp.Read(path); if (before == null) return;
                    await Task.Delay(150 + Math.Min(attempt, 4) * 50, stop.Token);
                    if (disposed) return;
                    Stamp after = Stamp.Read(path); if (after == null) return;
                    if (before.Length == 0 || !before.Same(after)) continue;
                    Stamp previous; bool isNew = !known.TryGetValue(path, out previous);
                    if (!isNew && previous.Same(after)) return;
                    try
                    {
                        await onFile(path, isNew);
                        if (disposed) return;
                        // A second write during decoding must not become the baseline.
                        if (!after.Same(Stamp.Read(path))) continue;
                        known[path] = after; errorReported = false; return;
                    }
                    catch (Exception) { if (attempt == 11) throw; }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Report(e); }
            finally { if (acquired) workers.Release(); pending.Remove(path); }
        }
        async void Scan()
        {
            if (disposed || !ready || scanning) return; scanning = true;
            try
            {
                if (!Directory.Exists(folder)) { ResetWatch(); return; }
                TryWatch();
                var files = await Task.Run(new Func<Dictionary<string, Stamp>>(Listing));
                if (disposed) return;
                foreach (string old in known.Keys.Where(k => !files.ContainsKey(k)).ToArray()) known.Remove(old);
                foreach (var pair in files)
                { Stamp old; if (!known.TryGetValue(pair.Key, out old) || !old.Same(pair.Value)) Check(pair.Key); }
                onDelete();
            }
            catch (Exception e) { Report(e); }
            finally { scanning = false; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; stop.Cancel();
            if (fallback != null) fallback.Stop(); ResetWatch(); beforeReady.Clear();
            // WaitAsync continuations may still release the semaphore; do not dispose it here.
        }
    }
}
