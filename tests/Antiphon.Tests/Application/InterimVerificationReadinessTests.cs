using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Files;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-9 / G-22..G-33. The production <see cref="InterimVerificationReadinessReader"/>
/// against real temp files: every field of an otherwise ready qualification receipt and saved monitor
/// varied one at a time, exact freshness equalities, and fail-closed reads. A green here
/// authenticates the local attestation contract only; CARD-0545's qualification proves the receipt.
/// </summary>
[Category("Integration")]
public sealed class InterimVerificationReadinessTests
{
    private static readonly Guid ProjectId = Guid.Parse("c5440000-5555-6666-7777-888888888888");
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task C544_DisabledByDefault()
    {
        new InterimVerificationSettings().Enabled.ShouldBeFalse("shipped default");
        using var state = new StateFixture();
        var defaults = new InterimVerificationSettings { CanonicalRepositoryPath = state.Repository, ProjectId = ProjectId, StateRoot = state.Root };
        (await Read(state, defaults)).ShouldBe((false, "interim_verification_disabled"), "default-settings");
        (await Read(state, state.Enabled())).ShouldBe((true, "ready"), "control-enabled");

        // Caller data cannot carry authority: the create contract has no such members and ignores them.
        var names = typeof(CreateAgentTaskRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        foreach (var forbidden in new[] { "Ready", "Readiness", "StateRoot", "InterimVerificationEnabled", "Enabled" })
            names.ShouldNotContain(forbidden, $"request-field {forbidden}");
        var request = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
            """{"goal":"g","role":"Review","ready":true,"stateRoot":"C:\\anything","enabled":true}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!;
        request.Goal.ShouldBe("g");
        (await Read(state, defaults)).ShouldBe((false, "interim_verification_disabled"), "after-request-fields");
    }

    [Test]
    public async Task C544_QualifiedRepository()
    {
        using var state = new StateFixture();
        (await ReadFor(state, state.Enabled(), Path.Combine(state.Root, "other-repo"), ProjectId)).ShouldBe((false, "readiness_repository_unqualified"), "task-repository");
        state.Receipt["repositoryPath"] = Path.Combine(state.Root, "other-repo");
        state.Write();
        (await Read(state, state.Enabled())).ShouldBe((false, "qualification_repository_mismatch"), "receipt-repository");
        state.Reset();
        ((JsonObject)state.Monitor["Identity"]!)["RepositoryPath"] = Path.Combine(state.Root, "other-repo");
        state.Write();
        (await Read(state, state.Enabled())).ShouldBe((false, "monitor_repository_mismatch"), "monitor-repository");
    }

    [Test]
    public async Task C544_QualifiedProject()
    {
        using var state = new StateFixture();
        (await ReadFor(state, state.Enabled(), state.Repository, Guid.NewGuid())).ShouldBe((false, "readiness_project_unqualified"), "foreign-project");
        (await ReadFor(state, state.Enabled(), state.Repository, null)).ShouldBe((false, "readiness_project_unqualified"), "null-request-vs-configured");
        var nullConfigured = state.Enabled();
        nullConfigured.ProjectId = null;
        (await ReadFor(state, nullConfigured, state.Repository, ProjectId)).ShouldBe((false, "readiness_project_unqualified"), "configured-null-vs-request");
        state.Receipt["projectId"] = null;
        ((JsonObject)state.Monitor["Identity"]!)["ProjectId"] = null;
        state.Write();
        (await ReadFor(state, nullConfigured, state.Repository, null)).ShouldBe((true, "ready"), "null-matches-null");
        state.Receipt["projectId"] = Guid.NewGuid().ToString();
        state.Write();
        (await ReadFor(state, nullConfigured, state.Repository, null)).ShouldBe((false, "qualification_project_mismatch"), "receipt-foreign-project");
    }

    [Test]
    public async Task C544_ReadFailure()
    {
        using var state = new StateFixture();
        var rows = new (string Row, Action Setup, string Expected)[]
        {
            ("missing-receipt", () => File.Delete(state.ReceiptPath), "qualification_receipt_missing"),
            ("missing-monitor", () => File.Delete(state.MonitorPath), "monitor_missing"),
            ("torn-receipt", () => File.WriteAllText(state.ReceiptPath, "{\"schemaVersion\": 1, \"repositoryPath\": "), "qualification_receipt_malformed"),
            ("torn-monitor", () => File.WriteAllText(state.MonitorPath, "{\"Health\": {"), "monitor_malformed"),
            ("empty-monitor", () => File.WriteAllText(state.MonitorPath, ""), "monitor_empty"),
            ("oversized-receipt", () => File.WriteAllText(state.ReceiptPath, new string(' ', 64 * 1024 + 1)), "qualification_receipt_oversized"),
            ("missing-state-root", () => Directory.Delete(state.Root, recursive: true), "qualification_receipt_missing"),
        };
        foreach (var (row, setup, expected) in rows)
        {
            state.Reset();
            setup();
            (await Read(state, state.Enabled())).ShouldBe((false, expected), row);
        }

        state.Reset();
        await using (var denied = new FileStream(state.MonitorPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            (await Read(state, state.Enabled())).ShouldBe((false, "monitor_unreadable"), "read-denied");
        (await Read(state, state.Enabled())).ShouldBe((true, "ready"), "recovered-pair");
    }

    [Test]
    public async Task C544_QualificationReceipt()
    {
        using var state = new StateFixture();
        var rows = new (string Row, Action<JsonObject> Change, string Expected)[]
        {
            ("no-recipient-evidence", r => r.Remove("recipientEvidenceIds"), "qualification_recipient_evidence_missing"),
            ("empty-recipient-evidence", r => r["recipientEvidenceIds"] = new JsonArray(), "qualification_recipient_evidence_missing"),
            ("blank-recipient-evidence", r => r["recipientEvidenceIds"] = new JsonArray(" "), "qualification_recipient_evidence_missing"),
            ("no-outage-evidence", r => r.Remove("outageRecoveryEvidenceIds"), "qualification_outage_evidence_missing"),
            ("no-scheduled-run", r => r.Remove("scheduledRunId"), "qualification_runs_missing"),
            ("no-manual-run", r => r.Remove("manualRunId"), "qualification_runs_missing"),
            ("unsupported-schema", r => r["schemaVersion"] = 2, "qualification_receipt_schema_unsupported"),
        };
        foreach (var (row, change, expected) in rows)
        {
            state.Reset();
            change(state.Receipt);
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, expected), row);
        }

        state.Reset();
        var filenameOnly = new JsonObject { ["qualificationArtifactPath"] = state.Receipt["qualificationArtifactPath"]!.GetValue<string>() };
        File.WriteAllText(state.ReceiptPath, filenameOnly.ToJsonString());
        (await Read(state, state.Enabled())).Ready.ShouldBeFalse("markdown-filename-alone");
    }

    [Test]
    public async Task C544_QualificationRevision()
    {
        using var state = new StateFixture();
        foreach (var (row, value) in new (string, string?)[]
                 {
                     ("missing", null), ("short", Commit[..12]), ("uppercase", Commit.ToUpperInvariant()), ("not-hex", new string('z', 40)),
                 })
        {
            state.Reset();
            if (value is null) state.Receipt.Remove("qualificationArtifactCommitSha");
            else state.Receipt["qualificationArtifactCommitSha"] = value;
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, "qualification_revision_invalid"), row);
        }
    }

    [Test]
    public async Task C544_PolicyHash()
    {
        using var state = new StateFixture();
        ((JsonObject)state.Monitor["Identity"]!)["PolicyHash"] = "different-policy";
        state.Write();
        (await Read(state, state.Enabled())).ShouldBe((false, "monitor_policy_hash_mismatch"), "different-policy-hash");
    }

    [Test]
    public async Task C544_ScriptHash()
    {
        using var state = new StateFixture();
        ((JsonObject)state.Monitor["Identity"]!)["ScriptHash"] = "different-script";
        state.Write();
        (await Read(state, state.Enabled())).ShouldBe((false, "monitor_script_hash_mismatch"), "different-script-hash");
    }

    [Test]
    public async Task C544_RunIdentity()
    {
        using var state = new StateFixture();
        foreach (var (row, change) in new (string, Action<JsonObject>)[]
                 {
                     ("crossed-run", i => i["JobNativeRunId"] = "run-other"),
                     ("missing-job", i => i.Remove("WindmillJobId")),
                     ("missing-run", i => { i.Remove("ScheduledRunId"); i.Remove("JobNativeRunId"); }),
                 })
        {
            state.Reset();
            change((JsonObject)state.Monitor["Identity"]!);
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, "monitor_run_identity_mismatch"), row);
        }
    }

    [Test]
    public async Task C544_MonitorAge()
    {
        using var state = new StateFixture();
        foreach (var (row, age, ready) in new (string, TimeSpan, bool)[]
                 {
                     ("age-0", TimeSpan.Zero, true),
                     ("age-59m59s", new TimeSpan(0, 59, 59), true),
                     ("age-60m", TimeSpan.FromMinutes(60), true),
                     ("age-60m-plus-tick", TimeSpan.FromMinutes(60) + TimeSpan.FromTicks(1), false),
                 })
        {
            state.Reset();
            state.Monitor["RecordedAt"] = (Now - age).ToString("o");
            state.Write();
            var verdict = await Read(state, state.Enabled());
            verdict.Ready.ShouldBe(ready, row);
            if (!ready) verdict.Reason.ShouldBe("monitor_stale", row);
        }
        foreach (var (row, value) in new (string, JsonNode?)[] { ("missing-timestamp", null), ("malformed-timestamp", "yesterday"), ("numeric-timestamp", 1) })
        {
            state.Reset();
            if (value is null) state.Monitor.Remove("RecordedAt"); else state.Monitor["RecordedAt"] = value;
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, "monitor_timestamp_invalid"), row);
        }
    }

    [Test]
    public async Task C544_FutureMonitor()
    {
        using var state = new StateFixture();
        state.Monitor["RecordedAt"] = (Now + TimeSpan.FromTicks(1)).ToString("o");
        state.Write();
        (await Read(state, state.Enabled())).ShouldBe((false, "monitor_future"), "one-tick-future");
    }

    [Test]
    public async Task C544_MonitorVerdict()
    {
        using var state = new StateFixture();
        foreach (var (row, change, expected) in new (string, Action<JsonObject>, string)[]
                 {
                     ("unhealthy", h => h["Healthy"] = false, "monitor_unhealthy"),
                     ("not-ready-for-deferral", h => h["ReadyForDeferral"] = false, "monitor_not_ready_for_deferral"),
                     ("string-true-healthy", h => h["Healthy"] = "true", "monitor_malformed"),
                     ("numeric-ready", h => h["ReadyForDeferral"] = 1, "monitor_malformed"),
                     ("missing-ready", h => h.Remove("ReadyForDeferral"), "monitor_malformed"),
                 })
        {
            state.Reset();
            change((JsonObject)state.Monitor["Health"]!);
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, expected), row);
        }
    }

    [Test]
    public async Task C545_WatchdogInstance()
    {
        using var state = new StateFixture();
        var match = await new InterimVerificationReadinessReader(Options.Create(state.Enabled()), new FakeTimeProvider(Now))
            .ReadAsync(state.Repository, ProjectId, CancellationToken.None);
        (match.Ready, match.Reason).ShouldBe((true, "ready"), "match");
        match.Snapshot.ShouldNotBeNull();
        match.Snapshot.WatchdogInstanceId.ShouldBe("wd-1", "match snapshot");

        var rows = new (string Row, Action Change, string Expected)[]
        {
            ("receipt-missing", () => state.Receipt.Remove("watchdogInstanceId"), "qualification_watchdog_missing"),
            ("receipt-blank", () => state.Receipt["watchdogInstanceId"] = " ", "qualification_watchdog_missing"),
            ("monitor-missing", () => ((JsonObject)state.Monitor["Identity"]!).Remove("WatchdogInstanceId"), "monitor_watchdog_mismatch"),
            ("monitor-blank", () => ((JsonObject)state.Monitor["Identity"]!)["WatchdogInstanceId"] = " ", "monitor_watchdog_mismatch"),
            ("mismatch", () => ((JsonObject)state.Monitor["Identity"]!)["WatchdogInstanceId"] = "wd-2", "monitor_watchdog_mismatch"),
            ("case-differs", () => ((JsonObject)state.Monitor["Identity"]!)["WatchdogInstanceId"] = "WD-1", "monitor_watchdog_mismatch"),
            ("stale-and-mismatch", () =>
            {
                state.Monitor["RecordedAt"] = Now.AddMinutes(-61).ToString("o");
                ((JsonObject)state.Monitor["Identity"]!)["WatchdogInstanceId"] = "wd-2";
            }, "monitor_stale"),
        };
        foreach (var (row, change, expected) in rows)
        {
            state.Reset();
            change();
            state.Write();
            (await Read(state, state.Enabled())).ShouldBe((false, expected), row);
        }
    }

    private static Task<(bool Ready, string Reason)> Read(StateFixture state, InterimVerificationSettings settings) =>
        ReadFor(state, settings, state.Repository, settings.ProjectId);

    private static async Task<(bool Ready, string Reason)> ReadFor(StateFixture state, InterimVerificationSettings settings,
        string repository, Guid? project)
    {
        var reader = new InterimVerificationReadinessReader(Options.Create(settings), new FakeTimeProvider(Now));
        var verdict = await reader.ReadAsync(repository, project, CancellationToken.None);
        if (verdict.Ready)
        {
            verdict.Snapshot.ShouldNotBeNull();
            verdict.Snapshot.QualificationArtifactCommitSha.ShouldBe(Commit);
            verdict.Snapshot.RecipientEvidenceIds.ShouldNotBeEmpty();
        }
        return (verdict.Ready, verdict.Reason);
    }

    private sealed class StateFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("antiphon-c544-readiness").FullName;
        public string Repository => Path.Combine(Root, "canonical-repo");
        public string ReceiptPath => Path.Combine(Root, InterimVerificationReadinessReader.ReceiptFileName);
        public string MonitorPath => Path.Combine(Root, InterimVerificationReadinessReader.MonitorFileName);
        public JsonObject Receipt { get; private set; } = null!;
        public JsonObject Monitor { get; private set; } = null!;

        public StateFixture() => Reset();

        public InterimVerificationSettings Enabled() => new()
        {
            Enabled = true, CanonicalRepositoryPath = Repository, ProjectId = ProjectId, StateRoot = Root,
        };

        public void Reset()
        {
            Directory.CreateDirectory(Root);
            Receipt = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["repositoryPath"] = Repository,
                ["projectId"] = ProjectId.ToString(),
                ["qualificationArtifactPath"] = "docs/investigations/2026-09-20-card-0487-nightly-qualification.md",
                ["qualificationArtifactCommitSha"] = Commit,
                ["policyHash"] = "policy-hash-1",
                ["scriptHash"] = "script-hash-1",
                ["manualRunId"] = "run-manual-1",
                ["scheduledRunId"] = "run-scheduled-1",
                ["scheduledJobId"] = "job-scheduled-1",
                ["recipientEvidenceIds"] = new JsonArray("recipient-1", "recipient-2"),
                ["outageRecoveryEvidenceIds"] = new JsonArray("outage-1"),
                ["watchdogInstanceId"] = "wd-1",
                ["acceptedAt"] = "2026-09-16T08:00:00Z",
            };
            Monitor = new JsonObject
            {
                ["ExitCode"] = 0,
                ["Health"] = new JsonObject { ["Healthy"] = true, ["Pending"] = false, ["ReadyForDeferral"] = true, ["Reasons"] = new JsonArray() },
                ["Receipt"] = false,
                ["RecordedAt"] = Now.AddMinutes(-5).ToString("o"),
                ["Identity"] = new JsonObject
                {
                    ["RepositoryPath"] = Repository,
                    ["ProjectId"] = ProjectId.ToString(),
                    ["PolicyHash"] = "policy-hash-1",
                    ["ScriptHash"] = "script-hash-1",
                    ["ScheduledRunId"] = "run-nightly-17",
                    ["JobNativeRunId"] = "run-nightly-17",
                    ["WindmillJobId"] = "job-nightly-17",
                    ["WatchdogInstanceId"] = "wd-1",
                    ["WatchdogHeartbeatAt"] = Now.AddMinutes(-6).ToString("o"),
                },
            };
            Write();
        }

        public void Write()
        {
            File.WriteAllText(ReceiptPath, Receipt.ToJsonString());
            File.WriteAllText(MonitorPath, Monitor.ToJsonString());
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
