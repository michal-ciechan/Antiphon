using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Review;

/// <summary>
/// Demo data for the review UI's http mode (<c>POST /api/review-runs/demo</c>, only while
/// <c>Review:DemoSeedEnabled</c>): ten runs across every status, with node executions, findings,
/// responses and gates. Each call adds a fresh set.
/// </summary>
public sealed class ReviewDemoSeeder(AppDbContext db, IEventBus events, TimeProvider time, ReviewWorkflowService workflows, ReviewService reviews)
{
    private const string Jev = "jev-1.14.0";
    private const string Luna = "gpt-5.6-luna";
    private const string Sonnet = "claude-sonnet-4-6";

    private static readonly string[] IntakeNodes = ["intake", "delta", "categorise", "select", "group", "packs"];
    private static readonly string[] Groups = ["correctness", "concurrency", "security", "api-compat"];

    private DateTime _now;
    private Guid _revisionId;

    public async Task<IReadOnlyList<Guid>> SeedAsync(CancellationToken ct)
    {
        _now = time.GetUtcNow().UtcDateTime;
        _revisionId = await reviews.EnsureDefaultRevisionAsync(ct);
        var runs = new List<ReviewRun>
        {
            AwaitingFixes(),
            BotRoundLimitAtGate(),
            Reviewing(),
            AwaitingMerge(),
            NeedsHumanApproval(),
            AnotherRoundQueued(),
            Merged(),
            Closed(),
            FailedVerify(),
            LocalDisputed(),
        };
        foreach (var run in runs)
        {
            run.IsDemo = true;
            run.CostUsd = run.Nodes.Sum(n => n.CostUsd);
            db.ReviewRuns.Add(run);
        }
        await db.SaveChangesAsync(ct);
        foreach (var run in runs)
            await events.PublishToAllAsync(ReviewService.RunChangedEvent, new ReviewRunChangedEvent(run.Id), ct);
        return runs.Select(r => r.Id).ToList();
    }

    // ---- Scenarios ----

    private ReviewRun AwaitingFixes()
    {
        var run = Run(ReviewHost.GitHub, "acme/orders-service", 812, "Release the order lock only after SaveChangesAsync completes",
            "priya-n", "Priya Natarajan", "fix/order-lock-release", ["@acme/orders", "dmitri-k", "sam-o"], hoursAgo: 26);
        Categorise(run, ["bugfix", "concurrency", "persistence"], reach: 2, contract: 1, data: 3, rollback: 2, reversal: 2, exposure: 4);
        Round(run, 1, startedHoursAgo: 26, reviewers: 3, through: "route");
        Wait(run, "await-fixes", 1, hoursAgo: 25);
        run.Status = ReviewRunStatus.AwaitingFixes;
        run.Stage = ReviewStage.AwaitingFixes;
        run.WaitingOn = ReviewWaitingOn.Author;

        var lockFinding = Finding(run, "F1", "CHK-0142", ReviewSeverity.Blocker, 0.86, "src/Orders/OrderSaver.cs", 41, 44,
            "_lock.Release();", "Lock released before the async save completes",
            "`_lock.Release()` runs before `SaveChangesAsync` is awaited, so a second writer can enter while the first save is still in flight. Under load two orders can share an invoice number.",
            "await _db.SaveChangesAsync(ct);\n_lock.Release();", ["data-loss"],
            why: "Releasing a lock before the save completes lets two writers interleave; we lost invoice numbers to this twice in 2026.");
        lockFinding.BypassesRoundBar = true;
        Respond(lockFinding, ReviewFindingAction.WillFix, "priya-n", "Priya Natarajan", text: "Good catch, moving the release after the await.", hoursAgo: 24);
        lockFinding.Status = ReviewFindingStatus.WillFix;

        Finding(run, "F2", "CHK-0007", ReviewSeverity.Major, 0.72, "src/Orders/OrderSaver.cs", 58, 58,
            "catch (DbUpdateException) { }", "Swallowed DbUpdateException hides a failed save",
            "The empty catch turns a failed save into a silent success; the caller then publishes `OrderSaved` for an order that was never written.",
            "catch (DbUpdateException ex)\n{\n    _log.LogError(ex, \"Saving order {OrderId} failed\", order.Id);\n    throw;\n}", [],
            why: "Swallowed persistence errors turn data loss into a support ticket weeks later.");
        Finding(run, "F3", null, ReviewSeverity.Nit, 0.55, "tests/Orders/OrderSaverTests.cs", 12, 12,
            "public async Task SaveWorks()", "Test name doesn't say what it proves",
            "`SaveWorks` could be `Second_writer_waits_until_the_first_save_completes`, which is what the test asserts.", null, []);
        return run;
    }

