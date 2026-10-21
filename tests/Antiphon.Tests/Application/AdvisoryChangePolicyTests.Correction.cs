using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Server.Infrastructure.Verification;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AdvisoryChangePolicyTests
{
    [Test]
    [Arguments("missing-blob")]
    [Arguments("missing-bundle")]
    [Arguments("changed-blob")]
    public async Task C1303_R12_LandRefusesUnavailableCorrectedEvidence(string defect)
    {
        await using var w = await CorrectionWorld.CreateAsync();
        using var corrected = await w.PostAsync(w.Request);
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt = (await corrected.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>()).ShouldNotBeNull();
        receipt.Disposition.ShouldBe("corrected");
        var evidenceId = receipt.SuccessorEvidenceId.ShouldNotBeNull();
        string inventoryPath;
        await using (var db = w.Db())
        {
            inventoryPath = (await db.ReviewEvidenceCorrections.SingleAsync()).ImportedEvidenceIdentity;
            var subject = await db.AgentTasks.SingleAsync(t => t.Id == w.Request.SubjectTaskId);
            (await LandApproval.LoadUsableEvidenceAsync(db, evidenceId, w.Request.SourceSha,
                subject, CancellationToken.None)).Id.ShouldBe(evidenceId, "intact correction approves this source");
        }
        using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryPath));
        var relative = inventory.RootElement.GetProperty("files")[0].GetProperty("relativePath").GetString()!;
        var missingPath = defect == "missing-bundle" ? inventoryPath
            : Path.Combine(Path.GetDirectoryName(inventoryPath)!, relative);
        var original = await File.ReadAllBytesAsync(missingPath);
        if (defect == "changed-blob") await File.AppendAllTextAsync(missingPath, "changed retained bytes");
        else File.Delete(missingPath);

        var queue = new AgentTaskLandQueue();
        await using (var db = w.Db())
        {
            var error = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                Land(db, queue).RequestAsync(w.Request.SubjectTaskId,
                    new LandAgentTaskRequest(ExpectedSourceSha: w.Request.SourceSha, ReviewEvidenceId: evidenceId),
                    CancellationToken.None));
            error.StatusCode.ShouldBe(409);
            error.Code.ShouldBe("review_evidence_correction_retained_evidence_unavailable");
            error.Message.ShouldContain("Retained checkpoint bundle");
            error.Message.ShouldContain(w.Request.BundleDigest);
            error.Message.ShouldContain(inventoryPath);
            error.Message.ShouldContain("missing, unreadable, or changed");
            error.Message.ShouldContain("CheckpointReceipts:StorageRoot");
            error.Message.ShouldContain("Restore the original bundle");
            error.Message.ShouldContain("retry land");
            error.Message.ShouldContain("commission a new Review");
        }
        await using (var fresh = w.Db())
        {
            (await fresh.AgentTaskLandRequests.CountAsync()).ShouldBe(0, "refusal persists no landing request");
            var subject = await fresh.AgentTasks.SingleAsync(t => t.Id == w.Request.SubjectTaskId);
            subject.LandRequestedAt.ShouldBeNull();
            subject.CurrentLandRequestId.ShouldBeNull();
            (await fresh.StageOutcomes.CountAsync()).ShouldBe(2, "historical and corrected outcomes survive refusal");
        }
        queue.PendingCount.ShouldBe(0, "unverifiable evidence cannot enqueue publication");

        await File.WriteAllBytesAsync(missingPath, original);
        await using var restored = w.Db();
        var accepted = await Land(restored, queue).RequestAsync(w.Request.SubjectTaskId,
            new LandAgentTaskRequest(ExpectedSourceSha: w.Request.SourceSha, ReviewEvidenceId: evidenceId),
            CancellationToken.None);
        accepted.Status.ShouldBe("queued", "restoring the exact bundle permits retry");
        (await restored.AgentTaskLandRequests.SingleAsync()).ReviewEvidenceId.ShouldBe(evidenceId);
        queue.PendingCount.ShouldBe(1);
    }

    [Test]
    public async Task C1281_ReceiptCorrection()
    {
        await using var w = await CorrectionWorld.CreateAsync();
        var first = await w.PostAsync(w.Request);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, "valid-correction-route");
        var receipt = (await first.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>()).ShouldNotBeNull();
        receipt.Disposition.ShouldBe("corrected");
        Directory.Delete(w.ProducerRoot, true);
        await w.RestartAsync();
        using var repeat = await w.PostAsync(w.Request);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await repeat.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>())!.CorrectionId.ShouldBe(receipt.CorrectionId);
        await using var db = w.Db();
        (await db.ReviewEvidenceCorrections.CountAsync()).ShouldBe(1, "one-audited-correction");
        (await db.AgentTaskEvents.CountAsync()).ShouldBe(1, "one-audit");
        (await db.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Review)).ShouldBe(1, "zero-new-reviews");
        var old = await db.StageOutcomes.AsNoTracking().SingleAsync(o => o.Id == w.Request.PredecessorEvidenceId);
        old.Outcome.ShouldBe(StageOutcomeKind.Found, "historical-row-unchanged");
        old.CostUsd.ShouldBe(12.34m);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.Review.Id)).Result.ShouldBe(w.Review.Result);
        var subject = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.Request.SubjectTaskId);
        (await LandApproval.LoadUsableEvidenceAsync(db, receipt.SuccessorEvidenceId!.Value, w.Request.SourceSha,
            subject, CancellationToken.None)).Id.ShouldBe(receipt.SuccessorEvidenceId.Value, "actual-landing-consumer");
        foreach (var (request, label, code) in new[] {
            (w.Request with { SubjectTaskId = Guid.NewGuid() }, "foreign-subject-refused", "review_ineligible"),
            (w.Request with { SourceSha = new string('a', 40) }, "foreign-sha-refused", "assessment_coordinates_mismatch"),
            (w.Request with { SourceRef = "refs/heads/foreign" }, "foreign-ref-refused", "assessment_coordinates_mismatch"),
            (w.Request with { RepositoryPath = w.Request.RepositoryPath + "-sibling" }, "foreign-repository-refused", "review_ineligible") })
        {
            using var denied = await w.PostAsync(request);
            denied.StatusCode.ShouldBe(HttpStatusCode.Conflict, label);
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe("review_evidence_correction_" + code, label);
        }
        (await db.ReviewEvidenceCorrections.CountAsync()).ShouldBe(1);
        var successor = await db.StageOutcomes.SingleAsync(o => o.Id == receipt.SuccessorEvidenceId);
        successor.Ref = "broken-link";
        await db.SaveChangesAsync();
        var broken = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            LandApproval.LoadUsableEvidenceAsync(db, successor.Id, w.Request.SourceSha, subject, CancellationToken.None));
        broken.Code.ShouldBe("review_evidence_correction_link_mismatch");
    }

    [Test]
    public async Task C1281_IncompleteNeverClean()
    {
        await using var w = await CorrectionWorld.CreateAsync();
        // The control must reach the actual route before any refusal can count as evidence.
        using var missing = await w.PostAsync(w.Request with { BundleDigest = new string('0', 64) });
        missing.StatusCode.ShouldBe(HttpStatusCode.OK, "missing-execution-route-reached");
        var absent = (await missing.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>()).ShouldNotBeNull();
        absent.Disposition.ShouldBe("incomplete", "missing-run-no-approval");
        absent.MissingRows.ShouldBe(w.Request.Selection.Rows, "missing-selection-exact");
        await using (var db = w.Db()) (await db.StageOutcomes.CountAsync()).ShouldBe(1);
        foreach (var behavior in new[] { "Substantive", "Unknown", "Legacy" })
        {
            await w.BehaviorAsync(behavior);
            using var denied = await w.PostAsync(w.Request);
            denied.StatusCode.ShouldBe(HttpStatusCode.Conflict, "missing-behavior-substantive");
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().ShouldBe("review_evidence_correction_behavior_requires_review");
        }
        await w.BehaviorAsync("Satisfied");
        using var valid = await w.PostAsync(w.Request);
        valid.StatusCode.ShouldBe(HttpStatusCode.OK, "restored-valid-control");
        foreach (var defect in new[] { "failed-row", "missing-trx", "missing-provenance", "wrong-receipt", "bad-number" })
        {
            await using var invalid = await CorrectionWorld.CreateAsync(defect);
            using var response = await invalid.PostAsync(invalid.Request);
            if (defect is "wrong-receipt" or "bad-number")
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Conflict, defect);
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString().ShouldBe("review_evidence_correction_" +
                    (defect == "bad-number" ? "receipt_invalid" : "receipt_coordinates_mismatch"));
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK, defect);
                var incomplete = (await response.Content.ReadFromJsonAsync<ReviewEvidenceCorrectionResponse>()).ShouldNotBeNull();
                incomplete.Disposition.ShouldBe("incomplete", defect + "-no-approval");
                incomplete.MissingRows.ShouldBe(defect == "missing-provenance" ? invalid.Request.Selection.Rows : ["CP-15"], defect + "-exact-selection");
            }
            await using var fresh = invalid.Db();
            (await fresh.ReviewEvidenceCorrections.CountAsync()).ShouldBe(0, defect + "-no-correction");
            (await fresh.StageOutcomes.CountAsync()).ShouldBe(1, defect + "-no-approval");
        }
    }

    internal sealed class CorrectionWorld : IAsyncDisposable
    {
        private IsolatedTestSchema _schema = null!;
        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private string _operatorPath = "";
        public string Root { get; private set; } = "";
        public string ProducerRoot { get; private set; } = "";
        public string Repository { get; private set; } = "";
        public AgentTask Review { get; private set; } = null!;
        public ReviewEvidenceCorrectionRequest Request { get; private set; } = null!;
        public WebApplication App => _app;
        public Action<IServiceCollection>? Configure { get; set; }
        public string ConnectionString => _schema.ConnectionString;
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));
        public static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        public static async Task<CorrectionWorld> CreateAsync(string? defect = null, Action<IServiceCollection>? configure = null)
        {
            var git = new LandingGit();
            var common = await git.CommonDirectoryAsync(DelegateScriptRunner.RepoRoot, CancellationToken.None);
            var original = Path.Combine(common, "antiphon", "code-evidence", "04228bff");
            var producerIndex = Path.Combine(original, "producer-pack-index.json");
            // CARD-1425: these tests must run on every clone; a missing pack is a failure, not a skip.
            File.Exists(producerIndex).ShouldBeTrue("CARD-1425: no producer pack and no fixture");
            var w = new CorrectionWorld { _schema = await TestDbFixture.CreateIsolatedSchemaAsync(),
                Root = Path.Combine(Path.GetTempPath(), "c1281-correction-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(w.Root);
            w.Configure = configure;
            w.Repository = DelegateScriptRunner.RepoRoot;
            var inventory = await File.ReadAllTextAsync(producerIndex);
            Digest(inventory).ShouldBe("5afb848873bdd8d653a3587654445d239b88cc31ea59f664dbf23efd78b98661",
                "authentic-producer-inventory");
            // The positive is the unedited actual producer pack. Negative arms edit only this
            // private parser fixture, never the retained producer or production code.
            w.ProducerRoot = Path.Combine(w.Root, "input");
            Directory.CreateDirectory(w.ProducerRoot);
            var node = JsonNode.Parse(inventory)!;
            foreach (var item in node["files"]!.AsArray())
            {
                var relative = item!["relativePath"]!.GetValue<string>();
                var destination = Path.Combine(w.ProducerRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(original, relative), destination);
            }
            if (defect is not null)
            {
                var run = node["cleanCheckpointRun"]!.GetValue<string>();
                var reportRelative = $".antiphon/checkpoints/{run}/report.json";
                if (defect is "failed-row" or "wrong-receipt" or "bad-number")
                {
                    var path = Path.Combine(w.ProducerRoot, reportRelative);
                    var report = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
                    if (defect == "bad-number") report["schemaVersion"] = 2.5;
                    else if (defect == "wrong-receipt") report["commit"] = new string('a', 40);
                    else report["rows"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "CP-15")!["state"] = "red";
                    var content = report.ToJsonString();
                    await File.WriteAllTextAsync(path, content);
                    var entry = node["files"]!.AsArray().Single(e => e!["relativePath"]!.GetValue<string>() == reportRelative)!;
                    entry["bytes"] = Encoding.UTF8.GetByteCount(content); entry["sha256"] = Digest(content);
                }
                else
                {
                    var entry = node["files"]!.AsArray().Single(e => defect == "missing-trx"
                        ? e!["relativePath"]!.GetValue<string>() == $".antiphon/checkpoints/{run}/rows/CP-15/run.trx"
                        : e!["relativePath"]!.GetValue<string>().EndsWith("bin-c1281-foundation/checkpoint-build-source.json"));
                    File.Delete(Path.Combine(w.ProducerRoot, entry!["relativePath"]!.GetValue<string>()));
                    node["files"]!.AsArray().Remove(entry);
                }
                inventory = node.ToJsonString();
            }
            await File.WriteAllTextAsync(Path.Combine(w.ProducerRoot, "producer-pack-index.json"), inventory);
            using var index = JsonDocument.Parse(inventory);
            var source = index.RootElement.GetProperty("sourceCommit").GetString()!;
            var worktree = index.RootElement.GetProperty("sourceWorktree").GetString()!;
            var selectionPath = "docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md";
            var plan = await git.RunAsync(w.Repository, ["show", source + ":" + selectionPath], CancellationToken.None);
            plan.Succeeded.ShouldBeTrue();
            var selection = new ReviewCorrectionSelection(selectionPath, source,
                "## Shared bindings B-1/B-2/B-3 (TestDesign, 2026-10-15, task 598a38d5)",
                Digest(plan.Output), ["CP-14", "CP-15", "CP-16"]);
            var subject = TaskRow(worktree, worktree, "feat/card-task-04228bff", AgentTaskRole.Code);
            w.Review = TaskRow(w.Root, worktree, "feat/review-fixture", AgentTaskRole.Review);
            w.Review.Stage = OrchestrationStage.Review;
            w.Review.FollowUpOfTaskId = subject.Id;
            w.Review.VerificationProfileVersion = 1;
            w.Review.VerificationRound = VerificationRound.Final;
            w.Review.Goal = "Review this component.\n--- correction selection v1 ---\n" + JsonSerializer.Serialize(selection);
            var old = AdvisoryChangePolicyTests.Review(subject, source, StageOutcomeKind.Found);
            old.StageTaskId = w.Review.Id; old.ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch;
            old.ReviewedSourceClean = false; old.OrdinaryScopeCompleted = VerificationScope.None;
            old.CostUsd = 12.34m;
            w.Request = new(old.Id, "", subject.Id, source, old.ReviewedSourceRef!, worktree,
                selection, index.RootElement.GetProperty("task").GetString()!, Digest(inventory), "attach retained execution");
            await using (var db = w.Db()) { db.AgentTasks.AddRange(subject, w.Review); db.StageOutcomes.Add(old); await db.SaveChangesAsync(); }
            await w.BehaviorAsync("Satisfied");
            w._operatorPath = Path.Combine(w.Root, "operator-token");
            await w.RestartAsync();
            return w;
        }

        public async Task BehaviorAsync(string behavior)
        {
            Review.Result = "Retained typed assessment.\n--- correction assessment v1 ---\n" +
                JsonSerializer.Serialize(new { Behavior = behavior, SubjectTaskId = Request.SubjectTaskId,
                    SourceSha = Request.SourceSha, SourceRef = Request.SourceRef, RepositoryPath = Request.RepositoryPath });
            if (behavior == "Legacy") Review.Result = "Historical unclassified Found report.";
            Request = Request with { ExpectedReportDigest = Digest(Review.Result) };
            await using var db = Db();
            (await db.AgentTasks.SingleAsync(t => t.Id == Review.Id)).Result = Review.Result;
            await db.SaveChangesAsync();
        }

        public async Task RestartAsync()
        {
            if (_app is not null) { _client.Dispose(); await _app.DisposeAsync(); }
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_schema.ConnectionString));
            builder.Services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings { OperatorTokenPath = _operatorPath }));
            ConfigureCorrectionServices(builder.Services);
            Configure?.Invoke(builder.Services);
            _app = builder.Build();
            _app.UseMiddleware<ExceptionMiddleware>();
            _app.MapReviewEvidenceCorrectionEndpoints();
            await _app.StartAsync();
            _client = _app.GetTestClient();
            _client.DefaultRequestHeaders.Add(OperatorTokenFile.Header, OperatorTokenFile.ReadOrCreate(_operatorPath));
        }
        private void ConfigureCorrectionServices(IServiceCollection services)
        {
            services.AddSingleton<ILandingGit, LandingGit>();
            services.AddSingleton(Options.Create(new CheckpointReceiptSettings {
                StorageRoot = Path.Combine(Root, "retained"),
                ValidatorScript = Path.Combine(Repository, "scripts", "validate-checkpoint-receipt.ps1"),
                Producers = [new(Request.ProducerIdentity, Path.Combine(ProducerRoot, "producer-pack-index.json"),
                    Request.BundleDigest, Request.RepositoryPath, Repository)] }));
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddSingleton<LandDeliveryBoundary>();
            services.AddScoped<ICheckpointReceiptValidator, CheckpointReceiptValidator>();
            services.AddScoped<ReviewEvidenceCorrectionService>();
        }
        public Task<HttpResponseMessage> PostAsync(ReviewEvidenceCorrectionRequest request) =>
            _client.PostAsJsonAsync($"/api/agent-tasks/{Review.Id:D}/review-evidence/corrections", request);
        public async ValueTask DisposeAsync()
        {
            _client?.Dispose(); if (_app is not null) await _app.DisposeAsync();
            await _schema.DisposeAsync(); Directory.Delete(Root, true);
        }
    }
}
