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
            eventArgs.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
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
        if (args.Arguments.Contains("--background", StringComparison.OrdinalIgnoreCase))
        {
            Window.AppWindow.Hide();
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