    private ReviewRun BotRoundLimitAtGate()
    {
        var run = Run(ReviewHost.GitHub, "acme/billing", 441, "Write invoices to a private temp path",
            "lee-c", "Lee Chen", "fix/invoice-temp-path", ["@acme/billing", "ana-r", "tomas-v"], hoursAgo: 52);
        Categorise(run, ["bugfix", "security"], reach: 2, contract: 1, data: 4, rollback: 2, reversal: 3, exposure: 3);
        for (var round = 1; round <= 3; round++)
        {
            Round(run, round, startedHoursAgo: 52 - (round - 1) * 14, reviewers: 2, through: "route", trigger: round == 1 ? "opened" : "push",
                routeOutcome: round == 3 ? "bot-round-limit" : "findings");
            if (round < 3)
                Executed(run, "await-fixes", ReviewNodeKind.Wait, round, 51 - (round - 1) * 14, 0m, null, summary: "push received", outcome: "push");
        }

        run.Round = 3;
        run.BotOnlyRounds = 3;
        run.Status = ReviewRunStatus.AtGate;
        run.Stage = ReviewStage.HumanGate;
        run.WaitingOn = ReviewWaitingOn.Humans;
        Wait(run, "human-review", 3, hoursAgo: 23);
        Gate(run, ReviewGateKind.ReviewGate, "human-review", 3, "@acme/billing", hoursAgo: 23,
            ["Three bot-only rounds without a clean result (rounds.humanGateAfterBotOnlyRounds: 3)", "An open blocker carries the security flag"]);
        var limitGate = run.Gates[^1];
        limitGate.Passes.Add(new ReviewGatePass
        {
            Id = Guid.NewGuid(),
            GateId = limitGate.Id,
            Login = "tomas-v",
            Reason = "On leave until Monday.",
            At = _now.AddHours(-20),
        });

        var temp = Finding(run, "F1", "CHK-0510", ReviewSeverity.Blocker, 0.9, "src/Billing/InvoiceWriter.cs", 88, 88,
            "File.WriteAllText(path, json);", "Customer data written to a world-readable temp path",
            "`Path.GetTempPath()` is shared on the build agents; the invoice JSON includes customer addresses. Write under the service's own data directory with a 0600 mode.",
            "File.WriteAllText(Path.Combine(_paths.PrivateTemp, name), json);", ["security", "data-loss"],
            why: "Customer data in shared temp directories has leaked between tenants before.");
        temp.RoundRaised = 1;
        Respond(temp, ReviewFindingAction.Dispute, "lee-c", "Lee Chen", text: "The agents are single-tenant; the temp dir is per job.", hoursAgo: 40);
        temp.Status = ReviewFindingStatus.Disputed;
        var retry = Finding(run, "F4", "CHK-0311", ReviewSeverity.Minor, 0.64, "src/Billing/InvoiceWriter.cs", 102, 105,
            "for (var i = 0; i < 3; i++)", "Retry loop has no backoff",
            "Three immediate retries against the blob store will all hit the same throttle window.", null, []);
        retry.RoundRaised = 2;
        return run;
    }

    private ReviewRun Reviewing()
    {
        var run = Run(ReviewHost.GitHub, "acme/web-portal", 1290, "Add saved filters to the orders table",
            "maria-g", "Maria Garcia", "feat/saved-filters", ["@acme/web", "jo-b"], hoursAgo: 1);
        Categorise(run, ["feature", "ui"], reach: 2, contract: 2, data: 2, rollback: 1, reversal: 2, exposure: 4);
        Round(run, 1, startedHoursAgo: 1, reviewers: 0, through: "packs");
        Executed(run, "review", ReviewNodeKind.FanOut, 1, 0.6, 0, null, ReviewNodeStatus.Running, summary: "reviewers ×4 · 2/4 done");
        for (var i = 0; i < 4; i++)
        {
            Executed(run, "reviewer", ReviewNodeKind.Agent, 1, 0.6, i < 2 ? 0.48m : 0m, Sonnet,
                i < 2 ? ReviewNodeStatus.Succeeded : ReviewNodeStatus.Running, provider: "claude", transport: ReviewTransport.Seat,
                input: i < 2 ? 96_000 : 0, output: i < 2 ? 2_400 : 0, summary: i < 2 ? "1 finding" : "reviewing", itemKey: Groups[i]);
        }

        run.Status = ReviewRunStatus.Running;
        run.Stage = ReviewStage.Review;
        run.WaitingOn = ReviewWaitingOn.Bot;
        return run;
    }

