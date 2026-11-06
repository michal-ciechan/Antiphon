using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Application;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

internal static class RefOnlyDecisionWitness
{
    public static async Task VerifyAsync()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var now = DateTime.UtcNow;
        var generation = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        var id = Guid.NewGuid();
        await using var recipient = await RunnerSeatReleaseFixture.LiveSeat.CreateAsync(id, generation, "Claude", phoneHome: true);
        var root = f.Git.Runner;
        const string relative = "scripts/deploy-gym-stat.ps1";
        var script = Path.Combine(root, "scripts", "deploy-gym-stat.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        await File.WriteAllTextAsync(script, "bound runner bytes");
        Directory.CreateDirectory(Path.Combine(f.Identity, "scripts"));
        var decoy = Path.Combine(f.Identity, "scripts", "deploy-gym-stat.ps1");
        await File.WriteAllTextAsync(decoy, "foreign identity bytes");
        recipient.Checkout = root;
        recipient.Session.RetainCheckout(root);
        var session = new AgentSession { Id = id, Cwd = f.Identity, RunnerId = recipient.PhoneHomeRunnerId,
            RunnerStoreId = recipient.Runtime.RunnerStoreId, RunnerCwd = root,
            StartedAt = generation, CreatedAt = generation, Status = SessionStatus.Running };
        f.Db.AgentSessions.Add(session);
        f.Task.RunnerId = session.RunnerId;
        f.Task.RemoteWorktreePath = root;
        f.Task.AgentSessionId = id;
        f.Task.Status = AgentTaskStatus.Working;
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(), AgentTaskRole.Code,
            WorkspaceMode.Worktree, InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        f.Task.InternalDecisionPolicyJson = InternalDecisionPolicy.Serialize(policy);
        f.Task.InternalDecisionPolicyHash = InternalDecisionPolicy.Hash(f.Task.InternalDecisionPolicyJson);
        await f.Db.SaveChangesAsync();
        var request = new InternalDecisionQuestionRequest(Guid.NewGuid(), f.Task.Attempt, "backup-transport",
            InternalDecisionCategory.ShellTransport, [relative], null, null,
            InternalDecisionImpact.None, "Repair quoting?", "Preserve the command arguments.", "Exact bytes compared.");
        var caller = new AgentTaskService.Caller(f.Task, id, f.Identity);
        var service = new AgentTaskDecisionQuestionService(f.Db, runners: recipient.PhoneHomeDirectory);
        var inspected = await recipient.Client.InspectDecisionPathsAsync(new(id, session.RunnerStoreId!.Value,
            generation, root, [relative]), default);
        inspected.ShouldNotBeNull("reached:U2:real-phone-home:inspection").Contained.ShouldBeTrue("runner-file-contained");
        var granted = await service.CheckAsync(f.Task.Id, request, caller, default);
        granted.Disposition.ShouldBe(InternalDecisionDisposition.Continue, "grant-root=runner");
        granted.Reason.ShouldBe("dispatch_grant");
        (await service.CheckAsync(f.Task.Id, request, caller, default)).ShouldBe(granted, "durable-grant-idempotent");

        // A real external link at the granted spelling must withhold, despite a regular
        // same-named identity file and a previously successful exact-file grant.
        File.Delete(script);
        File.CreateSymbolicLink(script, decoy);
        var linked = await service.CheckAsync(f.Task.Id, request with { RequestId = Guid.NewGuid() }, caller, default);
        linked.Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman, "runner-link-refused");
        File.Delete(script);
        await File.WriteAllTextAsync(script, "bound runner bytes");
        var store = session.RunnerStoreId;
        session.RunnerStoreId = Guid.NewGuid();
        await f.Db.SaveChangesAsync();
        (await service.CheckAsync(f.Task.Id, request with { RequestId = Guid.NewGuid() }, caller, default))
            .Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman, "wrong-store-refused");
        session.RunnerStoreId = store;
        session.StartedAt = generation.AddSeconds(1);
        await f.Db.SaveChangesAsync();
        (await service.CheckAsync(f.Task.Id, request with { RequestId = Guid.NewGuid() }, caller, default))
            .Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman, "wrong-generation-refused");
        session.StartedAt = generation;
        session.RunnerId = "different-runner";
        await f.Db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => service.CheckAsync(f.Task.Id,
            request with { RequestId = Guid.NewGuid() }, caller, default)))
            .Code.ShouldBe("ref_only_execution_binding_required", "wrong-runner-refused");
        session.RunnerId = f.Task.RunnerId;
        await f.Db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => service.CheckAsync(f.Task.Id,
            request with { RequestId = Guid.NewGuid() }, caller with { SessionId = Guid.NewGuid() }, default)))
            .Code.ShouldBe("decision_question_stale", "wrong-session-refused");
        foreach (var path in new[] { "../outside.ps1", decoy, Path.Combine(f.Git.Desktop, relative) })
            await Should.ThrowAsync<ValidationException>(() => service.CheckAsync(f.Task.Id,
                request with { RequestId = Guid.NewGuid(), Paths = [path] }, caller, default));
        (await new AgentTaskDecisionQuestionService(f.Db).CheckAsync(f.Task.Id,
            request with { RequestId = Guid.NewGuid() }, caller, default)).Disposition
            .ShouldBe(InternalDecisionDisposition.NeedsHuman, "unsupported-verifier-withholds");
        (await service.CheckAsync(f.Task.Id, request with { RequestId = Guid.NewGuid() }, caller, default))
            .Disposition.ShouldBe(InternalDecisionDisposition.Continue, "restored-runner-file-grants");
        (await File.ReadAllTextAsync(decoy)).ShouldBe("foreign identity bytes", "identity-file-intact");
        (await File.ReadAllTextAsync(Path.Combine(f.Git.Desktop, "README.md"))).ShouldBe("base\n", "main-file-intact");
        recipient.NativeSubmissions.ShouldBeEmpty("decision-inspection-never-types-input");
    }
}
