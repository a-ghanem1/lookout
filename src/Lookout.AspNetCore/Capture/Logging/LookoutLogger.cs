using System.Diagnostics;
using System.Text.Json;
using Lookout.Core;
using Lookout.Core.Diagnostics;
using Lookout.Core.Schemas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore.Capture.Logging;

internal sealed class LookoutLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IOptions<LookoutOptions> _options;
    private readonly LookoutLoggerProvider _provider;

    internal LookoutLogger(
        string categoryName,
        IOptions<LookoutOptions> options,
        LookoutLoggerProvider provider)
    {
        _categoryName = categoryName;
        _options = options;
        _provider = provider;
    }

    /// <inheritdoc />
    /// <remarks>Hot path — no allocation, no recorder call.</remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None) return false;
        var opts = _options.Value.Logging;
        return opts.Capture && logLevel >= opts.MinimumLevel;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a no-op; we don't push scopes ourselves. Active scopes pushed by other
    /// <see cref="ISupportExternalScope"/> providers in the logging factory are read via
    /// the shared <see cref="IExternalScopeProvider"/> in <see cref="Log{TState}"/>.
    /// </remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        NullScope.Instance;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var opts = _options.Value.Logging;
        if (!IsEnabled(logLevel)) return;
        if (IsCategoryIgnored(_categoryName, opts.IgnoreCategories)) return;

        try
        {
            var message = formatter(state, exception);
            var scopes = CollectScopes(_provider.ScopeProvider, opts.MaxScopeFrames);

            var content = new LogEntryContent(
                Level: logLevel.ToString(),
                Category: _categoryName,
                Message: message,
                EventId: new LogEventId(eventId.Id, eventId.Name),
                Scopes: scopes,
                ExceptionType: exception is not null
                    ? (exception.GetType().FullName ?? exception.GetType().Name)
                    : null,
                ExceptionMessage: exception?.Message);

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["log.level"] = logLevel.ToString(),
                ["log.category"] = _categoryName,
            };
            if (eventId.Id != 0)
                tags["log.event-id"] = eventId.Id.ToString();

            var entry = new LookoutEntry(
                Id: Guid.NewGuid(),
                Type: "log",
                Timestamp: DateTimeOffset.UtcNow,
                RequestId: Activity.Current?.RootId,
                DurationMs: 0,
                Tags: tags,
                Content: JsonSerializer.Serialize(content, LookoutJson.Options));

            N1RequestScope.Current?.TrackLog(logLevel);
            _provider.Recorder.Record(entry);
        }
        catch
        {
            // Never propagate from log capture
        }
    }

    private static bool IsCategoryIgnored(string category, IList<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(category, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<string> CollectScopes(
        IExternalScopeProvider scopeProvider, int maxFrames)
    {
        if (maxFrames <= 0) return [];
        var result = new List<string>(maxFrames);
        scopeProvider.ForEachScope(
            static (scope, state) =>
            {
                var (list, max) = state;
                if (list.Count >= max) return;
                var text = scope?.ToString();
                if (!string.IsNullOrEmpty(text))
                    list.Add(text!);
            },
            (result, maxFrames));
        return result;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