    private ReviewRun AwaitingMerge()
    {
        var run = Run(ReviewHost.GitHub, "acme/platform-infra", 77, "Bump Postgres to 16.4 in the staging stack",
            "renovate[bot]", "Renovate", "renovate/postgres-16.x", ["@acme/platform", "kofi-a"], hoursAgo: 8);
        run.Subject.AuthorResolvedBy = "Co-authored-by";
        run.Subject.OpenedByLogin = "renovate[bot]";
        run.Subject.AuthorLogin = "kofi-a";
        run.Subject.AuthorDisplayName = "Kofi Asante";
        Categorise(run, ["dependency", "infra"], reach: 3, contract: 1, data: 2, rollback: 2, reversal: 1, exposure: 2);
        Round(run, 1, startedHoursAgo: 8, reviewers: 1, through: "route", routeOutcome: "clean");
        Executed(run, "needs-human-approval", ReviewNodeKind.Code, 1, 7.4, 0, null, summary: "no: no risk rule fired", outcome: "no");
        Executed(run, "bot-approve", ReviewNodeKind.Code, 1, 7.4, 0, null, summary: "approved at the reviewed SHA");
        Executed(run, "base-moved", ReviewNodeKind.Code, 1, 7.3, 0, null, summary: "unchanged", outcome: "unchanged");
        Wait(run, "await-merge", 1, hoursAgo: 7.3);
        run.NeedsHumanApproval = false;
        run.Status = ReviewRunStatus.AwaitingMerge;
        run.Stage = ReviewStage.BranchMaintenance;
        run.WaitingOn = ReviewWaitingOn.Author;
        return run;
    }

    private ReviewRun NeedsHumanApproval()
    {
        var run = Run(ReviewHost.GitHub, "antiphon/antiphon", 2051, "Session runner: retry transcript reads on EBUSY",
            "dmitri-k", "Dmitri Kuznetsov", "fix/transcript-ebusy", ["@antiphon/runtime", "mike", "sam-o"], hoursAgo: 30);
        Categorise(run, ["bugfix", "session-runtime"], reach: 4, contract: 4, data: 2, rollback: 3, reversal: 2, exposure: 3);
        Round(run, 1, startedHoursAgo: 30, reviewers: 2, through: "route");
        Executed(run, "await-fixes", ReviewNodeKind.Wait, 1, 29, 0, null, summary: "push received", outcome: "push");
        Round(run, 2, startedHoursAgo: 20, reviewers: 1, through: "route", trigger: "push", routeOutcome: "clean");
        Executed(run, "needs-human-approval", ReviewNodeKind.Code, 2, 19.5, 0, null, summary: "yes: session runtime; external_contract 4", outcome: "yes");
        Wait(run, "human-approve", 2, hoursAgo: 19.5);
        run.Round = 2;
        run.BotOnlyRounds = 2;
        run.NeedsHumanApproval = true;
        run.Status = ReviewRunStatus.AtGate;
        run.Stage = ReviewStage.HumanGate;
        run.WaitingOn = ReviewWaitingOn.Humans;
        var gate = Gate(run, ReviewGateKind.ApprovalGate, "human-approve", 2, "@antiphon/runtime", hoursAgo: 19.5,
            ["Touches the session runtime risk area", "external_contract is 4 (threshold 4)"]);
        gate.Status = ReviewQueueItemStatus.Assigned;
        gate.AssigneeLogin = "mike";
        gate.AssigneeDisplayName = "Mike";

        var fixedFinding = Finding(run, "F1", "CHK-0230", ReviewSeverity.Major, 0.77, "server/Infrastructure/Agents/TranscriptReader.cs", 120, 126,
            "catch (IOException)", "Retry loop retries every IOException, not just EBUSY",
            "A missing file would be retried for the full budget and the session would look stalled.", null, []);
        Respond(fixedFinding, ReviewFindingAction.Fixed, "dmitri-k", "Dmitri Kuznetsov", text: "Narrowed to HResult 0x20 / EBUSY.", hoursAgo: 21);
        fixedFinding.Status = ReviewFindingStatus.Fixed;
        return run;
    }

