using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Pegline")]
[assembly: AssemblyProduct("Pegline")]
[assembly: AssemblyDescription("Local Windows screenshot clothesline")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace Pegline
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.InitializeDpi();
            System.Windows.Forms.Application.EnableVisualStyles();
            bool first;
            using (var mutex = new Mutex(true, @"Local\Pegline.Instance.v1", out first))
            {
                if (!first)
                {
                    try { using (var signal = EventWaitHandle.OpenExisting(@"Local\Pegline.Show.v1")) signal.Set(); } catch { }
                    return 0;
                }
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Pegline.Show.v1"))
                    using (var controller = new Controller(app.Dispatcher))
                    {
                        var wait = ThreadPool.RegisterWaitForSingleObject(signal, delegate { app.Dispatcher.BeginInvoke(new Action(controller.Toggle)); }, null, -1, false);
                        app.Startup += delegate { controller.Start(); };
                        // Report errors locally; do not silently swallow corrupt application state.
                        app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                        {
                            MessageBox.Show(Ui.L("Pegline stopped after an unexpected error. Your original screenshots have not been intentionally deleted.\n\n", "Pegline se detuvo tras un error inesperado. No se han eliminado intencionadamente tus capturas originales.\n\n") + e.Exception.Message, "Pegline", MessageBoxButton.OK, MessageBoxImage.Error);
                            e.Handled = true; app.Shutdown(1);
                        };
                        try { return app.Run(); } finally { wait.Unregister(null); }
                    }
                }
                catch (Exception e)
                { MessageBox.Show("Pegline could not start.\n\n" + e.Message, "Pegline", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            }
        }
    }
}
