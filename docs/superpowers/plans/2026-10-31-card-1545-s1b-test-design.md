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

### Guard inventory

Plan references: **A** = Execution/admission and S1b-admission; **O** = Post-run
classifier/setup table and S1b-outcomes; **F** = S1 Full preservation. All guards
below are safety-critical because bypass can admit an unqualified launch, release
custody early, misreport a failure or change an existing qualification obligation.
Independently bypassable correlation checks have separate controls. No guard is
declared unnecessary. Native implementation guards beyond this scope remain S1c
obligations, explicitly excluded below.

| Guard | Plan reference and invariant | Distinct positive control |
|---|---|---|
| G-1 | A: native lock precedes slot request | PC-1 |
| G-2 | A: busy live owner returns 21 without takeover/start | PC-2 |
| G-3 | A/O: stale owner returns 2 without age-based takeover | PC-3 |
| G-4 | A: slot admission timeout returns 4 without unleased start | PC-4 |
| G-5 | A: missing commission does not admit a launch | PC-5 |
| G-6 | A: exhausted supplied budget does not admit a launch | PC-6 |
| G-7 | A: checked-in paid spec cannot execute as generic checkpoint YAML | PC-7 |
| G-8 | A: missing lane context refuses even with headed opt-in | PC-8 |
| G-9 | A: lane identity must match context | PC-9 |
| G-10 | A: expanded UID membership must match context | PC-10 |
| G-11 | A: source SHA must match context | PC-11 |
| G-12 | A: executable identity must match context | PC-12 |
| G-13 | A: profile identity must match context | PC-13 |
| G-14 | A: context's run/PID/start owner is still holding the same lock | PC-14 |
| G-15 | A: S1b cannot enable native/paid mode before S1c | PC-15 |
| G-16 | A: child environment is an allowlisted selected-child map | PC-16 |
| G-17 | A: absent, empty and valued environment entries remain distinct | PC-17 |
| G-18 | A: constructing/launching the child never changes the parent env | PC-18 |
| G-19 | A/O: checkpoint 75 requires waiting the same owned run | PC-19 |
| G-20 | A: deadline stops and joins owned children before release | PC-20 |
| G-21 | A/O: unproved cleanup prevents green and clean lock release | PC-21 |
| G-22 | F: candidate policy/readers/old discovery inputs equal pinned B | PC-22 |
| G-23 | O: original child exit/outcome is retained separately | PC-23 |
| G-24 | O: missing terminal evidence cannot yield green | PC-24 |
| G-25 | O: nonzero exact expanded roster, not summary counts | PC-25 |
| G-26 | O: any skip prevents green | PC-26 |
| G-27 | O: known external/setup premise facts use typed 20 subtypes | PC-27 |
| G-28 | O: independently demonstrated healthy setup violation remains red | PC-28 |
| G-29 | O: transcript outage attribution requires complete matching prompt | PC-29 |
| G-30 | O: transcript session must match affected row | PC-30 |
| G-31 | O: transcript request must match affected turn | PC-31 |
| G-32 | O: typed outage must follow that prompt | PC-32 |
| G-33 | O: transcript error must have an admitted typed error enum | PC-33 |
| G-34 | O: outage probe account matches affected account | PC-34 |
| G-35 | O: outage probe endpoint matches affected endpoint | PC-35 |
| G-36 | O: outage probe dependency matches affected dependency | PC-36 |
| G-37 | O: qualifying post-preflight outage is 20, never code red | PC-37 |
| G-38 | O: healthy-provider assertion stays 1 | PC-38 |
| G-39 | O: unknown/timeout/string evidence cannot infer outage | PC-39 |
| G-40 | O: independent row red outranks unrelated outage | PC-40 |
| G-41 | O: malformed/source/build/roster/authority invalidity outranks all | PC-41 |
| G-42 | O: at most one bounded pre-submission availability retry | PC-42 |
| G-43 | O: quota exhaustion has no retry | PC-43 |
| G-44 | O: UserPrompt receipt forbids automatic model resubmission | PC-44 |
| G-45 | O: post-failure probe is bounded to 15 seconds | PC-45 |
| G-46 | O: at most one post-failure probe | PC-46 |
| G-47 | O: post-failure probe submits zero model prompts | PC-47 |
| G-48 | O: changed input/executable observation bars green | PC-48 |
| G-49 | A/O: handoff exceptions cannot leave a completed green or retry run | PC-49 |
| G-50 | O: unknown/legacy setup skip stays 20/unclassified-skip | PC-50 |
| G-51 | O: malformed schema/build/OS/UID facts are invalid, not outage | PC-51 |
| G-52 | F: wrapper cannot write master/Interim/rc gate state | PC-52 |
| G-53 | O: ambiguous readiness/trust/bridge setup stays 20 | PC-53 |
| G-54 | O: missing executable/profile/context stays typed 20 | PC-54 |
| G-55 | A/O: refused setup submits no prompt | PC-55 |
| G-56 | A: context must be issued by this wrapper run, not copied/forged | PC-56 |
| G-57 | A: wrong candidate SHA or dirty candidate refuses before admission | PC-57 |
| G-58 | A: cleanup cannot release a foreign/replaced native lock | PC-58 |

