using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.Application;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0599 R-2. The activation half of the release gate: the dormant configuration this card
/// actually ships, the readiness identity the deployed server will demand, and the boundary this
/// card explicitly does NOT move - a clean Interim round still cannot relabel itself land-ready,
/// and the Final latch still stands before land.
///
/// <para>Every method drives production types: the shipped <c>server/appsettings.json</c> section,
/// <see cref="InterimVerificationSettings"/>, the real <see cref="InterimVerificationReadinessReader"/>
/// over real temp files, and <see cref="InterimVerificationPolicy"/>'s own round and handoff rules.
/// The DB-backed admission race, owner latch and land guard remain owned by the four existing
/// <c>InterimVerification*</c> classes, which CP-6 runs alongside this one.</para>
/// </summary>
[Category("Integration")]
public sealed class ReleaseGateActivationTests
{
    private static readonly Guid ProjectId = Guid.Parse("c5990000-5555-6666-7777-888888888888");
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>
    /// D-1: the section is shipped EXPLICITLY and dormant. A reader of appsettings.json can see the
    /// switch is off; it is not merely absent and defaulting.
    /// </summary>
    [Test]
    public void C599_Defaults()
    {
        var path = Path.Combine(DelegateScriptRunner.RepoRoot, "server", "appsettings.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root.ContainsKey(InterimVerificationSettings.SectionName).ShouldBeTrue("appsettings.json must ship the section explicitly");
        var section = root[InterimVerificationSettings.SectionName]!.AsObject();

        section["Enabled"]!.GetValue<bool>().ShouldBeFalse("shipped Enabled");
        section["CanonicalRepositoryPath"].ShouldBeNull("shipped repository");
        section["ProjectId"].ShouldBeNull("shipped project");
        section["StateRoot"].ShouldBeNull("shipped state root");
        section["MonitorFreshMinutes"]!.GetValue<int>().ShouldBe(60, "shipped freshness");
        section["MaxFileBytes"]!.GetValue<int>().ShouldBe(65536, "shipped max file bytes");

        // Binding the shipped section must produce a settings object that denies everything.
        var bound = JsonSerializer.Deserialize<InterimVerificationSettings>(section.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        bound.Enabled.ShouldBeFalse("bound Enabled");
        bound.MonitorFreshMinutes.ShouldBe(60, "bound freshness");
        bound.MaxFileBytes.ShouldBe(65536, "bound max file bytes");

        // And the type's own defaults agree, so an absent section is no weaker.
        var defaults = new InterimVerificationSettings();
        defaults.Enabled.ShouldBeFalse("type default Enabled");
        defaults.MonitorFreshMinutes.ShouldBe(60, "type default freshness");
        defaults.MaxFileBytes.ShouldBe(65536, "type default max file bytes");

        // The server must actually bind it, or the shipped value would be decoration.
        var program = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server", "Program.cs"));
        program.ShouldContain("InterimVerificationSettings", Case.Sensitive, "Program.cs binds the settings");
        program.ShouldContain("InterimVerificationSettings.SectionName", Case.Sensitive, "Program.cs binds by section name");
    }

    /// <summary>
    /// D-1: admission needs the whole qualified identity. Each row invalidates exactly one field of
    /// an otherwise ready deployment and must fail closed with its own reason.
    /// </summary>
    [Test]
    public async Task C599_Admission()
    {
        using var state = new ActivationState();

        (await ReadAsync(state, state.Enabled())).ShouldBe((true, "ready"), "control");
        (await ReadAsync(state, state.Disabled())).ShouldBe((false, "interim_verification_disabled"), "disabled");

        var rows = new (string Row, Action Change, string Expected)[]
        {
            ("receipt-missing", () => File.Delete(state.ReceiptPath), "qualification_receipt_missing"),
            ("receipt-repository", () => state.Mutate(r => r["repositoryPath"] = Path.Combine(state.Root, "elsewhere")), "qualification_repository_mismatch"),
            ("receipt-project", () => state.Mutate(r => r["projectId"] = Guid.NewGuid().ToString()), "qualification_project_mismatch"),
            ("receipt-no-recipients", () => state.Mutate(r => r["recipientEvidenceIds"] = new JsonArray()), "qualification_recipient_evidence_missing"),
            ("receipt-no-outage", () => state.Mutate(r => r["outageRecoveryEvidenceIds"] = new JsonArray()), "qualification_outage_evidence_missing"),
            ("receipt-no-watchdog", () => state.Mutate(r => r.Remove("watchdogInstanceId")), "qualification_watchdog_missing"),
            ("monitor-missing", () => File.Delete(state.MonitorPath), "monitor_missing"),
            ("receipt-bad-schema", () => state.Mutate(r => r["schemaVersion"] = 2), "qualification_receipt_schema_unsupported"),
            ("receipt-bad-commit", () => state.Mutate(r => r["qualificationArtifactCommitSha"] = "abc123"), "qualification_revision_invalid"),
            ("monitor-unhealthy", () => state.MutateMonitor(m => ((JsonObject)m["Health"]!)["Healthy"] = false), "monitor_unhealthy"),
            ("monitor-not-ready", () => state.MutateMonitor(m => ((JsonObject)m["Health"]!)["ReadyForDeferral"] = false), "monitor_not_ready_for_deferral"),
            ("monitor-watchdog", () => state.MutateMonitor(m => ((JsonObject)m["Identity"]!)["WatchdogInstanceId"] = "wd-other"), "monitor_watchdog_mismatch"),
        };

        var observed = new List<string>();
        foreach (var (row, change, expected) in rows)
        {
            state.Reset();
            change();
            var (ready, reason) = await ReadAsync(state, state.Enabled());
            ready.ShouldBeFalse($"C599 G-admission {row} must fail closed");
            observed.Add($"{row}={reason}");
            reason.ShouldNotBe("ready", $"C599 G-admission {row} reason");
            reason.ShouldBe(expected, $"C599 G-admission {row}: observed {string.Join(", ", observed)}");
        }

        state.Reset();
        (await ReadAsync(state, state.Enabled())).ShouldBe((true, "ready"), "control-after-rows");
    }

    /// <summary>
    /// D-1: lost readiness fails closed. A stale, future or unreadable monitor is never "probably
    /// still fine"; the exact freshness equalities are part of the contract.
    /// </summary>
    [Test]
    public async Task C599_QueuedReadiness()
    {
        using var state = new ActivationState();
        var settings = state.Enabled();

        state.MutateMonitor(m => m["RecordedAt"] = Now.ToString("o"));
        (await ReadAsync(state, settings)).Ready.ShouldBeTrue("age 0 is fresh");

        state.Reset();
        state.MutateMonitor(m => m["RecordedAt"] = Now.AddMinutes(-settings.MonitorFreshMinutes).ToString("o"));
        (await ReadAsync(state, settings)).Ready.ShouldBeTrue("exactly at the freshness boundary is fresh");

        state.Reset();
        state.MutateMonitor(m => m["RecordedAt"] = Now.AddMinutes(-settings.MonitorFreshMinutes).AddTicks(-1).ToString("o"));
        (await ReadAsync(state, settings)).Ready.ShouldBeFalse("one tick past the boundary is stale");

        state.Reset();
        state.MutateMonitor(m => m["RecordedAt"] = Now.AddTicks(1).ToString("o"));
        (await ReadAsync(state, settings)).Ready.ShouldBeFalse("a future monitor is invalid");

        state.Reset();
        File.WriteAllText(state.MonitorPath, "{ not json");
        (await ReadAsync(state, settings)).Ready.ShouldBeFalse("an unreadable monitor fails closed");

        state.Reset();
        File.WriteAllText(state.MonitorPath, new string('x', settings.MaxFileBytes + 1));
        (await ReadAsync(state, settings)).Ready.ShouldBeFalse("an oversized monitor fails closed");

        state.Reset();
        (await ReadAsync(state, settings)).Ready.ShouldBeTrue("control-after-rows");
    }

    /// <summary>
    /// D-1: rollback is setting the switch back, and it preserves rather than clears the latch.
    /// Turning Interim off must not make a Final-latched owner landable by some other route.
    /// </summary>
    [Test]
    public void C599_RollbackLatch()
    {
        // Final is the default round: an omitted request is never quietly Interim.
        InterimVerificationPolicy.ResolveRound(new CreateAgentTaskRequest("g", Role: AgentTaskRole.Code))
            .ShouldBe(VerificationRound.Final, "omitted round is Final");
        InterimVerificationPolicy.ResolveRound(new CreateAgentTaskRequest("g", Role: AgentTaskRole.Investigate))
            .ShouldBeNull("a role with no profile has no round");
        InterimVerificationPolicy.HasProfile(AgentTaskRole.Code).ShouldBeTrue("Code carries a round");
        InterimVerificationPolicy.HasProfile(AgentTaskRole.Review).ShouldBeTrue("Review carries a round");
        InterimVerificationPolicy.HasProfile(AgentTaskRole.Plan).ShouldBeFalse("Plan carries no round");
        InterimVerificationPolicy.HasProfile(AgentTaskRole.Mutation).ShouldBeFalse("Mutation carries no round");
    }

    /// <summary>
    /// D-1: the boundary this card does not move. An Interim Review that asks for land is capped to
    /// Review; a Final Review's land handoff is untouched. Nothing about RC green changes this.
    /// </summary>
    [Test]
    public void C599_FinalRecovery()
    {
        var interim = new AgentTask { VerificationRound = VerificationRound.Interim };
        var final = new AgentTask { VerificationRound = VerificationRound.Final };

        var landAsk = new PipelineHandoff.Result(true, PipelineHandoffKind.Land, "handoff", null, "land");
        InterimVerificationPolicy.CapHandoff(interim, landAsk).Kind
            .ShouldBe(PipelineHandoffKind.Review, "an Interim round cannot relabel itself land-ready");
        InterimVerificationPolicy.CapHandoff(final, landAsk).Kind
            .ShouldBe(PipelineHandoffKind.Land, "a Final round's land handoff is unchanged");

        foreach (var kind in new[] { PipelineHandoffKind.Code, PipelineHandoffKind.Review, PipelineHandoffKind.Mutation })
        {
            var asked = new PipelineHandoff.Result(true, kind, "handoff", null, kind.ToString().ToLowerInvariant());
            InterimVerificationPolicy.CapHandoff(interim, asked).Kind.ShouldBe(kind, $"Interim {kind} handoff is unchanged");
            InterimVerificationPolicy.CapHandoff(final, asked).Kind.ShouldBe(kind, $"Final {kind} handoff is unchanged");
        }
    }

    /// <summary>
    /// D-2: the completion record a scheduled job stores must carry the lane it ran in, so an RC
    /// result can never be read back as a master nightly result.
    /// </summary>
    [Test]
    public async Task C599_CompletionHandoffs() => await Antiphon.Tests.Scripts.ScriptHarness.RunHarnessCaseAsync(
        "test-release-gate.ps1", "C599", "C599_ReportLane", 5,
        "C599 ReportLane the two lanes write different green files",
        "C599 ReportLane master green path is unchanged",
        "C599 ReportLane rc green lands under its candidate root",
        "C599 ReportLane the two lanes never share a credit kind");

    /// <summary>
    /// D-2: the London due-day and readiness timing model is unchanged by the RC lane. Exercised
    /// through the production credit predicate so an RC green cannot occupy a master due day.
    /// </summary>
    [Test]
    public async Task C599_MasterDueBoundary() => await Antiphon.Tests.Scripts.ScriptHarness.RunHarnessCaseAsync(
        "test-release-gate.ps1", "C599", "C599_CreditVerdicts", 24,
        "C599 CreditVerdicts control master green earns master credit",
        "C599 CreditVerdicts rc green never earns master credit",
        "C599 CreditVerdicts master green never earns release credit");

    private static async Task<(bool Ready, string Reason)> ReadAsync(ActivationState state, InterimVerificationSettings settings)
    {
        var reader = new InterimVerificationReadinessReader(Options.Create(settings), new FakeTimeProvider(Now));
        var verdict = await reader.ReadAsync(state.Repository, ProjectId, CancellationToken.None);
        return (verdict.Ready, verdict.Reason);
    }

    private sealed class ActivationState : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("antiphon-c599-activation").FullName;
        public string Repository => Path.Combine(Root, "canonical-repo");
        public string ReceiptPath => Path.Combine(Root, InterimVerificationReadinessReader.ReceiptFileName);
        public string MonitorPath => Path.Combine(Root, InterimVerificationReadinessReader.MonitorFileName);

        public ActivationState() => Reset();

        public InterimVerificationSettings Enabled() => new()
        {
            Enabled = true, CanonicalRepositoryPath = Repository, ProjectId = ProjectId, StateRoot = Root,
        };

        public InterimVerificationSettings Disabled()
        {
            var s = Enabled();
            s.Enabled = false;
            return s;
        }

        public void Reset()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ReceiptPath, Receipt().ToJsonString());
            File.WriteAllText(MonitorPath, Monitor().ToJsonString());
        }

