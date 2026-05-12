using Lookout.AspNetCore.Capture.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lookout.AspNetCore;

internal sealed class LookoutStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LookoutStartupValidator> _logger;

    public LookoutStartupValidator(IServiceProvider services, ILogger<LookoutStartupValidator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var memoryCache = _services.GetService<IMemoryCache>();
        if (memoryCache is not null and not LookoutMemoryCacheDecorator)
        {
            _logger.LogWarning(
                "Lookout cache capture is inactive because AddMemoryCache() was called after AddLookout(). " +
                "Move AddMemoryCache() before AddLookout() to enable capture.");
        }

        var distributedCache = _services.GetService<IDistributedCache>();
        if (distributedCache is not null and not LookoutDistributedCacheDecorator)
        {
            _logger.LogWarning(
                "Lookout distributed cache capture is inactive because the distributed cache was registered after AddLookout(). " +
                "Move the distributed cache registration before AddLookout() to enable capture.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