### Positive controls

**All recipes: binding required after Code.** No payload currently exists. Every
row is classification `binding` / reason `binding-missing`, not mechanically
eligible. The binder at confirmed landed source must supply symbol, literal patch,
unique context/match count, file/preimage/mutant hashes, exact commands and failure
set per [mechanical PC contract](../../mechanical-pc-contract.md). If Code introduces
separate additional guards, split their PC rows before acceptance. No regex search,
line-number guess or mutation of the test oracle qualifies.

Each row below is a **recipe**, variant `v1` unless explicitly expanded; case and
oracle use the stable names in V-1..10. Apply a parseable PowerShell/data change to
the actual production decision (or the actual candidate protected input for PC-22),
never an exception/compile error. Tests compile unchanged. `Runner.M/case` means
the exact TUnit method `LiveLaneRunnerTests.M` and assertion
`C1545 M/case`; `Outcome` expands similarly. Every cycle uses only
`/*/Antiphon.Tests.Scripts/<Class>/<Method>`, `MinExecuted=1`, exactly one failed
method on mutant, zero skipped; baseline/restored run exactly one passed method.
Internal variant assertions remain separate evidence even though TUnit counts one.

Reachability common to all rows: valid source, admitted fixture mode, budget, real
owned lock, complete context and terminal control; no other error or automatic retry
unless that row names it. Exercise positive control before the negative variant,
record trace entry, then assert the specified result. Outage tests disable the
alternative proof route so it cannot mask the mutated route. Precedence tests
keep the independent row's evidence healthy/deterministic. Earlier failure cannot
stand in for the intended assertion. Rows whose negative fixture targets identity
still carry valid evidence for every other identity component.

