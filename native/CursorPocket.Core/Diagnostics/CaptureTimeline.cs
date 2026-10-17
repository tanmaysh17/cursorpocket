using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CursorPocket.Core.Diagnostics;

/// <summary>
/// Milliseconds between the steps of one capture, from the command to an editor that
/// takes input. Off unless <c>CURSORPOCKET_PERF=1</c> is set, and even then it only ever
/// becomes a line in a local log file — nothing here leaves the machine.
/// </summary>
/// <remarks>
/// It exists so speed work on the capture path can be measured on the installed build
/// instead of argued about. Marks can arrive from the UI thread and from the worker that
/// encodes the PNG, so they are taken under a lock.
/// </remarks>
public sealed class CaptureTimeline
{
    public const string EnvironmentVariable = "CURSORPOCKET_PERF";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(string Step, double Milliseconds)> _marks = [];
    private readonly object _gate = new();
    private bool _completed;

    private CaptureTimeline(string kind) => Kind = kind;

    public string Kind { get; }

    public static bool IsEnabled(Func<string, string?>? environment = null) =>
        (environment ?? Environment.GetEnvironmentVariable)(EnvironmentVariable) == "1";

    /// <summary>A timeline when measurement is switched on, otherwise null.</summary>
    public static CaptureTimeline? Start(string kind, Func<string, string?>? environment = null) =>
        IsEnabled(environment) ? new CaptureTimeline(kind) : null;

    public void Mark(string step)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _marks.Add((step, _clock.Elapsed.TotalMilliseconds));
            }
        }
    }

    /// <summary>
    /// Closes the timeline and returns its log line, or null if it was already closed —
    /// the editor and the commit both try, and only the later of the two should write.
    /// </summary>
    public string? Complete(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return null;
            }

            _completed = true;
            return Format(now, Kind, _marks);
        }
    }

    public static string Format(DateTimeOffset now, string kind, IReadOnlyList<(string Step, double Milliseconds)> marks)
    {
        var line = new StringBuilder();
        line.Append(now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
        line.Append(' ').Append(kind);
        foreach (var (step, milliseconds) in marks)
        {
            line.Append(' ').Append(step).Append('=')
                .Append(milliseconds.ToString("0.0", CultureInfo.InvariantCulture));
        }

        return line.ToString();
    }
}
