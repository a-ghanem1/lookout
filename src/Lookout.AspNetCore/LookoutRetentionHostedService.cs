using Lookout.Core;
using Lookout.Storage.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lookout.AspNetCore;

internal sealed class LookoutRetentionHostedService : BackgroundService
{
    private readonly SqliteLookoutStorage _storage;
    private readonly LookoutOptions _options;
    private readonly ILogger<LookoutRetentionHostedService> _logger;

    public LookoutRetentionHostedService(
        SqliteLookoutStorage storage,
        IOptions<LookoutOptions> options,
        ILogger<LookoutRetentionHostedService> logger)
    {
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.RetentionInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await PruneAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_options.MaxAgeHours);
            var byAge = await _storage.PruneOlderThanAsync(cutoff, ct).ConfigureAwait(false);
            if (byAge > 0)
                _logger.LogDebug(
                    "Retention: removed {Count} entries older than {MaxAgeHours}h.",
                    byAge, _options.MaxAgeHours);

            var byCount = await _storage.PruneToMaxCountAsync(_options.MaxEntryCount, ct).ConfigureAwait(false);
            if (byCount > 0)
                _logger.LogDebug(
                    "Retention: removed {Count} excess entries (cap: {MaxEntryCount}).",
                    byCount, _options.MaxEntryCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retention prune pass failed.");
        }
    }
}