    private ReviewRun AnotherRoundQueued()
    {
        var run = Run(ReviewHost.GitHub, "acme/orders-service", 820, "Remove the legacy discount endpoint",
            "sam-o", "Sam Okafor", "chore/remove-discount-v1", ["@acme/orders", "priya-n"], hoursAgo: 49);
        Categorise(run, ["refactor", "api"], reach: 3, contract: 5, data: 1, rollback: 4, reversal: 4, exposure: 3);
        Round(run, 1, startedHoursAgo: 49, reviewers: 2, through: "route", routeOutcome: "clean");
        Executed(run, "needs-human-approval", ReviewNodeKind.Code, 1, 48, 0, null, summary: "yes: external_contract 5", outcome: "yes");
        Executed(run, "human-approve", ReviewNodeKind.Gate, 1, 30, 0, null, summary: "AnotherRound by priya-n", outcome: "another-round");
        Executed(run, "intake", ReviewNodeKind.Code, 2, 0.1, 0, null, ReviewNodeStatus.Pending);
        Iteration(run, 2, 2, "another-round", hoursAgo: 0.2);
        var gate = Gate(run, ReviewGateKind.ApprovalGate, "human-approve", 1, "@acme/orders", hoursAgo: 47,
            ["external_contract is 5 (threshold 4)", "reversal_after_months is 4 (threshold 4)"]);
        gate.Status = ReviewQueueItemStatus.Done;
        gate.Decision = ReviewGateDecision.AnotherRound;
        gate.DecidedAt = _now.AddHours(-0.2);
        gate.DecidedBy = "priya-n";
        gate.AssigneeLogin = "priya-n";
        gate.SteerPrompt = "Check every caller in the mobile app repo before removing the route; the v1 route is still in the 3.x client.";
        run.SteerPrompt = gate.SteerPrompt;
        run.Round = 2;
        run.NeedsHumanApproval = true;
        run.Status = ReviewRunStatus.Queued;
        run.Stage = ReviewStage.Intake;
        run.WaitingOn = ReviewWaitingOn.Bot;

        var contract = Finding(run, "F1", "CHK-0601", ReviewSeverity.Major, 0.81, "src/Orders/Api/DiscountEndpoints.cs", 14, 14,
            "app.MapPost(\"/api/v1/discounts\", ...)", "Removes a public route without a deprecation window",
            "The route is in the published OpenAPI document; consumers get a 404 with no sunset header first.", null, [],
            why: "Breaking public routes without notice breaks partner integrations we don't control.");
        Respond(contract, ReviewFindingAction.Question, "sam-o", "Sam Okafor", text: "Do we have usage numbers for the v1 route?", hoursAgo: 46);
        return run;
    }

