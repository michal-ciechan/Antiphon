using System.Reflection;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0672 D-2 (review 3488192e (3)): the turnstile only works when the dispatcher that
/// registers waiters and the land service that yields to them hold the SAME registry. Both take
/// it as an optional constructor parameter, so a missing Program.cs registration would compile,
/// boot and silently disable the yield. Resolved from the booted Program's own container; nothing
/// here registers anything.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class RepositoryLeaseWaitersWiringTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly AntiphonWebAppFactory _factory;
    public RepositoryLeaseWaitersWiringTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Program_hands_one_waiter_registry_to_the_dispatcher_and_the_land_service()
    {
        var root = _factory.Services.GetService<RepositoryLeaseWaiters>();
        root.ShouldNotBeNull("Program.cs must register RepositoryLeaseWaiters or no land ever yields to a dispatch");
        var git = _factory.Services.GetRequiredService<ILandingGit>();
        _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value
            .LandYieldToDispatchMaxSeconds.ShouldBeGreaterThan(0, "the shipped configuration keeps the yield on");

        await using var a = _factory.Services.CreateAsyncScope();
        await using var b = _factory.Services.CreateAsyncScope();
        foreach (var scope in new[] { a, b })
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            var land = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();
            Field<RepositoryLeaseWaiters>(dispatcher).ShouldBeSameAs(root, "the dispatcher registers waiters here");
            Field<RepositoryLeaseWaiters>(land).ShouldBeSameAs(root, "the land yields to the waiters registered here");
            // Both sides key the registry by the common directory the same git service resolves.
            Field<ILandingGit>(dispatcher).ShouldBeSameAs(git);
            Field<ILandingGit>(land).ShouldBeSameAs(git);
        }
    }

    private static T? Field<T>(object owner) where T : class =>
        (T?)owner.GetType().GetFields(Fields).Single(f => f.FieldType == typeof(T)).GetValue(owner);
}
