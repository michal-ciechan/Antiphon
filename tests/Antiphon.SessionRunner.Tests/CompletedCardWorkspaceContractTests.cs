using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class CompletedCardWorkspaceContractTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Test]
    public async Task C1032_WireRoundTrip()
    {
        ((int)PhoneHomeOperation.WorkspaceRemove).ShouldBe(15);
        var guardedOperationValue = (int)PhoneHomeOperation.CompletedCardWorkspaceRemove;
        guardedOperationValue.ShouldNotBe(15);
        ((int)PhoneHomeOperation.CompletedCardWorkspaceInspect).ShouldBe(40);
        guardedOperationValue.ShouldBe(41);
        guardedOperationValue.ShouldNotBe((int)PhoneHomeOperation.CompletedCardWorkspaceInspect);

        foreach (var type in new[]
        {
            typeof(CompletedCardWorkspaceCommand), typeof(CompletedCardWorkspaceInspectRequest),
            typeof(CompletedCardWorkspaceBinding), typeof(CompletedCardWorkspaceProof),
            typeof(CompletedCardWorkspaceReceipt), typeof(PhoneHomeWorkspaceRemoveRequest),
        })
        {
            if (type == typeof(PhoneHomeWorkspaceRemoveRequest))
                continue;
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(property => property.Name.Equals("Force", StringComparison.OrdinalIgnoreCase))
                .ShouldBeFalse(type.Name);
        }

        var command = Sample(CompletedCardWorkspaceProtocol.ProofRebased, OtherSha);
        var inspect = new CompletedCardWorkspaceInspectRequest(CompletedCardWorkspaceProtocol.SchemaVersion, command.Binding);
        await RoundTripAsync(PhoneHomeOperation.CompletedCardWorkspaceInspect, inspect);
        await RoundTripAsync(PhoneHomeOperation.CompletedCardWorkspaceRemove, command);
        var serialized = JsonSerializer.Serialize(command, PhoneHomeFraming.Json);
        ContainsName(JsonDocument.Parse(serialized).RootElement, "force").ShouldBeFalse();

        var legacy = JsonSerializer.Deserialize<PhoneHomeFrame>(
            """{"kind":1,"epoch":3,"requestId":"6f4ac2d3-d134-4e9e-ae41-4cac96f84405","operation":15,"payload":{"path":"/tmp/legacy-mirror","force":true,"publishedSha":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}}""",
            PhoneHomeFraming.Json);
        legacy.ShouldNotBeNull();
        legacy.Operation.ShouldBe(PhoneHomeOperation.WorkspaceRemove);
        var body = legacy.Payload!.Value.Deserialize<PhoneHomeWorkspaceRemoveRequest>(PhoneHomeFraming.Json);
        body.ShouldNotBeNull();
        body.Path.ShouldBe("/tmp/legacy-mirror");
        body.Force.ShouldBeTrue();
        body.PublishedSha.ShouldBe("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");

        PhoneHomeRuntimeAdapter.StaticFeatures.Any(feature => string.Equals(
            feature, RunnerCapabilityFeatures.CompletedCardWorkspaceV1, StringComparison.Ordinal)).ShouldBeFalse();
    }

    [Test]
    public async Task C1032_WireRequiredFieldsAndVersion()
    {
        var contained = JsonSerializer.Serialize(Sample(CompletedCardWorkspaceProtocol.ProofContained, null), PhoneHomeFraming.Json);
        var rebased = JsonSerializer.Serialize(Sample(CompletedCardWorkspaceProtocol.ProofRebased, Sha), PhoneHomeFraming.Json);
        CompletedCardWorkspaceContract.Validate(contained).IsValid.ShouldBeTrue();
        CompletedCardWorkspaceContract.Validate(rebased).IsValid.ShouldBeTrue();

        foreach (var (label, remove) in RequiredFields())
        {
            var node = JsonNode.Parse(contained)!.AsObject();
            remove(node);
            var validation = CompletedCardWorkspaceContract.Validate(node.ToJsonString());
            validation.IsValid.ShouldBeFalse(label);
        }

        var versionAccepted = CompletedCardWorkspaceContract.Validate(With(rebased, node => node["schemaVersion"] = 0)).IsValid;
        versionAccepted.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(rebased, node => node["schemaVersion"] = 2)).IsValid.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(rebased, node => node.Remove("schemaVersion"))).IsValid.ShouldBeFalse();

        var duplicate = rebased.Replace("\"operationId\":", "\"operationId\":\"11111111-1111-1111-1111-111111111111\",\"operationId\":", StringComparison.Ordinal);
        var duplicateAccepted = CompletedCardWorkspaceContract.Validate(duplicate).IsValid;
        duplicateAccepted.ShouldBeFalse();

        var forced = rebased.Replace("{", "{\"force\":true,", StringComparison.Ordinal);
        CompletedCardWorkspaceContract.Validate(forced).IsValid.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(rebased, node => node["proof"]!.AsObject()["proofKind"] = "Unknown")).IsValid.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(rebased, node => node["disposition"] = "Publication")).IsValid.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(contained, node => node["proof"]!.AsObject()["rebasedSourceSha"] = Sha)).IsValid.ShouldBeFalse();
        CompletedCardWorkspaceContract.Validate(With(rebased, node => node["proof"]!.AsObject().Remove("rebasedSourceSha"))).IsValid.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C1032_WireDigestFieldCoverage()
    {
        var rebased = JsonSerializer.Serialize(Sample(CompletedCardWorkspaceProtocol.ProofRebased, Sha), PhoneHomeFraming.Json);
        var originalDigest = CompletedCardWorkspaceContract.CanonicalDigest(rebased);
        var equivalentDigest = CompletedCardWorkspaceContract.CanonicalDigest(Reorder(rebased).ToJsonString());
        equivalentDigest.ShouldBe(originalDigest);

        var containedAbsent = JsonSerializer.Serialize(Sample(CompletedCardWorkspaceProtocol.ProofContained, null), PhoneHomeFraming.Json);
        var containedNull = With(containedAbsent, node => node["proof"]!.AsObject()["rebasedSourceSha"] = null);
        CompletedCardWorkspaceContract.CanonicalDigest(containedAbsent).ShouldBe(CompletedCardWorkspaceContract.CanonicalDigest(containedNull));
        CompletedCardWorkspaceContract.CanonicalDigest(containedAbsent).ShouldNotBe(originalDigest);

        foreach (var (label, change) in DigestFields())
        {
            var changedDigest = CompletedCardWorkspaceContract.CanonicalDigest(With(rebased, change));
            changedDigest.ShouldNotBe(originalDigest, label);
        }

        await Task.CompletedTask;
    }

    // F4/VP-08: the landing certificate covers Binding.TaskId. Changing only that field,
    // including to the empty identity, must refuse; mutation: normalize TaskId before the MAC.
    [Test]
    public async Task C1032_LandingSignatureCoversTaskId()
    {
        const string key = "landing-map-key";
        var landing = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var signed = CompletedCardWorkspaceLandingAuthority.Sign(Sample(CompletedCardWorkspaceProtocol.ProofRebased, Sha), landing, key);
        signed.Proof.ProofIdentity.ShouldStartWith("landed-v1:" + landing.ToString("D") + ":");
        CompletedCardWorkspaceLandingAuthority.Verify(signed, key).ShouldBeTrue();
        CompletedCardWorkspaceLandingAuthority.Verify(signed, "other-key").ShouldBeFalse();

        foreach (var taskId in new[] { Guid.Parse("55555555-5555-5555-5555-555555555555"), Guid.Empty })
        {
            var changed = signed with { Binding = signed.Binding with { TaskId = taskId } };
            changed.Binding.TaskId.ShouldNotBe(signed.Binding.TaskId);
            (changed with { Binding = signed.Binding }).ShouldBe(signed);
            CompletedCardWorkspaceLandingAuthority.Verify(changed, key).ShouldBeFalse(taskId.ToString("D"));
        }

        await Task.CompletedTask;
    }

    private static async Task RoundTripAsync<T>(PhoneHomeOperation operation, T body)
    {
        var frame = new PhoneHomeFrame(
            PhoneHomeFrameKind.Request, 4, Guid.Parse("6f4ac2d3-d134-4e9e-ae41-4cac96f84405"), operation,
            JsonSerializer.SerializeToElement(body, PhoneHomeFraming.Json));
        var json = JsonSerializer.Serialize(frame, PhoneHomeFraming.Json);
        var back = JsonSerializer.Deserialize<PhoneHomeFrame>(json, PhoneHomeFraming.Json);
        back.ShouldNotBeNull();
        back.Operation.ShouldBe(operation);
        back.Epoch.ShouldBe(4);
        var restored = back.Payload!.Value.Deserialize<T>(PhoneHomeFraming.Json);
        JsonSerializer.Serialize(restored, PhoneHomeFraming.Json).ShouldBe(JsonSerializer.Serialize(body, PhoneHomeFraming.Json));
        await Task.CompletedTask;
    }

    private static CompletedCardWorkspaceCommand Sample(string proofKind, string? rebased) => new(
        CompletedCardWorkspaceProtocol.SchemaVersion,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
        Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
        CompletedCardWorkspaceProtocol.DispositionCardDone,
        new CompletedCardWorkspaceBinding(
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 1,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "runner-a", "store-a", "repo-a", "/common/a", "/git/a", "/trees/a",
            "refs/heads/feat/a", Sha),
        new CompletedCardWorkspaceProof(Sha, "fingerprint-a", "refs/heads/master", Sha, proofKind, "proof-a", Sha, rebased),
        "report-a", "artifact-a", "policy-a",
        ["pointer-b", "pointer-a"],
        ["pattern-b", "pattern-a"]);

    private static (string Label, Action<JsonObject> Remove)[] RequiredFields() =>
    [
        ("required.CardId", node => node.Remove("cardId")),
        ("required.DoneRevisionId", node => node.Remove("doneRevisionId")),
        ("required.TargetId", node => node.Remove("targetId")),
        ("required.EndpointId", node => node.Remove("endpointId")),
        ("required.TaskId", node => node["binding"]!.AsObject().Remove("taskId")),
        ("required.Attempt", node => node["binding"]!.AsObject().Remove("attempt")),
        ("required.BindingId", node => node["binding"]!.AsObject().Remove("bindingId")),
        ("required.PreparationId", node => node["binding"]!.AsObject().Remove("preparationId")),
        ("required.RunnerId", node => node["binding"]!.AsObject().Remove("runnerId")),
        ("required.RunnerStoreId", node => node["binding"]!.AsObject().Remove("runnerStoreId")),
        ("required.RepositoryIdentity", node => node["binding"]!.AsObject().Remove("repositoryIdentity")),
        ("required.CommonDirectory", node => node["binding"]!.AsObject().Remove("commonDirectory")),
        ("required.GitDirectory", node => node["binding"]!.AsObject().Remove("gitDirectory")),
        ("required.WorktreePath", node => node["binding"]!.AsObject().Remove("worktreePath")),
        ("required.SourceFullRef", node => node["binding"]!.AsObject().Remove("sourceFullRef")),
        ("required.CreationBaselineSha", node => node["binding"]!.AsObject().Remove("creationBaselineSha")),
        ("required.OperationId", node => node.Remove("operationId")),
        ("required.ExpectedHeadSha", node => node["proof"]!.AsObject().Remove("expectedHeadSha")),
        ("required.DestinationFingerprint", node => node["proof"]!.AsObject().Remove("destinationFingerprint")),
        ("required.DestinationRef", node => node["proof"]!.AsObject().Remove("destinationRef")),
        ("required.ObservedTargetSha", node => node["proof"]!.AsObject().Remove("observedTargetSha")),
        ("required.ProofKind", node => node["proof"]!.AsObject().Remove("proofKind")),
        ("required.ProofIdentity", node => node["proof"]!.AsObject().Remove("proofIdentity")),
        ("required.PreservedSourceSha", node => node["proof"]!.AsObject().Remove("preservedSourceSha")),
        ("required.ReportDigest", node => node.Remove("reportDigest")),
        ("required.ArtifactProofDigest", node => node.Remove("artifactProofDigest")),
        ("required.DisposalPolicyDigest", node => node.Remove("disposalPolicyDigest")),
        ("required.Disposition", node => node.Remove("disposition")),
    ];

    private static (string Label, Action<JsonObject> Change)[] DigestFields() =>
    [
        ("digest.SchemaVersion", node => node["schemaVersion"] = 2),
        ("digest.CardId", node => node["cardId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.DoneRevisionId", node => node["doneRevisionId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.TargetId", node => node["targetId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.EndpointId", node => node["endpointId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.TaskId", node => node["binding"]!.AsObject()["taskId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.Attempt", node => node["binding"]!.AsObject()["attempt"] = 2),
        ("digest.BindingId", node => node["binding"]!.AsObject()["bindingId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.PreparationId", node => node["binding"]!.AsObject()["preparationId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.RunnerId", node => node["binding"]!.AsObject()["runnerId"] = "runner-b"),
        ("digest.RunnerStoreId", node => node["binding"]!.AsObject()["runnerStoreId"] = "store-b"),
        ("digest.RepositoryIdentity", node => node["binding"]!.AsObject()["repositoryIdentity"] = "repo-b"),
        ("digest.CommonDirectory", node => node["binding"]!.AsObject()["commonDirectory"] = "/common/b"),
        ("digest.GitDirectory", node => node["binding"]!.AsObject()["gitDirectory"] = "/git/b"),
        ("digest.WorktreePath", node => node["binding"]!.AsObject()["worktreePath"] = "/trees/b"),
        ("digest.SourceFullRef", node => node["binding"]!.AsObject()["sourceFullRef"] = "refs/heads/feat/b"),
        ("digest.CreationBaselineSha", node => node["binding"]!.AsObject()["creationBaselineSha"] = OtherSha),
        ("digest.OperationId", node => node["operationId"] = "99999999-9999-9999-9999-999999999999"),
        ("digest.ExpectedHeadSha", node => node["proof"]!.AsObject()["expectedHeadSha"] = OtherSha),
        ("digest.DestinationFingerprint", node => node["proof"]!.AsObject()["destinationFingerprint"] = "fingerprint-b"),
        ("digest.DestinationRef", node => node["proof"]!.AsObject()["destinationRef"] = "refs/heads/other"),
        ("digest.ObservedTargetSha", node => node["proof"]!.AsObject()["observedTargetSha"] = OtherSha),
        ("digest.ProofKind", node => node["proof"]!.AsObject()["proofKind"] = CompletedCardWorkspaceProtocol.ProofContained),
        ("digest.ProofIdentity", node => node["proof"]!.AsObject()["proofIdentity"] = "proof-b"),
        ("digest.PreservedSourceSha", node => node["proof"]!.AsObject()["preservedSourceSha"] = OtherSha),
        ("digest.ReportDigest", node => node["reportDigest"] = "report-b"),
        ("digest.ArtifactProofDigest", node => node["artifactProofDigest"] = "artifact-b"),
        ("digest.DisposalPolicyDigest", node => node["disposalPolicyDigest"] = "policy-b"),
        ("digest.Disposition", node => node["disposition"] = "Other"),
        ("digest.RebasedSourceSha", node => node["proof"]!.AsObject()["rebasedSourceSha"] = OtherSha),
        ("digest.ArtifactPointers", node => node["artifactPointers"] = new JsonArray("pointer-z")),
        ("digest.PolicyPatterns", node => node["policyPatterns"] = new JsonArray("pattern-z")),
    ];

    private static string With(string json, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    private static JsonObject Reorder(string json)
    {
        var source = JsonNode.Parse(json)!.AsObject();
        var reversed = Reverse(source);
        reversed["artifactPointers"] = ReverseArray(source["artifactPointers"]!.AsArray());
        reversed["policyPatterns"] = ReverseArray(source["policyPatterns"]!.AsArray());
        reversed["binding"] = Reverse(source["binding"]!.AsObject());
        reversed["proof"] = Reverse(source["proof"]!.AsObject());
        return reversed;
    }

    private static JsonObject Reverse(JsonObject source)
    {
        var reversed = new JsonObject();
        foreach (var property in source.Reverse())
            reversed[property.Key] = property.Value?.DeepClone();
        return reversed;
    }

    private static JsonArray ReverseArray(JsonArray source)
    {
        var reversed = new JsonArray();
        foreach (var item in source.Reverse())
            reversed.Add(item?.DeepClone());
        return reversed;
    }

    private static bool ContainsName(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || ContainsName(property.Value, name))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsName(item, name))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }
}