    private ReviewRun Merged()
    {
        var run = Run(ReviewHost.GitHub, "acme/billing", 450, "Recalculate VAT for EU reverse charge",
            "ana-r", "Ana Rossi", "feat/eu-reverse-charge", ["@acme/billing", "lee-c"], hoursAgo: 120);
        Categorise(run, ["feature", "persistence"], reach: 3, contract: 2, data: 4, rollback: 3, reversal: 3, exposure: 4);
        Round(run, 1, startedHoursAgo: 120, reviewers: 3, through: "route");
        Executed(run, "await-fixes", ReviewNodeKind.Wait, 1, 118, 0, null, summary: "push received", outcome: "push");
        Round(run, 2, startedHoursAgo: 100, reviewers: 1, through: "route", trigger: "push", routeOutcome: "clean");
        Executed(run, "needs-human-approval", ReviewNodeKind.Code, 2, 99, 0, null, summary: "yes: persistent data", outcome: "yes");
        Executed(run, "human-approve", ReviewNodeKind.Gate, 2, 90, 0, null, summary: "Approve by lee-c", outcome: "approve");
        Executed(run, "base-moved", ReviewNodeKind.Code, 2, 90, 0, null, summary: "unchanged", outcome: "unchanged");
        Executed(run, "await-merge", ReviewNodeKind.Wait, 2, 89, 0, null, summary: "merged");
        var gate = Gate(run, ReviewGateKind.ApprovalGate, "human-approve", 2, "@acme/billing", hoursAgo: 99, ["Persistent data (data 4)"]);
        gate.Status = ReviewQueueItemStatus.Done;
        gate.Decision = ReviewGateDecision.Approve;
        gate.DecidedAt = _now.AddHours(-90);
        gate.DecidedBy = "lee-c";
        gate.AssigneeLogin = "lee-c";
        run.Round = 2;
        run.NeedsHumanApproval = true;
        run.Status = ReviewRunStatus.Merged;
        run.Stage = ReviewStage.Done;
        run.WaitingOn = null;

        var rounding = Finding(run, "F1", "CHK-0418", ReviewSeverity.Major, 0.83, "src/Billing/Vat/ReverseCharge.cs", 33, 33,
            "Math.Round(net * rate, 2)", "Rounds half-to-even where invoices round half-up",
            "`Math.Round` defaults to banker's rounding; the invoice PDF rounds half-up, so totals can differ by a cent.",
            "Math.Round(net * rate, 2, MidpointRounding.AwayFromZero)", []);
        Respond(rounding, ReviewFindingAction.WillFix, "ana-r", "Ana Rossi", hoursAgo: 117);
        rounding.Status = ReviewFindingStatus.Fixed;
        var style = Finding(run, "F2", "CHK-0003", ReviewSeverity.Nit, 0.51, "src/Billing/Vat/ReverseCharge.cs", 8, 8,
            "var r = rate;", "Single-letter local", "`r` shadows the meaning of `rate`; inline it.", null, []);
        Respond(style, ReviewFindingAction.Dismiss, "ana-r", "Ana Rossi", ReviewReasonCategory.StylePreference, ReviewDismissScope.Instance, "Keeping it; mirrors the spec's notation.", hoursAgo: 116);
        style.Status = ReviewFindingStatus.Dismissed;
        return run;
    }

    private ReviewRun Closed()
    {
        var run = Run(ReviewHost.GitHub, "acme/web-portal", 1301, "Fix typo in checkout copy",
            "jo-b", "Jo Bailey", "fix/checkout-typo", ["@acme/web"], hoursAgo: 72);
        Categorise(run, ["docs"], reach: 1, contract: 1, data: 1, rollback: 1, reversal: 1, exposure: 2);
        Round(run, 1, startedHoursAgo: 72, reviewers: 1, through: "route");
        Executed(run, "await-fixes", ReviewNodeKind.Wait, 1, 71, 0, null, summary: "closed");
        run.Status = ReviewRunStatus.Closed;
        run.Stage = ReviewStage.Done;
        run.WaitingOn = null;
        var copy = Finding(run, "F1", "CHK-0044", ReviewSeverity.Nit, 0.6, "src/checkout/copy.ts", 21, 21,
            "\"Procede to payment\"", "Typo remains in the button label", "The label still reads \"Procede\".", "\"Proceed to payment\"", []);
        copy.Status = ReviewFindingStatus.Outdated;
        return run;
    }

    private ReviewRun FailedVerify()
    {
        var run = Run(ReviewHost.GitHub, "acme/platform-infra", 81, "Rotate the staging TLS certificate",
            "kofi-a", "Kofi Asante", "ops/rotate-staging-tls", ["@acme/platform"], hoursAgo: 5);
        Categorise(run, ["infra", "security"], reach: 2, contract: 1, data: 1, rollback: 2, reversal: 1, exposure: 3);
        Round(run, 1, startedHoursAgo: 5, reviewers: 1, through: "collate");
        Executed(run, "verify", ReviewNodeKind.Completion, 1, 4.6, 0, Luna, ReviewNodeStatus.Failed, provider: "luna-seat", transport: ReviewTransport.Seat,
            summary: "completions-cheap: provider rejected the request (422); no fallback");
        run.Status = ReviewRunStatus.Failed;
        run.Stage = ReviewStage.Verify;
        run.WaitingOn = null;
        return run;
    }

