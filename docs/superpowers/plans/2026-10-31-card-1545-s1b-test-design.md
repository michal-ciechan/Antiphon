# CARD-1545 S1b admission and outcomes: test design

Scope: S1b-admission and S1b-outcomes only, against
`f971a87c673d34083a1df395405410419b22102c`. This companion appends verification
detail to the [landed fix plan](2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md);
it does not amend that plan or commission S1a/S1c. CP-2 and CP-16 below refine only
those two rows, preserving their four and six TUnit methods respectively.

Read both `.antiphon/task-a62610b7.md` and `.antiphon/task-917262f2.md`.
The latter's section (4) explicitly permits these two portable slices despite its
S1c defects. D-3/D-4/D-5/D-6/D-9/D-11 remain PROVISIONAL. Fixtures use opaque profile,
executable and account identities; they choose no credential host, model, reuse
policy, issuer or activation policy. All future results from these slices are
diagnostic, with no Full or release credit.

## Verification design

### Inspection

- Landed plan: Execution/credentials/lock/outcomes, typed setup table, implementation
  slices, performance budget and CP-2/CP-16; both review reports in full for the
  affected defects/advisories. Boundaries: wrapper admission and classifier only.
- `tests/Antiphon.Tests/Scripts/ReleaseGateRunTests.cs` and
  `scripts/lib/release-gate.ps1` native-lock acquisition/release bodies: real scratch
  lock ownership, refusal and continuation identity are the closest admission fixture.
- `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` and
  `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`: offline slot/command seams,
  assembly process limiter, exact named PASS inventory and exit assertions are the
  nearest pattern for the two new test classes. A zero assertion count is not success.
- `docs/testing-and-build.md` checkpoint runner/manifest, build-slot, mutation,
  asynchronous-delivery sections; `docs/test-performance-standards.md`; orchestration
  D-2 advisory policy. No test execution is authorized by this documentation task.

### Delivery inventory

These slices add no message queue, recipient session input or asynchronous user
notification. They orchestrate a local owned checkpoint child and classify its
terminal evidence. The post-run transcript input is a synthetic classifier fixture,
not delivery evidence. Real queue busy/eligible recipient and crash/enqueue recovery
tests belong to a changed delivery path; none is changed here. S1c must independently
prove its complete matching UserPrompt and bridge/turn oracles. This document cannot
credit those paths from a request, ack, event, queue insert or child exit.

Portable substitutes do not prove Windows custody, paid caller wiring, provider
availability or actual prompt receipt.

| Producer -> destination | Persistence and durable identity | Recovery and observable receipt | Witness |
|---|---|---|---|
| Wrapper admission -> checkpoint adapter | Owned per-run context and manifest; run/attempt, lane, candidate SHA, exact expanded UID roster, opaque executable/profile, native-owner PID/start/run | Refuse before start if context cannot be written; no resume from a bare manifest. Child adapter records the complete admitted tuple and returns a distinct run ID. Missing/foreign tuple is not admission. | V-2, V-4 context-write/launch-failure cases |
| Checkpoint adapter -> wrapper waiter | Checkpoint run ID plus wrapper run/attempt/source; terminal result and artifacts | `75` remains running. Wait that same owned ID to terminal; on cancellation request stop and join. Failure after start but before result retention stays incomplete; no second run. | V-4, V-5 |
| Terminal observations -> classifier -> diagnostic receipt | Same run/attempt/source/UID plus session/request or probe correlation; original child exit and typed observations | Missing/corrupt/foreign evidence cannot yield green. Write failure prevents completed green; retain prior failure facts. A successfully written receipt proves only classification, not session delivery. | V-5 through V-9 |

Crash cases are injected exceptions at context write, child start, first wait, terminal
read and diagnostic receipt write. They exercise the real wrapper's `finally` path
with an owned fake lifecycle. They do **not** prove host-death process custody or
durable restart recovery. S1b adds no replay service: a dead-owner context is refused;
it does not enqueue a replacement or automatically resubmit a prompt.

### Fixtures and binding conventions

Read additionally before specifying these cases:

- `scripts/test-release-gate.ps1`: `Test-C599_SharedLock`,
  `Test-C599_Continuation`; `scripts/test-build-slot.ps1`: setup, launch and
  wait helpers; both `scripts/fixtures/c589-{slot,command}-shim.ps1` bodies.
