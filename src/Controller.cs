using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Pegline
{
    internal sealed class Controller : IDisposable
    {
        public readonly List<Card> Cards = new List<Card>();
        public Card Dragging { get; private set; }
        public double Now { get { return clock.Elapsed.TotalSeconds; } }
        public bool ReducedMotion { get { return store.Preferences.ReducedMotion || !SystemParameters.ClientAreaAnimation; } }
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dispatcher dispatcher;
        readonly Store store;
        readonly VisibilityState state = new VisibilityState();
        readonly RecentIngress dedup = new RecentIngress();
        readonly List<ScreenshotWatcher> watchers = new List<ScreenshotWatcher>();
        readonly Dictionary<string, double> ownWrites = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, EditorWindow> editors = new Dictionary<string, EditorWindow>(StringComparer.OrdinalIgnoreCase);
        readonly Random random = new Random();
        readonly List<int> hotkeys = new List<int>();
        readonly Dictionary<int, Action> keyActions = new Dictionary<int, Action>();
        readonly HashSet<uint> pendingClipboard = new HashSet<uint>();
        readonly Queue<uint> handledClipboard = new Queue<uint>();
        readonly System.Threading.SemaphoreSlim importGate = new System.Threading.SemaphoreSlim(1);
        readonly System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource();
        LibraryWindow library;
        SettingsWindow settings;
        public event Action LibraryChanged;
        public IEnumerable<RecentCapture> RecentItems { get { return store.Catalog.Items; } }
        public bool CollectionPaused { get; private set; }
        ShelfWindow shelf;
        Forms.NotifyIcon tray;
        HwndSource messages;
        DispatcherTimer mouse, housekeeping;
        bool keepOpen, capturing, disposed;
        int collectionGeneration;
        double nextGust, nextDesktopCheck;
        public Controller(Dispatcher dispatcher) : this(dispatcher, new Store()) { }
        internal Controller(Dispatcher dispatcher, Store store) { this.dispatcher = dispatcher; this.store = store; Ui.Preferences = store.Preferences; }
        internal Store LocalStore { get { return store; } }
        public void Start()
        {
            shelf = new ShelfWindow(this); shelf.Place(Native.PointerScreen);
            var parameters = new HwndSourceParameters("PeglineMessages") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            messages = new HwndSource(parameters); messages.AddHook(Message);
            CreateTray(); RegisterHotkeys();
            if (!Native.AddClipboardFormatListener(messages.Handle)) Info(Ui.L("Windows did not enable clipboard listening. Folder capture still works.", "Windows no habilitó la escucha del portapapeles. Las capturas en carpetas siguen funcionando."));
            foreach (var saved in store.Restore())
            {
                if (saved == null || saved.Path == null || !File.Exists(saved.Path) || Cards.Any(c => SafeFiles.Same(c.Path, saved.Path))) continue;
                try
                {
                    var c = new Card(saved.Path, saved.Owned && SafeFiles.OwnedLocation(store.Inbox, saved.Path), Images.Load(saved.Path, 480), Now - 10) { Tilt = ShelfLayout.Clamp(saved.Tilt, -2.5, 2.5) };
                    Cards.Add(c);
                }
                catch (Exception e) { Info(Ui.L("A saved image could not be restored: ", "No se pudo restaurar una imagen guardada: ") + e.Message); }
            }
            StartWatchers(); LimitCapacity();
            mouse = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) }; mouse.Tick += delegate { Tick(); }; mouse.Start();
            housekeeping = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            housekeeping.Tick += delegate
            {
                Prune(); foreach (string p in ownWrites.Keys.Where(p => Now - ownWrites[p] > 15).ToArray()) ownWrites.Remove(p);
                if (!Forms.Screen.AllScreens.Any(s => s.DeviceName == shelf.Screen.DeviceName)) shelf.Place(Native.PointerScreen);
            }; housekeeping.Start();
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            nextGust = Now + 7 + random.NextDouble() * 9;
            if (!store.Preferences.Welcomed)
            {
                store.Preferences.Welcomed = true; SavePreferences(); keepOpen = true; state.Show(Now, true, 5); shelf.SetRevealed(true);
                Info(Ui.L("Capture: Ctrl+Alt+4. Show the line: Ctrl+Alt+T. Right-click the tray icon for tools and settings. No telemetry or updates.", "Capturar: Ctrl+Alt+4. Mostrar: Ctrl+Alt+T. Clic derecho en la bandeja para herramientas y ajustes. Sin telemetría ni actualizaciones."), false);
                Later(5, delegate { if (Cards.Count == 0) { keepOpen = false; state.Hide(); shelf.SetRevealed(false); } });
            }
        }
        void DisplayChanged(object sender, EventArgs e)
        { if (!disposed) dispatcher.BeginInvoke(new Action(delegate { if (!disposed) { shelf.Place(Native.PointerScreen); LimitCapacity(); } })); }
        IntPtr Message(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY) { Action action; if (keyActions.TryGetValue(w.ToInt32(), out action)) action(); handled = true; }
            else if (msg == Native.WM_CLIPBOARDUPDATE) ReadClipboard();
            return IntPtr.Zero;
        }
        void Tick()
        {
            if (disposed || capturing) return;
            bool wanted = keepOpen || Cards.Any(c => !c.Falling);
            if (!wanted && !state.Revealed) return;
            var p = Native.Cursor; var pointerScreen = Native.PointerScreen;
            int band = Math.Max(3, pointerScreen.WorkingArea.Top - pointerScreen.Bounds.Top);
            bool inBand = p.Y >= pointerScreen.Bounds.Top && p.Y < pointerScreen.Bounds.Top + band;
            if (!state.Revealed && inBand && Dragging == null && shelf.Screen.DeviceName != pointerScreen.DeviceName) { shelf.Place(pointerScreen); LimitCapacity(); }
            bool sameScreen = pointerScreen.DeviceName == shelf.Screen.DeviceName;
            Point local = shelf.ToLocal(p);
            bool busy = Dragging != null || shelf.View.IsPressed || shelf.View.MenuOpen;
            bool inside = sameScreen && local.X >= 0 && local.X < shelf.View.ActualWidth && p.Y >= shelf.Screen.Bounds.Top && local.Y <= ShelfLayout.Height;
            bool button = (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
            state.Tick(Now, wanted, Native.FullScreen(shelf.Screen), sameScreen && inBand, inside, busy, button);
            shelf.SetRevealed(state.Revealed);
            if (state.Revealed && !busy)
            { shelf.View.SetPointer(local); shelf.SetClickThrough(shelf.View.Hit(local) == null); }
            if (Now >= nextDesktopCheck) { shelf.FollowDesktop(); nextDesktopCheck = Now + .4; }
            if (Now >= nextGust)
            {
                nextGust = Now + 7 + random.NextDouble() * 9;
                if (state.Revealed && !busy && !ReducedMotion)
                    foreach (var item in Cards.Where(c => !c.Falling).ToArray())
                    { Card card = item; Later(random.NextDouble() * .35, delegate { Nudge(card, 1.6 + random.NextDouble() * 1.8); }); }
            }
        }
        void Nudge(Card card, double degrees) { card.NudgeAt = Now; card.NudgeDegrees = degrees; shelf.View.Kick(); }
        public void Toggle()
        {
            if (disposed || capturing) return;
            if (state.Revealed) { state.Hide(); if (!Cards.Any(c => !c.Falling)) keepOpen = false; }
            else
            {
                shelf.Place(Native.PointerScreen); LimitCapacity(); keepOpen = true;
                if (!Native.FullScreen(shelf.Screen)) state.Show(Now, true, 0);
            }
            shelf.SetRevealed(state.Revealed);
        }
        void StartWatchers()
        {
            int generation = ++collectionGeneration;
            foreach (var w in watchers) w.Dispose(); watchers.Clear();
            if (CollectionPaused || disposed) return;
            string screenshots = store.StandardScreenshotsFolder;
            try { Directory.CreateDirectory(screenshots); } catch (Exception e) { Info(e.Message); }
            var folders = new List<string> { screenshots, store.Inbox }; folders.AddRange(store.Preferences.Folders);
            foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    watchers.Add(new ScreenshotWatcher(folder, dispatcher, async delegate(string file, bool isNew)
                    {
                        if (disposed || generation != collectionGeneration) return;
                        Card existing = Cards.FirstOrDefault(c => !c.Falling && SafeFiles.Same(c.Path, file));
                        if (existing != null) { await ReloadAsync(existing); return; }
                        if (ownWrites.ContainsKey(file)) return;
                        if (isNew) await HangAsync(file, false, IngressKind.Folder, null, null);
                    }, Prune, InfoWarning));
                }
                catch (Exception e) { Info(Ui.L("A folder is unavailable: ", "Una carpeta no está disponible: ") + e.Message); }
            }
        }
        void InfoWarning(string text) { Info(text); }
        async Task HangAsync(string path, bool owned, IngressKind kind, Box? origin, BitmapSource supplied)
        {
            bool acquired = false;
            int generation = collectionGeneration;
            bool automatic = kind == IngressKind.Folder || kind == IngressKind.Clipboard;
            try
            {
                await importGate.WaitAsync(stop.Token); acquired = true;
                if (disposed || (automatic && (CollectionPaused || generation != collectionGeneration))) return;
                if (Cards.Any(c => !c.Falling && SafeFiles.Same(c.Path, path))) return;
                var prepared = await Task.Run(delegate { return Images.Prepare(path, supplied); });
                if (disposed) return;
                // Never move the line to a different monitor beneath an active gesture.
                while (!disposed && (Dragging != null || shelf.View.IsPressed || shelf.View.MenuOpen)) await Task.Delay(75, stop.Token);
                if (!disposed && !(automatic && (CollectionPaused || generation != collectionGeneration))) HangPrepared(path, owned, kind, origin, prepared);
            }
            catch (OperationCanceledException) { }
            finally { if (acquired) importGate.Release(); }
        }
        void HangPrepared(string path, bool owned, IngressKind kind, Box? origin, PreparedImage prepared)
        {
            string hash = prepared.Hash;
            string twinPath = dedup.FindTwin(hash, kind, Now);
            if (twinPath != null)
            {
                Card twin = Cards.FirstOrDefault(c => !c.Falling && SafeFiles.Same(c.Path, twinPath));
                if (twin != null)
                {
                    // Prefer the external saved file. Keep the old owned copy on disk; no silent deletion.
                    if (twin.Owned && !owned) { store.Catalog.Forget(twin.Path); twin.Path = path; twin.Owned = false; twin.Stamp = File.GetLastWriteTimeUtc(path); Track(twin, prepared.Width, prepared.Height); Save(); }
                    return;
                }
            }
            dedup.Remember(hash, path, kind, Now);
            var monitor = origin.HasValue ? Forms.Screen.FromPoint(new System.Drawing.Point((int)origin.Value.CenterX, (int)origin.Value.CenterY)) : Native.PointerScreen;
            shelf.Place(monitor);
            BitmapSource thumb = prepared.Thumbnail;
            var card = new Card(path, owned && SafeFiles.OwnedLocation(store.Inbox, path), thumb, Now) { Hash = hash };
            card.HasFlown = origin.HasValue && !ReducedMotion && store.Preferences.ShowNewCaptures; card.Flying = card.HasFlown;
            Cards.Add(card); Track(card, prepared.Width, prepared.Height); LimitCapacity(); Save();
            if (store.Preferences.Sound) Sounds.Play(0);
            if (!Native.FullScreen(monitor) && store.Preferences.ShowNewCaptures) { state.Show(Now, false, ShelfLayout.PeekDuration); shelf.SetRevealed(true); }
            else { card.Flying = false; card.HasFlown = true; }
            shelf.View.Kick();
            if (card.Flying && origin.HasValue)
            {
                Box source = origin.Value;
                Later(.04, delegate
                {
                    if (!state.Revealed || card.Falling || !shelf.IsVisible) { Land(card); return; }
                    shelf.View.UpdateLayout();
                    try
                    {
                        var from = new System.Drawing.RectangleF((float)source.X, (float)source.Y, (float)source.Width, (float)source.Height);
                        Flight.Arrive(Images.ToGdi(Images.Load(card.Path, 3000)), from, shelf.ToScreen(shelf.View.RestingBox(card)), card.Tilt, shelf.Scale, delegate { Land(card); });
                    }
                    catch { Land(card); }
                });
            }
        }
        void Land(Card card) { card.Flying = false; card.HasFlown = true; if (!disposed) Nudge(card, 2.2); }
        void LimitCapacity()
        {
            double width = shelf.View.ActualWidth > 0 ? shelf.View.ActualWidth : shelf.Screen.WorkingArea.Width / shelf.Scale;
            while (Cards.Count(c => !c.Falling) > ShelfLayout.Capacity(width)) Drop(Cards.First(c => !c.Falling), true);
        }
        void Drop(Card card, bool quiet)
        {
            if (card == null || card.Falling) return;
            if (state.Revealed && !card.Flying && !ReducedMotion && card.LastBox.Width > 0)
            { try { Flight.Fall(Images.ToGdi(card.Thumbnail), shelf.ToScreen(card.LastBox), card.Tilt, shelf.Scale); } catch { } }
            card.Falling = true; Save(); if (!quiet && store.Preferences.Sound) Sounds.Play(1);
            shelf.View.Kick();
            Later(.6, delegate
            {
                Cards.Remove(card); shelf.View.Kick();
                if (!Cards.Any(c => !c.Falling)) { keepOpen = false; state.Hide(); shelf.SetRevealed(false); }
            });
        }
        void Clear()
        {
            var live = Cards.Where(c => !c.Falling).ToArray();
            for (int n = 0; n < live.Length; n++) { Card card = live[n]; bool quiet = n > 0; Later(.06 * n, delegate { Drop(card, quiet); }); }
        }
        void Prune()
        {
            if (disposed) return;
            foreach (var c in Cards.Where(c => !c.Falling).ToArray())
            {
                try
                {
                    if (!File.Exists(c.Path)) Drop(c, true);
                    else if (File.GetLastWriteTimeUtc(c.Path) != c.Stamp) ReloadLater(c);
                }
                catch (IOException) { /* File may still be written: next watcher pass retries. */ }
                catch (UnauthorizedAccessException) { /* Keep the card; do not delete an inaccessible file. */ }
            }
        }
        async Task ReloadAsync(Card card)
        {
            if (card.Reloading || card.Falling || disposed) return;
            card.Reloading = true; string path = card.Path;
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                var prepared = await Task.Run(delegate { return Images.Prepare(path, null); });
                if (disposed || card.Falling || !SafeFiles.Same(path, card.Path)) return;
                if (stamp != File.GetLastWriteTimeUtc(path)) throw new IOException("Image is still being written.");
                card.Thumbnail = prepared.Thumbnail; card.Hash = prepared.Hash; card.Stamp = stamp;
                card.PixelWidth = prepared.Width; card.PixelHeight = prepared.Height;
                shelf.View.Kick();
            }
            finally { card.Reloading = false; }
        }
        async void ReloadLater(Card card)
        { try { await ReloadAsync(card); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (Exception e) { Info(e.Message); } }
        public async void Copy(Card card)
        {
            if (card == null || disposed) return;
            string path = card.Path;
            try
            {
                BitmapSource image = await Task.Run(delegate { return Images.Load(path, 0); });
                for (int attempt = 0; attempt < 6 && !disposed; attempt++)
                {
                    try { Images.PutClipboard(image, path); card.CopiedUntil = Now + 1.2; Nudge(card, 3); return; }
                    catch (COMException) { if (attempt == 5) throw; }
                    await Task.Delay(60);
                }
            }
            catch (Exception e) { Info(Ui.L("Could not copy: ", "No se pudo copiar: ") + e.Message); }
        }
        async void ReadClipboard()
        {
            if (disposed || CollectionPaused || (!store.Preferences.ClipboardSnips && !store.Preferences.AllClipboardImages)) return;
            uint sequence = Native.GetClipboardSequenceNumber(); int generation = collectionGeneration;
            if (handledClipboard.Contains(sequence) || !pendingClipboard.Add(sequence)) return;
            string owner = Native.ClipboardOwnerName();
            bool snip = new string[] { "SnippingTool", "ScreenClippingHost", "ScreenSketch" }.Any(s => s.Equals(owner, StringComparison.OrdinalIgnoreCase));
            try
            {
                if (!store.Preferences.AllClipboardImages && (!snip || !store.Preferences.ClipboardSnips)) return;
                for (int attempt = 0; attempt < 6 && !disposed; attempt++)
                {
                    await Task.Delay(70 + attempt * 30);
                    if (sequence != Native.GetClipboardSequenceNumber() || disposed || generation != collectionGeneration) return;
                    try
                    {
                        BitmapSource image = Images.ReadClipboard(); if (image == null) return;
                        // A producer may have replaced the clipboard while GetData rendered formats.
                        if (sequence != Native.GetClipboardSequenceNumber() || disposed || CollectionPaused || generation != collectionGeneration) return;
                        string path = store.SaveClipboard(image); ownWrites[path] = Now;
                        await HangAsync(path, true, IngressKind.Clipboard, null, image);
                        handledClipboard.Enqueue(sequence); while (handledClipboard.Count > 64) handledClipboard.Dequeue(); return;
                    }
                    catch (System.Runtime.InteropServices.ExternalException) { if (attempt == 5) Info(Ui.L("Clipboard was busy; this image was not collected.", "El portapapeles estaba ocupado; no se recogió esta imagen.")); }
                }
            }
            catch (Exception e) { Info(Ui.L("Could not collect the clipboard image: ", "No se pudo recoger la imagen del portapapeles: ") + e.Message); }
            finally { pendingClipboard.Remove(sequence); }
        }
        public void Open(Card card) { Shell(card.Path, null); }
        public void Discard(Card card) { if (store.IsOwned(card)) Trash(card); else Drop(card, false); }
        void Trash(Card card)
        {
            try { Recycle(card.Path); Drop(card, true); if (store.Preferences.Sound) Sounds.Play(2); }
            catch (Exception e) { Info(Ui.L("The file was not discarded: ", "El archivo no se descartó: ") + e.Message); }
        }
        static void Recycle(string path)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));
            if (drive.DriveType != DriveType.Fixed) throw new IOException(Ui.L("A safe Recycle Bin operation is not available on this drive. Manage the file in Explorer instead.", "No hay una operación segura de Papelera en esta unidad. Gestiona el archivo en el Explorador."));
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
        void SaveToDesktop(Card card)
        {
            try
            {
                string destination = SafeFiles.Unique(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetFileName(card.Path));
                if (string.Equals(Path.GetPathRoot(card.Path), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase)) File.Move(card.Path, destination);
                else
                {
                    File.Copy(card.Path, destination, false);
                    if (SafeFiles.FileHash(card.Path) != SafeFiles.FileHash(destination)) throw new IOException("The copy could not be verified. Both files were retained.");
                    // The verified destination is preserved even if source recycling is cancelled.
                    Recycle(card.Path);
                }
                Drop(card, true);
            }
            catch (Exception e) { Info(Ui.L("Could not save to the Desktop: ", "No se pudo guardar en el Escritorio: ") + e.Message); }
        }
        public ContextMenu MenuFor(Card card)
        {
            var menu = new ContextMenu();
            Add(menu, Ui.L("Copy", "Copiar"), delegate { Copy(card); }); Add(menu, Ui.L("Open", "Abrir"), delegate { Open(card); });
            Add(menu, Ui.L("Markup", "Marcación"), delegate { Edit(card); });
            Add(menu, Ui.L("Show in Explorer", "Mostrar en el Explorador"), delegate { Shell("explorer.exe", "/select,\"" + card.Path + "\""); });
            bool own = store.IsOwned(card); if (own) Add(menu, Ui.L("Save to Desktop", "Guardar en el Escritorio"), delegate { SaveToDesktop(card); });
            menu.Items.Add(new Separator()); Add(menu, own ? Ui.L("Discard", "Descartar") : Ui.L("Take down", "Descolgar"), delegate { Discard(card); });
            if (!own) Add(menu, Ui.L("Move to Recycle Bin", "Mover a la Papelera"), delegate { Trash(card); }); return menu;
        }
        static void Add(ContextMenu menu, string label, Action action)
        { var i = new MenuItem { Header = label }; i.Click += delegate { action(); }; menu.Items.Add(i); }
        public void Drag(Card card)
        {
            if (card.Falling || !File.Exists(card.Path)) return;
            Dragging = card; shelf.SetClickThrough(false); shelf.View.Kick();
            System.Runtime.InteropServices.ComTypes.IDataObject shell = null;
            try
            {
                shell = Native.ShellObject(card.Path); DragDropEffects effect; int? performed = null;
                if (shell != null)
                {
                    Native.PreferMove(shell);
                    using (var bitmap = Images.ToGdi(card.Thumbnail)) Native.DragImage(shell, bitmap);
                    effect = System.Windows.DragDrop.DoDragDrop(shelf.View, new DataObject(shell), DragDropEffects.Copy | DragDropEffects.Move);
                    performed = Native.DropEffect(shell, "Performed DropEffect");
                }
                else
                {
                    var data = new DataObject(DataFormats.FileDrop, new string[] { card.Path });
                    effect = System.Windows.DragDrop.DoDragDrop(shelf.View, data, DragDropEffects.Copy); // Safe fallback cannot silently remove the source.
                }
                // Only explicit, mutually confirmed unoptimized move completion authorizes source cleanup.
                // Optimized Explorer/Recycle Bin moves already removed the file: never delete twice.
                if (effect == DragDropEffects.Move && performed == 2 && File.Exists(card.Path)) Recycle(card.Path);
                if (!File.Exists(card.Path)) Drop(card, true);
                else if (effect == DragDropEffects.None && performed != 2 && !ReducedMotion && state.Revealed)
                {
                    var p = Native.Cursor; var to = shelf.ToScreen(shelf.View.RestingBox(card));
                    var from = new System.Drawing.RectangleF(p.X - to.Width / 2, p.Y - to.Height / 2, to.Width, to.Height);
                    card.Flying = true; Flight.Return(Images.ToGdi(card.Thumbnail), from, to, card.Tilt, shelf.Scale, delegate { Land(card); });
                }
            }
            catch (Exception e) { Info(Ui.L("Drag could not finish: ", "No se pudo completar el arrastre: ") + e.Message); }
            finally
            {
                // Release our COM reference only. Destinations retaining the data
                // object keep their own reference for delayed file consumption.
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
                Dragging = null; if (!disposed) { shelf.View.Kick(); Prune(); Later(.6, Prune); Later(1.5, Prune); }
            }
        }
        public void Edit(Card card)
        {
            try
            {
                EditorWindow existing;
                if (editors.TryGetValue(card.Path, out existing)) { existing.Activate(); return; }
                var editor = new EditorWindow(card.Path, store); editors[card.Path] = editor;
                editor.CanSaveTarget = delegate(string target) { return !editors.Any(pair => pair.Value != editor && SafeFiles.Same(pair.Key, target)); };
                editor.Saved += delegate(string oldPath, string newPath)
                {
                    bool same = SafeFiles.Same(oldPath, newPath); if (!same) { editors.Remove(oldPath); editors[newPath] = editor; card.Owned = false; }
                    ownWrites[newPath] = Now; card.Path = newPath;
                    ReloadLater(card); Track(card, editor.DocumentWidth, editor.DocumentHeight); Save();
                };
                editor.Closed += delegate { foreach (string key in editors.Keys.Where(k => editors[k] == editor).ToArray()) editors.Remove(key); };
                editor.Show(); editor.Activate();
            }
            catch (Exception e) { Info(Ui.L("Could not open Markup: ", "No se pudo abrir Marcación: ") + e.Message); }
        }
        async void BeginCapture(CaptureMode mode)
        {
            if (capturing || disposed || Dragging != null) return; capturing = true;
            BitmapSource result = null; Box origin = new Box();
            try
            {
                state.Hide(); shelf.ForceHide(); Flight.CloseAll();
                await Task.Delay(140); if (disposed) return;
                CaptureService.Run(mode, store.Preferences, delegate(BitmapSource image, Box bounds, bool repeatable)
                {
                    result = image; origin = bounds;
                    if (repeatable)
                    {
                        store.Preferences.LastRegion = new CaptureRegion { X = (int)bounds.X, Y = (int)bounds.Y, Width = (int)bounds.Width, Height = (int)bounds.Height,
                            DisplaySignature = CaptureService.DisplaySignature() }; SavePreferences();
                    }
                });
            }
            catch (Exception e) { Info(Ui.L("Capture failed: ", "Error de captura: ") + e.Message); }
            finally { capturing = false; }
            if (result == null || disposed) return;
            try
            {
                string path = store.SaveCapture(result); ownWrites[path] = Now; await HangAsync(path, store.Preferences.ManagedInbox, IngressKind.Managed, origin, result);
                if (store.Preferences.CopyCaptures)
                {
                    var card = Cards.FirstOrDefault(c => SafeFiles.Same(c.Path, path)); if (card != null) Copy(card);
                }
            }
            catch (Exception e) { Info(Ui.L("Could not save the capture: ", "No se pudo guardar la captura: ") + e.Message); }
        }
        void CreateTray()
        {
            System.Drawing.Icon icon;
            try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); }
            catch { icon = null; }
            if (icon == null) icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
            tray = new Forms.NotifyIcon { Icon = icon, Text = "Pegline", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
            tray.MouseClick += delegate(object s, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
            tray.ContextMenuStrip.Opening += delegate { BuildTray(); }; BuildTray();
        }
        void BuildTray()
        {
            var menu = tray.ContextMenuStrip;
            foreach (Forms.ToolStripItem old in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) old.Dispose(); menu.Items.Clear();
            Func<string, Action, Forms.ToolStripMenuItem> add = delegate(string label, Action action)
            { var item = new Forms.ToolStripMenuItem(label); item.Click += delegate { action(); }; menu.Items.Add(item); return item; };
            add(state.Revealed ? Ui.L("Hide line", "Ocultar tendedero") : Ui.L("Show line", "Mostrar tendedero"), Toggle);
            add(Ui.L("Take everything down", "Descolgar todo"), Clear).Enabled = Cards.Any(c => !c.Falling);
            menu.Items.Add(new Forms.ToolStripSeparator());
            add(Ui.L("Screenshot library…", "Biblioteca de capturas…"), OpenLibrary);
            add(Ui.L("Repeat last region", "Repetir última zona"), delegate { BeginCapture(CaptureMode.LastRegion); }).Enabled = store.Preferences.LastRegion != null;
            add(Ui.L("Capture region", "Capturar zona"), delegate { BeginCapture(CaptureMode.Region); });
            add(Ui.L("Capture window", "Capturar ventana"), delegate { BeginCapture(CaptureMode.Window); });
            add(Ui.L("Capture this monitor", "Capturar este monitor"), delegate { BeginCapture(CaptureMode.Monitor); });
            add(Ui.L("More capture options…", "Más opciones de captura…"), delegate { CaptureService.Choose(BeginCapture, store.Preferences); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            add(Ui.L("Use a private capture inbox", "Usar bandeja privada de capturas"), delegate { store.Preferences.ManagedInbox = !store.Preferences.ManagedInbox; SavePreferences(); }).Checked = store.Preferences.ManagedInbox;
            add(Ui.L("Open screenshot folder", "Abrir carpeta de capturas"), delegate { Shell(Native.ScreenshotsFolder(), null); });
            add(Ui.L("Open Pegline inbox", "Abrir bandeja de Pegline"), delegate { Shell(store.Inbox, null); });
            add(Ui.L("Open edit backups", "Abrir copias de seguridad"), delegate { Shell(store.Recovery, null); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            add(Ui.L("Sounds", "Sonidos"), delegate { store.Preferences.Sound = !store.Preferences.Sound; SavePreferences(); }).Checked = store.Preferences.Sound;
            add(Ui.L("Start with Windows", "Iniciar con Windows"), delegate { try { SetStartup(!StartupEnabled); } catch (Exception e) { Info(e.Message); } }).Checked = StartupEnabled;
            add(CollectionPaused ? Ui.L("Resume collection", "Reanudar recogida") : Ui.L("Pause collection", "Pausar recogida"), delegate { CollectionPaused = !CollectionPaused; StartWatchers(); });
            add(Ui.L("Settings…", "Ajustes…"), Settings);
            add(Ui.L("About Pegline", "Acerca de Pegline"), delegate { MessageBox.Show(Ui.L("Pegline 0.2 — Windows screenshot clothesline\nInspired by Tendedero by Alejandro Buján. Independently named and branded.\n\nNo telemetry, network service, account, or automatic updates.\nSource build; see VERIFICATION.md for test status.", "Pegline 0.2 — Tendedero de capturas para Windows\nInspirado en Tendedero de Alejandro Buján. Nombre e identidad independientes.\n\nSin telemetría, red, cuenta ni actualizaciones automáticas.\nConsulta VERIFICATION.md para el estado de pruebas."), "Pegline"); });
            menu.Items.Add(new Forms.ToolStripSeparator()); add(Ui.L("Quit Pegline", "Salir de Pegline"), Quit);
        }
        void Settings()
        {
            if (settings != null) { settings.Activate(); return; }
            settings = new SettingsWindow(store.Preferences, StartupEnabled, ValidShortcut, SetStartup);
            try
            {
                if (settings.ShowDialog() == true)
                { Ui.Preferences = store.Preferences; SavePreferences(); Ui.RefreshTheme(); RegisterHotkeys(); StartWatchers(); shelf.View.Kick(); }
            }
            finally { settings = null; }
        }
        void Quit()
        {
            foreach (var editor in editors.Values.Distinct().ToArray()) { editor.Close(); if (editor.IsVisible) return; }
            Application.Current.Shutdown();
        }
        bool StartupEnabled
        {
            get { return string.Equals(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", "Pegline", "") as string, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"", StringComparison.OrdinalIgnoreCase); }
        }
        void SetStartup(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            { if (on) key.SetValue("Pegline", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\""); else key.DeleteValue("Pegline", false); }
        }
        internal static bool ParseShortcut(string text, out uint mods, out uint key)
        { return HotkeyChord.TryParse(text, out mods, out key); }
        static bool ValidShortcut(string text) { uint mods, key; return ParseShortcut(text, out mods, out key); }
        void RegisterHotkeys()
        {
            foreach (int id in hotkeys) Native.UnregisterHotKey(messages.Handle, id); hotkeys.Clear(); keyActions.Clear();
            Register(1, store.Preferences.ToggleKey, Toggle);
            Register(2, store.Preferences.RegionKey, delegate { BeginCapture(CaptureMode.Region); });
            Register(3, store.Preferences.ScreenKey, delegate { BeginCapture(CaptureMode.Monitor); });
            Register(4, store.Preferences.CaptureKey, delegate { CaptureService.Choose(BeginCapture, store.Preferences); });
            Register(5, store.Preferences.RepeatKey, delegate { BeginCapture(CaptureMode.LastRegion); });
            Register(6, store.Preferences.LibraryKey, OpenLibrary);
        }
        void Register(int id, string text, Action action)
        {
            uint mods, key; if (string.IsNullOrWhiteSpace(text)) return;
            if (!ParseShortcut(text, out mods, out key) || !Native.RegisterHotKey(messages.Handle, id, mods | 0x4000, key))
            { Info(Ui.L("Shortcut unavailable: ", "Atajo no disponible: ") + text + Ui.L(". Choose another in Settings.", ". Elige otro en Ajustes.")); return; }
            hotkeys.Add(id); keyActions[id] = action;
        }
        void Track(Card card, int width, int height)
        {
            card.PixelWidth = width; card.PixelHeight = height;
            store.Catalog.Track(card.Path, store.IsOwned(card), width, height, DateTime.UtcNow.Ticks);
            try { store.SaveCatalog(); } catch (Exception e) { Info(e.Message); }
            if (LibraryChanged != null) LibraryChanged();
        }
        public void OpenLibrary()
        {
            if (disposed) return;
            if (library != null) { library.Show(); library.Activate(); return; }
            library = new LibraryWindow(this); library.Closed += delegate { library = null; };
            library.Show(); library.Activate();
        }
        public async void RecentAction(string action, RecentCapture entry)
        {
            if (entry == null || disposed) return;
            try
            {
                if (action == "forget")
                {
                    store.Catalog.Forget(entry.Path); store.SaveCatalog(); if (LibraryChanged != null) LibraryChanged(); return;
                }
                if (!Images.SupportedPath(entry.Path)) throw new IOException("This library entry is not a supported image.");
                if (action == "reveal") { Shell("explorer.exe", "/select,\"" + entry.Path + "\""); return; }
                if (action == "open") { Shell(entry.Path, null); return; }
                if (action == "hang") { await HangAsync(entry.Path, entry.Owned, IngressKind.Manual, null, null); ToggleIfHidden(); return; }
                Card card = Cards.FirstOrDefault(c => !c.Falling && SafeFiles.Same(c.Path, entry.Path));
                if (card == null)
                {
                    var image = await Task.Run(delegate { return Images.Load(entry.Path, 480); });
                    if (disposed) return;
                    card = new Card(entry.Path, entry.Owned && SafeFiles.OwnedLocation(store.Inbox, entry.Path), image, Now);
                }
                if (action == "copy") Copy(card); else if (action == "edit") Edit(card);
            }
            catch (Exception e) { Info(e.Message); }
        }
        void ToggleIfHidden() { if (!state.Revealed) Toggle(); }
        public async void ImportFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths.Take(32))
            {
                if (disposed) return;
                if (!Images.SupportedPath(path)) continue;
                try { await HangAsync(path, false, IngressKind.Manual, null, null); }
                catch (Exception e) { Info(e.Message); }
            }
            ToggleIfHidden();
        }
        void Save() { try { store.Save(Cards); } catch (Exception e) { Info(Ui.L("Could not save the shelf: ", "No se pudo guardar el tendedero: ") + e.Message); } }
        void SavePreferences() { try { store.SavePreferences(); } catch (Exception e) { Info(e.Message); } }
        async void Later(double seconds, Action action)
        {
            try { await Task.Delay(Math.Max(1, (int)(seconds * 1000)), stop.Token); if (!disposed) action(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Info(e.Message); }
        }
        void Info(string text, bool warning = true)
        { if (!disposed && tray != null) tray.ShowBalloonTip(6000, "Pegline", text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info); }
        void Shell(string file, string args)
        { try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch (Exception e) { Info(e.Message); } }
        public void Dispose()
        {
            if (disposed) return; disposed = true; stop.Cancel();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (mouse != null) mouse.Stop(); if (housekeeping != null) housekeeping.Stop();
            foreach (var w in watchers) w.Dispose(); Flight.CloseAll();
            if (library != null) library.Close();
            if (messages != null) { foreach (int id in hotkeys) Native.UnregisterHotKey(messages.Handle, id); Native.RemoveClipboardFormatListener(messages.Handle); messages.Dispose(); }
            if (shelf != null) shelf.Close();
            if (tray != null) { tray.Visible = false; if (tray.Icon != null) tray.Icon.Dispose(); tray.Dispose(); }
        }
    }
}
