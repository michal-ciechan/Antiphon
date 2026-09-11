using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PostLandMutationReceiptPolicyTests
{
    [Test]
    [Arguments("schema")]
    [Arguments("revision")]
    [Arguments("drain")]
    [Arguments("seal")]
    [Arguments("observation-time")]
    [Arguments("method")]
    [Arguments("nonempty")]
    [Arguments("unknown")]
    [Arguments("unsupported")]
    [Arguments("store")]
    [Arguments("host")]
    [Arguments("container")]
    [Arguments("task")]
    [Arguments("operation")]
    [Arguments("sha")]
    [Arguments("execution")]
    [Arguments("session")]
    [Arguments("generation")]
    [Arguments("creation")]
    [Arguments("path")]
    [Arguments("repository")]
    [Arguments("common")]
    [Arguments("git-directory")]
    [Arguments("branch")]
    [Arguments("missing-field")]
    public void C478_ImportedReceiptRequiresEveryIndependentCoordinate(string variant)
    {
        var now = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        var binding = new VerificationExecutionBinding(Guid.NewGuid(), new(Guid.NewGuid(), Guid.NewGuid(), new string('a', 40)),
            new(Guid.NewGuid(), now), new(@"C:\repo", @"C:\repo\.git", @"C:\trees\snapshot", @"C:\repo\.git\worktrees\snapshot",
                "feat/card-task-12345678", Guid.NewGuid()), RunnerStoreId: Guid.NewGuid());
        var host = new VerificationHostIdentity(binding.RunnerStoreId, Guid.NewGuid(), Guid.NewGuid(), 123, now);
        var valid = new VerificationCustodyReceipt(1, binding, host, 3, now, now, "JobObjectBasicAccountingInformation", 0, true,
            VerificationCustodyState.Exited, 124, now);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var policy = new VerificationReceiptPolicy();
        policy.Validate(JsonSerializer.SerializeToUtf8Bytes(valid, options), binding, host).ShouldBe(valid);
        var bad = variant switch
        {
            "schema" => valid with { SchemaVersion = 2 },
            "revision" => valid with { StateRevision = 2 },
            "drain" => valid with { OutputDrained = false },
            "seal" => valid with { SealedAtUtc = default },
            "observation-time" => valid with { ObservedAtUtc = now.AddSeconds(-1) },
            "method" => valid with { ObservationMethod = "root-exit" },
            "nonempty" => valid with { ActiveProcesses = 1 },
            "unknown" => valid with { Disposition = VerificationCustodyState.Unknown },
            "unsupported" => valid with { Disposition = VerificationCustodyState.UnsupportedBackend },
            "store" => valid with { Host = host with { RunnerStoreId = Guid.NewGuid() } },
            "host" => valid with { Host = host with { HostInstanceId = Guid.NewGuid() } },
            "container" => valid with { Host = host with { ContainerId = Guid.NewGuid() } },
            "task" => valid with { Binding = binding with { Source = binding.Source with { TaskId = Guid.NewGuid() } } },
            "operation" => valid with { Binding = binding with { Source = binding.Source with { SourceOperationId = Guid.NewGuid() } } },
            "sha" => valid with { Binding = binding with { Source = binding.Source with { LandedSha = new string('b', 40) } } },
            "execution" => valid with { Binding = binding with { ExecutionId = Guid.NewGuid() } },
            "session" => valid with { Binding = binding with { Generation = binding.Generation with { SessionId = Guid.NewGuid() } } },
            "generation" => valid with { Binding = binding with { Generation = binding.Generation with { AcceptedStartedAt = now.AddSeconds(1) } } },
            "creation" => valid with { Binding = binding with { Creation = binding.Creation with { CreationId = Guid.NewGuid() } } },
            "path" => valid with { Binding = binding with { Creation = binding.Creation with { WorktreePath = @"C:\stranger" } } },
            "repository" => valid with { Binding = binding with { Creation = binding.Creation with { RepositoryPath = @"C:\stranger" } } },
            "common" => valid with { Binding = binding with { Creation = binding.Creation with { CommonGitDirectory = @"C:\stranger" } } },
            "git-directory" => valid with { Binding = binding with { Creation = binding.Creation with { WorktreeGitDirectory = @"C:\stranger" } } },
            "branch" => valid with { Binding = binding with { Creation = binding.Creation with { Branch = "stranger" } } },
            _ => valid,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bad, options);
        if (variant == "missing-field")
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
            document.AsObject().Remove("sealedAtUtc");
            Should.Throw<JsonException>(() => policy.Validate(JsonSerializer.SerializeToUtf8Bytes(document, options), binding, host));
        }
        else Should.Throw<VerificationCustodyException>(() => policy.Validate(bytes, binding, host));
    }
}