    private ReviewRun LocalDisputed()
    {
        var run = Run(ReviewHost.Local, "antiphon/antiphon", null, "Local review: feat/review-engine",
            "mike", "Mike", "feat/review-engine", ["@antiphon/core", "dmitri-k", "sam-o"], hoursAgo: 6);
        run.Subject.Url = null;
        Categorise(run, ["feature"], reach: 3, contract: 3, data: 3, rollback: 2, reversal: 3, exposure: 2);
        Round(run, 1, startedHoursAgo: 6, reviewers: 2, through: "route");
        Executed(run, "await-fixes", ReviewNodeKind.Wait, 1, 5.5, 0, null, summary: "recheck requested", outcome: "recheck");
        Round(run, 2, startedHoursAgo: 3, reviewers: 1, through: "route", trigger: "recheck");
        Wait(run, "await-fixes", 2, hoursAgo: 2.8);
        run.Round = 2;
        run.BotOnlyRounds = 2;
        run.Status = ReviewRunStatus.AwaitingFixes;
        run.Stage = ReviewStage.AwaitingFixes;
        run.WaitingOn = ReviewWaitingOn.Author;

        var migration = Finding(run, "F1", "CHK-0090", ReviewSeverity.Major, 0.7, "server/Migrations/20261122000000_AddReviewEngine.cs", 210, 214,
            "protected override void Down(MigrationBuilder migrationBuilder)", "Down drops tables that hold review data",
            "The operator rule is that Down fails safely; dropping six tables loses every finding and response.", null, ["data-loss"]);
        Respond(migration, ReviewFindingAction.Dispute, "mike", "Mike", text: "Down throws; the drop calls are unreachable.", hoursAgo: 2.9);
        migration.Status = ReviewFindingStatus.Disputed;
        Finding(run, "F2", "CHK-0012", ReviewSeverity.Minor, 0.62, "server/Application/Review/ReviewService.cs", 140, 140,
            "Random.Shared.Next(candidates.Count)", "Random assignment ignores open load",
            "Design 11.4 weights the pick by open load; a uniform pick will pile reviews onto whoever is listed.", null, []).RoundRaised = 2;
        return run;
    }

    // ---- Builders ----

    private ReviewRun Run(ReviewHost host, string repo, int? pr, string title, string author, string authorName, string head,
        List<string> owners, double hoursAgo)
    {
        var started = _now.AddHours(-hoursAgo);
        var subject = new ReviewSubject
        {
            Id = Guid.NewGuid(),
            Host = host,
            Repo = repo,
            PrNumber = pr,
            Title = title,
            Url = pr is { } n ? $"https://github.com/{repo}/pull/{n}" : null,
            AuthorLogin = author,
            AuthorDisplayName = authorName,
            OpenedByLogin = author,
            BaseRef = "main",
            HeadRef = head,
            BaseSha = Sha(repo + "base" + pr),
            HeadSha = Sha(repo + "head" + pr + title),
            OwnerTeams = owners.Where(IsTeam).ToList(),
            OwnerUsers = owners.Where(o => !IsTeam(o)).ToList(),
            CreatedAt = started,
        };
        return new ReviewRun
        {
            Id = Guid.NewGuid(),
            SubjectId = subject.Id,
            Subject = subject,
            Round = 1,
            WorkflowName = workflows.Default.Name,
            WorkflowHash = workflows.DefaultHash,
            WorkflowRevisionId = _revisionId,
            StartedAt = started,
            UpdatedAt = _now.AddMinutes(-Random.Shared.Next(1, 50)),
        };
    }

    private static void Categorise(ReviewRun run, List<string> categories, int reach, int contract, int data, int rollback, int reversal, int exposure)
    {
        run.Categories = categories;
        run.BlastReach = reach;
        run.BlastExternalContract = contract;
        run.BlastData = data;
        run.BlastRollbackAtRelease = rollback;
        run.BlastReversalAfterMonths = reversal;
        run.BlastExposure = exposure;
    }