| PC / guard | Compiling defect recipe / variant | Exact method and decisive assertion expected red | Reachability beyond common fixture |
|---|---|---|---|
| PC-1 / G-1 | Move slot acquire before native lock | Runner.LockBeforeSlot/ordered: first slot sequence must follow lock-acquired | Free lock and granted slot; successful child control |
| PC-2 / G-2 | On deferred-busy, continue admission instead of return 21 | Runner.LockBeforeSlot/busy: `(21, slotCalls=0, starts=0, ownerUnchanged=true)` | Real live foreign lock, all other admission valid |
| PC-3 / G-3 | Treat stale-shared-lock as acquired | Outcome.StaleSharedLockRefuses/stale: `(2, unchanged bytes, slotCalls=0, starts=0)` | Real dead-owner scratch record, no real process killed |
| PC-4 / G-4 | Convert slot timeout to unleased admission | Runner.LockBeforeSlot/slot-timeout: `(4, starts=0)` | Own lock, shim busy until virtual admission deadline |
| PC-5 / G-5 | Remove commission-presence check | Runner.LockBeforeSlot/commission: `(20, starts=0)` | Budget/context/source valid |
| PC-6 / G-6 | Bypass remaining-budget check | Runner.LockBeforeSlot/budget: `(20, starts=0)` | Present commission, fake budget exactly exhausted |
| PC-7 / G-7 | Turn checked-in wrapper spec into valid generic checkpoint YAML with inert command | Runner.WrapperOnlyPaidManifest/direct-manifest: real loader must refuse | Parser operates on candidate bytes, not a fixture copy; command never executed |
| PC-8 / G-8 | Synthesize context from headed opt-in when context absent | Runner.WrapperOnlyPaidManifest/context-missing: `(20, lane-context-required, starts=0)` | Headed key set, valid commission; no independent context-default refusal |
| PC-9 / G-9 | Omit lane equality comparison | Runner.WrapperOnlyPaidManifest/context-lane: `(2, context-mismatch, starts=0)` | Only lane differs |
| PC-10 / G-10 | Omit UID membership comparison | Runner.WrapperOnlyPaidManifest/context-uid: same tuple above | Only requested expanded UID differs |
| PC-11 / G-11 | Omit context/source equality | Runner.WrapperOnlyPaidManifest/context-source: same tuple above | Candidate checkout valid; only context SHA differs |
| PC-12 / G-12 | Omit executable equality | Runner.WrapperOnlyPaidManifest/context-executable: same tuple above | Both opaque executables exist in fake catalog |
| PC-13 / G-13 | Omit profile equality | Runner.WrapperOnlyPaidManifest/context-profile: same tuple above | Both opaque profiles exist in fake catalog |
| PC-14 / G-14 | Trust context's saved owner without rereading current lock | Runner.WrapperOnlyPaidManifest/context-owner: `(2, context-stale, starts=0)` | Barrier replaces/releases lock after valid context issuance |
| PC-15 / G-15 | Allow native mode from fake commission | Runner.WrapperOnlyPaidManifest/native-disabled: `(20, native-unqualified, starts=0)` | Fake native adapter only records a forbidden call; no provider executable |
| PC-16 / G-16 | Merge all inherited env entries into child | Runner.ChildEnvironmentIsScoped/allowlist: unapproved key must be absent | Seed hostile inert value and approved positive marker; inspect actual StartInfo |
| PC-17 / G-17 | Delete empty env entries instead of retaining empty strings | Runner.ChildEnvironmentIsScoped/empty: key-present and value-empty must both be true | Inspect membership before launch; absent and valued companions still asserted |
| PC-18 / G-18 | Write a child override into process-global env | Runner.ChildEnvironmentIsScoped/parent: parent snapshot must equal before | Seed distinct parent/child values; fixture finally restores after observing failure |
| PC-19 / G-19 | Treat first 75 as terminal success | Runner.DeadlineJoinsOwnedChildren/wait75: waits=3, starts=1, terminal receipt only after final wait | Sequence 75/75/0, all evidence healthy at terminal only |
| PC-20 / G-20 | Remove join before release after stop | Runner.DeadlineJoinsOwnedChildren/deadline: every owned join precedes release | Fake child acknowledges stop but remains alive until join |
| PC-21 / G-21 | Mark unknown/failed join as clean | Runner.DeadlineJoinsOwnedChildren/cleanup-unknown: `(20, cleanRelease=false)` | Terminal rows otherwise all pass; sole failure is join |
| PC-22 / G-22 | In candidate scratch policy add C680 exclusion and recompute hash; second variant edit candidate coverage reader to exclude C680 | Runner.WrapperOnlyPaidManifest/full-inputs: pinned protected inputs and required/excluded sets unchanged | Before side remains pinned B. Both variants run individually; no mutation of comparator |
| PC-23 / G-23 | Store mapped lane exit in rawChildExit | Outcome.ProviderOutcomeIsDistinct/raw-child: raw child 3/8 preserved, lane 2 | Valid typed invalid terminal record separates the namespaces |
| PC-24 / G-24 | Permit green without terminal artifact completeness | Outcome.ProviderOutcomeIsDistinct/evidence: missing evidence gives 20 | Healthy exact roster; remove evidence only, not identity/schema |
| PC-25 / G-25 | Replace exact nonzero UID multiset equality by passing-count test | Outcome.SkipAndZeroNeverGreen/exact-roster: same counts/wrong UID gives 2 | Complete row records; no typed refusal that explains zero |
| PC-26 / G-26 | Ignore skipped count/rows when selecting green | Outcome.SkipAndZeroNeverGreen/skipped: known skip gives 20 | One passed plus one skipped expected UID; no missing roster |
| PC-27 / G-27 | Map a known external setup status to 1 instead of 20 | Outcome.TypedSetupOutcomes/external-setup: exact status/subtype table | Parameterized recipe variants: AuthMissing, AuthExpired, QuotaExceeded, DependencyUnavailable, StartupDefaultAbsent; each mutated separately; other branches unchanged |
| PC-28 / G-28 | Map healthy SetupContractViolation to inconclusive | Outcome.TypedSetupOutcomes/setup-contract: lane exit must be 1 | Deterministic healthy setup evidence present; no outage |
| PC-29 / G-29 | Accept a prompt fragment instead of complete body equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/prompt-partial: lane exit must be 1 | All session/request/type/order facts valid, alternative probe absent |
| PC-30 / G-30 | Drop session equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/session-foreign: exit 1 | Whole body/request/type/order match; only session differs |
| PC-31 / G-31 | Drop request equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/request-foreign: exit 1 | Whole body/session/type/order match; only request differs |
| PC-32 / G-32 | Ignore transcript error ordering | Outcome.MidRunOutageAfterReceiptIsInconclusive/error-before-prompt: exit 1 | Matching identities and typed error, order alone invalid |
| PC-33 / G-33 | Accept arbitrary error text as typed outage | Outcome.MidRunOutageAfterReceiptIsInconclusive/untyped-error: exit 1 | Complete matching prompt; error string contains overload wording but enum absent |
| PC-34 / G-34 | Drop probe account equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/account-foreign: exit 1 | Typed available-to-unavailable transition, endpoint/dependency match; no transcript proof |
| PC-35 / G-35 | Drop probe endpoint equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/endpoint-foreign: exit 1 | Account/dependency match, no transcript proof |
| PC-36 / G-36 | Drop probe dependency equality | Outcome.MidRunOutageAfterReceiptIsInconclusive/dependency-foreign: exit 1 | Account/endpoint match, no transcript proof |
| PC-37 / G-37 | Let preflight success force later failed row to RED | Outcome.MidRunOutageAfterReceiptIsInconclusive/transcript-positive: `(20, provider-outage)` | Preflight success and valid later correlated typed outage; no independent red |
| PC-38 / G-38 | Relabel healthy-provider assertion as outage | Outcome.HealthyProviderAssertionStaysRed/healthy-red: exit 1 | Healthy probe after failed deterministic assertion |
| PC-39 / G-39 | Treat probe timeout/unknown as outage | Outcome.HealthyProviderAssertionStaysRed/unproven-red: exit 1 | No transcript outage proof and no independent invalidity |
| PC-40 / G-40 | Return inconclusive if any row has outage before checking independent reds | Outcome.HealthyProviderAssertionStaysRed/mixed-red: exit 1 in both orders | Row-A healthy deterministic red, row-B proven outage |
| PC-41 / G-41 | Check red/outage before invalid aggregate | Outcome.HealthyProviderAssertionStaysRed/mixed-invalid: exit 2 in both orders | Invalid fact retained independently of failed/outage rows |
| PC-42 / G-42 | Permit a second pre-submission retry | Outcome.ProviderOutcomeIsDistinct/pre-retry: attempts<=2 and elapsed<=30 s retry allowance | Fake clock/probe demand third attempt, no actual sleeping |
| PC-43 / G-43 | Route quota to availability retry loop | Outcome.ProviderOutcomeIsDistinct/quota-retry: retries=0 | Typed quota fact before submission, budget otherwise valid |
| PC-44 / G-44 | Retry the model test after a UserPrompt | Outcome.ProviderOutcomeIsDistinct/post-submit: modelSubmissions=1, runStarts=1 | Whole prompt receipt then typed outage; retry adapter records forbidden attempt |
| PC-45 / G-45 | Extend post-failure probe deadline beyond 15 s | Outcome.MidRunOutageAfterReceiptIsInconclusive/post-probe-bound: elapsed<=15 s and exit 1 on timeout | Probe would return typed outage only after deadline; virtual clock |
| PC-46 / G-46 | Allow second post-failure probe | Outcome.MidRunOutageAfterReceiptIsInconclusive/post-probe-count: probes=1 | First probe unknown; second would succeed, making retry visible |
| PC-47 / G-47 | Have probe request include a model turn | Outcome.MidRunOutageAfterReceiptIsInconclusive/post-probe-no-prompt: probePromptCount=0 | Fake probe adapter captures request type and fields, not just result |
| PC-48 / G-48 | Ignore changed executable/input observation | Outcome.ProviderOutcomeIsDistinct/drift: exit 20 | Exact healthy roster/evidence; pre/post digest alone differs |
| PC-49 / G-49 | Catch handoff failure and publish completed green | Runner.DeadlineJoinsOwnedChildren/receipt-write: no completed green; starts<=1 | Terminal child already healthy, inject final evidence-write failure; earlier phases valid |
| PC-50 / G-50 | Default unknown setup enum to GREEN | Outcome.TypedSetupOutcomes/unknown-skip: `(20, unclassified-skip)` | Legacy skip has no typed facts; preserve safe UID/reason |
| PC-51 / G-51 | Reclassify invalid schema/build/OS/UID as outage | Outcome.SkipAndZeroNeverGreen/invalid-schema: exit 2 | Invalid terminal fact plus outage-looking text; no legitimate outage override |
| PC-52 / G-52 | Write diagnostic success into seeded master green state | Runner.WrapperOnlyPaidManifest/legacy-state: all seeded legacy state bytes unchanged | Valid diagnostic success; seeds exist and are nonempty before wrapper call |
| PC-53 / G-53 | Map an ambiguous setup timeout to RED | Outcome.TypedSetupOutcomes/unclassified-setup: `(20, setup-unclassified)` | Variants ReadyTimeout, TrustNotCleared, BridgeNotEstablished; no healthy contract proof |
| PC-54 / G-54 | Treat a missing required prerequisite as GREEN | Outcome.TypedSetupOutcomes/missing-prerequisite: exact 20 subtype | Variants executable, profile, context; other prerequisites present |
| PC-55 / G-55 | Submit after typed preflight refusal | Outcome.TypedSetupOutcomes/setup-no-prompt: promptCount=0 | Typed AuthMissing control, fake submit adapter records unexpected call |
| PC-56 / G-56 | Accept an unissued context merely because its fields match | Runner.WrapperOnlyPaidManifest/context-forged: `(2, context-unissued, starts=0)` | Copy full valid-looking tuple to unissued context, valid live lock; issuance identity alone fails |
| PC-57 / G-57 | Skip candidate source/cleanliness validation | Runner.LockBeforeSlot/candidate: `(2, slotCalls=0, starts=0)` | Separate variants wrong actual SHA and dirty tree; context otherwise coherent |
| PC-58 / G-58 | Unconditionally delete lock during cleanup | Runner.DeadlineJoinsOwnedChildren/foreign-lock: replacement owner bytes remain | Barrier replaces owner after owned child joined, before release; no child remains |

