using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Shouldly;
using TUnit.Core;
using RunnerSettings = Antiphon.SessionRunner.SessionRunnerSettings;
using ServerSettings = Antiphon.Server.Application.Settings.SessionRunnerSettings;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1153 S5 (V-21): the plan's five owner sentences in their owning documents
/// (session-runtime-invariants, ops-http, antiphon-api, agent-credentials), each followed by the
/// tests that pin it; the S4 rules the runtime owner states (cold-dispatch prepare, monotonic
/// certificate age, activation); the runner route map listing both POST routes without /api; and
/// the credential section naming the key setting without a literal key or path. A documentation
/// pin, not behavioural proof.
///
/// <para>Sentence five is not the plan's text. The plan wrote "CARD-1151 owns the existing
/// boot-stall retry behavior", but CARD-1151 option B retired the automatic boot retry
/// (<c>BootStallWorkingTickCharacterizationTests</c> pins detection without a stop or requeue), so
/// the owner says the boot-stall behavior neither stops nor retries a session.</para>
///
/// <para>Route, feature, problem-type, operation, setting and lifetime text is compared with the
/// production constants, so a code change without its document fails here. Server-side pins are
/// <c>nameof</c>; runner-side pins (another test assembly) are checked against the runner test
/// source, so a renamed method without its document fails too.</para>
/// </summary>
[Category("Unit")]
public class SessionRunnerAbsenceEvidenceDocumentationTests
{
    internal const string CertificateSentence =
        "CARD-1153 accepts native-empty evidence only from a fresh, authenticated never-created "
        + "certificate for the same session, generation and owning runner store; a 404 or an empty "
        + "transcript is not that certificate.";

    internal const string ClosureSentence =
        "Certification closes an unused session identity against delayed launch and never stops a "
        + "process; old IDs and preparation from an earlier runner epoch remain unknown.";

    internal const string RoutesSentence =
        "POST /sessions/{id}/absence-evidence/prepare and POST /sessions/{id}/absence-evidence are "
        + "runner routes without /api; ordinary session and transcript GETs still return 404 for a "
        + "never-created ID.";

    internal const string CredentialSentence =
        "Direct HTTP absence evidence requires its dedicated key-file configuration and signed "
        + "request/response; missing authentication disables only absence certification. Phone-home "
        + "uses its authenticated owning connection.";

    internal const string NoDeadlineSentence =
        "CARD-1153 adds no automatic relaunch or input-wait release deadline; parking remains disabled "
        + "by default (CARD-1083), and CARD-1151 owns the boot-stall behavior, which neither stops nor "
        + "retries a session.";

    /// <summary>The operator-context rule for preparation (S4).</summary>
    internal const string PrepareSentence =
        "A fresh cold dispatch sends one prepare for the session id it just allocated, after the claim "
        + "commits and before either launch sink; warm reuse, boot-wedge relaunch and interrupted-launch "
        + "resume never prepare, and a failed, unsupported or slow prepare never gates the launch.";

    /// <summary>The operator-context rule for the hold's recheck (S4 monotonic-age repair).</summary>
    internal const string RecheckSentence =
        "The hold re-checks the first read's certificate under the queue gate and task-row lock: the "
        + "same session, generation and store as the locked row, a monotonic age of 0 to 5 s since "
        + "before the request, and the wall deadline taken then; anything else withholds, and the next "
        + "due pass asks the runner again.";

    /// <summary>The restart/legacy caveat.</summary>
    internal const string ActivationSentence =
        "Activation is runners first, then AppHost; a server whose runner does not advertise "
        + "`sessionAbsenceEvidenceV1` sends no prepare and keeps the existing failure, and health alone "
        + "is not activation.";

    private const string PlanBootStallRetrySentence = "CARD-1151 owns the existing boot-stall retry behavior";

    private static readonly string[] OwnerSentences =
    [
        CertificateSentence, ClosureSentence, RoutesSentence, CredentialSentence, NoDeadlineSentence,
        PrepareSentence, RecheckSentence, ActivationSentence,
    ];

    private const string RunnerTests = "tests/Antiphon.SessionRunner.Tests";

