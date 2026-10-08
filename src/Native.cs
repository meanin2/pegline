using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Forms = System.Windows.Forms;
using Wpf = System.Windows;
using System.Windows.Interop;

namespace Pegline
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct RECT
        { public int Left, Top, Right, Bottom; public Rectangle Rectangle { get { return System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Width, Height; public SIZE(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int Size, Flags; public IntPtr Cursor; public POINT Position; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool Icon; public uint XHotspot, YHotspot; public IntPtr Mask, Color; }
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
        public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
        public const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
        public const int WM_MOUSEACTIVATE = 0x21, WM_NCHITTEST = 0x84, WM_DPICHANGED = 0x2E0, WM_HOTKEY = 0x312, WM_CLIPBOARDUPDATE = 0x31D;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x10, SWP_NOMOVE = 2, SWP_NOSIZE = 1;
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int idx, IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint id);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int mode);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int DwmGetCloaked(IntPtr h, int attr, out int value, int size);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint flags, out uint result);
        [DllImport("shell32.dll")] static extern IntPtr ILFindLastID(IntPtr pidl);
        [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);
        [DllImport("shell32.dll")] static extern int SHCreateDataObject(IntPtr folder, uint count, IntPtr[] child, IntPtr inner, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        [DllImport("ole32.dll")] public static extern void ReleaseStgMedium(ref STGMEDIUM medium);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size, IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, uint step, IntPtr brush, uint flags);

        public static void InitializeDpi()
        { try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { } try { SetProcessDpiAwareness(2); } catch { } }
        public static POINT Cursor { get { POINT p; GetCursorPos(out p); return p; } }
        public static Forms.Screen PointerScreen { get { POINT p = Cursor; return Forms.Screen.FromPoint(new Point(p.X, p.Y)); } }
        public static string ClipboardOwnerName()
        { try { uint pid; GetWindowThreadProcessId(GetClipboardOwner(), out pid); using (var p = Process.GetProcessById((int)pid)) return p.ProcessName; } catch { return ""; } }
        public static Rectangle Bounds(IntPtr hwnd)
        {
            RECT r;
            try { if (DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))) == 0) return r.Rectangle; } catch { }
            return GetWindowRect(hwnd, out r) ? r.Rectangle : Rectangle.Empty;
        }
        public static bool IsCloaked(IntPtr hwnd)
        { int v; try { return DwmGetCloaked(hwnd, 14, out v, 4) == 0 && v != 0; } catch { return false; } }
        static readonly uint ownProcessId = GetOwnProcessId();
        static uint GetOwnProcessId() { using (var process = Process.GetCurrentProcess()) return (uint)process.Id; }
        public static bool IsOwn(IntPtr hwnd)
        { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid == ownProcessId; }
        sealed class FullscreenResult { public long At; public IntPtr Foreground; public bool Value; public Rectangle Bounds; }
        static readonly System.Collections.Generic.Dictionary<string, FullscreenResult> fullscreen = new System.Collections.Generic.Dictionary<string, FullscreenResult>();
        public static bool FullScreen(Forms.Screen screen)
        {
            bool blocked = false; IntPtr foreground = GetForegroundWindow(); long tick = Stopwatch.GetTimestamp();
            FullscreenResult previous;
            if (fullscreen.TryGetValue(screen.DeviceName, out previous) && previous.Foreground == foreground && previous.Bounds == screen.Bounds &&
                (double)(tick - previous.At) / Stopwatch.Frequency < .2) return previous.Value;
            // Inspect the topmost ordinary app on this display, not just the foreground
            // app (which may be on a different monitor). Exclude cloaked virtual desktops.
            EnumWindows(delegate(IntPtr h, IntPtr unused)
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h) || IsOwn(h)) return true;
                var s = new StringBuilder(128); GetClassName(h, s, s.Capacity);
                if (s.ToString() == "Progman" || s.ToString() == "WorkerW" || s.ToString().Contains("TrayWnd")) return true;
                Rectangle r = Bounds(h), b = screen.Bounds;
                if (!r.IntersectsWith(b)) return true;
                long style = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
                blocked = r.Width > 0 && r.Left <= b.Left + 1 && r.Top <= b.Top + 1 && r.Right >= b.Right - 1 && r.Bottom >= b.Bottom - 1 && (style & 0x00C00000) != 0x00C00000;
                return false;
            }, IntPtr.Zero);
            fullscreen[screen.DeviceName] = new FullscreenResult { At = tick, Foreground = foreground, Value = blocked, Bounds = screen.Bounds };
            return blocked;
        }
        public static Rectangle WindowAt(POINT p)
        {
            Rectangle found = Rectangle.Empty;
            EnumWindows(delegate(IntPtr h, IntPtr unused)
            {
                if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h) || IsOwn(h)) return true;
                var s = new StringBuilder(128); GetClassName(h, s, s.Capacity);
                if (s.ToString() == "Progman" || s.ToString() == "WorkerW" || s.ToString().Contains("TrayWnd")) return true;
                Rectangle r = Bounds(h);
                if (r.Contains(p.X, p.Y)) { found = r; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        public static string ScreenshotsFolder()
        {
            var id = new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F"); IntPtr p;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out p) == 0)
            { try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); } }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
        public static void SetOverlay(IntPtr h, bool clickThrough)
        {
            long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            ex = clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
            SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
        }
        public static void ExcludeFromCapture(IntPtr h) { try { SetWindowDisplayAffinity(h, 0x11); } catch { } }
        public static System.Runtime.InteropServices.ComTypes.IDataObject ShellObject(string path)
        {
            IntPtr folder = IntPtr.Zero, full = IntPtr.Zero; uint ignored;
            try
            {
                if (SHParseDisplayName(System.IO.Path.GetDirectoryName(path), IntPtr.Zero, out folder, 0, out ignored) != 0) return null;
                if (SHParseDisplayName(path, IntPtr.Zero, out full, 0, out ignored) != 0) return null;
                var iid = new Guid("0000010E-0000-0000-C000-000000000046"); object result;
                if (SHCreateDataObject(folder, 1, new IntPtr[] { ILFindLastID(full) }, IntPtr.Zero, ref iid, out result) != 0) return null;
                return result as System.Runtime.InteropServices.ComTypes.IDataObject;
            }
            finally { if (folder != IntPtr.Zero) ILFree(folder); if (full != IntPtr.Zero) ILFree(full); }
        }
        public static void PreferMove(System.Runtime.InteropServices.ComTypes.IDataObject data)
        {
            var f = new FORMATETC { cfFormat = (short)Forms.DataFormats.GetFormat("Preferred DropEffect").Id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
            IntPtr h = GlobalAlloc(0x42, new UIntPtr(4)); if (h == IntPtr.Zero) return;
            var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = h, pUnkForRelease = null };
            try
            {
                IntPtr p = GlobalLock(h); if (p == IntPtr.Zero) { ReleaseStgMedium(ref medium); return; }
                Marshal.WriteInt32(p, 2); GlobalUnlock(h); data.SetData(ref f, ref medium, true);
            }
            catch { ReleaseStgMedium(ref medium); }
        }
        public static int? DropEffect(System.Runtime.InteropServices.ComTypes.IDataObject data, string format)
        {
            var f = new FORMATETC { cfFormat = (short)Forms.DataFormats.GetFormat(format).Id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
            STGMEDIUM m;
            try
            {
                data.GetData(ref f, out m);
                try { IntPtr p = GlobalLock(m.unionmember); if (p == IntPtr.Zero) return null; try { return Marshal.ReadInt32(p); } finally { GlobalUnlock(m.unionmember); } }
                finally { ReleaseStgMedium(ref m); }
            }
            catch { return null; }
        }

        [ComImport, Guid("DE5BF786-477A-11D2-839D-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDragSourceHelper
        { void InitializeFromBitmap(ref SHDRAGIMAGE image, System.Runtime.InteropServices.ComTypes.IDataObject data); void InitializeFromWindow(IntPtr hwnd, ref POINT p, System.Runtime.InteropServices.ComTypes.IDataObject data); }
        [StructLayout(LayoutKind.Sequential)] struct SHDRAGIMAGE { public int Width, Height, X, Y; public IntPtr Bitmap; public int Key; }
        public static void DragImage(System.Runtime.InteropServices.ComTypes.IDataObject data, Bitmap bitmap)
        {
            object helper = null; IntPtr hbmp = IntPtr.Zero;
            try
            {
                helper = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4657278A-411B-11D2-839A-00C04FD918D0")));
                hbmp = bitmap.GetHbitmap(Color.FromArgb(0));
                var image = new SHDRAGIMAGE { Width = bitmap.Width, Height = bitmap.Height, X = bitmap.Width / 2, Y = bitmap.Height / 2, Bitmap = hbmp, Key = -1 };
                ((IDragSourceHelper)helper).InitializeFromBitmap(ref image, data);
            }
            catch { /* The real file drag works even when a shell drag image is unavailable. */ }
            finally { if (hbmp != IntPtr.Zero) DeleteObject(hbmp); if (helper != null && Marshal.IsComObject(helper)) Marshal.ReleaseComObject(helper); }
        }
        [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr h, [MarshalAs(UnmanagedType.Bool)] out bool current);
            [PreserveSig] int GetWindowDesktopId(IntPtr h, out Guid id);
            [PreserveSig] int MoveWindowToDesktop(IntPtr h, ref Guid id);
        }
        static IVirtualDesktopManager desktops;
        public static void FollowDesktop(IntPtr ownWindow)
        {
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A")));
                bool current; if (desktops.IsWindowOnCurrentVirtualDesktop(ownWindow, out current) != 0 || current) return;
                IntPtr foreground = GetForegroundWindow(); Guid id;
                if (foreground != IntPtr.Zero && !IsOwn(foreground) && desktops.GetWindowDesktopId(foreground, out id) == 0 && id != Guid.Empty)
                    desktops.MoveWindowToDesktop(ownWindow, ref id);
            }
            catch { /* Unsupported shell: don't use undocumented desktop interfaces. */ }
        }
    }
}
