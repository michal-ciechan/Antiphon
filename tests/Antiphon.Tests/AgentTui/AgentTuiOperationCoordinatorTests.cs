using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

public sealed class AgentTuiOperationCoordinatorTests
{
    [Test]
    public async Task Already_cancelled_first_caller_does_not_create_shared_work()
    {
        var scopes = new CountingScopeFactory();
        var coordinator = new AgentTuiOperationCoordinator(
            scopes,
            Options.Create(new AgentTuiSettings { ProbeTimeoutSeconds = 5 }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            coordinator.RefreshModelsAsync(Guid.NewGuid(), cancellation.Token));

        scopes.CreateCount.ShouldBe(0);
    }

    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        public int CreateCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CreateCount++;
            return new EmptyScope();
        }
    }

    private sealed class EmptyScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new EmptyProvider();
        public void Dispose()
        {
        }
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