    /// <summary>One round of the pipeline from intake to <paramref name="through"/>, every node succeeded.</summary>
    private void Round(ReviewRun run, int iteration, double startedHoursAgo, int reviewers, string through,
        string? trigger = null, string routeOutcome = "findings")
    {
        Iteration(run, iteration, iteration, trigger ?? (iteration == 1 ? "opened" : "push"), startedHoursAgo);
        var t = startedHoursAgo;
        foreach (var node in IntakeNodes)
        {
            (ReviewNodeKind Kind, string? Model, decimal Cost, long Input, string? Provider) step = node switch
            {
                "categorise" => (ReviewNodeKind.Decision, Jev, 0.00042m, 9_800L, "jev"),
                "select" => (ReviewNodeKind.Decision, Jev, 0.00131m, 31_200L, "jev"),
                _ => (ReviewNodeKind.Code, null, 0m, 0L, null),
            };
            var (kind, model, cost, input, provider) = step;
            Executed(run, node, kind, iteration, t, cost, model, provider: provider, transport: model is null ? null : ReviewTransport.Http,
                input: input, summary: Summary(node, iteration, reviewers));
            t -= 0.02;
            if (node == through)
                return;
        }

        if (reviewers > 0)
        {
            Executed(run, "review", ReviewNodeKind.FanOut, iteration, t, 0, null, summary: $"reviewers ×{reviewers}");
            for (var i = 0; i < reviewers; i++)
            {
                Executed(run, "reviewer", ReviewNodeKind.Agent, iteration, t, 0.40m + 0.11m * i, Sonnet, provider: "claude", transport: ReviewTransport.Seat,
                    input: 88_000 + 7_000 * i, output: 2_100 + 300 * i, summary: i == 0 ? "1 finding" : "no findings", itemKey: Groups[i % Groups.Length]);
            }

            t -= 0.3;
        }

        foreach (var node in new[] { "collate", "verify", "publish", "route" })
        {
            (ReviewNodeKind Kind, string? Model, decimal Cost, string? Provider) step = node switch
            {
                "collate" => (ReviewNodeKind.Join, null, 0m, null),
                "verify" => (ReviewNodeKind.Completion, Luna, 0.052m, "luna-seat"),
                _ => (ReviewNodeKind.Code, null, 0m, null),
            };
            var (kind, model, cost, provider) = step;
            Executed(run, node, kind, iteration, t, cost, model, provider: provider, transport: model is null ? null : ReviewTransport.Seat,
                input: model is null ? 0 : 41_000, output: model is null ? 0 : 900,
                summary: node == "route" ? routeOutcome : Summary(node, iteration, reviewers), outcome: node == "route" ? routeOutcome : null);
            t -= 0.02;
            if (node == through)
                return;
        }
    }

    private static string? Summary(string node, int iteration, int reviewers) => node switch
    {
        "intake" => "pinned base and head; policy read at base",
        "delta" => iteration == 1 ? "full review" : "delta since the last reviewed SHA",
        "categorise" => "7 questions answered; blast radius recorded",
        "select" => $"{12 + reviewers * 3} of 164 candidate checks apply",
        "group" => $"{Math.Max(1, reviewers)} reviewer groups",
        "packs" => "shared prefix 38k tokens",
        "collate" => "duplicates collapsed",
        "verify" => "1 finding disproved and dropped",
        "publish" => "review posted; summary updated",
        "route" => "findings",
        _ => null,
    };