Supplement the V tables with the explicitly named cases above: V-1 `candidate`,
V-2 `legacy-state`/`context-forged`, V-4 `foreign-lock`, V-8 `setup-no-prompt`.
These are variants inside the existing ten methods, not new methods or CP rows.
PC-22 has 2 variants, PC-27 has 5, PC-53 has 3, PC-54 has 3, PC-57 has 2;
every other PC has one: **58 guards, 58 controls, 68 variant cycles**.

Code runs V/R. Ordinary Review judges the PC recipes and their binding completeness
before land. Mutation after confirmed land runs **every** variant
baseline-green / break / assertion-red / restore / green, with fresh evidence and
forced builds as the owner contract requires. An implementation that moves a guard
must update its recipe and reachability record, not declare its mutant equivalent.
No broad class PC filters. Controls sharing a file/method cannot be batched together.

TestDesign audit: **guards=58, mapped=58, missing=0, duplicate PC maps=0**.
Execution audit: **PCs run=0/58; variant cycles run=0/68**, pending Code, ordinary
Review, land and binding. All PCs are mandatory for the later Mutation handoff;
none is falsely reported run in this docs-only stage. A Mutation completion report
must establish all 58 controls/68 variants executed and restored or retain the gate
as incomplete.

### Out of scope

- S1a roster inventory/tagging and its compiled two-project census; S1c executable,
  profile/cwd/settings, native startup/delivery oracles, sanitizer and actual paid
  caller wiring. No changes to the fix plan or its in-flight amendment.
