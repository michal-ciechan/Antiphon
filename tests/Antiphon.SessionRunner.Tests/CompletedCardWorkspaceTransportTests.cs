using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

public sealed partial class CompletedCardWorkspaceRemovalTests
{
    private static readonly RunnerBuildDto TransportBuild = new("test", null, DateTime.UtcNow, DateTime.UtcNow);

    private static PhoneHomeFrame TransportFrame(PhoneHomeOperation operation, object? body) =>
        new(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), operation,
            body is null ? null : JsonSerializer.SerializeToElement(body, PhoneHomeFraming.Json));

    private static PhoneHomeCommandDispatcher TransportDispatcher(RemovalRig r, bool configured)
    {
        if (configured)
        {
            // Baseline must compile and fail at the missing S6 composition seam.
            var type = typeof(SessionRunnerRuntime).Assembly.GetType("Antiphon.SessionRunner.RunnerCompletedCardWorkspaceTransport");
            type.ShouldNotBeNull("S6 explicit typed transport must exist");
            var transport = Activator.CreateInstance(type!, r.Workspace, r.Journal, r.Gate)!;
            var property = typeof(SessionRunnerRuntime).GetProperty("CompletedCardWorkspace", BindingFlags.Instance | BindingFlags.NonPublic);
            property.ShouldNotBeNull("S6 runtime opt-in composition must exist");
            property!.SetValue(r.Fixture.Runtime, transport);
        }
        return new(new PhoneHomeRuntimeAdapter(r.Fixture.Runtime, TransportBuild), new PhoneHomeSettings
        {
            RunnerId = "fixture-runner", AllowedCwd = Path.Combine(r.Fixture.Root, "work"),
            RunnerRepository = r.Fixture.Repository, RunnerCloneSource = r.Fixture.Origin,
            LaunchGenerationsPath = Path.Combine(r.Fixture.Root, "transport-launch"),
            CapacityStatePath = Path.Combine(r.Fixture.Root, "transport-capacity"),
        });
    }

    // PC-52: substitute legacy operation 15 -> guarded reply/receipt and physical removal fail.
    [Test] public async Task C1017_GuardedOperationIsDistinct()
    {
        await using var r = new RemovalRig();
        var d = TransportDispatcher(r, true);
        var frame = TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove, r.Command);
        var reply = await d.DispatchAsync(frame, CancellationToken.None);
        reply.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        reply.Operation.ShouldBe(PhoneHomeOperation.CompletedCardWorkspaceRemove);
        reply.RequestId.ShouldBe(frame.RequestId);
        reply.Payload!.Value.GetProperty("complete").GetBoolean().ShouldBeTrue();
        Directory.Exists(r.Fixture.Checkout).ShouldBeFalse();
        r.Journal.Read()!.Single().Receipt!.OperationId.ShouldBe(r.Command.OperationId);
        CompletedCardRunnerFixture.Git(r.Fixture.Repository, "rev-parse", r.Command.Binding.SourceFullRef).Trim().ShouldBe(r.Fixture.Sha);
    }

    // PC-267/268: auto-compose or advertise unconditionally -> default refusal/effect/feature oracle fails.
    [Test] public async Task C1032_TransportDefaultInert()
    {
        await using var r = new RemovalRig();
        var d = TransportDispatcher(r, false);
        d.Capabilities().Features!.Contains(RunnerCapabilityFeatures.CompletedCardWorkspaceV1).ShouldBeFalse();
        r.Fixture.Runtime.DescribeCapabilities(TransportBuild, [],
            [RunnerCapabilityFeatures.CompletedCardWorkspaceV1]).Features!
            .Contains(RunnerCapabilityFeatures.CompletedCardWorkspaceV1).ShouldBeFalse();
        foreach (var op in new[] { PhoneHomeOperation.CompletedCardWorkspaceInspect, PhoneHomeOperation.CompletedCardWorkspaceRemove })
        {
            var response = await d.DispatchAsync(TransportFrame(op, op == PhoneHomeOperation.CompletedCardWorkspaceRemove
                ? r.Command : new CompletedCardWorkspaceInspectRequest(1, r.Command.Binding)), CancellationToken.None);
            response.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedOperation);
        }
        r.Preserved(); r.Journal.Read()!.Count.ShouldBe(0);
        var configured = TransportDispatcher(r, true);
        configured.Capabilities().Features!.Contains(RunnerCapabilityFeatures.CompletedCardWorkspaceV1).ShouldBeTrue();
        var capability = await configured.DispatchAsync(TransportFrame(PhoneHomeOperation.Capabilities, null), CancellationToken.None);
        capability.Payload!.Value.Deserialize<RunnerCapabilitiesDto>(PhoneHomeFraming.Json)!.Features!
            .Contains(RunnerCapabilityFeatures.CompletedCardWorkspaceV1).ShouldBeTrue();
        var read = await configured.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceInspect,
            new CompletedCardWorkspaceInspectRequest(1, r.Command.Binding)), CancellationToken.None);
        read.Kind.ShouldBe(PhoneHomeFrameKind.Result);
        read.Payload!.Value.GetProperty("headSha").GetString().ShouldBe(r.Fixture.Sha);
        r.Preserved(); r.Journal.Read()!.Count.ShouldBe(0);
    }

    // PC-269: default missing fields/Force/version -> exact validation reason or no-Git/no-intent fails.
    [Test] public async Task C1032_TransportRequiredPayload()
    {
        await using var r = new RemovalRig();
        var d = TransportDispatcher(r, true);
        var raw = JsonSerializer.SerializeToNode(r.Command, PhoneHomeFraming.Json)!.AsObject();
        foreach (var field in raw.Select(p => p.Key).ToArray())
        {
            var changed = raw.DeepClone().AsObject(); changed.Remove(field);
            var response = await d.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove, changed), CancellationToken.None);
            response.Kind.ShouldBe(PhoneHomeFrameKind.Error, field);
            response.ErrorCode.ShouldBe("invalid_completed_card_workspace", field);
        }
        foreach (var (body, reason) in new (JsonObject Body, string Reason)[]
        {
            (new() { ["force"] = true }, "force_forbidden"),
            (new() { ["schemaVersion"] = 99 }, "schema_version"),
        })
        {
            var response = await d.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove, body), CancellationToken.None);
            response.ErrorCode.ShouldBe("invalid_completed_card_workspace"); response.ErrorDetail.ShouldBe(reason);
        }
        var inspect = JsonSerializer.SerializeToNode(new CompletedCardWorkspaceInspectRequest(1, r.Command.Binding), PhoneHomeFraming.Json)!.AsObject();
        foreach (var field in inspect["binding"]!.AsObject().Select(p => p.Key).ToArray())
        {
            var changed = inspect.DeepClone().AsObject(); changed["binding"]!.AsObject().Remove(field);
            var response = await d.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceInspect, changed), CancellationToken.None);
            response.ErrorCode.ShouldBe("invalid_completed_card_workspace", field);
        }
        r.Preserved(); r.Journal.Read()!.Count.ShouldBe(0);
    }

    // PC-270: allocate receipt/repeat effects on a new transport request -> durable byte identity fails.
    [Test] public async Task C1032_TransportDispatchReplay()
    {
        await using var r = new RemovalRig();
        var d = TransportDispatcher(r, true);
        var first = await d.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove, r.Command), CancellationToken.None);
        first.Kind.ShouldBe(PhoneHomeFrameKind.Result); first.Payload!.Value.GetProperty("complete").GetBoolean().ShouldBeTrue();
        var saved = new RunnerWorkspaceRemovalJournal(r.Fixture.State, r.Fixture.StoreId).Read()!.Single().Receipt;
        saved.ShouldNotBeNull();
        first.Payload.Value.GetProperty("receipt").GetRawText().ShouldBe(JsonSerializer.Serialize(saved, PhoneHomeFraming.Json));
        Directory.CreateDirectory(r.Fixture.Checkout); File.WriteAllText(Path.Combine(r.Fixture.Checkout, "replacement"), "keep replacement");
        var starts = r.Fixture.GitStarts;
        var replay = await TransportDispatcher(r, true).DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove, r.Command), CancellationToken.None);
        replay.Payload!.Value.GetRawText().ShouldBe(first.Payload.Value.GetRawText());
        r.Fixture.GitStarts.ShouldBe(starts);
        File.ReadAllText(Path.Combine(r.Fixture.Checkout, "replacement")).ShouldBe("keep replacement");
        var changed = await d.DispatchAsync(TransportFrame(PhoneHomeOperation.CompletedCardWorkspaceRemove,
            r.Command with { ReportDigest = new string('f', 64) }), CancellationToken.None);
        changed.Payload!.Value.GetProperty("reason").GetString().ShouldBe("operation_digest_mismatch");
    }
}
