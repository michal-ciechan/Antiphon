using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandRemoteSourceRetentionDTests
{
    [Test]
    [Arguments("V28")]
    [Arguments("V29")]
    [Arguments("V30")]
    [Arguments("V31")]
    [Arguments("V32")]
    [Arguments("V33")]
    [Arguments("V34")]
    [Arguments("V35")]
    public async Task C448_V21_RefusalNeverDeletesRemoteSource(string family)
    {
        await using var h = new LandingSafetyHarness();
        if (family == "V34")
            h.ConfigureServices = services => services.AddSingleton<ILandingVerifier>(new LandingVerifier());
        await h.InitializeAsync();
        await Card0452LandingCases.AssertSeededRemoteSourceAsync(h);
        if (family != "V29") await h.AddSourceAsync();
        h.Fixture.Git.Trace.Clear();
        switch (family)
        {
            case "V28": await DeadJournalHoldsAsync(h); break;
            case "V29": await ExactClaimHoldsAsync(h); break;
            case "V30": await AmbiguousDestinationAsync(h); break;
            case "V31": await UnknownSchemaAsync(h); break;
            case "V32": await PreparedPinFailureAsync(h); break;
            case "V33": await RepeatedCleanupRefusalAsync(h); break;
            case "V34": await RealFailingVerifierAsync(h); break;
            case "V35": await ProtectedBuildOutputAsync(h); break;
        }
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
        await h.RestartServicesAsync();
        await h.SweepAsync();
        if (family == "V35") await Card0452LandingCases.RunScopedResidueAsync(h);
        await Card0452LandingCases.AssertRemoteSourceAndPushSafetyAsync(h);
    }

    private static async Task DeadJournalHoldsAsync(LandingSafetyHarness h)
    {
        var journal = await RepositoryChildJournal.BeginAsync(h.Fixture.Repository, CancellationToken.None);
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 1" }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        await journal.StartedAsync(child, CancellationToken.None);
        await child.WaitForExitAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        await using var db = h.CreateContext();
        (await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId))
            .HoldReasonCode.ShouldBe("repository_mutation_lease_busy");
        (await h.OperationAsync()).ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
    }

    private static async Task ExactClaimHoldsAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        var claimant = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = claimant, RootTaskId = claimant, Title = "exact source follow-up", Goal = "follow up",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, Workspace = WorkspaceMode.Worktree,
            Status = AgentTaskStatus.Working, RepoPath = h.Fixture.Repository, WorkingDirectory = h.Fixture.Repository,
            WorktreePath = h.Fixture.Source, WorktreeBranch = h.Fixture.SourceRef[11..],
            FollowUpOfTaskId = h.Fixture.TaskId, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await using (var native = await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Source, CancellationToken.None)) native.ShouldNotBeNull();
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        request.HoldReasonCode.ShouldBe("repository_or_source_writer");
        request.HoldingTaskId.ShouldBe(claimant);
        (await h.OperationAsync()).ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
    }

    private static async Task AmbiguousDestinationAsync(LandingSafetyHarness h)
    {
        var other = Path.Combine(h.Fixture.Root, "second-endpoint.git");
        await h.Fixture.RequiredAsync(h.Fixture.Root, "clone", "--bare", h.Fixture.Remote, other);
        (await h.Fixture.RequiredAsync(other, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        var fired = false;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (!fired && args[0] == "symbolic-ref" && args.Contains(h.Fixture.TargetRef))
            {
                fired = true;
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "--add", "remote.origin.pushurl", h.Fixture.Remote);
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "config", "--add", "remote.origin.pushurl", other);
            }
            return null;
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        fired.ShouldBeTrue("the source must resolve before the target endpoint becomes ambiguous");
        var op = await h.OperationAsync();
        (op?.RemoteConfirmedAt).ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        await using (var db = h.CreateContext())
        {
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.SourceRefusalReason.ShouldBe("landing_io_error");
            request.SourceDiagnosticExceptionType.ShouldBe("IOException");
        }
        var endpoints = await h.Fixture.RequiredAsync(h.Fixture.Repository, "remote", "get-url", "--push", "--all", "origin");
        endpoints.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(other, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        var observer = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        var fetched = await observer.RunAsync(h.Fixture.Observer,
            ["fetch", "--no-tags", other, h.Fixture.SourceRef], CancellationToken.None);
        fetched.Succeeded.ShouldBeTrue(fetched.Diagnostic);
    }

    private static async Task UnknownSchemaAsync(LandingSafetyHarness h)
    {
        h.Fault.Phase = LandPhase.Verified;
        h.Fault.AfterCommit = true;
        await Should.ThrowAsync<LandingSafetyHarness.InjectedSaveFailure>(() => h.RunAsync());
        h.Fault.Triggered.ShouldBeTrue();
        await using (var db = h.CreateContext())
        {
            var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == h.Fixture.TaskId);
            op.SchemaVersion = 999;
            await db.SaveChangesAsync();
        }
        h.Fault.AfterCommit = false;
        await h.RestartServicesAsync();
        h.Fixture.Git.Trace.Clear();
        try { await h.RunAsync(); }
        catch (InvalidOperationException) { }
        (await h.OperationAsync()).ShouldNotBeNull().LastReason.ShouldBe("landing_schema_unsupported");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase") || a.Contains("remove"));
    }

    private static async Task PreparedPinFailureAsync(LandingSafetyHarness h)
    {
        var fired = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (!fired && args[0] == "update-ref" && args.Count == 4 && args[1].EndsWith("/prepared", StringComparison.Ordinal))
            {
                fired = true;
                return Task.FromResult<LandingGitResult?>(new(128, "", "prepared pin failed"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        await h.RunAsync();
        fired.ShouldBeTrue();
        (await h.OperationAsync()).ShouldNotBeNull().LastReason.ShouldBe("recovery_pin_failed");
        h.Verifier.Calls.ShouldBe(0);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        h.Fixture.Git.BeforeCommand = null;
    }

    private static async Task RepeatedCleanupRefusalAsync(LandingSafetyHarness h)
    {
        var sentinel = Path.Combine(h.Fixture.Source, "bin-land", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "private\n");
        await h.RunAsync();
        var receipt = (await h.OperationAsync()).ShouldNotBeNull();
        receipt.RemoteConfirmedAt.ShouldNotBeNull();
        receipt.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        await h.RepostAsync();
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        var retry = (await h.OperationAsync()).ShouldNotBeNull();
        retry.Id.ShouldBe(receipt.Id);
        retry.RemoteConfirmedAt.ShouldBe(receipt.RemoteConfirmedAt);
        retry.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId &&
            (e.Type == AgentTaskEventType.Landed || e.Type == AgentTaskEventType.LandedWithResidue ||
             e.Type == AgentTaskEventType.AlreadyPresent))).ShouldBe(1);
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("private\n");
    }

    private static async Task RealFailingVerifierAsync(LandingSafetyHarness h)
    {
        var project = Path.Combine(h.Fixture.Source, "tests", "Antiphon.Tests");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
            <ItemGroup><ProjectReference Include="tests/Antiphon.Tests/Antiphon.Tests.csproj" /></ItemGroup></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(project, "Antiphon.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>
            <EnableMicrosoftTestingPlatformRunner>true</EnableMicrosoftTestingPlatformRunner>
            <ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
            <PackageReference Include="TUnit" Version="1.44.0" /></ItemGroup></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(project, "Tests.cs"), """
            using TUnit.Core;
            [Category("Integration")]
            public sealed class VerificationProbe
            {
                [Test] public void SelectedFailure() => throw new Exception("selected failure");
            }
            """);
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-m", "tiny failing verifier project");
        h.Fixture.Git.Trace.Clear();
        await h.RequestAsync(filter: "/*/*/VerificationProbe/SelectedFailure");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LastReason.ShouldBe("verification_failed");
        op.RemoteConfirmedAt.ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
    }

    private static async Task ProtectedBuildOutputAsync(LandingSafetyHarness h)
    {
        var sentinel = Path.Combine(h.Fixture.Source, "bin-land", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "protected settings\n");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("ignored_content_preserved");
        await h.RepostAsync();
        await h.RunAsync();
        (await File.ReadAllTextAsync(sentinel)).ShouldBe("protected settings\n");
    }

}