- Native Windows custody, supervisor-death recovery, PTY/ConPTY, real account/auth,
  model/bridge behaviour and paid manifest execution. Portable owner fakes prove
  ordering/decisions only. Linux `Start-NightlyNativeOwner` explicitly refuses;
  never bypass it or claim its mock qualifies Windows. Any new OS branch must get
  a separately designed native row before activation.
- No queue/session delivery change, real provider outage simulation, credential
  copying, signing trust, receipt reuse, last-green reuse logic, Full gate migration,
  release/publication/tag/deploy or master/Interim qualification credit.
- No broad full-suite run: existing policy/readers/test inputs stay fixed, with ten
  additive test UIDs. This does not turn existing Full skips into green.

### Implementation steps

1. **S1b-admission, 45-60 minutes authoring.** Add/commit/push the four runnable
   methods and offline harness with their named assertions before implementation.
   Add minimal callable surface only to obtain behavioural reds; retain the red
   receipts. Implement the wrapper/core and inert wrapper-only native spec, reusing
   real native locks and StartInfo construction, with injected fake boundaries.
   Add pinned Full preservation and state-byte checks. Commit/push the fix before
   CP-2; run only CP-2, inspect all four actual results, commit/push any repair and
   rerun that row. Record authoring and build/run time separately. Until outcomes
   lands, terminal classification may conservatively refuse credit.
