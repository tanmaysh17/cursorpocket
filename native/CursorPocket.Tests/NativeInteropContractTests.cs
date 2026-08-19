using System.Runtime.InteropServices;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class NativeInteropContractTests
{
    [Fact]
    public void WindowClassUsesTheSameUnicodeLayoutAsRegisterClassW()
    {
        Assert.Equal(CharSet.Unicode, typeof(NativeMethods.WindowClass).StructLayoutAttribute?.CharSet);
    }

    [Fact]
    public void HotkeyRegistrationRunsOnTheMessageWindowThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        using var service = new GlobalHotkeyService();
        using var invoked = new ManualResetEventSlim();
        service.Invoked += (_, _) => invoked.Set();

        Assert.True(service.TryRegister("Ctrl+Alt+Shift+Y"));
        Assert.Equal("Ctrl+Alt+Shift+Y", service.RegisteredShortcut);
        Assert.True(NativeMethods.PostMessage(service.MessageWindowHandle, NativeMethods.WmHotkey, 0xC07, 0));
        Assert.True(invoked.Wait(TimeSpan.FromSeconds(2)));
    }
}
