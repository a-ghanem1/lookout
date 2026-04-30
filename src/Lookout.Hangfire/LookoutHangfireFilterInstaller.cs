using Hangfire;
using Lookout.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lookout.Hangfire;

/// <summary>
/// Installs Lookout's Hangfire filters into <see cref="GlobalJobFilters"/> at application startup.
/// </summary>
internal sealed class LookoutHangfireFilterInstaller : IHostedService
{
    private readonly LookoutHangfireClientFilter _clientFilter;
    private readonly LookoutHangfireServerFilter _serverFilter;
    private readonly IServiceProvider _sp;
    private readonly ILogger<LookoutHangfireFilterInstaller> _logger;

    public LookoutHangfireFilterInstaller(
        LookoutHangfireClientFilter clientFilter,
        LookoutHangfireServerFilter serverFilter,
        IServiceProvider sp,
        ILogger<LookoutHangfireFilterInstaller> logger)
    {
        _clientFilter = clientFilter;
        _serverFilter = serverFilter;
        _sp = sp;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_sp.GetService<ILookoutRecorder>() is null)
        {
            _logger.LogWarning(
                "Lookout Hangfire filter registration skipped: ILookoutRecorder is not registered. " +
                "Call AddLookout() before AddLookoutHangfire().");
            return Task.CompletedTask;
        }

        if (!GlobalJobFilters.Filters.Any(f => f.Instance is LookoutHangfireClientFilter))
            GlobalJobFilters.Filters.Add(_clientFilter);

        if (!GlobalJobFilters.Filters.Any(f => f.Instance is LookoutHangfireServerFilter))
            GlobalJobFilters.Filters.Add(_serverFilter);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
