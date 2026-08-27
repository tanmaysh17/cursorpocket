using CursorPocket_App.Services;
using Microsoft.UI.Xaml;

namespace CursorPocket_App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;

    public static Window Window { get; private set; } = null!;
    public static bool StartedInBackground { get; private set; }
    public static AppServices Services { get; private set; } = null!;
    public static ThemeCoordinator Theme { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);
    public static bool AnimationsEnabled { get; private set; } = true;

    public App()
    {
        InitializeComponent();
        try
        {
            AnimationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            AnimationsEnabled = true;
        }
        UnhandledException += (_, eventArgs) =>
        {
            System.Diagnostics.Debug.WriteLine(eventArgs.Exception);
            WriteCrashLog(eventArgs.Exception);
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, "Local\\CursorPocket.Native.SingleInstance", out var firstInstance);
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CursorPocket.Native.ShowLibrary");
            if (!firstInstance)
            {
                _activationEvent.Set();
                Exit();
                return;
            }

            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Services = await AppServices.CreateAsync();
            Theme = new ThemeCoordinator(Services.Settings.ThemeMode, Services.Settings.GlassTransparency);
            // Resolved before the window exists so the Library knows not to build
            // itself for a tray-only launch, rather than racing the deferred hide.
            StartedInBackground = args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase)
                || Environment.GetCommandLineArgs().Any(argument =>
                    argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
            Window = new MainWindow();
            Services.Hotkey.Invoked += (_, _) => DispatcherQueue.TryEnqueue(() => (Window as MainWindow)?.ShowCommandPalette());
            StartActivationListener();
            Window.Activate();
            Services.StartOrphanRecovery();
            if (StartedInBackground)
            {
                Window.AppWindow.Hide();
                // WinUI posts its first-show work after Activate returns. Hide once
                // more at low priority so startup remains tray-only without racing
                // that deferred show, while still creating the HWND and services.
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () => Window.AppWindow.Hide());
            }

            // --annotate <path> opens the editor on an image CursorPocket did not take,
            // which also makes Explorer's "Open with" work without the installer claiming
            // a file association. A silent default-app grab is the kind of system change
            // users resent, and the installer stays per-user and admin-free.
            var commandLine = Environment.GetCommandLineArgs();
            var annotateAt = Array.FindIndex(
                commandLine,
                argument => argument.Equals("--annotate", StringComparison.OrdinalIgnoreCase));
            if (annotateAt >= 0 && annotateAt + 1 < commandLine.Length)
            {
                var path = commandLine[annotateAt + 1];
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    async () => await ((MainWindow)Window).AnnotateFileAsync(path));
            }

            // Deterministic installed-build review entry point. It is deliberately
            // command-line only and never appears in product navigation.
            var qaAt = Array.FindIndex(
                commandLine,
                argument => argument.Equals("--qa-surface", StringComparison.OrdinalIgnoreCase));
            if (qaAt >= 0 && qaAt + 1 < commandLine.Length)
            {
                var surface = commandLine[qaAt + 1];
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    async () => await ((MainWindow)Window).ShowQaSurfaceAsync(surface));
            }
        }
        catch (Exception error)
        {
            WriteCrashLog(error);
            Exit();
        }
    }

    private static void WriteCrashLog(Exception error)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CursorPocket");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash reporting must never replace the original failure.
        }
    }

    private void StartActivationListener()
    {
        if (_activationEvent is null)
        {
            return;
        }
        // A named event needs no polling. The previous 500 ms wait loop woke this
        // process twice a second for its entire lifetime.
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => DispatcherQueue.TryEnqueue(() => (Window as MainWindow)?.ShowLibrary()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Shutdown()
    {
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _activationEvent?.Dispose();
        Theme?.Dispose();
        Services?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }
}