2. **S1b-outcomes, 30-60 minutes authoring.** Add/commit/push six methods and typed
   fixtures before the classifier. Implement closed typed observations, per-row
   outage attribution and aggregate precedence, preserving raw child outcomes;
   wire the wrapper to this classifier and bound probes/retry counters. Commit/push
   before CP-16. Run CP-16 and, because this slice edits the shared wrapper path,
   rerun CP-2 as the explicit after-outcomes regression row below. Count all reruns.
   Bind actual methods/assertion inventories and candidate PC sites; keep payloads
   labelled binding required after Code. No paid/native or release enablement.

Each is a separate Code commission. Checkpoint time is extra, not hidden in the
30-60 minute authoring estimate. If a minimal seam cannot be implemented within the
listed paths, stop that scope expansion and return `next: plan` with the exact
unverifiable seam. No operator choice is needed for these two diagnostic slices.

## Performance budget

No builds/tests ran in TestDesign. All new duration targets are **estimated,
unmeasured, sample n=0**, owned by CARD-1545/CARD-1404; they are not measured p95.
Use the first red/fix/green and ordinary observations to collect three comparable
samples per method, then calibrate without buying repetition solely for statistics.

| Phase | Provisional p95 target / expected time | Explicit bound and recording |
|---|---|---|
| Tool setup/bootstrap | 2 min total once per Code environment | Host slot gate; retain source/tool identity and bootstrap timing |
| Antiphon.Tests isolated build | 8 min expected; parent plan sample 509.43 s n=1 is context, not this host's p95 | 16 min build bound; record restore/build separately |
| Each harness method | 15 s target | ScriptHarnessOptions ExecutionBudget=30 s, CleanupBudget=10 s, PostExitDrainBudget=10 s; provisional cleanup allowance, no real sleep-based deadlines |
| Four-method runner class | <=1 min target, up to 2 min budgeted in CP estimate | 4 min row timeout; includes filesystem/Git comparison, interpreter startup and cleanup |
| Six-method outcomes class | <=1.5 min target, up to 2 min budgeted in CP estimate | 4 min row timeout; measure per case and per method |
| Slot admission | Unknown percentile; expected wait 0 for cost floor | build.slotWaitMinutes=5 per acquisition, admission time reported separately; no unleased retry on timeout |
| Each commissioned row group | Build 8 + execution 2 = 10 min expected | 20 min charged execution and 25 min hard wall; report admission-only union separately |
| Virtual subject deadlines | Preflight <=60 s, one retry allowance <=30 s; post-probe <=15 s; cleanup observation <=30 s | Manual clock, no wall sleeps or real external operations; these are wrapper contract values, not harness timeouts |

