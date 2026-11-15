using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// Startup and provider validation are the subject; one isolated Program host per session.
[Category("Integration")]
[NotInParallel]
[ClassDataSource<RefOnlyProductionCompositionFactory>(Shared = SharedType.PerTestSession)]
public sealed class RefOnlyProductionCompositionTests(RefOnlyProductionCompositionFactory factory)
{
    [Test]
    public void Validated_production_container_resolves_ref_only_provisioner_and_dispatcher()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITaskProgressGit>().ShouldBeOfType<TaskProgressGit>();
        scope.ServiceProvider.GetRequiredService<RefOnlyDesktopProvisioner>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().ShouldNotBeNull();
        Should.Throw<InvalidOperationException>(() =>
            factory.Services.GetRequiredService<RefOnlyDesktopProvisioner>());
    }
}

public sealed class RefOnlyProductionCompositionFactory : AntiphonWebAppFactory
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        return base.CreateHost(builder);
    }
}