- `scripts/lib/c487-harness.ps1`: named assertion, owned-root and seam helpers;
  `ScriptHarnessProcess.cs`: options and owned-process lifecycle;
  `TestHelpers/ProcessSpawnLimit.cs` (assembly-local limit one).
- `scripts/lib/nightly-owned-process.ps1`: real `New-NightlyChildStartInfo` and
  Windows-only owner factory; `nightly-common.ps1`'s `Start-NightlyProcess`.
  The latter removes empty values: it is unsuitable for this new environment path.
- `ManifestLoader.LoadYaml`, `ManifestValidator.Validate`, `CheckpointManifest`
  and `RowTimeout.DeriveRowMinutes`; the loader ignores unknown YAML fields.
- `nightly-policy.ps1` policy load/hash and `nightly-coverage.ps1`
  discovery exclusion/disposition/census bodies; `NightlyDiscoveryParserTests.cs`
  and its nearest golden `scripts/fixtures/nightly/c487-probe/discovery.diag`.
- `Antiphon.Tests.csproj` references/shared inputs and C680's actual environment
  construction. No changes to these inspected helpers or existing tests are planned.

All ten new methods are parameterless `[Test]` methods in namespace
`Antiphon.Tests.Scripts`, project `tests/Antiphon.Tests` (`net9.0`). Both classes
use `[Category("Integration")]` and `[ParallelLimiter<ProcessSpawnLimit>]`, matching
the nearest script fixtures. App ownership is script/release orchestration; runtime
script inputs are explicitly included here, not inferred from ProjectReference.
They start owned `pwsh` harnesses only; no CLI provider, native backend, HTTP broker,
DB, AppHost, Docker or network call. Fail a forbidden fake call immediately. The
ordinary outer checkpoint still obtains the host build slot as required.

Use `ScriptHarness.RunHarnessCaseAsync` with prefix `C1545`, script
`test-live-lane-run.ps1` or `test-live-lane-outcome.ps1`, and case
`C1545_<ExactMethodName>`. Internal variants below are not extra TUnit executions.
Give each assertion the exact name `C1545 <MethodName>/<case-id>`; require that
name, exact PASS count and harness exit 0. Code binds the complete name inventory
and count in each C# method; no wildcard PASS matching or count copied from minutes.
Fixtures stay within the two harness files unless a reusable fixture is necessary;
an added helper must be declared in the slice footprint and inspected by Review.

Harnesses call the **production** `run-live-lane.ps1`/`live-lane-run.ps1` path and
`live-lane-outcome.ps1` classifier. They replace only candidate reads, checkpoint
start/wait/stop, monotonic clock, provider probe, slot transport and owned process
operations. Do not replace the verdict function, environment builder, context
validator, native-lock implementation, or manifest parser with expected answers.
Use real atomic native locks in private scratch coordination roots, the real
`ProcessStartInfo.Environment` produced by `New-NightlyChildStartInfo`, and the
real `ManifestLoader.LoadYaml` called directly from the C# test. No checkpoint
executor is started by the direct-manifest negative case.

Use one inert fixture schema throughout: lane `fixture-live`, run `run-A`, attempt 1,
candidate SHA `a` repeated 40, row `row-A`, expanded UID `fixture.A.0`, account
`account-A`, endpoint `endpoint-A`, dependency `provider-A`, session `session-A`,
request `request-A`. `fixture.B.0` supplies an independent row. These are synthetic
identities, not the S1a six-UID inventory. A valid control has exact roster equality,
one pass per UID, zero failed/skipped, stable observations, terminal child exit 0,
complete evidence and successful cleanup. The typed transcript fixture contains a
unique **whole** multiline UserPrompt, its session/request, an ordered typed API
error and completion metadata. No secret or real user text is used or emitted.

The adapter trace records entry, all boundary calls, identities and return facts.
Every negative case first asserts its setup/entry marker and healthy companion,
then changes only the named field or handoff. This prevents a missing executable,
disabled feature, empty sink or earlier refusal from masquerading as the intended
negative. Refusal tests also prove an admissible companion reaches the fake child.
Never make production admission depend on a test environment opt-in alone.

