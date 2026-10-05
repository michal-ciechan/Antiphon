using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReviewEvidenceRecoveryEndpointTests
{
    private sealed class Factory(ReviewRecoveryWorld world) : AntiphonWebAppFactory
    {
        protected override string ConnectionString => world.World.Schema.ConnectionString;
        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && (d.ImplementationType == typeof(AgentTaskLandHostedService)
                    || d.ImplementationType == typeof(AgentTaskLandSweepHostedService))).ToList()) services.Remove(descriptor);
            services.RemoveAll<ITaskProgressGit>(); services.AddSingleton<ITaskProgressGit>(world.World.Git.Git);
        }
    }
    private static string Url(ReviewRecoveryWorld w) => $"/api/agent-tasks/{w.ReviewId:D}/review-evidence/rebind";
    private static async Task<Guid> ProjectAsync(ReviewRecoveryWorld w)
    {
        await using var db = w.Db();
        var project = new Project { Id = Guid.NewGuid(), Name = "recovery HTTP", LocalRepositoryPath = w.World.Git.Desktop,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(project);
        (await db.AgentTasks.SingleAsync(t => t.Id == w.ReviewId)).ProjectId = project.Id;
        await db.SaveChangesAsync(); return project.Id;
    }
    private static async Task<string> TaskCredentialAsync(ReviewRecoveryWorld w, Guid project, AgentTaskKind kind)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await using var db = w.Db(); var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Title = "credential", Goal = "credential",
            WorkingDirectory = w.World.Git.Desktop, RepoPath = w.World.Git.Desktop, ProjectId = project,
            Kind = kind, TokenHash = AgentTaskService.HashToken(token) });
        await db.SaveChangesAsync(); return token;
    }
    private static async Task<(string Token, Guid Id)> CapabilityAsync(ReviewRecoveryWorld w, Guid project, string root, bool revoked = false)
    {
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await using var db = w.Db(); var id = Guid.NewGuid();
        db.DelegationCapabilities.Add(new DelegationCapability { Id = id, Name = "recovery-" + id.ToString("N")[..8],
            TokenHash = AgentTaskService.HashToken(token), ProjectId = project, RootsJson = JsonSerializer.Serialize(new[] { root }),
            CreatedAt = DateTime.UtcNow, RevokedAt = revoked ? DateTime.UtcNow : null });
        await db.SaveChangesAsync(); return (token, id);
    }
    private static void Operator(HttpClient client, Factory factory) =>
        client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(factory.OperatorTokenPath));
    private static void Principal(HttpClient client, string token) => client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", token);
    private static async Task DeniedAsync(ReviewRecoveryWorld w, HttpClient client, string label)
    {
        using var response = await client.PostAsJsonAsync(Url(w), w.Request);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, label);
        await w.UnchangedAsync(label);
    }
    [Test]
    public async Task C1043_CredentialRequired()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); var project = await ProjectAsync(w);
        await using var factory = new Factory(w);
        using (var client = factory.CreateClient()) await DeniedAsync(w, client, "G64 anonymous");
        using (var client = factory.CreateClient()) { Principal(client, "synthetic-invalid"); await DeniedAsync(w, client, "G64 invalid"); }
        var revoked = await CapabilityAsync(w, project, w.World.Git.Desktop, true);
        using (var client = factory.CreateClient()) { Principal(client, revoked.Token); await DeniedAsync(w, client, "G64 revoked"); }
        using var op = factory.CreateClient(); Operator(op, factory);
        using var result = await op.PostAsJsonAsync(Url(w), w.Request);
        result.StatusCode.ShouldBe(HttpStatusCode.OK, "operator positive");
    }
    [Test]
    public async Task C1043_WorkerDenied()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); var project = await ProjectAsync(w);
        await using var factory = new Factory(w);
        using (var worker = factory.CreateClient())
        { Principal(worker, await TaskCredentialAsync(w, project, AgentTaskKind.Worker)); await DeniedAsync(w, worker, "G65 worker"); }
        using var orchestrator = factory.CreateClient();
        Principal(orchestrator, await TaskCredentialAsync(w, project, AgentTaskKind.Orchestrator));
        using var response = await orchestrator.PostAsJsonAsync(Url(w), w.Request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "orchestrator task positive");
    }
    [Test]
    public async Task C1043_ProjectScope()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); var project = await ProjectAsync(w);
        await using var factory = new Factory(w);
        var other = await ProjectAsync(w); await w.ChangeAsync(t => t.ProjectId = project);
        using (var client = factory.CreateClient())
        {
            Principal(client, await TaskCredentialAsync(w, other, AgentTaskKind.Orchestrator));
            await DeniedAsync(w, client, "G66 wrong project task");
        }
        var cap = await CapabilityAsync(w, other, w.World.Git.Desktop);
        using (var client = factory.CreateClient()) { Principal(client, cap.Token); await DeniedAsync(w, client, "G66 wrong project capability"); }
        // A standing session's project is derived from its board, never request text.
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await using (var db = w.Db())
        {
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project, Name = "recovery" };
            db.Boards.Add(board);
            db.Agents.Add(new Agent { Id = Guid.NewGuid(), Name = "recovery", Slug = "recovery", BoardId = board.Id,
                WorkingDirectory = w.World.Git.Desktop, PersistentSessionId = w.World.CallerSessionId.ToString("D") });
            (await db.AgentSessions.SingleAsync(s => s.Id == w.World.CallerSessionId)).DelegationTokenHash = AgentTaskService.HashToken(token);
            await db.SaveChangesAsync();
        }
        using var standing = factory.CreateClient(); Principal(standing, token);
        using var success = await standing.PostAsJsonAsync(Url(w), w.Request);
        success.StatusCode.ShouldBe(HttpStatusCode.OK, "standing session positive");
    }
    [Test]
    public async Task C1043_RepositoryScope()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); var project = await ProjectAsync(w);
        await using var factory = new Factory(w);
        foreach (var root in new[] { w.World.Git.Desktop + "-sibling", Path.Combine(w.World.Git.Desktop, "child"), Path.GetTempPath() + "unrelated-" + Guid.NewGuid() })
        {
            var cap = await CapabilityAsync(w, project, root);
            using var client = factory.CreateClient(); Principal(client, cap.Token);
            await DeniedAsync(w, client, "G67 root boundaries");
        }
        var valid = await CapabilityAsync(w, project, w.World.Git.Desktop);
        using var scoped = factory.CreateClient(); Principal(scoped, valid.Token);
        using var response = await scoped.PostAsJsonAsync(Url(w), w.Request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "capability positive");
    }
    [Test]
    public async Task C1043_SelectorsOnly()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); await ProjectAsync(w);
        await using var factory = new Factory(w); using var client = factory.CreateClient(); Operator(client, factory);
        foreach (var field in new[] { "subjectTaskId", "reviewedSourceSha", "reviewedSourceClean", "ordinaryScopeCompleted", "actor" })
        {
            var body = new Dictionary<string, object> { ["evidenceId"] = w.OldId,
                ["expectedReportSha256"] = w.Request.ExpectedReportSha256, ["reason"] = "HTTP audit", [field] = "override" };
            using var response = await client.PostAsync(Url(w), new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "G68 " + field);
            await w.UnchangedAsync("G68 " + field);
        }
    }
    [Test]
    public async Task C1043_ResponseAfterCommit()
    {
        await using var w = await ReviewRecoveryWorld.CreateAsync(); var project = await ProjectAsync(w);
        var cap = await CapabilityAsync(w, project, w.World.Git.Desktop);
        await using var factory = new Factory(w); using var client = factory.CreateClient(); Principal(client, cap.Token);
        using var first = await client.PostAsJsonAsync(Url(w), w.Request);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt = (await first.Content.ReadFromJsonAsync<ReviewEvidenceRecoveryResponse>()).ShouldNotBeNull();
        receipt.Disposition.ShouldBe("bound"); receipt.PreviousEvidenceId.ShouldBe(w.OldId);
        receipt.ReportSha256.ShouldBe(w.Request.ExpectedReportSha256); receipt.SubjectTaskId.ShouldBe(w.SubjectId);
        await using (var db = w.Db())
        {
            var row = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == receipt.ReviewEvidenceId);
            row.SupersedesId.ShouldBe(w.OldId, "G69 fresh committed DB row");
            var actor = "capability:" + cap.Id.ToString("D");
            ReviewRebindProvenance.TryParse(row.Ref)!.Actor.ShouldBe(actor, "G112");
            JsonDocument.Parse((await w.AuditsAsync()).ShouldHaveSingleItem().Detail).RootElement.GetProperty("actor").GetString().ShouldBe(actor, "G112");
        }
        using var get = await client.GetAsync($"/api/agent-tasks/{w.ReviewId:D}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await get.Content.ReadAsStringAsync()).ShouldContain(receipt.ReviewEvidenceId.ToString("D"), "G69 fresh GET");
        // Ignore the first response as if lost; a new HTTP client retries the exact selectors.
        using var retry = factory.CreateClient(); Principal(retry, cap.Token);
        using var repeated = await retry.PostAsJsonAsync(Url(w), w.Request);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = (await repeated.Content.ReadFromJsonAsync<ReviewEvidenceRecoveryResponse>()).ShouldNotBeNull();
        second.Disposition.ShouldBe("already-bound"); second.ReviewEvidenceId.ShouldBe(receipt.ReviewEvidenceId, "G69");
        (await w.AuditsAsync()).ShouldHaveSingleItem();
    }
}