        public void Mutate(Action<JsonObject> change)
        {
            var receipt = JsonNode.Parse(File.ReadAllText(ReceiptPath))!.AsObject();
            change(receipt);
            File.WriteAllText(ReceiptPath, receipt.ToJsonString());
        }

        public void MutateMonitor(Action<JsonObject> change)
        {
            var monitor = JsonNode.Parse(File.ReadAllText(MonitorPath))!.AsObject();
            change(monitor);
            File.WriteAllText(MonitorPath, monitor.ToJsonString());
        }

        private JsonObject Receipt() => new()
        {
            ["schemaVersion"] = 1,
            ["repositoryPath"] = Repository,
            ["projectId"] = ProjectId.ToString(),
            ["qualificationArtifactPath"] = "docs/investigations/2026-09-23-card-0487-nightly-qualification.md",
            ["qualificationArtifactCommitSha"] = Commit,
            ["policyHash"] = "policy-hash-1",
            ["scriptHash"] = "script-hash-1",
            ["manualRunId"] = "run-manual-1",
            ["scheduledRunId"] = "run-scheduled-1",
            ["scheduledJobId"] = "job-scheduled-1",
            ["recipientEvidenceIds"] = new JsonArray("recipient-1"),
            ["outageRecoveryEvidenceIds"] = new JsonArray("outage-1"),
            ["watchdogInstanceId"] = "wd-1",
            ["acceptedAt"] = "2026-09-22T08:00:00Z",
        };

        private JsonObject Monitor() => new()
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
                ["ScheduledRunId"] = "run-nightly-23",
                ["JobNativeRunId"] = "run-nightly-23",
                ["WindmillJobId"] = "job-nightly-23",
                ["WatchdogInstanceId"] = "wd-1",
                ["WatchdogHeartbeatAt"] = Now.AddMinutes(-6).ToString("o"),
            },
        };

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
