using FluentAssertions;
using Lookout.Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lookout.AspNetCore.Tests.Hangfire;

public sealed class HangfireDiRegistrationTests
{
    [Fact]
    public void AddLookoutHangfire_RegistersClientFilterAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLookoutHangfire();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(LookoutHangfireClientFilter));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLookoutHangfire_RegistersServerFilterAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLookoutHangfire();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(LookoutHangfireServerFilter));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLookoutHangfire_RegistersInstallerAsHostedService()
    {
        var services = new ServiceCollection();
        services.AddLookoutHangfire();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(LookoutHangfireFilterInstaller));

        descriptor.Should().NotBeNull("installer must be registered as IHostedService");
    }

    [Fact]
    public void AddLookoutHangfire_CalledTwice_RegistersInstallerOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLookoutHangfire();
        services.AddLookoutHangfire();

        var installerCount = services.Count(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(LookoutHangfireFilterInstaller));

        installerCount.Should().Be(1, "AddHostedService<T> uses TryAddEnumerable internally");
    }
}
