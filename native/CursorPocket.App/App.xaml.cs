using CursorPocket_App.Services;
using Microsoft.UI.Xaml;

namespace CursorPocket_App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationListener;

    public static Window Window { get; private set; } = null!;
    public static AppServices Services { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
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
            Window = new MainWindow();
            Services.Hotkey.Invoked += (_, _) => DispatcherQueue.TryEnqueue(() => (Window as MainWindow)?.ShowCommandPalette());
            StartActivationListener();
            Window.Activate();
            var backgroundLaunch = args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase)
                || Environment.GetCommandLineArgs().Any(argument =>
                    argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
            if (backgroundLaunch)
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
        _activationListener = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            while (!_activationListener.IsCancellationRequested)
            {
                if (_activationEvent?.WaitOne(500) == true)
                {
                    DispatcherQueue.TryEnqueue(() => (Window as MainWindow)?.ShowLibrary());
                }
            }
        }, _activationListener.Token);
    }

    public void Shutdown()
    {
        _activationListener?.Cancel();
        _activationEvent?.Dispose();
        Services?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }
}