S1b remains diagnostic/fake-only until S1c is complete. The checked-in native YAML
is wrapper input, **not an executable generic checkpoint manifest**: require a
format which the real validator rejects before any driver (an unknown extra field
alone does not achieve this). The wrapper may materialize a fixture-only manifest
under its private root after admission. Test this format mechanically; do not claim
that S1b authenticates a malicious same-user writer or protects existing manual
canaries. If the implementation cannot make this boundary observable without
changing S1c/tools, return to Plan for that seam instead of widening this slice.

### Proves it works now

These are planned witnesses, not claims of execution. Method abbreviations `Runner`
and `Outcome` below mean `LiveLaneRunnerTests` and `LiveLaneOutcomeTests`.
The exact method filter is always
`/*/Antiphon.Tests.Scripts/<Class>/<Method>`; class filters appear only in ordinary
CP-2/CP-16. Scripts are runtime inputs, so mutating their actual decision branch
must affect the same method that Code runs.

| ID | Behaviour/layer | Exact class.method / script case | Variants, decisive oracle and reachability |
|---|---|---|---|
| V-1 | Admission ordering, script integration | Runner.LockBeforeSlot / C1545_LockBeforeSlot | `ordered`: real lock acquired before any slot/start. `busy`: foreign live owner, exit 21, zero slot/start calls, unchanged owner. `slot-timeout`: own lock then bounded denied slot, exit 4, zero starts and own lock released. `commission`, `budget`: omit commission or exhaust supplied fake budget, exit 20, zero starts. All source/context inputs otherwise valid. |
| V-2 | Wrapper-only paid manifest and context, parser + script integration | Runner.WrapperOnlyPaidManifest / C1545_WrapperOnlyPaidManifest | `direct-manifest`: real loader rejects committed native spec; paid command count zero. `context-missing`: exit 20/lane-context-required even with headed opt-in. `context-lane`, `context-uid`, `context-source`, `context-executable`, `context-profile`: alter one bound field with the rest valid; exit 2/context-mismatch and zero starts. `context-owner`: release/replace lock after context creation; exit 2/context-stale, zero starts. `native-disabled`: attempt nonfake mode with a syntactically valid fake commission, exit 20/native-unqualified. `full-inputs`: pinned preservation procedure below. `valid-context` launches one inert child with exact tuple. |
| V-3 | Actual child environment, script integration | Runner.ChildEnvironmentIsScoped / C1545_ChildEnvironmentIsScoped | `allowlist`: hostile inherited provider/task values removed, approved opaque path/context propagated only to selected child. `empty`: each of CLAUDE_CODE_CHILD_SESSION, CLAUDE_CODE_SESSION_ID, CLAUDE_CODE_BRIDGE_SESSION_ID and CLAUDECODE tested absent, present-empty and valued in the real StartInfo map; presence and exact value both asserted. `parent`: parent env snapshot unchanged on success/refusal/throw. Pre-create these keys explicitly; never use GetEnvironmentVariable alone to distinguish absent/empty. |
| V-4 | Owned lifetime and handoff recovery, script integration with fake owner | Runner.DeadlineJoinsOwnedChildren / C1545_DeadlineJoinsOwnedChildren | `wait75`: start once, wait sequence 75/75/terminal for the same run, no early receipt. `deadline`: virtual deadline cancels, stop then join each admitted fake child before release; foreign owner untouched. `cleanup-unknown`: failed/unknown join gives 20/cleanup-incomplete and no clean lock release. `context-write`, `launch-failure`, `wait-failure`, `terminal-read`, `receipt-write`: fault at named handoff, zero duplicate start/resubmit, owned cleanup and no green receipt. No real kill or OS-custody credit. |
| V-5 | Terminal wrapper outcomes, script integration | Outcome.ProviderOutcomeIsDistinct / C1545_ProviderOutcomeIsDistinct | `green`: complete fixture produces exit 0/GREEN and diagnostic-only receipt. `raw-child`: checkpoint 2/3/8 and terminal failure retained verbatim while invalid structured facts classify 2; 75 alone is nonterminal. `evidence`: missing/unreadable/incomplete terminal artifacts prevent green; malformed identity/schema is 2, absent evidence is 20. `drift`: unstable input/executable observation is 20. `pre-retry`: at most one pre-submission availability retry within 30 s, total preflight 60 s. `quota-retry`: zero retry on quota. `post-submit`: zero automatic model reruns after receipt. |
| V-6 | Post-preflight outage classification, production classifier through wrapper | Outcome.MidRunOutageAfterReceiptIsInconclusive / C1545_MidRunOutageAfterReceiptIsInconclusive | Healthy preflight, affected failed row; `transcript-positive`: complete matching UserPrompt then typed overload/service-unavailable/rate-limit gives 20/provider-outage. `prompt-partial`, `session-foreign`, `request-foreign`, `error-before-prompt`, `untyped-error`: one missing correlation fact, no alternate probe proof, same contract assertion stays 1. `probe-positive`: no transcript proof but bounded typed outage from same account/endpoint/dependency gives 20; `account-foreign`, `endpoint-foreign`, `dependency-foreign` stay 1. `post-probe-bound`, `post-probe-count`, `post-probe-no-prompt`: one <=15 s probe, no prompt; virtual timeout alone stays 1. |
| V-7 | Red and invalid precedence, production aggregate classifier | Outcome.HealthyProviderAssertionStaysRed / C1545_HealthyProviderAssertionStaysRed | `healthy-red`: deterministic Enter-lost/bridge-dropped assertion plus healthy probe stays 1. `unproven-red`: missing proof, probe timeout, generic internet failure, screenshot/string or model refusal alone stays 1. `mixed-red`: independent healthy row-A red plus row-B evidenced outage stays 1; both input orders. `mixed-invalid`: parser/source/build/roster/authority invalidity plus red/outage stays 2; both orders. Preserve every row observation; never relabel the unrelated row. |
| V-8 | Closed typed setup mapping, production classifier | Outcome.TypedSetupOutcomes / C1545_TypedSetupOutcomes | `external-setup`: AuthMissing/AuthExpired -> 20/auth-required; QuotaExceeded -> 20/quota-exhausted; DependencyUnavailable plus typed proof -> 20/provider-outage; StartupDefaultAbsent -> provisional 20/premise-absent. `unclassified-setup`: ReadyTimeout/TrustNotCleared/BridgeNotEstablished without independent proof -> 20/setup-unclassified. `setup-contract`: deterministic healthy SetupContractViolation -> 1; lack of healthy evidence -> 20/setup-unclassified. `missing-prerequisite`: missing binary/profile/context -> 20/executable-missing, profile-required, lane-context-required. `unknown-skip`: arbitrary/legacy/unknown enum -> 20/unclassified-skip. All external/missing prerequisites admit zero prompts. |
| V-9 | Complete nonzero exact roster, wrapper + classifier | Outcome.SkipAndZeroNeverGreen / C1545_SkipAndZeroNeverGreen | `exact-roster`: exact two-UID green companion, then zero, missing, duplicate, unexpected UID and same counts/wrong UID -> 2/roster-incomplete. `skipped`: one pass plus one known/unknown skip -> 20, never 0 (typed setup refusal wins over the zero caused by that refusal). `invalid-schema`: malformed manifest/build/OS/UID/receipt facts -> 2, never 20 outage. Reject summary-only passes without matching expanded outcomes. |
| V-10 | Stale native owner, real scratch lock + wrapper | Outcome.StaleSharedLockRefuses / C1545_StaleSharedLockRefuses | `stale`: seed dead-owner native lock with old acquiredAt, actual lock reader returns stale-shared-lock, wrapper returns 2; bytes unchanged, zero slot/start. A fresh live-owner companion returns 21; changing age never grants ownership. |