    private void Executed(ReviewRun run, string nodeId, ReviewNodeKind kind, int iteration, double hoursAgo, decimal cost, string? model,
        ReviewNodeStatus status = ReviewNodeStatus.Succeeded, string? provider = null, ReviewTransport? transport = null,
        long input = 0, long output = 0, string? summary = null, string? itemKey = null, string? outcome = null)
    {
        var started = _now.AddHours(-hoursAgo);
        run.Nodes.Add(new ReviewNodeExecution
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            NodeId = nodeId,
            Iteration = iteration,
            Kind = kind,
            Status = status,
            ItemKey = itemKey,
            Outcome = outcome,
            Provider = provider,
            Model = model,
            Transport = transport,
            InputTokens = input,
            OutputTokens = output,
            CostUsd = cost,
            CostBasis = model is null ? null : transport == ReviewTransport.Seat ? "api-equivalent" : "actual",
            StartedAt = status == ReviewNodeStatus.Pending ? null : started,
            CompletedAt = status is ReviewNodeStatus.Succeeded or ReviewNodeStatus.Failed ? started.AddMinutes(model is null ? 0.2 : 1.5) : null,
            Summary = summary,
        });
    }

    private void Wait(ReviewRun run, string nodeId, int iteration, double hoursAgo) =>
        Executed(run, nodeId, nodeId.StartsWith("human", StringComparison.Ordinal) ? ReviewNodeKind.Gate : ReviewNodeKind.Wait,
            iteration, hoursAgo, 0, null, ReviewNodeStatus.Waiting, summary: "waiting");

    private ReviewGate Gate(ReviewRun run, ReviewGateKind kind, string nodeId, int iteration, string team, double hoursAgo, List<string> reasons)
    {
        var gate = new ReviewGate
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Kind = kind,
            NodeId = nodeId,
            Iteration = iteration,
            Status = ReviewQueueItemStatus.Unassigned,
            TeamKey = team,
            OpenedAt = _now.AddHours(-hoursAgo),
            Reasons = reasons,
        };
        run.Gates.Add(gate);
        return gate;
    }

    private ReviewFinding Finding(ReviewRun run, string key, string? check, ReviewSeverity severity, double confidence, string file,
        int? start, int? end, string anchor, string title, string body, string? suggestion, List<string> flags, string? why = null)
    {
        var finding = new ReviewFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Key = key,
            CheckId = check,
            CheckWhy = why,
            Severity = severity,
            Flags = flags,
            Confidence = confidence,
            FilePath = file,
            LineStart = start,
            LineEnd = end,
            Anchor = anchor,
            Title = title,
            Body = body,
            Suggestion = suggestion,
            Hunk = Hunk(start ?? 1, anchor, suggestion),
            Status = ReviewFindingStatus.Open,
            RoundRaised = 1,
            Posted = run.Subject.Host == ReviewHost.GitHub && severity != ReviewSeverity.Nit,
            SyncState = run.Subject.Host == ReviewHost.GitHub ? ReviewSyncState.Synced : ReviewSyncState.NotApplicable,
            ExternalThreadUrl = run.Subject.Url is { } url && severity != ReviewSeverity.Nit ? $"{url}#discussion_r{Random.Shared.Next(100_000_000, 999_999_999)}" : null,
            CreatedAt = run.StartedAt.AddMinutes(40),
            UpdatedAt = run.StartedAt.AddMinutes(40),
        };
        run.Findings.Add(finding);
        return finding;
    }

    private void Respond(ReviewFinding finding, ReviewFindingAction action, string login, string name,
        ReviewReasonCategory? reason = null, ReviewDismissScope? scope = null, string? text = null, double hoursAgo = 1)
    {
        finding.Events.Add(new ReviewFindingEvent
        {
            Id = Guid.NewGuid(),
            FindingId = finding.Id,
            Action = action,
            ActorLogin = login,
            ActorDisplayName = name,
            ReasonCategory = reason,
            Scope = scope,
            Text = text,
            SyncState = finding.SyncState == ReviewSyncState.NotApplicable ? ReviewSyncState.NotApplicable : ReviewSyncState.Synced,
            CreatedAt = _now.AddHours(-hoursAgo),
        });
    }

    private void Iteration(ReviewRun run, int iteration, int round, string trigger, double hoursAgo) =>
        run.Iterations.Add(new ReviewIteration
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            Iteration = iteration,
            Round = round,
            Trigger = trigger,
            StartedAt = _now.AddHours(-hoursAgo),
        });

    private static bool IsTeam(string owner) => owner.StartsWith('@') && owner.Contains('/');

    /// <summary>A plausible unified-diff hunk with the anchor as the added line.</summary>
    private static string Hunk(int line, string anchor, string? suggestion)
    {
        var start = Math.Max(1, line - 3);
        var first = suggestion?.Split('\n')[0].Trim();
        var before = !string.IsNullOrEmpty(first) && first != anchor.Trim() ? first : "// previous implementation";
        return $"@@ -{start},6 +{start},6 @@\n"
            + "         {\n"
            + "             var current = await LoadAsync(id, ct);\n"
            + $"-            {before}\n"
            + $"+            {anchor.Trim()}\n"
            + "             return current;\n"
            + "         }";
    }

    private static string Sha(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
}
