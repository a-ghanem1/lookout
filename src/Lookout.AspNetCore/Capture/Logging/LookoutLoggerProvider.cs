using Lookout.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore.Capture.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that routes log entries to <see cref="ILookoutRecorder"/>.
/// Registered as an additive provider — all other configured providers (Console, Debug, Seq …)
/// continue to receive every log call unchanged.
/// </summary>
/// <remarks>
/// The recorder is resolved lazily via <see cref="IServiceProvider"/> to break the otherwise
/// circular dependency: <c>ILoggerFactory → ILoggerProvider → ILookoutRecorder →
/// ChannelLookoutRecorder → ILogger&lt;T&gt; → ILoggerFactory</c>.
/// </remarks>
internal sealed class LookoutLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IServiceProvider _services;
    private readonly IOptions<LookoutOptions> _options;
    private IExternalScopeProvider _scopeProvider = NoOpScopeProvider.Instance;
    private ILookoutRecorder? _recorder;

    public LookoutLoggerProvider(IServiceProvider services, IOptions<LookoutOptions> options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// The currently active external scope provider, set by the host logging infrastructure.
    /// </summary>
    internal IExternalScopeProvider ScopeProvider => _scopeProvider;

    /// <summary>
    /// Lazily-resolved recorder — deferred to break the ILoggerFactory circular dependency.
    /// </summary>
    internal ILookoutRecorder Recorder =>
        _recorder ??= _services.GetRequiredService<ILookoutRecorder>();

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new LookoutLogger(categoryName, _options, this);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class NoOpScopeProvider : IExternalScopeProvider
    {
        public static readonly NoOpScopeProvider Instance = new();
        public void ForEachScope<TState>(Action<object?, TState> callback, TState state) { }
        public IDisposable Push(object? state) => NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static readonly NoOpDisposable Instance = new();
        public void Dispose() { }
    }
}