### Current-base red contract

At the pinned base neither new class, harness nor live-lane implementation exists.
There is **no existing behavioural red measurement** to claim. Each named method's
first current-base failure is its explicit missing-production-surface/entry assertion;
an import exception, compile error or zero-test selection is not an accepted PC red.
Code commits the ten runnable test methods before their fixes, then a minimal
callable, fail-closed surface if needed. Its positive controls above (`ordered`,
`valid-context`, `allowlist`, terminal `wait75`, `green`, `transcript-positive`,
`healthy-red`, each typed setup, exact roster, stale lock) must then fail at their
**value/trace** assertions until implemented. Negative cases that already pass on a
fail-closed shell remain unproven until their assigned post-implementation PC goes
red. Do not invent a legacy outage result or count missing files as proof of safety.

| Method | Expected current-base observable failure after test introduction | Compiling post-implementation defect |
|---|---|---|
| LockBeforeSlot | No real admission trace/typed 21/4 result | PC-1, PC-2, PC-4..6 |
| WrapperOnlyPaidManifest | No valid materialized context/diagnostic adapter receipt | PC-7..15, PC-22 |
| ChildEnvironmentIsScoped | No production StartInfo with exact absent/empty/value map | PC-16..18 |
| DeadlineJoinsOwnedChildren | No owned wait/stop/join trace or complete terminal result | PC-19..21, PC-49 |
| ProviderOutcomeIsDistinct | No terminal GREEN/typed distinct outcomes/raw child record | PC-23..24, PC-42..44, PC-48 |
| MidRunOutageAfterReceiptIsInconclusive | No correlated post-preflight 20 result | PC-29..37, PC-45..47 |
| HealthyProviderAssertionStaysRed | No independent 1/2 precedence verdict | PC-38..41 |
| TypedSetupOutcomes | No closed typed 20/1 mapping | PC-27..28, PC-50 |
| SkipAndZeroNeverGreen | No exact-roster verdict and required-UID equality | PC-25..26, PC-51 |
| StaleSharedLockRefuses | No wrapper stale-lock 2 refusal with unchanged lock | PC-3 |

