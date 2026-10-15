using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class PipelineSpeedIntegrationTests
{
    [Test]
    public async Task C1281_B1Authorization()
    {
        foreach (var allowed in new[] { "task", "session", "capability", "operator" })
        {
            await using var w = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync(configure: services =>
                services.AddScoped(sp => new AgentTaskService(sp.GetRequiredService<AppDbContext>(),
                    new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                    Options.Create(new DelegationSettings()), new MockEventBus(), new RecordingSessionStopper(),
                    TimeProvider.System, NullLogger<AgentTaskService>.Instance)));
            var (project, board, card) = await SeedCorrectionScopeAsync(w);
            foreach (var denied in new[] { "anonymous", "invalid", "revoked", "worker", "project", "repository", "board", "card", "session-card" })
            {
                using var client = w.App.GetTestClient();
                if (denied != "anonymous")
                    client.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", (await CorrectionCredentialAsync(w, project, board, card, denied)).Token);
                using var response = await client.PostAsJsonAsync(CorrectionUrl(w), w.Request);
                response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "correction-principal-refused " + denied);
                await using var db = w.Db();
                (await db.ReviewEvidenceCorrections.CountAsync()).ShouldBe(0, "denied-no-correction");
                (await db.AgentTaskEvents.CountAsync()).ShouldBe(0, "denied-no-audit");
                (await db.StageOutcomes.CountAsync()).ShouldBe(1, "denied-no-successor");
            }
            string actor;
            HttpResponseMessage success;
            using var scoped = w.App.GetTestClient();
            if (allowed == "operator") { actor = "operator"; success = await w.PostAsync(w.Request); }
            else
            {
                var principal = await CorrectionCredentialAsync(w, project, board, card, allowed);
                actor = principal.Actor;
                scoped.DefaultRequestHeaders.Add("X-Antiphon-Task-Token", principal.Token);
                success = await scoped.PostAsJsonAsync(CorrectionUrl(w), w.Request);
            }
            using (success)
                success.StatusCode.ShouldBe(HttpStatusCode.OK, "allowed-" + allowed + ": " + await success.Content.ReadAsStringAsync());
            await using var fresh = w.Db();
            (await fresh.ReviewEvidenceCorrections.SingleAsync()).Actor.ShouldBe(actor, "authenticated-actor-retained");
            var forged = JsonSerializer.SerializeToNode(w.Request)!;
            forged["actor"] = "forged";
            using var deniedActor = await scoped.PostAsJsonAsync(CorrectionUrl(w), forged);
            deniedActor.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "actor-cannot-be-supplied");
        }
    }

    private static string CorrectionUrl(AdvisoryChangePolicyTests.CorrectionWorld w) =>
        $"/api/agent-tasks/{w.Review.Id:D}/review-evidence/corrections";

    private static async Task<(Guid Project, Guid Board, Guid Card)> SeedCorrectionScopeAsync(AdvisoryChangePolicyTests.CorrectionWorld w)
    {
        await using var db = w.Db();
        var project = new Project { Id = Guid.NewGuid(), Name = "correction scope" };
        var board = new Board { Id = Guid.NewGuid(), Name = "correction scope", ProjectId = project.Id };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Review" };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "CARD-1281", Title = "correction scope" };
        db.AddRange(project, board, column, card);
        foreach (var task in await db.AgentTasks.ToListAsync()) { task.ProjectId = project.Id; task.CardId = card.Id; }
        await db.SaveChangesAsync();
        return (project.Id, board.Id, card.Id);
    }

    private static async Task<(string Token, string Actor)> CorrectionCredentialAsync(
        AdvisoryChangePolicyTests.CorrectionWorld w, Guid project, Guid board, Guid card, string variant)
    {
        if (variant == "invalid") return ("invalid-test-credential", "");
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        await using var db = w.Db();
        if (variant == "project")
        {
            project = Guid.NewGuid(); db.Add(new Project { Id = project, Name = "foreign" });
        }
        if (variant == "board")
        {
            board = Guid.NewGuid(); db.Add(new Board { Id = board, ProjectId = project, Name = "foreign" });
        }
        if (variant is "card" or "session-card")
        {
            var target = await db.Cards.SingleAsync(c => c.Id == card);
            card = Guid.NewGuid(); db.Add(new Card { Id = card, BoardId = board, BoardColumnId = target.BoardColumnId,
                Identifier = "CARD-9999", Title = "foreign" });
        }
        string actor;
        if (variant is "revoked" or "repository" or "board" or "capability")
        {
            db.Add(new DelegationCapability { Id = id, Name = "correction-" + id.ToString("N"), ProjectId = project,
                BoardId = board, TokenHash = AgentTaskService.HashToken(token),
                RootsJson = JsonSerializer.Serialize(new[] { w.Request.RepositoryPath + (variant == "repository" ? "-sibling" : "") }),
                RevokedAt = variant == "revoked" ? DateTime.UtcNow : null });
            actor = "capability:" + id.ToString("D");
        }
        else if (variant is "session" or "session-card")
        {
            db.Add(new AgentSession { Id = id, Cwd = w.Request.RepositoryPath, CardId = card,
                DelegationTokenHash = AgentTaskService.HashToken(token) });
            actor = "session:" + id.ToString("D");
        }
        else
        {
            db.Add(new AgentTask { Id = id, RootTaskId = id, Title = "credential", Goal = "credential",
                WorkingDirectory = w.Request.RepositoryPath, RepoPath = w.Request.RepositoryPath,
                ProjectId = project, CardId = card, Kind = variant == "worker" ? AgentTaskKind.Worker : AgentTaskKind.Orchestrator,
                TokenHash = AgentTaskService.HashToken(token) });
            actor = "task:" + id.ToString("D");
        }
        await db.SaveChangesAsync(); return (token, actor);
    }

    private sealed class CorrectionBoundary : LandDeliveryBoundary
    {
        public Func<string, Task>? Observe { get; set; }
        public int Hits { get; set; }
        public override Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct) =>
            Observe?.Invoke(boundary) ?? Task.CompletedTask;
    }

    private static async Task ReceiptVariantsAsync()
    {
        await using var w = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync();
        using (var producer = await w.PostAsync(w.Request with { ProducerIdentity = "foreign-producer" }))
            await RequireProblemAsync(producer, "review_evidence_correction_producer_untrusted");
        foreach (var selection in new[] { w.Request.Selection with { CommitSha = new string('a', 40) },
            w.Request.Selection with { Section = "## Different selection" }, w.Request.Selection with { Rows = ["CP-14"] } })
        {
            using var denied = await w.PostAsync(w.Request with { Selection = selection });
            await RequireProblemAsync(denied, "review_evidence_correction_commissioned_selection_mismatch");
        }
        var settings = w.App.Services.GetRequiredService<IOptions<CheckpointReceiptSettings>>().Value;
        var storage = settings.StorageRoot;
        settings.StorageRoot = Path.Combine(w.Root, "occupied-storage");
        await File.WriteAllTextAsync(settings.StorageRoot, "owned fixture blocks directory import");
        using (var denied = await w.PostAsync(w.Request))
            await RequireProblemAsync(denied, "review_evidence_correction_receipt_invalid");
        await using (var db = w.Db())
        {
            (await db.ReviewEvidenceCorrections.CountAsync()).ShouldBe(0, "import-failure-no-approval");
            (await db.StageOutcomes.CountAsync()).ShouldBe(1);
        }
        settings.StorageRoot = storage;
        using var good = await w.PostAsync(w.Request);
        good.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var fresh = w.Db();
        var retained = await fresh.ReviewEvidenceCorrections.SingleAsync();
        using var proof = JsonDocument.Parse(retained.ValidationResultJson);
        var arguments = proof.RootElement.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToArray();
        arguments.ShouldContain(w.Request.SourceSha, "actual-validator-source-argument");
        arguments.ShouldContain(string.Join(',', w.Request.Selection.Rows), "actual-validator-rows-argument");
        AdvisoryChangePolicyTests.CorrectionWorld.Digest(await File.ReadAllTextAsync(retained.ImportedEvidenceIdentity))
            .ShouldBe(w.Request.BundleDigest, "retained-content-digest");
        // The input was copied from the authentic producer. Corrupt a referenced blob only;
        // the pinned inventory stays unchanged, so import must detect its digest mismatch.
        await using var tampered = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync();
        var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(tampered.ProducerRoot, "producer-pack-index.json")));
        using (inventory)
        {
            var relative = inventory.RootElement.GetProperty("files").EnumerateArray()
                .Select(e => e.GetProperty("relativePath").GetString()!).Single(p => p.EndsWith("manifest.resolved.yaml"));
            await File.AppendAllTextAsync(Path.Combine(tampered.ProducerRoot, relative), "\n# tampered fixture\n");
        }
        using var corrupted = await tampered.PostAsync(tampered.Request);
        await RequireProblemAsync(corrupted, "review_evidence_correction_receipt_invalid");
        await using var rejected = tampered.Db();
        (await rejected.ReviewEvidenceCorrections.CountAsync()).ShouldBe(0, "tampered-blob-refused");
    }

    private static async Task CorrectionTransactionsAsync()
    {
        foreach (var cut in new[] { "before-audit", "before-save", "before-commit", "committed", "prepared-report", "prepared-predecessor" })
        {
            var boundary = new CorrectionBoundary();
            await using var w = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync(configure: services =>
                services.AddSingleton<LandDeliveryBoundary>(boundary));
            boundary.Observe = async point =>
            {
                if (point != "review-correction-" + (cut.StartsWith("prepared-") ? "prepared" : cut)) return;
                boundary.Hits++;
                if (cut.StartsWith("prepared-"))
                {
                    await using var db = w.Db();
                    if (cut == "prepared-report") (await db.AgentTasks.SingleAsync(t => t.Id == w.Review.Id)).Result += "changed";
                    else db.StageOutcomes.Add(new StageOutcome { Id = Guid.NewGuid(), StageTaskId = w.Review.Id,
                        Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Found, Source = StageOutcomeSource.Orchestrator,
                        SupersedesId = w.Request.PredecessorEvidenceId, RecordedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync();
                }
                else throw new IOException("correction cut " + cut);
            };
            using var response = await w.PostAsync(w.Request);
            response.StatusCode.ShouldBe(cut.StartsWith("prepared-") ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError, cut);
            boundary.Hits.ShouldBe(1, "correction-cut-reached " + cut);
            if (cut.StartsWith("prepared-"))
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString().ShouldBe("review_evidence_correction_" +
                    (cut == "prepared-report" ? "prepared_report_changed" : "predecessor_superseded"));
            }
            await using (var db = w.Db())
            {
                var expected = cut == "committed" ? 1 : 0;
                (await db.ReviewEvidenceCorrections.CountAsync()).ShouldBe(expected, "correction-effects-atomic");
                (await db.AgentTaskEvents.CountAsync()).ShouldBe(expected, "correction-effects-atomic");
                (await db.StageOutcomes.CountAsync(o => o.Outcome == StageOutcomeKind.Clean)).ShouldBe(expected, "correction-effects-atomic");
            }
            boundary.Observe = null;
            if (cut.StartsWith("prepared-")) continue;
            await w.RestartAsync();
            using var recovered = await w.PostAsync(w.Request);
            recovered.StatusCode.ShouldBe(HttpStatusCode.OK, "correction-recovery");
            using var repeated = await w.PostAsync(w.Request);
            repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
            await using var fresh = w.Db();
            (await fresh.ReviewEvidenceCorrections.CountAsync()).ShouldBe(1);
            (await fresh.AgentTaskEvents.CountAsync()).ShouldBe(1);
        }
        await using var concurrent = await AdvisoryChangePolicyTests.CorrectionWorld.CreateAsync();
        var pair = await Task.WhenAll(concurrent.PostAsync(concurrent.Request), concurrent.PostAsync(concurrent.Request));
        try
        {
            foreach (var response in pair) response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var ids = await Task.WhenAll(pair.Select(r => r.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>()));
            ids[0]!.CorrectionId.ShouldBe(ids[1]!.CorrectionId, "concurrent-duplicate-one-correction");
        }
        finally { foreach (var response in pair) response.Dispose(); }
    }
}
