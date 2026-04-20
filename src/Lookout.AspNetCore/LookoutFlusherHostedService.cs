using Lookout.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lookout.AspNetCore;

internal sealed class LookoutFlusherHostedService : BackgroundService
{
    private const int BatchSize = 100;
    private const int ShutdownDrainTimeoutMs = 5_000;

    private readonly ChannelLookoutRecorder _recorder;
    private readonly ILookoutStorage _storage;
    private readonly ILogger<LookoutFlusherHostedService> _logger;

    public LookoutFlusherHostedService(
        ChannelLookoutRecorder recorder,
        ILookoutStorage storage,
        ILogger<LookoutFlusherHostedService> logger)
    {
        _recorder = recorder;
        _storage = storage;
        _logger = logger;
    }

    // Tracked separately so StopAsync can await loop completion before draining.
    private Task _loopTask = Task.CompletedTask;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _loopTask = RunLoopAsync(stoppingToken);
        return _loopTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Cancels stoppingToken and waits for _executeTask (== _loopTask) with host timeout.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        // Safety net: if the host timeout fired before the loop exited, wait for it now so
        // DrainRemainingAsync never runs concurrently with the loop.
        await _loopTask.ConfigureAwait(false);
        await DrainRemainingAsync().ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (true)
        {
            LookoutEntry entry;
            try
            {
                // Blocks until an entry arrives or shutdown is requested.
                entry = await _recorder.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            var batch = new List<LookoutEntry>(BatchSize) { entry };

            // Greedily drain any already-queued items without blocking.
            while (batch.Count < BatchSize && _recorder.Reader.TryRead(out var next))
                batch.Add(next);

            // Never cancel in-progress writes with stoppingToken — let the write
            // complete so we don't lose a partially-built batch.
            await WriteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task DrainRemainingAsync()
    {
        using var cts = new CancellationTokenSource(ShutdownDrainTimeoutMs);
        try
        {
            while (_recorder.Reader.TryRead(out var entry))
            {
                var batch = new List<LookoutEntry>(BatchSize) { entry };
                while (batch.Count < BatchSize && _recorder.Reader.TryRead(out var next))
                    batch.Add(next);

                await WriteBatchAsync(batch, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Lookout flusher: shutdown drain timed out after {TimeoutMs}ms; some entries may be lost.",
                ShutdownDrainTimeoutMs);
        }
    }

    private async Task WriteBatchAsync(List<LookoutEntry> batch, CancellationToken ct)
    {
        try
        {
            await _storage.WriteAsync(batch, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Lookout flusher: write failed for batch of {Count} entries; entries discarded.",
                batch.Count);
        }
    }
}