### Guards the regression

- R-1: lock ownership, order, budget and wrapper-only admission cannot start a
  second/unauthorized child; V-1/V-2/V-10 assert exit, exact call counts and lock bytes.
- R-2: empty markers remain empty and parent environment is isolated; V-3 asserts
  the real StartInfo map's membership/value, including absent inputs.
- R-3: running checkpoint or incomplete cleanup/evidence cannot appear terminal
  green; V-4/V-5 assert same owned run ID, joined lifecycle and complete receipt.
- R-4: original R-D-6, preflight-then-outage is 20, genuine contract red is 1;
  V-6/V-7/V-8 assert typed evidence, correlations and independent-red precedence.
- R-5: raw child status, zero/skips/roster/schema faults cannot be laundered into
  green or provider outages; V-5/V-9 assert raw values and expanded UID equality.
- R-6: no Full gate, prior census obligation or master/Interim state changes in
  S1b; V-2 `full-inputs` compares pinned Git objects and actual candidate inputs,
  and wrapper scenarios compare seeded legacy state bytes before/after.
- R-7: no probe retry becomes a paid turn or indefinite wait; V-5/V-6 assert
  exact probe/submit/start counts and elapsed virtual deadlines.

### Full-census-unchanged guarantee for this slice

This is a narrower proof than S1a's compiled roster census. S1b adds exactly the ten
ordinary test methods above; it does not tag/move existing canaries, change category
metadata or modify policy/gate/readers. The old Full census stays fixed and these ten
methods are visible additions, never removed to force equal overall counts.

In `WrapperOnlyPaidManifest/full-inputs`, pin the pre-S1b commission base **B** in
the fixture metadata (initial B is the 40-hex SHA above). Read B's Git objects,
not a second copy of working-tree policy. Compare candidate bytes for the existing
policy, Full selection/coverage and release gate producers/consumers, build and
project definitions, existing test sources and discovery/category helpers. Derive
the complete protected set from B's tree; the only executable additions allowed
are the S1b files listed in the fix plan and these two new test classes. Plan/report
Markdown is excluded explicitly. New fixtures, if necessary, are enumerated rather
than allowing `scripts/**` or `tests/**` wholesale. No S1a changes are authorized here.

Run the real pinned/candidate nightly policy/coverage readers in separate script
scopes on the same frozen discovery fixture, comparing canonical policyHash and
exact required/excluded UID sets, not counts. Include C680, a required ordinary
UID and an explicitly excluded live UID as semantic sentinels; these synthetic
sentinels supplement, and do not replace, the all-existing-input byte comparison.
Add a scratch candidate policy with a C680 exclusion **and its correctly recomputed
policyHash**: preservation must reject it. A second candidate reader edit that
excludes C680 with unchanged policy is also rejected by the protected-input compare.
This cannot self-compare away an actual candidate change (917262f2 D-2).

