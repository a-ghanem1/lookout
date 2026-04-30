namespace Lookout.Core;

/// <summary>Exception capture configuration.</summary>
public sealed class ExceptionOptions
{
    /// <summary>
    /// When <c>true</c>, exceptions are captured. Default: <c>true</c>.
    /// </summary>
    public bool Capture { get; set; } = true;

    /// <summary>
    /// Full CLR type names whose exceptions (and subclasses) are dropped before recording.
    /// Default: <c>TaskCanceledException</c> and <c>OperationCanceledException</c> — cancellation
    /// noise on every dev hot-reload is the fastest way to make exception capture annoying.
    /// </summary>
    public IList<string> IgnoreExceptionTypes { get; set; } =
    [
        "System.Threading.Tasks.TaskCanceledException",
        "System.OperationCanceledException",
    ];

    /// <summary>
    /// Maximum number of user-code stack frames to capture per exception. Default: <c>20</c>.
    /// </summary>
    public int MaxStackFrames { get; set; } = 20;
}
