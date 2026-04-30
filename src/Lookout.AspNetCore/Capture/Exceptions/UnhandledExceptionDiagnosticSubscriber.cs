using System.Diagnostics;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using N1Scope = Lookout.Core.Diagnostics.N1RequestScope;

namespace Lookout.AspNetCore.Capture.Exceptions;

/// <summary>
/// Captures unhandled exceptions via the <c>Microsoft.AspNetCore.Diagnostics.UnhandledException</c>
/// diagnostic event — the fallback path for exceptions that do not go through
/// <c>UseExceptionHandler()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton <see cref="IHostedService"/> via <c>AddLookout()</c>.
/// <see cref="StartAsync"/> subscribes to <see cref="DiagnosticListener.AllListeners"/>;
/// <see cref="StopAsync"/> disposes all subscriptions.
/// </para>
/// <para>
/// Checks <see cref="ExceptionRegistry"/> before recording — if
/// <see cref="LookoutExceptionHandler"/> already stamped the exception this subscriber no-ops,
/// preventing duplicate entries for the same throw.
/// </para>
/// </remarks>
public sealed class UnhandledExceptionDiagnosticSubscriber
    : IHostedService, IObserver<DiagnosticListener>
{
    private readonly ILookoutRecorder _recorder;
    private readonly ExceptionOptions _options;
    private readonly ILogger<UnhandledExceptionDiagnosticSubscriber> _logger;

    private IDisposable? _allListenersSubscription;
    private readonly List<IDisposable> _sourceSubscriptions = [];

    internal const string ListenerName = "Microsoft.AspNetCore";
    internal const string EventName = "Microsoft.AspNetCore.Diagnostics.UnhandledException";

    public UnhandledExceptionDiagnosticSubscriber(
        ILookoutRecorder recorder,
        IOptions<LookoutOptions> options,
        ILogger<UnhandledExceptionDiagnosticSubscriber> logger)
    {
        _recorder = recorder;
        _options = options.Value.Exceptions;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sourceSubscriptions)
        {
            foreach (var sub in _sourceSubscriptions)
                sub.Dispose();
            _sourceSubscriptions.Clear();
        }
        _allListenersSubscription?.Dispose();
        _allListenersSubscription = null;
        return Task.CompletedTask;
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (!string.Equals(listener.Name, ListenerName, StringComparison.OrdinalIgnoreCase))
            return;
        var sub = listener.Subscribe(new EventObserver(this));
        lock (_sourceSubscriptions)
            _sourceSubscriptions.Add(sub);
    }

    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }

    internal void OnUnhandledException(Exception exception)
    {
        if (!_options.Capture) return;
        if (ExceptionCapture.IsIgnored(exception, _options.IgnoreExceptionTypes)) return;
        if (ExceptionRegistry.IsStamped(exception)) return;

        try
        {
            var entry = ExceptionCapture.BuildEntry(exception, handled: false, _options.MaxStackFrames);
            N1Scope.Current?.TrackException();
            _recorder.Record(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lookout failed to capture unhandled exception.");
        }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        private readonly UnhandledExceptionDiagnosticSubscriber _parent;

        public EventObserver(UnhandledExceptionDiagnosticSubscriber parent) => _parent = parent;

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!string.Equals(value.Key, EventName, StringComparison.OrdinalIgnoreCase))
                return;
            if (value.Value is null) return;

            var payloadType = value.Value.GetType();
            var exception = (payloadType.GetProperty("exception")?.GetValue(value.Value)
                          ?? payloadType.GetProperty("Exception")?.GetValue(value.Value)) as Exception;
            if (exception is null) return;

            _parent.OnUnhandledException(exception);
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