No expensive service setup exists. Reuse immutable fixture definitions per harness;
fresh small scratch roots/lock ownership per case are the subject, so retain their
IO timings. No new csproj is justified for ten short script contracts (R2); each
runtime script input is declared above. Preserve phase/run/group/test/SHA/host/OS/
category identities, per-test TRX durations, filesystem, Git, PowerShell startup,
fake probe/wait and teardown spans in ignored evidence. DB/HTTP/native phases are
N/A; unknown timing is null with remainder, never fabricated zero. Ordinary gates
use TRX; fake traces are assertion evidence only.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-2 | S1b-admission | tests/Antiphon.Tests -> bin-live-runner/ | portable-live-runner | /*/Antiphon.Tests.Scripts/LiveLaneRunnerTests/* | V-1..4, R-1..3, R-6 | LockBeforeSlot,WrapperOnlyPaidManifest,ChildEnvironmentIsScoped,DeadlineJoinsOwnedChildren; exactly 4 passed, 0 failed/skipped | 4 | 10 |
| CP-16 | S1b-outcomes | tests/Antiphon.Tests -> bin-live-outcomes/ | portable-live-outcomes | /*/Antiphon.Tests.Scripts/LiveLaneOutcomeTests/* | V-5..10, R-1, R-3..5, R-7 | ProviderOutcomeIsDistinct,MidRunOutageAfterReceiptIsInconclusive,HealthyProviderAssertionStaysRed,TypedSetupOutcomes,SkipAndZeroNeverGreen,StaleSharedLockRefuses; exactly 6 passed, 0 failed/skipped | 6 | 10 |
| CP-23 | S1b-outcomes | CP-16 | portable-live-runner-after-outcomes | /*/Antiphon.Tests.Scripts/LiveLaneRunnerTests/* | V-1..4, R-1..3, R-6 | LockBeforeSlot,WrapperOnlyPaidManifest,ChildEnvironmentIsScoped,DeadlineJoinsOwnedChildren; exactly 4 passed, 0 failed/skipped | 4 | 2 |

CP-23 is this companion's explicit regression row for the shared wrapper edit; it
does not alter another slice or recompile the same project in its group. Each row
has one isolated build identity and one exact filter; the union is the entire
ordinary scope, 14 TUnit executions across the two commissions, ten distinct
methods. `Min` is executions, never script PASS count. CP-2 is not rerun after
outcomes as an unlisted extra: CP-23 is that declared run using CP-16's build.

Use the checkpoint tool, one invocation per committed `After` group, and wait until
not 75. Import this companion's table, not the main plan's future rows. The supported
YAML timeout pins are necessary because `run --plan` alone derives 30-minute row
timeouts from estimate 10, and `--row-timeout` also replaces the build deadline.
Use the documented import path (Program.Import/LoadSelection inspected):

```text
dotnet run --project tools/Antiphon.Checkpoints -- import --plan docs/superpowers/plans/2026-10-31-card-1545-s1b-test-design.md --out .antiphon/s1b-checkpoints.yaml
```

That bootstrap/build driver goes through `scripts/build-slot.ps1`; afterwards run
the built checkpoint tool with no implicit build. Set only these timeout/scheduling
values in the ignored imported YAML, preserving all table selections/counts:
`build.slotWaitMinutes: 5`, `timeouts.rowMinutes: 16`, each
`checkpoints[].timeoutMinutes: 4`, `parallel.maxRows: 1`. This gives the build the
16-minute default floor and each row its explicit four-minute cap. Retain the
imported and resolved manifests/digests as evidence. For outcomes the charged
execution allowance is 24 min and hard wall 29 min (one build plus two row caps);
admission uses 20/25. Invoke the built tool with positional manifest path, the exact
`--after S1b-admission` or `--after S1b-outcomes`, `--expected-source-sha <committed-sha>`,
`--total-timeout 20m` / `24m`, `--hard-wall-timeout 25m` / `29m`, and `--serial`.
This is the documented manifest route for explicit deadlines, not an extra test run.
The scheduler takes row/build leases itself: never hold an outer slot during its run.

Keep all CHECKPOINT lines unedited and validate fresh receipts at the committed SHA.
No policy/gtest timeout widening, no retry on slot expiry, no negative case skipped.
Delete only owned `bin-live-*` outputs after their evidence is retained. Generated
TRX/JSON/logs/manifests stay ignored. Code/Review run `check-evidence-diff.ps1` over
their actual base..HEAD. A run/batch expected or observed over 30 minutes requires
Debug with exact selection, SHA, phases and host/load before repetition (R1).

### Cost

**Estimated, not measured.** Ordinary V/R floor (Code) = CP-2 10 + CP-16 10 +
CP-23 2 = **22 minutes**, comprising two isolated builds at 8 minutes each and
6 minutes allocated to the three exact class filters. Setup/bootstrap = 2 minutes;
ordinary total = **24 minutes**, excluding unknown slot wait and code authoring.
Admission commission costs 10 minutes verification; outcomes costs 12. No Full
suite, live run or native build is charged or credited.

PC floor (Mutation): 68 independent variant cycles. Each uses the exact **method**
filter in its PC row, three fresh forced builds/runs (baseline, mutant, restored),
estimated 8 minutes build + 0.25 minute method each, plus 0.25 minute binding check/
restore bookkeeping = **25 minutes per variant**. Thus PC execution floor =
**68 x 25 = 1,700 minutes**; pack authoring/binding is separate, unmeasured work.
All methods use the same estimate pending their first measured samples. A numeric
zero saving is intentional: do not assume a compiled-pack reuse exception to the
forced-build contract, or batch controls sharing production files/methods.

Total planned machine time = setup 2 + ordinary build 16 + ordinary V/R execution
6 + PC baseline/red/restore/green 1,700 = **1,724 minutes**, plus admission waits
and binding. This large aggregate **requires a Debug timing/cost task before a
multi-PC battery is commissioned**, under CARD-1514/R1; splitting into individually
bounded 25-minute cycles does not waive that obligation. That later task can qualify
a cheaper execution method through the owner contract; this design does not assume
one. It does not block the 10/12-minute Code verification groups.

Savings: versus rebuilding separately for the outcomes regression, CP-23 shares
CP-16 and saves **8 estimated build minutes**. Further PC savings are **0** until
measured/qualified. The Full preservation proof avoids an unrelated Full run; no
numeric Full saving is claimed because its comparable duration is unknown.
