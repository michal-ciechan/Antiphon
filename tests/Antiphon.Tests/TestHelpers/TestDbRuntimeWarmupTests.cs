using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

// Observe EF itself from assembly load. A counter assigned by the warm-up would not establish
// that the real provider/model was initialized once, outside every timed test body.
internal sealed class RuntimeModelObserver : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private static readonly ConcurrentBag<(string Fixture, IModel Model, bool OutsideBody)> Initialized = [];
    private static readonly ConcurrentBag<IDisposable> Subscriptions = [];

    [ModuleInitializer]
    internal static void Install()
    {
        var activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TestTrace.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(activities);
        Subscriptions.Add(activities);
        Subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(new RuntimeModelObserver()));
    }

    public static (IModel Model, bool OutsideBody)[] Snapshot(string fixture = "db-runtime") =>
        [.. Initialized.Where(i => i.Fixture == fixture).Select(i => (i.Model, i.OutsideBody))];

    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.EntityFrameworkCore")
            Subscriptions.Add(listener.Subscribe(this, name => name == CoreEventId.ContextInitialized.Name));
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is ContextInitializedEventData { Context: AppDbContext context }
            && Activity.Current?.GetTagItem("antiphon.fixture") is string fixture and ("db-runtime" or "harness-runtime"))
            // TUnit keeps the first test ambient while awaiting assembly hooks. Its concrete
            // assembly-hook scope, rather than TestContext presence, identifies this boundary.
            Initialized.Add((fixture, context.Model, AssemblyHookContext.Current?.Assembly == typeof(TestDbFixture).Assembly));
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}

[Category("Integration")]
public sealed class TestDbRuntimeWarmupTests
{
    [Test]
    public void Real_provider_and_model_are_ready_before_the_timed_body()
    {
        // This call makes the selection a default-store consumer. Observe before constructing a
        // context here: doing the expensive work in this body is too late.
        var options = TestDbFixture.CreateDbContextOptions();
        var initialized = RuntimeModelObserver.Snapshot();
        initialized.Length.ShouldBe(1, "one real EF context must initialize in the assembly runtime warm-up, including cache hits");
        initialized[0].OutsideBody.ShouldBeTrue("assembly setup must finish outside the test's timeout");
        using var context = new AppDbContext(options);
        ReferenceEquals(initialized[0].Model, context.Model).ShouldBeTrue(
            "consumers must reuse the exact warmed model with their normal Npgsql options");
        context.Model.FindEntityType("Antiphon.Server.Domain.Entities.AgentTask").ShouldNotBeNull();
        RuntimeModelObserver.Snapshot().Length.ShouldBe(1, "consumer contexts must not rerun assembly warm-up");
    }

    [Test]
    public async Task Harness_model_is_ready_before_the_timed_body()
    {
        var options = TestDbFixture.CreateDbContextOptions();
        var warmed = RuntimeModelObserver.Snapshot("harness-runtime");
        warmed.Length.ShouldBe(1, "one harness-shaped EF context must initialize in the assembly runtime warm-up");
        warmed[0].OutsideBody.ShouldBeTrue("assembly setup must finish outside the test's timeout");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        using var scope = h.Provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
        ReferenceEquals(warmed[0].Model, model).ShouldBeTrue("a harness context must reuse the model warmed for it");
        using var fixtureContext = new AppDbContext(options);
        ReferenceEquals(fixtureContext.Model, model).ShouldBeFalse(
            "the harness's options build a second model; if they share the fixture's, BridgeQueueHarness.WarmModel is redundant");
    }
}
