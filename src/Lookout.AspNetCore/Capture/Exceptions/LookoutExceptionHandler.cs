using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N1Scope = Lookout.Core.Diagnostics.N1RequestScope;

namespace Lookout.AspNetCore.Capture.Exceptions;

/// <summary>
/// Captures handled exceptions via the ASP.NET Core <see cref="IExceptionHandler"/> pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Registered as the first <see cref="IExceptionHandler"/> via <c>AddLookout()</c> so it runs
/// before consumer-registered handlers. Always returns <c>false</c> — Lookout observes the
/// exception but never suppresses it; the host's own error handling still runs.
/// </para>
/// <para>
/// Requires <c>app.UseExceptionHandler()</c> to be present in the pipeline. The
/// <see cref="UnhandledExceptionDiagnosticSubscriber"/> acts as the fallback capture path for
/// exceptions that do not go through <c>UseExceptionHandler()</c>.
/// </para>
/// <para>
/// Stamps each captured exception in <see cref="ExceptionRegistry"/> so the diagnostic-listener
/// path can deduplicate and avoid double-recording.
/// </para>
/// </remarks>
public sealed class LookoutExceptionHandler : IExceptionHandler
{
    private readonly ILookoutRecorder _recorder;
    private readonly ExceptionOptions _options;
    private readonly ILogger<LookoutExceptionHandler> _logger;

    public LookoutExceptionHandler(
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options,
        ILogger<LookoutExceptionHandler> logger)
    {
        _recorder = recorder;
        _options = options.Value.Exceptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!_options.Capture)
            return new ValueTask<bool>(false);

        if (ExceptionCapture.IsIgnored(exception, _options.IgnoreExceptionTypes))
            return new ValueTask<bool>(false);

        try
        {
            ExceptionRegistry.TryStamp(exception);
            var entry = ExceptionCapture.BuildEntry(exception, handled: true, _options.MaxStackFrames);
            N1Scope.Current?.TrackException();
            _recorder.Record(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lookout failed to capture exception.");
        }

        // Always return false — we observe only; the host error handler still runs.
        return new ValueTask<bool>(false);
    }
}
