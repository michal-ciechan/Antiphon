using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

[Category("Integration")]
[ClassDataSource<ReviewRunIntakeApiTests.Factory>(Shared = SharedType.PerClass)]
public sealed class ReviewRunIntakeApiTests(ReviewRunIntakeApiTests.Factory factory)
{
    private const string Repo = "acme/widgets";

    [Test]
    public async Task Repeated_intake_returns_one_run(CancellationToken ct)
    {
        const int number = 209600;
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var existing = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);

        Id(existing).ShouldBe(Id(created));
        created.GetProperty("existing").GetBoolean().ShouldBeFalse();
        existing.GetProperty("existing").GetBoolean().ShouldBeTrue();
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Every_non_closed_status_keeps_its_run(CancellationToken ct)
    {
        var cases = Enum.GetValues<ReviewRunStatus>().Where(status => status != ReviewRunStatus.Closed)
            .Select(status => (Status: status, Stage: ReviewStage.Intake))
            .Select(item => item.Status == ReviewRunStatus.Failed ? (item.Status, Stage: ReviewStage.Review) : item)
            .Append((Status: ReviewRunStatus.Failed, Stage: ReviewStage.Publish))
            .ToArray();

        for (var i = 0; i < cases.Length; i++)
        {
            var number = 209610 + i;
            var (status, stage) = cases[i];
            using var client = OpenPullRequest(number);
            var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var run = await db.ReviewRuns.SingleAsync(r => r.Id == Id(created), ct);
                run.Status = status;
                run.Stage = stage;
                await db.SaveChangesAsync(ct);
            }

            var existing = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);
            Id(existing).ShouldBe(Id(created), $"{status} at {stage}");
            existing.GetProperty("status").GetString().ShouldBe(status.ToString());
            existing.GetProperty("stage").GetString().ShouldBe(stage.ToString());

            if (status == ReviewRunStatus.Failed)
            {
                using var refreshed = await client.PostAsync($"/api/review-runs/{Id(created)}/refresh", null, ct);
                refreshed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
                var retried = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{Id(created)}", ct);
                Id(retried).ShouldBe(Id(created));
                retried.GetProperty("round").GetInt32().ShouldBe(1);
                retried.GetProperty("iterations").EnumerateArray().Last().GetProperty("trigger").GetString().ShouldBe("recheck");
            }

