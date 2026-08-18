using System.Diagnostics;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class WindowContextIntegrationTests
{
    [Fact]
    public async Task Reads_the_live_notepad_selection_when_hardware_tests_are_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("CURSORPOCKET_HARDWARE_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var configuredHandle = Environment.GetEnvironmentVariable("CURSORPOCKET_TEST_WINDOW_HANDLE");
        var handle = long.TryParse(configuredHandle, out var parsed)
            ? new nint(parsed)
            : Process.GetProcessesByName("Notepad").FirstOrDefault(process => process.MainWindowHandle != 0)?.MainWindowHandle ?? 0;
        if (handle == 0)
        {
            throw new InvalidOperationException("Open the CursorPocket QA note in Notepad before running hardware tests.");
        }
        var selection = await new WindowContextService().ReadSelectedTextAsync(handle);

        Assert.Contains("CursorPocket QA sample", selection, StringComparison.Ordinal);
    }
}