    [Test]
    public void C1153_Owner_sentences_match_the_protocol()
    {
        var runtime = Read("docs/session-runtime-invariants.md");
        var ops = Read("docs/ops-http.md");
        var api = Read("docs/antiphon-api.md");
        var credentials = Read("docs/agent-credentials.md");

        // 1, 2 and 5 plus the S4 rules: the runtime owner, each sentence followed by its pins.
        Pinned(runtime, CertificateSentence,
            RunnerPin("RunnerAbsenceEvidenceContractTests", "C1153_Real_unknown_transcript_has_a_separate_certificate"),
            Pin(nameof(SessionRunnerAbsenceEvidenceClientTests),
                nameof(SessionRunnerAbsenceEvidenceClientTests.C1153_Rejects_noncertificate_wire_shapes)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_certificate_holds_original_input)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_bad_evidence_keeps_failure)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Final_certificate_is_revalidated_under_lock)));
        Pinned(runtime, PrepareSentence,
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Only_new_cold_dispatch_prepares_evidence)));
        Pinned(runtime, RecheckSentence,
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Final_certificate_is_revalidated_under_lock)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Certificate_age_is_monotonic)));
        Pinned(runtime, ClosureSentence,
            RunnerPin("RunnerAbsenceEvidenceTests", "C1153_Restart_or_store_change_never_renews_proof"),
            RunnerPin("RunnerAbsenceEvidenceRuntimeTests", "C1153_Creation_consumes_proof_before_effects"),
            RunnerPin("RunnerAbsenceEvidenceRuntimeTests", "C1153_Certificate_and_launch_race_is_serialized"),
            RunnerPin("RunnerAbsenceEvidenceRuntimeTests", "C1153_Closed_identity_refuses_delayed_creation"));
        Pinned(runtime, NoDeadlineSentence,
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_certificate_holds_original_input)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Working_or_unknown_inventory_withholds)),
            Pin(nameof(BootStallWorkingTickCharacterizationTests),
                nameof(BootStallWorkingTickCharacterizationTests.Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing)));
        Pinned(runtime, ActivationSentence,
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Only_new_cold_dispatch_prepares_evidence)),
            Pin(nameof(DelegationDispatchRecoveryBoundaryTests),
                nameof(DelegationDispatchRecoveryBoundaryTests.C1153_Real_client_bad_evidence_keeps_failure)));
        runtime.ShouldNotContain(PlanBootStallRetrySentence, Case.Sensitive, "runtime: CARD-1151 retired the boot retry");

        // The runtime owner's numbers are the code's.
        RunnerAbsenceEvidenceValidator.Lifetime.ShouldBe(TimeSpan.FromSeconds(5), "certificate life");
        var freshness = $"within {RunnerAbsenceEvidence.RequestFreshness.TotalSeconds:0} s of the runner's clock";
        runtime.ShouldContain(freshness, Case.Sensitive, "runtime: request freshness window");
        runtime.ShouldContain($"`{RunnerAbsenceEvidence.ClosedIdentityProblemType}`", Case.Sensitive, "runtime: closed type");
        runtime.ShouldContain($"`{RunnerAbsenceEvidence.PhoneHomeClosedIdentityErrorType}`", Case.Sensitive, "runtime: phone-home closed type");
        ActivationSentence.ShouldContain($"`{RunnerAbsenceEvidence.Feature}`", Case.Sensitive);

        // 3: both HTTP/API owners; the sentence's routes are the production routes.
        RoutesSentence.ShouldContain($"POST {Template(AbsenceEvidenceRoutes.PrepareRoute)} and", Case.Sensitive);
        RoutesSentence.ShouldContain($"POST {Template(AbsenceEvidenceRoutes.CertifyRoute)} are", Case.Sensitive);
        ops.ShouldContain(RoutesSentence, Case.Sensitive, "ops-http: routes sentence");
        ops.ShouldContain($"`{RunnerAbsenceEvidence.Feature}`", Case.Sensitive, "ops-http: activation check");
        Pinned(api, RoutesSentence,
            RunnerPin("RunnerAbsenceEvidenceContractTests", "C1153_Real_unknown_transcript_has_a_separate_certificate"),
            RunnerPin("RunnerAbsenceEvidenceContractTests", "C1153_Http_authentication_covers_request_and_response"));

        // The internal runner route map lists both POST routes; nothing names them under /api.
        var routeMap = Section(api, "## 4. The session-runner's own API (internal)", "## 5. ");
        var block = Section(routeMap, "```\n", "\n```");
        block.ShouldContain($"\nPOST {Template(AbsenceEvidenceRoutes.PrepareRoute)} ", Case.Sensitive, "route map: prepare");
        block.ShouldContain($"\nPOST {Template(AbsenceEvidenceRoutes.CertifyRoute)} ", Case.Sensitive, "route map: certify");
        routeMap.ShouldContain($"`{RunnerAbsenceEvidence.ClosedIdentityProblemType}`", Case.Sensitive);
        routeMap.ShouldContain($"`{RunnerAbsenceEvidence.PhoneHomeClosedIdentityErrorType}`", Case.Sensitive);
        routeMap.ShouldContain(
            $"`{nameof(PhoneHomeOperation.PrepareAbsenceEvidence)}` ({(int)PhoneHomeOperation.PrepareAbsenceEvidence})",
            Case.Sensitive, "route map: phone-home prepare operation");
        routeMap.ShouldContain(
            $"`{nameof(PhoneHomeOperation.CertifyAbsence)}` ({(int)PhoneHomeOperation.CertifyAbsence})",
            Case.Sensitive, "route map: phone-home certify operation");
        foreach (var (owner, text) in new[] { ("runtime", runtime), ("ops-http", ops), ("api", api), ("credentials", credentials) })
            text.ShouldNotContain("/api/sessions/{id}/absence-evidence", Case.Sensitive, $"{owner}: no /api absence route");

        // 4: the credential owner, with the real setting on both sides and no literal key or path.
        Pinned(credentials, CredentialSentence,
            RunnerPin("RunnerAbsenceEvidenceContractTests", "C1153_Http_authentication_covers_request_and_response"),
            RunnerPin("RunnerAbsenceEvidencePhoneHomeTests", "C1153_Authenticated_operation_preserves_binding"),
            Pin(nameof(SessionRunnerAbsenceEvidenceClientTests),
                nameof(SessionRunnerAbsenceEvidenceClientTests.C1153_Rejects_noncertificate_wire_shapes)),
            Pin(nameof(SessionRunnerAbsenceEvidenceClientTests),
                nameof(SessionRunnerAbsenceEvidenceClientTests.C1153_Freshness_and_cancellation_are_bounded)));
        var custody = Section(credentials, "### Runner absence-evidence key (CARD-1153)", "\n### ");
        var serverKey = $"SessionRunner:{nameof(ServerSettings.AbsenceEvidence)}:{nameof(Antiphon.Server.Application.Settings.SessionRunnerAbsenceEvidenceSettings.KeyPath)}";
        var runnerKey = $"SessionRunner:{nameof(RunnerSettings.AbsenceEvidence)}:{nameof(RunnerAbsenceEvidenceSettings.KeyPath)}";
        runnerKey.ShouldBe(serverKey, "one setting name on both sides");
        custody.ShouldContain($"`{serverKey}`", Case.Sensitive, "credentials: key setting");
        custody.ShouldContain($"at least {AbsenceEvidenceAuthentication.MinimumKeyBytes} random bytes", Case.Sensitive);
        Regex.IsMatch(custody, @"[A-Za-z]:\\|~/|/home/|/etc/|/var/|/root/|\.antiphon|\\\\")
            .ShouldBeFalse("credentials: no literal key or fleet path");
        Regex.IsMatch(custody, @"[A-Za-z0-9+/]{43,}={0,2}").ShouldBeFalse("credentials: no literal key material");
        runtime.ShouldContain("agent-credentials.md#runner-absence-evidence-key-card-1153", Case.Sensitive);
        ops.ShouldContain("agent-credentials.md#runner-absence-evidence-key-card-1153", Case.Sensitive);
    }

    private static string Pin(string type, string method) => $"`{type}.{method}`";

    /// <summary>A pin into the runner test assembly, which this project cannot <c>nameof</c>.</summary>
    private static string RunnerPin(string type, string method)
    {
        var source = Read($"{RunnerTests}/{type}.cs");
        Regex.IsMatch(source, $@"public (async )?Task {Regex.Escape(method)}\(")
            .ShouldBeTrue($"runner pin {type}.{method} names no test method");
        return Pin(type, method);
    }

    private static string Template(string route) => route.Replace(":guid}", "}", StringComparison.Ordinal);

    /// <summary>The sentence verbatim, then each pin before the next owner sentence, bullet or paragraph.</summary>
    private static void Pinned(string text, string sentence, params string[] pins)
    {
        var at = text.IndexOf(sentence, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"owner sentence missing: {sentence}");
        var after = text[(at + sentence.Length)..];
        var end = new[] { after.IndexOf("\n- ", StringComparison.Ordinal), after.IndexOf("\n\n", StringComparison.Ordinal) }
            .Concat(OwnerSentences.Select(s => after.IndexOf(s, StringComparison.Ordinal)))
            .Where(i => i >= 0)
            .DefaultIfEmpty(after.Length)
            .Min();
        var tail = after[..end];
        foreach (var pin in pins)
            tail.ShouldContain(pin, Case.Sensitive, $"pin {pin} must follow: {sentence}");
    }

    private static string Section(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        from.ShouldBeGreaterThanOrEqualTo(0, $"section start missing: {start}");
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        to.ShouldBeGreaterThan(from, $"section end missing: {end}");
        return text[from..to];
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), relative)).Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Antiphon.sln")) || File.Exists(Path.Combine(dir, "docs", "orchestration-loop.md")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }
}