            await AssertStoredRunsAsync(number, 1, ct);
        }
    }

    [Test]
    public async Task Close_checks_token_preserves_audit_and_allows_a_new_run(CancellationToken ct)
    {
        const int number = 209630;
        using var client = OpenPullRequest(number);
        client.DefaultRequestHeaders.Add("X-Review-Viewer", "operator-alex");
        var created = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        var id = Id(created);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = await db.ReviewRuns.SingleAsync(r => r.Id == id, ct);
            run.Status = ReviewRunStatus.AtGate;
            run.Stage = ReviewStage.HumanGate;
            run.WaitingOn = ReviewWaitingOn.Humans;
            db.ReviewGates.Add(new ReviewGate
            {
                Id = Guid.NewGuid(), RunId = id, Kind = ReviewGateKind.ReviewGate, NodeId = "human-review",
                Iteration = 1, Status = ReviewQueueItemStatus.Unassigned, OpenedAt = DateTime.UtcNow,
            });
            db.ReviewNodeExecutions.Add(new ReviewNodeExecution
            {
                Id = Guid.NewGuid(), RunId = id, NodeId = "human-review", Iteration = 1, Sequence = 2,
                Kind = ReviewNodeKind.Gate, Status = ReviewNodeStatus.Waiting,
            });
            await db.SaveChangesAsync(ct);
        }

        var closeUri = $"/api/review-runs/{id}/close";
        var previousToken = created.TryGetProperty("concurrencyToken", out var token) ? token.GetUInt32() : uint.MaxValue;
        using var stale = await client.PostAsJsonAsync(closeUri, new { concurrencyToken = previousToken, reason = "stale" }, ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict, await stale.Content.ReadAsStringAsync(ct));
        var atGate = await client.GetFromJsonAsync<JsonElement>($"/api/review-runs/{id}", ct);
        var concurrencyToken = atGate.GetProperty("concurrencyToken").GetUInt32();
        var request = new { concurrencyToken, reason = "  superseded by the author  " };
        using var closed = await client.PostAsJsonAsync(closeUri, request, ct);
        closed.StatusCode.ShouldBe(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync(ct));
        var detail = await closed.Content.ReadFromJsonAsync<JsonElement>(ct);
        detail.GetProperty("status").GetString().ShouldBe("Closed");
        detail.GetProperty("stage").GetString().ShouldBe("Done");
        var closure = detail.GetProperty("closure");
        closure.GetProperty("actor").GetString().ShouldBe("operator-alex");
        closure.GetProperty("reason").GetString().ShouldBe("superseded by the author");
        var gate = detail.GetProperty("gates").EnumerateArray().Single();
        gate.GetProperty("status").GetString().ShouldBe("Done");
        gate.GetProperty("reasons").EnumerateArray().Single().GetString().ShouldBe("closed: superseded by the author");
        var gateNode = detail.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("nodeId").GetString() == "human-review");
        gateNode.GetProperty("status").GetString().ShouldBe("Skipped");

        using var repeated = await client.PostAsJsonAsync(closeUri, request with { reason = "another reason" }, ct);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var repeatedDetail = await repeated.Content.ReadFromJsonAsync<JsonElement>(ct);
        repeatedDetail.GetProperty("closure").ToString().ShouldBe(closure.ToString());
        repeatedDetail.GetProperty("concurrencyToken").GetUInt32().ShouldBe(detail.GetProperty("concurrencyToken").GetUInt32());

        var replacement = await IntakeAsync(client, Repo, number, HttpStatusCode.Created, ct);
        Id(replacement).ShouldNotBe(Id(created));
        await AssertStoredRunsAsync(number, 2, ct);
    }

    [Test]
    public async Task Parallel_intakes_save_one_run_and_return_one_created_response(CancellationToken ct)
    {
        const int number = 209640;
        factory.Race.Arm(number);
        using var client = OpenPullRequest(number);

        var first = client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = Repo, prNumber = number }, ct);
        var second = client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo = Repo, prNumber = number }, ct);
        var responses = await Task.WhenAll(first, second);
        using var left = responses[0];
        using var right = responses[1];

        responses.Select(r => r.StatusCode).Order().ShouldBe(new[] { HttpStatusCode.OK, HttpStatusCode.Created });
        Id(await left.Content.ReadFromJsonAsync<JsonElement>(ct)).ShouldBe(Id(await right.Content.ReadFromJsonAsync<JsonElement>(ct)));
        await AssertStoredRunsAsync(number, 1, ct);
    }

    [Test]
    public async Task Repo_case_and_pull_request_URL_resolve_to_the_same_run(CancellationToken ct)
    {
        const int number = 209650;
        using var client = OpenPullRequest(number);
        var created = await IntakeAsync(client, " Acme/Widgets ", number, HttpStatusCode.Created, ct);
        var byRepo = await IntakeAsync(client, Repo, number, HttpStatusCode.OK, ct);
        Id(byRepo).ShouldBe(Id(created));
        var byUrl = await IntakeAsync(client, $"https://GitHub.com/ACME/widgets/pull/{number}?tab=files", null, HttpStatusCode.OK, ct);
        Id(byUrl).ShouldBe(Id(created));
        var byPath = await IntakeAsync(client, $"ACME/widgets/{number}", null, HttpStatusCode.OK, ct);
        Id(byPath).ShouldBe(Id(created));

        using var mismatch = await client.PostAsJsonAsync("/api/review-runs",
            new { host = "GitHub", repo = $"https://github.com/acme/widgets/pull/{number}", prNumber = number + 1 }, ct);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        await AssertStoredRunsAsync(number, 1, ct);
    }

    private HttpClient OpenPullRequest(int number)
    {
        factory.Host.Open(number, "Fix the lease", "author", new string('a', 40), new string('b', 40), [], "");
        return factory.CreateClient();
    }

    private async Task AssertStoredRunsAsync(int number, int total, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = db.ReviewRuns.Where(r => r.Subject.PrNumber == number);
        (await runs.CountAsync(ct)).ShouldBe(total);
        (await runs.CountAsync(r => r.Status != ReviewRunStatus.Closed, ct)).ShouldBe(1);
        (await db.ReviewSubjects.CountAsync(s => s.PrNumber == number, ct)).ShouldBe(total);
    }

    private static Guid Id(JsonElement run) => run.GetProperty("id").GetGuid();

    private static async Task<JsonElement> IntakeAsync(HttpClient client, string repo, int? prNumber, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/review-runs", new { host = "GitHub", repo, prNumber }, ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public sealed class Factory : AntiphonWebAppFactory
    {
        internal ScriptedCodeHost Host { get; } = new();
        internal IntakeRace Race { get; } = new();

        protected override void ApplyTestOverrides(IServiceCollection services)
        {
            services.RemoveAll<ICodeHost>();
            services.AddSingleton<ICodeHost>(Host);
            services.PostConfigure<ReviewExecutorSettings>(options => options.Enabled = false);
            services.PostConfigure<ReviewSettings>(options => options.AllowViewerHeader = true);
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(Race));
        }
    }

    // Both requests have passed the existing-run query and are ready to insert in independent DbContexts.
    internal sealed class IntakeRace : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _number;
        private int _arrived;

        public void Arm(int number) => Volatile.Write(ref _number, number);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ReviewRun>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.Subject.PrNumber == Volatile.Read(ref _number)))
            {
                if (Interlocked.Increment(ref _arrived) == 2)
                    _bothReady.TrySetResult();
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }

            return result;
        }
    }
}