Before/after each wrapper scenario, compare seeded master `last-run.json`,
`last-complete-green.json`, `last-monitor.json`, `interim-qualification-receipt.json`
and rc gate/authority state byte-for-byte. Only the private diagnostic root changes.
If S1a has landed before commission, B is that reviewed pre-S1b tip, explicitly
recorded once; never repin B to an implementation tip to make preservation pass.

### Review-item coverage and limits

| Report item | Named witness or exclusion |
|---|---|
| a62610b7 D-6 (plan R-D-6) | MidRunOutageAfterReceiptIsInconclusive, HealthyProviderAssertionStaysRed, TypedSetupOutcomes; PC-27..41, PC-50 |
| a62610b7 A-1 | StaleSharedLockRefuses; PC-3 |
| a62610b7 A-2 | WrapperOnlyPaidManifest/direct-manifest, context variants and native-disabled; PC-7..15. Real canary revalidation remains S1c. |
| a62610b7 A-3 and 917262f2 D-2 | WrapperOnlyPaidManifest/full-inputs; PC-22. Pinned before tree, real candidate comparison. Does not claim to complete S1a. |
| a62610b7 A-4 | Separate 45-60 min admission and 30-60 min outcome authoring commissions below. |
| a62610b7 A-6 | ChildEnvironmentIsScoped/allowlist, empty, parent; PC-16..18 |
| a62610b7 A-7 | DeadlineJoinsOwnedChildren and no post-submission retry; PC-19..21, PC-44. No operator-input release deadline. |
| a62610b7 A-8/A-9 | Portable-only timing/batch below; declare runtime scripts; no per-project reuse credit. Native CP-4 estimate is untouched. |
| 917262f2 N-1 | WrapperOnlyPaidManifest/context-missing and missing executable field refuse even with ANTIPHON_HEADED_TESTS. Caller-side lane-mode selection is S1c, uncredited here. |
| 917262f2 D-1/D-3/D-4 | S1c compile/wiring, real row sanitizer and argv/settings attestation remain outside this slice, per review section (4). D-3's optional wrapper scan is not selected here: final S1c design owns that choice. |
| a62610b7 D-1..5/D-7, A-5; 917262f2 model advisory/N-2 | Native premise, transcript/bridge delivery, executable/profile/cwd, sanitizer and Explicit execution belong to S1c/S1a; no design or closure claimed here. Carried-report hygiene is documentation only. |

### Exit-code table

These are wrapper exits. The test harness exits 0 only when it asserts the expected
wrapper exit and evidence; it must not propagate an expected wrapper 20 as a failed
TUnit prerequisite. Checkpoint driver exit codes remain a separate namespace.

| Wrapper exit | Classification | Required example/evidence |
|---|---|---|
| 0 | GREEN, diagnostic only | Nonzero exact expanded roster, all passed, no skips, stable inputs, evidence and cleanup complete |
| 1 | RED | Healthy deterministic contract/setup violation; assertion without qualifying outage proof; independent red survives another row's outage |
| 2 | INVALID | Malformed/wrong schema/source/UID/build/authority, roster incomplete, stale-shared-lock; never a provider failure |
| 4 | SLOT-TIMEOUT | Bounded host slot admission failed; zero build/test starts; no unleased retry |
| 20 | INCONCLUSIVE/BLOCKED, **not code failure** | Typed provider outage, auth-required, quota-exhausted, missing executable/profile/context, unknown setup/skip, provisional premise-absent, input drift, incomplete evidence/cleanup, native-unqualified |
| 21 | DEFERRED-BUSY | Foreign live native owner; no slot/start and no takeover |

`75` is only a checkpoint still-running observation and cannot be a final lane exit.
Keep raw child 2/3/8/75 and all typed reasons in the diagnostic record. Precedence:
invalid > independent red > inconclusive > green. For the affected failed row alone,
qualified outage overrides that row's assertion to 20. A healthy probe, unknown
probe result or timeout cannot invent outage. Typed setup refusal causing zero
executions is 20; unexplained zero/missing roster is 2.
