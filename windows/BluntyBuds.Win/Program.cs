using System.Windows;
using System.Windows.Threading;
namespace BluntyBuds.Win;
public static class Program
{
    public static void Log(Exception ex, string where)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BluntyBuds");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] {where}\n{ex}\n\n");
        }
        catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log(ex, "AppDomain"); };
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception, "Task"); e.SetObserved(); };
        bool demo = args.Contains("--demo");
        using var single = new Mutex(true, demo ? @"Local\BluntyBuds.Demo" : @"Local\BluntyBuds.SingleInstance", out bool first);
        if (!first) return; // already running (e.g. in the tray)
        var app = new Application();
        try { app.Resources = Theme.Load(); } catch (Exception ex) { Log(ex, "Theme"); }
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception, "Dispatcher");
            MessageBox.Show(e.Exception.Message + "\n\nDetails: %LOCALAPPDATA%\\BluntyBuds\\crash.log", "BluntyBuds error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        try { app.Run(new MainWindow(args.Contains("--tray") && !demo, demo)); }
        catch (Exception ex)
        {
            Log(ex, "Startup");
            MessageBox.Show(ex.Message + "\n\nDetails: %LOCALAPPDATA%\\BluntyBuds\\crash.log", "BluntyBuds failed to start", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
