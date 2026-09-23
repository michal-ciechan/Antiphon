# CARD-0599: Activate Interim verification and periodic RC releases

Date: 2026-09-22. Stage: Plan. Task: `ba1a4d22`.
Source baseline: `b4c0cf16e6e3ce5915f66a2c10f20814e44df50d`.
Next: **TestDesign**, before Code. Verification design was not folded into this dispatch.

Activate the existing qualified nightly backstop and CARD-0544's bounded Interim rounds;
extend the nightly executor with an explicit RC profile that includes automated E2E;
publish an immutable tag and GitHub Release only for the exact fully tested candidate.
Keep master as the integration branch and preserve its scheduled readiness contract.

This is primarily activation work. It is not complete when another set of deployment
instructions lands: actual job registration, qualification receipts, an Interim pilot,
and an observed scheduled RC release are acceptance obligations on CARD-0599.

There is one material limit to the requested saving: the shipped Interim profile reduces
**repeated repair-round verification**, not the final ordinary verification required to
land each change. D-1 recommends retaining that boundary. No claim that activating it
moves all affected Slow/native tests from Final Review to the RC gate is valid.

## Ground truth

The [investigation](../../investigations/2026-09-22-card-0599-release-gate-lighter-per-change-tests-plus-rc-releases.md)
is the ground truth for the 2026-09-22 Windmill/GitHub census. This plan inspected the
source at the baseline above and read the live CARD-0599 description. It did not repeat
the external census, execute tests, change settings, register jobs, or publish releases.
Recheck live facts during activation; a checked-in definition is never registration evidence.

| Card assumption / proposed shortcut | What the code or investigation actually establishes | Planning consequence |
|---|---|---|
| A lighter verification system must be built. | `InterimVerificationSettings`, `InterimVerificationPolicy`, the service/dispatcher admission checks and stage `ROUND:` contracts already exist. `server/Program.cs` binds the section. No appsettings section enables it. | Configure and qualify the existing system; no new verification profile or land bypass. |
| Interim allows reduced-scope land. | `docs/orchestration-loop.md` and stage bundles require a Clean Final baseline, explicit per-card policies, cumulative selections, and later exact-SHA Final/Full Review. First rounds are Final. | State the smaller saving honestly; D-1 preserves the latch and Final Review. |
| Nightly backstop is operational. | Investigation found zero nightly script/schedule rows, no current monitor/green/receipt, and a failed September 4 feature-ref attempt. CARD-0545 S6 never ran. | S5 includes registration and live qualification as deliverables, not optional follow-up. |
| Any full green can refresh readiness. | `Test-NightlyCompleteGreenPredicate` accepts only master/origin/master + scheduled. `nightly-health.ps1` correlates a scheduled Windmill job/native run and London due day; the server reader consumes the qualified receipt and monitor. | Keep master readiness intact; accept RC green in a separate credit class and store. |
| Adding `-Suites e2e` runs E2E. | `nightly-tests-impl.ps1` unconditionally skips `mode=manual`; `$NightlyNativeGroups` also omits E2E. | Change selection, execution, serialization, cleanup and evidence accounting, not just one conditional. |
| E2E would be fully covered after enabling execution. | `nightly-coverage.ps1` marks OptIn/Explicit discovery nodes excluded in both JSON and diagnostic parsers and again in `Test-NightlyDiscoveryExcluded`. Most E2E classes are OptIn. | Profile-aware eligibility must reach parsing, required UID sets, chunk union and terminal reconciliation. No zero-required-UID green. |
| All E2E is unattended browser work. | `PlaywrightFixture` is headless and `AntiphonAppFixture` has an isolated random-port runner and disposable DB. `DelegationPipelineE2ETests`, `DelegationSequencingE2ETests` and `OutputDistillationApplyCanaryTests` are also Headed/live canaries. | Automated E2E is required; live canaries need explicit dispositions and retain their authorization guards. |
| RC runs can share the existing state root. | Run lock, `last-run.json` and `last-complete-green.json` are StateRoot-local. The readiness evaluator considers newer failed attempts. | Separate state/clone roots plus one shared native-run exclusion lock; RC attempts must not overwrite master state. |
| Green GitHub Actions is a valid release prerequisite. | Investigation records master CI red since September 1, both lint and Windows tooling failures; CARD-0610 owns repair. | Use native execution evidence; never translate a red/missing Actions status into green. Local lint failures still block qualification/releases. |
| Release identity already exists. | No tags or GitHub Releases at investigation time. `VersionEndpoints` returns build SHA, unmanaged `1.0.0+SHA`, and `land-v2`. | Add Git tags/releases and a SHA-bound manifest; leave API SHA and capability semantics intact. |
| A release makes it live locally. | Landing confirms publication; canonical restart and `/api/version` establish activation. | Release creation never restarts the shared stack. Exact SHA lookup identifies whether the running build is released. |

Owners read: [testing/build](../../testing-and-build.md), [nightly watchdog](../../nightly-watchdog.md),
[Windmill definitions](../../../scripts/windmill/README.md), [orchestration](../../orchestration-loop.md),
[card lifecycle](../../agent-card-lifecycle.md), [project conventions](../../project-context.md),
[HTTP operations](../../ops-http.md), [credential custody](../../agent-credentials.md),
[restart runbook](../../apphost-runbook.md), and CARD-0544/CARD-0545's S6 requirements.

## Decisions

These are the concrete recommendations for implementation, with reasons and rejected alternatives.
They resolve the design questions in the brief; they do not authorize this Plan task to deploy.

### D-1: Activate bounded Interim repair rounds; retain Final before land

Ship `InterimVerification.Enabled=false` explicitly in `server/appsettings.json`, with
null repository/project/state-root values, `MonitorFreshMinutes=60`, and `MaxFileBytes=65536`.
Retain `FullOnly` as the card default. After S5 qualification, configure the actual server
deployment and opt **one bounded repair card** into `AllowInterim` for the selected roles.
First establish a Clean Final baseline; later requests supply subject, baseline outcome,
and committed cumulative selection. Finish with exact-SHA Final/Full Review before land.

Keep whole Unit/affected-class ordinary V/R in Final. Interim selects cumulative changed
cases, unresolved-finding tests and named adjacent smoke, recording every deferred ID.
Unbounded shared/native impact remains Final. Unnamed broad integration/native work and
automated E2E run periodically on RCs; named ordinary checks are not silently reclassified.

Reason: this activates shipped enforcement without inventing a weaker landing policy.
Rejected: a global true default, bulk opt-in, calling nightly green a Final outcome, or
removing the land latch. Those would be a distinct policy change requiring a new design.
The initial result therefore reduces repair-cycle overhead; it does not promise that
every first/final pass becomes cheaper. Measure the pilot before claiming savings.

### D-2: RC green earns release credit, not master readiness credit

Generalize `Test-NightlyCompleteGreenPredicate` with an explicit credit kind
(`master-scheduled` or `rc-release`). Preserve the existing master branch/trigger path.
The RC path requires the `rc` profile, `trigger=rc`, a validated
`release/rc-YYYYMMDDTHHMMSSZ` ref, and equality to the candidate's pinned full SHA.
Both paths require real booleans for coverage/tests/report, exit 0, matching run/policy
identities, and completed fresh evidence. Unknown combinations fail closed.

Write master green only to `C:\Antiphon\nightly\last-complete-green.json`. Write RC
green to `C:\Antiphon\releases\candidates\<candidate-id>\complete-green.json`.
RCs never write the master attempt, green, monitor, or Interim qualification receipt.
Keep the once-nightly London due-date/08:00 readiness model and watchdog unchanged.
An RC success cannot repair a failed, missing, stale or overdue scheduled master run.
RC failure blocks that release and does not independently revoke a healthy master green.

Reason: an immutable older candidate says nothing about the current integration stream.
Rejected: accepting any `release/*` ref in the existing predicate and writing the same
last-green file; replacing due dates with a rolling age; treating a manual run as scheduled.
Preserving separate credit avoids a second daily-readiness state machine and requalification
of the watchdog's timing model. The cost is one nightly broad run in addition to RC runs.

### D-3: Two scheduled cuts, immutable candidates, master-first fixes

Register `u/lndcobra/antiphon_release_candidates` on `desktop`, cron
`0 30 8,16 * * *`, timezone `Europe/London`: 08:30 and 16:30 local. Keep the master job
at 00:30 and readiness every 30 minutes. These are attempted cuts, not a guarantee of
two successful releases a day. Record local schedule slot and UTC candidate timestamp.

`scripts/release-candidate.ps1` fetches origin/master in a producer-owned isolated clone,
pins its full SHA once, validates the remote repository, and pushes a create-only branch
`release/rc-20260922T073000Z` (example). Store the candidate journal before the push.
No force push, no direct repair commits, no moving a tested branch. Reject an existing
candidate ref with a different SHA. Same journal/ref/SHA is an idempotent resume.

Run `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate-ref> -ExpectedSha <sha>`
using `C:\Antiphon\releases\checkout` and the candidate's private state root.
All new parameters must survive wrapper/core/self-reexec/test-script hops. Fetch/reset
only an owned clone; refuse canonical checkouts, linked worktrees and unowned roots.
Before tests and before publication, compare checkout HEAD, candidate journal and remote
candidate ref to the pinned SHA. A moved ref fails; never retest implicitly under its old identity.

Fix RC findings through ordinary Code/Review/land onto master, then cut a new RC and run
its full profile from scratch. Keep the failed candidate evidence and link the successor.
Skip a scheduled cut if that exact master SHA already has a published release under the
current profile/hash. Do not reuse a partial run or a green under a different policy.

Reject mutable `release/current`, cherry-picks maintained independently of master, and
per-push full RC jobs: they obscure tested identity or reproduce per-change cost.

### D-4: Serialize native work across master and RC lanes

Retain each run's current owned lock and add one shared atomic verification lock at
`C:\Antiphon\verification\native-run.lock`. Both master and RC entrypoints acquire it
in the same order before cloning/building/testing, carry the ownership identity across
the self-reexec hop, and release it only after owned children are accounted for.
Tests inject a private coordination root. Owner identity includes PID, process start,
run ID and continuation identity. Never steal a live lock based on age.

On contention, a scheduled RC writes a `deferred-busy` attempt, returns non-green, and
waits for the next slot or an explicit operator rerun. Do not build an unbounded retry queue.
The master schedule retains its existing missed-run/readiness consequences; no killing
RCs to make it green. Qualification must measure durations and demonstrate no overlap
with 00:30; inability to fit is a capacity finding, not permission to raise timeouts.
All projects remain sequential, including E2E; assembly-local limiters are not cross-process locks.

### D-5: Define an honest, executable RC coverage profile

Introduce execution-policy schema v2 with `profiles.nightly` and `profiles.rc`.
`nightly` retains the seven current suites; `rc` requires those seven **plus E2E**.
Keep `defaultSuites` as the compatible nightly default while migrating existing consumers;
the selected profile becomes authoritative for required suites. Hash the entire policy,
including profile dispositions and exclusions. Reject unknown versions/profiles and stale hashes.
An explicit partial `-Suites` selection may be diagnostic but never complete-green for a profile.

RC means the full **automated Windows profile**, including all Unit, Integration and Slow
cases eligible for this host, native suites, disposable-broker messaging, client build/lint/
Vitest, script census and automated E2E. Do not describe it as every test on every platform.
Keep live provider/headed canaries, separately approved distiller tests, QEMU SourceLanding
controls and Linux-only lanes explicitly outside this profile, with reason and owner.
No affected test that D-1 requires per change may be deferred beyond both Final and RC.

For E2E, OptIn alone is **not** exclusion in the RC profile. Build an exact class roster
from discovery at the candidate SHA; initially all classes except the three live classes
named in Ground truth are required, including shared classification and teardown tests.
Every discovered class/expanded case must resolve to required or a named policy exclusion.
New/unclassified cases fail the coverage census, rather than silently inheriting manual status.
Record excluded cases separately; a required skip, missing UID, duplicate chunk membership,
missing TRX, unknown outcome, zero executed required cases or stale evidence blocks release.

Preserve raw discovery metadata (especially why a node was excluded) and apply the profile
disposition centrally. Do not globally reinterpret OptIn in the default nightly lane. Make
required-UID reconciliation profile-aware at every parser/union boundary; selecting a class
without counting its expanded cases is insufficient. Inventory existing native opt-in/platform
skips as well: make documented profile exclusions before execution, never erase red/skip rows
after seeing results. TestDesign must finish that census before Code's closed manifest is issued.

Reason: an executable unattended profile supplies useful full coverage without borrowing
interactive approvals or pretending unavailable platforms ran. Rejected: global
`-IncludeManual`, clearing every guard, excluding all E2E OptIn, and declaring skipped tests green.

### D-6: Execute E2E through the existing harness with real prerequisites

Make `e2e` a runnable suite disposition for `Profile=rc`, while nightly continues to leave
it out. Add it to native ownership/evidence handling (or replace the hard-coded native ID
list with project-based classification that retains its serialization semantics).
Use existing `AntiphonAppFixture`, `PlaywrightFixture` and `IsolatedSessionRunner`.

Before E2E: complete npm ci/build/lint at the pinned SHA, build its project, install the
matching Playwright Chromium using the generated `playwright.ps1 install chromium`, and
verify the browser launches headlessly. Keep a run/SHA-bound client-build receipt and
bundle digest; retain `EnsureClientBundleIsCurrent`. A missing browser, unavailable Docker,
stale bundle, fixture failure or unavailable owned runner is red/incomplete, not a skip.
Do not treat the current placeholder Docker/disk preflight facts as proof: actual owned
fixture startup and retained result evidence are required; add real checks for RC prerequisites.

Run the exact eligible class chunks with fresh TRX and MTP discovery/execution diagnostics,
serially. Include E2E in expanded UID accounting, slow-tripwire reporting and summary counts.
Use a measured suite budget: start the qualification trial with the existing 60-minute
native fallback, measure class costs, then use bounded disjoint chunks if needed. Do not
increase a deadline or add retries merely to turn a failed qualification green.

Assert the app's runner endpoint is its owned loopback random port, never production 17204;
use its disposable Postgres and owned scratch repos. Keep headed/live env variables cleared;
clear inherited task tokens/production API overrides and distiller approval paths in test
children. Normal E2E must not use a live broker or authenticated provider to satisfy coverage.
Audit the eligible fixtures during TestDesign, retaining any necessary isolation repairs in S2.
Capture per-test diagnostics and fixture teardown census; cleanup failure prevents green.

### D-7: Register jobs as an actual activation slice

S3 builds an idempotent registration front door, `scripts/register-release-gates.ps1`:
preview by default; explicit `-Apply`; exact approved script/schedule paths only; no token
creation; read token from an operator-placed file without printing it. Registration uses the
installed Windmill HTTP API, not database writes. Preview includes content digest, tag,
cron, timezone, args and current/live desired differences; apply compares the observed
revision/hash before updating and refuses unrelated collisions or concurrent drift.

Register master/readiness and the RC job in workspace `mc`. Create scripts, read back their
version/hash/content, create schedules **disabled**, read them back, then enable individual
schedules only at the corresponding S5/S6 acceptance step. Use the installed server's API
schema for updates/enablement; pin those request contracts in adapter fixtures. The existing
documented creation routes are `/api/w/mc/scripts/create` and `/api/w/mc/schedules/create`.
Retain readback of workers/version and the actual SSH/native completion result.

Correct trigger provenance while touching wrappers: the current master wrapper has empty
args and always lets native Trigger default to scheduled, including manual invocations.
Derive `scheduled` only from the runner's actual schedule context, checked against the expected
schedule path; otherwise pass `manual`. Capture trusted Windmill job/schedule IDs in the
completion envelope. An API caller's `trigger` string cannot manufacture scheduled credit.
Qualify the installed worker's context in S5; missing context must refuse scheduled credit.
RC uses `trigger=rc` plus separate invocation-source/job/slot fields; no master-credit alias.

The operator still owns secret placement, interactive Telegram login and authorization for
live notices, as [the custody rule](../../nightly-watchdog.md#custody) explicitly requires.
The rule says deployment/registration/live messages are "operator-run qualification steps
... never a delegate's." This plan does not execute them. It gives the operator-run S5/S6
exact work and evidence, and requires the caller to commission and track it before closure.
This is narrower than leaving the whole feature at "operator registers the job someday."

Concretize the front door with `-Profile <untracked-json>`, `-Apply`, and
`-EnableSchedule <nightly|readiness|rc>` (enablement requires `-Apply`). Profile fields:
`windmillBaseUrl`, `workspace` (must be `mc` for this deployment), `tokenFile`,
`repositoryPath`, and `projectId`; no inline token or watchdog credentials. Without an
enable selector, apply registers/reconciles definitions without enabling a new schedule.
For example, the S5 operator runs:

```powershell
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply -EnableSchedule readiness
```

Enable `nightly` after the manual green and `rc` after its qualification. An idempotent
reapply preserves an already enabled matching schedule; disabling is explicit and never
an accidental consequence of the disabled creation payload. Qualification records the
before/after/readback for each command, including a second apply proving no duplicates.

### D-8: CalVer tags and GitHub Releases bound to one tested SHA

On RC complete-green, `scripts/publish-release.ps1` validates the immutable journal,
profile/policy hash, all required suite results, report delivery and remote candidate SHA.
It refuses diagnostic/NoReport/seam-driven runs, incomplete evidence and changed candidates.
Use the existing authorized GitHub CLI identity; never mint credentials in the job.

Choose an annotated tag `vYYYY.MM.DD.N`, for example `v2026.09.22.1`, using the UTC cut date
and a positive daily sequence reserved in the publication journal. Name the GitHub Release
the same as the tag; its address is GitHub's `/releases/tag/v2026.09.22.1`. No new Antiphon
web route and no change to NuGet package versioning or the SDK's assembly version.

Publication sequence: persist tag/SHA/manifest digest intent; create local annotated tag at
that SHA; push only that tag without force; verify its peeled remote target; create a draft
GitHub Release with `gh release create --verify-tag --draft --notes-file <file>`; upload
`release-manifest.json` and a sanitized verification summary; read back tag/asset digests;
publish the draft. Persist the final release ID/URL. Notes contain commits since the prior
published tag, card references where present, test counts, profile/exclusions, evidence
identities and the previous release; first release says no earlier release. Never expose raw
logs, credentials, hostnames or transcripts in public assets.

The manifest includes schema version, repository, tag, candidate ref/full SHA, native run
and Windmill job/slot IDs, UTC start/end, policy/profile/script hashes, build/bundle hashes,
per-suite discovered/required/executed/passed/failed/skipped/excluded counts, exclusion reasons,
summary digest and build-reported capabilities. Read capabilities from the candidate's isolated
server identity probe, not the production server and not a copied constant.

Retry resumes the same journal. Same tag/SHA/manifest is success/recovery; any mismatch is a
hard refusal. A tag without a published release remains pending, not released; a draft missing
assets remains pending. Do not delete/recreate tags or overwrite another release's evidence.
Re-read remote state after ambiguous timeouts before another write. A crash after publication
but before local acknowledgement must recover the existing release ID, not allocate another N.

Reason: calendar versions fit multiple daily releases and the current lack of package-version
discipline. Rejected: new SemVer compatibility promises, moving stable tags, tagging before
the candidate's tests, or automatic GitHub tag creation at whatever master currently points to.

### D-9: Release identity and local activation remain separate

Leave `/api/version.version` as the built SHA and keep `capabilities` as feature probes.
Document an exact-SHA lookup against published release manifests (with a small read-only
`scripts/release-status.ps1` helper). Return running SHA/capabilities, matched release name/URL,
or `unreleased integration build`; missing GitHub data is `unknown`, not latest release.
An ancestor tag or the latest release is not the identity of a newer running SHA.

Creating a release never restarts AppHost/runner or checks out a tag in the main worktree.
If an operator later deploys a release, require `/api/version` SHA equality to that tag's
peeled SHA and the intended capabilities, using the canonical restart runbook. Updating
master and restarting it normally may leave the dev stack ahead of the latest release.

### D-10: CARD-0610 is not an Actions-health prerequisite, but failures still count

Neither candidate admission nor publication depends on GitHub Actions being green.
They depend on this profile's fresh native evidence. Keep CARD-0610 as the owner of
Actions repair, and state the missing CI signal in initial release notes/qualification.
Do not modify `ci.yml`, delete checks, or treat their known failures as release evidence here.

The same client lint defect can fail the native harness. Such a failure blocks qualification
and release until repaired on master (by CARD-0610 or an explicitly linked repair). Host
tooling required by eligible tests must exist and be recorded. No baseline-red waiver,
retry loop or exclusion created solely to bypass a failure. Separating the CI status dependency
does not waive overlapping defects. Linux-specific coverage remains owned by CARD-0590/0605.

## Activation configuration and acceptance

The deployed server needs this exact section after qualification (values resolved at deploy):

```json
{
  "InterimVerification": {
    "Enabled": true,
    "CanonicalRepositoryPath": "C:\\src\\Antiphon",
    "ProjectId": "<actual Antiphon project GUID>",
    "StateRoot": "C:\\Antiphon\\nightly",
    "MonitorFreshMinutes": 60,
    "MaxFileBytes": 65536
  }
}
```

Use the server's existing `antiphon-server` user-secrets configuration under the actual
AppHost service account (or corresponding `InterimVerification__...` server process
environment on a deployment without user secrets). Only these keys are changed; no
listing/export of the store. `Antiphon.AppHost/Program.cs` launches the server as Development
but does not forward an arbitrary AppHost configuration section. Therefore putting these
values only in AppHost appsettings does not activate the server. Verify effective behavior
through a real pilot admission/refusal and recorded deployment identity.

Resolve the project through the board/project API, not a hard-coded GUID and not the board ID.
The configured canonical path is the qualified repository identity, not the isolated nightly
clone. Validate it against the task's repository identity before opt-in. Preserve Windows
backslashes. Changes to script/policy hashes require current qualification identities;
never copy an old receipt to make a new profile look qualified.

Required artifacts before `Enabled=true`:

| Artifact | Required content / evidence |
|---|---|
| `docs/investigations/<date>-card-0487-nightly-qualification.md` | Accepted CARD-0545 S6 result; deployed code, script/policy hashes, manual full unattended green, later real scheduled full unattended green, all seven suite counts, job/native-run correlation, notification and outage/recovery receipts. |
| `C:\Antiphon\nightly\watchdog-deploy.json` and operator-owned watchdog env/session | Qualifying independent host, correct instance and reachable snapshot; custody remains local, secret values never committed. |
| `C:\Antiphon\nightly\readiness-config.json` | Expected script/policy hashes, watchdog URL/instance, canonical repository and actual project. |
| `C:\Antiphon\nightly\interim-qualification-receipt.json` | Schema 1; canonical repository/project; committed qualification artifact path/full commit; policy/script hashes; manualRunId, scheduledRunId, scheduledJobId; nonempty recipientEvidenceIds and outageRecoveryEvidenceIds; watchdogInstanceId. |
| `last-complete-green.json` and `last-monitor.json` under the same root | Real scheduled master green for the valid London due day; fresh Healthy/ReadyForDeferral booleans; matching repository/project/hash/native-run/job/watchdog identities. Server freshness remains 0-60 minutes, future timestamps rejected. |
| `docs/investigations/<date>-card-0544-verification-pilot.md` | Baseline/subject/selection/card revision, admission and refusal evidence, deferred cases actually run in Final, exact-SHA land, rollback check and measured costs. |
| `docs/investigations/<date>-card-0599-release-gate-qualification.md` | Registration readbacks, full automated RC roster/counts including E2E, two real schedule-slot observations, published tag/release/manifest and exact-SHA status lookup; all exclusions/limits. |

Commit the qualification document before writing its full commit SHA into the trusted receipt.
Do not bootstrap readiness by forging `Healthy=true`, accepting send acknowledgement as receipt,
or substituting a scheduled RC for the scheduled master acceptance run.

## Implementation slices

New paths below are proposed, not claims that files already exist. Keep each slice committed
and pushed with its actual verification status; Code completes S1-S4, Review then land precede
the operator-run S5-S6. The card's operational acceptance remains pending until S6 is evidenced.

### S1: Profile, credit and pinned-run plumbing

Files: `tests/test-execution-policy.json`; `scripts/nightly-{run,tests}.ps1`;
`scripts/lib/nightly-{policy,run-impl,coverage,tests-impl,common}.ps1`;
new `scripts/lib/release-gate.ps1`; `scripts/test-nightly-{run,tests,health}.ps1`.

Implement D-2/D-4/D-5, expected-SHA/profile forwarding, profile-specific state and real
boolean validation, shared lock and self-hop identity, and summary/completion fields.
Recompute policyHash with the existing canonical hasher. Preserve master adapter fields
and watchdog due-day semantics. Master evaluator/reader need regression evidence, not
a new RC readiness mode. Script census entries for new harnesses must be explicit.

Tests: new `tests/Antiphon.Tests/Scripts/ReleaseGatePolicyTests.cs` and
`ReleaseGateRunTests.cs` driving PowerShell fixtures; existing `NightlyScriptsTests`,
`NightlyVerificationContractTests`, `InterimVerificationReadinessTests`.
Acceptance: RC and master evidence cannot cross-credit; shared native runs never overlap;
partial/diagnostic/seam invocations cannot mint a release-ready receipt.

### S2: Required automated E2E execution

Files: `scripts/lib/nightly-tests-impl.ps1`, `nightly-coverage.ps1`, `nightly-policy.ps1`;
`scripts/test-nightly-tests.ps1`; `scripts/fixtures/nightly/` evidence fixtures;
`tests/test-execution-policy.json`; new `tests/Antiphon.Tests/Scripts/ReleaseGateE2EEvidenceTests.cs`.
Fixture safety changes if the audit requires them: `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs`,
`IsolatedSessionRunner.cs`, `SharedApp.cs`; new `tests/Antiphon.E2E/ReleaseGateIsolationTests.cs`.

Implement D-5/D-6's exact eligible roster/chunks, prerequisites and evidence accounting.
Reuse fixtures rather than booting the real shared stack. Add production-behavior assertions
for owned endpoints, bundle identity and teardown; do not substitute source-text checks.
Run the named smoke and isolation classes as ordinary evidence; the full RC E2E measurement
and run are S6 acceptance. No source edits while a long suite is running.

### S3: RC coordinator, publication and registration tooling

New files: `scripts/release-candidate.ps1`, `scripts/publish-release.ps1`,
`scripts/release-status.ps1`, `scripts/register-release-gates.ps1`, and their explicit
`scripts/test-*.ps1` offline harnesses; reuse `scripts/lib/release-gate.ps1`.
New `scripts/windmill/antiphon-release-candidates.json` and `.schedule.json`;
update `scripts/windmill/antiphon-nightly-tests.json` for honest trigger provenance.
Update `scripts/nightly-report.ps1` and `scripts/test-nightly-report.ps1` for lane-specific
incident identity; add a credential-free registration profile example under `scripts/windmill/`.
New test classes: `ReleaseGateRegistrationTests`, `ReleaseGatePublicationTests`,
`ReleaseGateStatusTests` under `tests/Antiphon.Tests/Scripts/`.

Implement D-3/D-7/D-8/D-9. Inject Git/Windmill/GitHub boundaries for offline contract tests;
exercise the coordinator with temporary local bare remotes, durable journals and simulated
lost responses. Credentials are file references, never script payload values. Definition
files stay inert until S5 applies them; future schedule payloads start disabled.
Do not add a Windows Scheduled Task or touch CI/NuGet workflows.

### S4: Explicit dormant config and executable operating contract

Files: `server/appsettings.json`; `docs/testing-and-build.md`; `docs/orchestration-loop.md`;
`docs/ops-http.md`; `docs/nightly-watchdog.md`; `scripts/windmill/README.md`;
new `docs/release-gates.md` (linked from the relevant owner docs).

Add D-1's false/default section and document the exact activation configuration, jobs,
receipt schemas, credit split, schedule, failure/repair flow, release status and rollback.
Replace the inaccurate `-Suites e2e` manual-execution claim. Stage `ROUND:` text remains
semantically unchanged; no need to duplicate or redesign it. Mark deployment unqualified
until real S5 evidence updates the docs. Tests: existing Interim readiness/policy/land guards,
plus binding/default checks in `InterimVerificationReadinessTests` if configuration changes.

### S5: Register and qualify the master backstop; activate the pilot

This is commissioned operator-run activation under CARD-0599, carrying CARD-0545 S6 and
CARD-0544 S6's existing acceptance. It is a required dependent dispatch, not work this Plan
or a Code delegate may claim performed. Record each operation and real outcome in the
qualification/pilot artifacts above. Do not close CARD-0599 after S4.

1. Confirm publication and canonical checkout contain reviewed S1-S4. Reconcile any
   out-of-band push with the main checkout before deployment. Collect operator custody
   inputs: watchdog deploy profile/independent host, scoped expiring Windmill token files,
   authorized Telegram destination and reader credentials/session, named acknowledging
   operator, GitHub identity allowed to create refs/releases. Missing input is a named
   activation blocker; it does not justify another replacement infrastructure card.
2. From the canonical checkout preview `register-release-gates.ps1` using the untracked
   registration profile, save its non-secret desired/readback diff, then `-Apply` to
   register all three scripts and disabled schedules. Record script content digest/server
   hash, cron, timezone, tag, enabled flag, args, worker version/ping and HTTP job path.
3. Deploy the watchdog via its existing profile preflight/`-Deploy` flow with destination
   initially unset. Run self-check, observe instance/snapshot, place desktop token and
   readiness-config. Enable the readiness schedule; verify a real tick writes a fresh
   monitor. It may correctly be **unready** before the first scheduled green; do not
   require or fabricate ReadyForDeferral at this step.
4. Invoke the registered master script manually through Windmill. Its result must say
   `trigger=manual` and give a native run ID; run all seven required suites, retain expanded
   counts and zero failures. A manual green does not advance master scheduled credit.
5. Enable the 00:30 master schedule and observe a subsequent genuine scheduled full green.
   Confirm schedule path/job result/native run, local due date and last-green/monitor
   identities. Local/injected runs cannot substitute for the overnight boundary.
6. Complete CARD-0545 F-1..F-6 on qualification-only targets: SSH hop failure, queue/worker
   loss, stub Windmill outage, held reader, crash cuts, missing heartbeat; export failure
   and recovery receipt identities. Do not stop the production worker, sshd or Windmill.
   Operator performs reader login and explicitly authorized live qualification notice.
7. Commit the accepted qualification document; publish its trusted receipt. Check the
   server build SHA/capabilities from the canonical restart procedure, apply the D-1
   configuration, and opt in one bounded repair card via a revision. Record baseline,
   selection and final-review owner. Exercise refusal of Interim land in an isolated
   repository; obtain real Final/Full Review and normal exact-SHA land in the pilot.
8. Verify disabling new admission/setting pilot roles FullOnly restores the default while
   preserving existing latches. Re-enable only the qualified pilot after that check.
   Record comparable Interim/Final costs at one committed source; no dollar savings claim
   without command-correlated usage. Broader opt-in is a later evidence-based decision.

### S6: Qualify and enable periodic RC publication

1. With the RC schedule still disabled, run a manual candidate through the new registered
   RC script. Measure full automated E2E class costs and required/excluded UID census;
   retain browser/runner/DB/teardown evidence. Any inherited failure gets a targeted base
   reproduction and linked repair, then a new master-derived candidate; no red waiver.
2. Require one complete full-profile green and successful exact-SHA tag/draft/assets/publish
   readback. Test crash/idempotency with offline fixtures, not deliberate live corruption.
   Exercise `release-status.ps1` against the isolated candidate server and the actual dev
   stack; a newer master build must not be falsely labelled as that release.
3. Enable the two-slot RC schedule and observe both real slot jobs: for each, record cut,
   tested SHA, all suite counts and published release, or an explicit no-new-SHA/busy/red
   outcome. At least one scheduled full green and published release is required. A slot
   that never fires fails operational acceptance. Explain capacity/CI overlap failures.
4. Record release job health/readback in the qualification artifact. The existing independent
   watchdog continues to monitor master only; RC job failure is visible in Windmill and a
   dedicated `release-gate` board incident. Extend `nightly-report.ps1` to isolate incident
   labels by lane so RC green cannot close a master nightly incident. A pre-native RC
   failure attempts the same incident report; reporting failure remains a failed job with
   a local durable record. Do not claim independent RC scheduler-outage notification.
5. Commit evidence/doc status updates and report the accepted scope. Periodic successful
   releases are established only after the above evidence, not when payload JSON lands.

## Verification requirements for TestDesign

TestDesign is a separate stage: finalize executable method names, expanded row floors,
guard-to-positive-control mappings and the eligibility census, then add the canonical
`## Verification design` section. Do not dispatch Code solely from this provisional manifest.
The following IDs are the required ordinary evidence; preserve them in that section.

| ID | Required behavior and decisive negative | Planned test home |
|---|---|---|
| V-1 | Schema/profile required-suite selection; stale hash/unknown profile/missing E2E rejected; OptIn E2E is required while named live exclusions remain explicit; new unclassified UID cannot vanish. | `ReleaseGatePolicyTests` |
| V-2 | Green matrix: master/scheduled/nightly vs valid RC/rc/rc; wrong trigger/ref/SHA/profile, string booleans, failed exit, NoReport and diagnostic subsets fail. Prove RC success/failure leaves master files byte-identical. | `ReleaseGateRunTests` |
| V-3 | Shared lock serializes both roots; continuation preserves owner and expected SHA/profile; live owner never stolen; moved remote ref and mismatched resume refuse. | `ReleaseGateRunTests` |
| V-4 | Production E2E executor launches only eligible roster, waits for owned cleanup, performs prerequisites and consumes fresh TRX + diagnostics. Remove one expanded required case, mark it skipped, or omit execution diagnostics: incomplete. | `ReleaseGateE2EEvidenceTests` and `ReleaseGateIsolationTests` |
| V-5 | Registration preview writes nothing; apply/readback exact; repeat is idempotent; wrong hash/path/cron/tag, missing worker, auth failure or concurrent revision refuses; manual job never gains scheduled provenance. | `ReleaseGateRegistrationTests` |
| V-6 | Coordinator pins master once and repairs use a new candidate; publication gates on that SHA/full profile. Recover each cut at journal write, branch/tag push, draft create, asset upload and publish, including lost responses, without moving refs or duplicate releases. | `ReleaseGatePublicationTests` |
| V-7 | Published manifest/tag/API SHA agree; newer/unknown live SHA is not labelled latest release; capability list comes from candidate identity. No release operation restarts the stack. | `ReleaseGateStatusTests` |
| R-1 | Existing nightly run/coverage/health/result-line/notification/readiness contracts, default manual exclusions, ASCII PS 5.1 wrappers, and isolated-tree guard remain valid. | `NightlyScriptsTests`, `NightlyVerificationContractTests`, `NightlyWatchdogCoreTests`; standalone `test-nightly-run.ps1` / `test-nightly-tests.ps1` C487 cases are not all covered by those C# wrappers and must also run |
| R-2 | Default disabled, repo/project/receipt/hash/watchdog/freshness identity guards; lost readiness holds queued Interim; Final latch still refuses Interim land. | `InterimVerificationReadinessTests`, `InterimVerificationPolicyTests`, `InterimVerificationLandGuardTests`, `InterimVerificationLandGitTests` |
| R-3 | A real headless browser can use the built bundle and owned app/DB/runner; teardown completes; wrong/stale bundle and production endpoint are refused. | `SmokeE2ETests`, `ReleaseGateIsolationTests`, `IsolatedSessionRunnerTeardownTests` |
| R-4 | RC failure/recovery uses its own incident and cannot update or close the nightly incident; report failure cannot yield green; existing board revision/assignment rules remain intact. | `scripts/test-nightly-report.ps1`, plus its new C599 lane cases invoked by `ReleaseGateRunTests` |
| V-8 | Real registration, manual/scheduled master greens, CARD-0545 F-1..F-6, receipt then pilot admission/Final. | S5 operational evidence, never a mocked receipt |
| V-9 | Full eight-suite automated RC green, expanded coverage census, public release readback, both real schedule slots observed. | S6 operational evidence, never a smoke-only release |

Delivery/persistence boundaries to exercise: Windmill schedule -> SSH -> native completion
record -> job-result readback; native summary -> lane-specific board report; candidate journal
-> remote ref; publish journal -> remote tag/draft/assets/published release; watchdog ledger
-> existing recipient reader (carried S5). At every boundary, request acceptance is distinct
from readback. Offline injected API responses test retry logic, not live delivery qualification.

TestDesign must ensure fixture assertions call production entrypoints and can fail when the
guard is removed. Deliberate positive controls belong to post-land Mutation, method-scoped;
they are not run in this Plan. Preserve all inherited S5 qualification controls rather than
renumbering them or assuming their landed implementations were operationally qualified.

## Failure handling, rollback and completion

- Failed RC: retain evidence and incident, publish nothing, fix through master and recut.
  Failed publication after green: resume the same journal; never change tested source.
- Lost master readiness: existing Interim admission/queued launch fails closed. Existing
  owner latches persist and require Final. RC green cannot restore eligibility.
- Roll back activation by setting pilot policies FullOnly, `Enabled=false`, and disabling
  RC scheduling/publication. Keep the master/readiness jobs and watchdog running for evidence.
  Preserve tags/releases, candidate journals and qualification receipts; no destructive rollback.
- Changed script/profile/hash: old qualification does not silently apply. Requalify identity
  and readiness before further Interim admission; old releases retain their original manifest.
- Code can complete S1-S4 after ordinary Review; CARD-0599 operating acceptance remains pending
  until S5/S6 receipts exist. If credentials/host/qualification failures block, report the exact
  missing item and retain the card obligation. Do not mark the release-gate model operational.

Plan validation: source tracing and live card read only. No build/test counts or live activation
success are claimed. TestDesign owns the final verification design and checkpoint manifest.

## Verification design

TestDesign: 2026-09-22, task 292f5645, inspected plan commit
4c4f8951fcd081533485f3ccc26079d1c4b6a634. This section supersedes the provisional
verification manifest and cost only; D-1..D-10 and S1..S6 remain the fix design.
Code may implement S1-S4 using the closed checkpoints below. Neither this document nor
ordinary green satisfies S5/S6. All timings below are estimates; no tests ran in TestDesign.

### Inspection

Bodies, not just names, were read before defining the cases:

| Bodies read | Boundaries and disposition |
|---|---|
| scripts/nightly-run.ps1, scripts/lib/nightly-run-impl.ps1, scripts/nightly-tests.ps1, scripts/lib/nightly-tests-impl.ps1, scripts/lib/nightly-common.ps1, scripts/lib/nightly-coverage.ps1, nightly-execution-policy.json; scripts/test-nightly-run.ps1 and test-nightly-tests.ps1, scripts/lib/c487-harness.ps1 setup/result writers | Profile/credit, wrapper parameters, clone/lock ownership, discovery and execution reconciliation -> V-1..V-4, R-1. Existing run G003/G009/G010/G013/G022..G025 and tests G032/G048/G050/G052..G055/G057..G058 include unconditional or self-comparing assertions: retaining their PASS totals does not prove these guards. New tests invoke production and observe effects. |
| Scripts/ScriptHarness.cs, NightlyScriptsTests.cs, NightlyVerificationContractTests.cs; notification/delivery bodies in NightlyVerificationContractTests.C545.cs; C545World.cs initialization, transport/reader/restart helpers; NightlyWatchdogCoreTests.cs outage matrix, persistence, receipt, HTTP and snapshot tests | Production loop plus SQLite with controlled transport/reader and clock -> R-1, V-8; actual recipients remain S5. ScriptHarness has a 120-second child deadline; split bounded cases rather than widen it. |
| scripts/test-nightly-report.ps1, nightly-report.ps1 and HTTP fixture store | Lane selection, assignment/revision and report acknowledgment -> R-4. G087..G094 include stub assertions; HTTP acceptance currently lacks the required persisted recipient readback. New report fixture must read through real Program/DB and exercise lost responses. |
| scripts/windmill/README.md, master/readiness registration payloads, docs/nightly-watchdog.md; CARD-0545 F-1..F-6 and qualification notice requirements | Registration and scheduled provenance -> V-5/V-8/V-9. Installed Windmill schema capture and real schedule/job readback remain explicit S5 setup, not claimed from checked-in JSON. |
| scripts/fixtures/nightly/c487-probe/Probe.cs and project/build imports; saved discovery/execution fixture shapes; tests/Shared/TestClassificationGuardTests.cs | Actual pinned TUnit/MTP discovery expansion -> V-1/V-4. Existing probe conflates Explicit and OptIn; extend the probe with separate OptIn-only and Explicit cases, inherited tests, Arguments, MethodDataSource and Slow. |
| E2E/Fixtures/AntiphonAppFixture.cs, PlaywrightFixture.cs, SharedApp.cs, IsolatedSessionRunner.cs, its teardown helper, IsolatedSessionRunnerTeardownTests.cs; SmokeE2ETests.cs; PtyBackendEnvGuard.cs | Actual app/DB/browser/runner, environment and final census -> V-4/R-3. Current smoke proves HTTP health, not persisted DB behavior or bundle identity. Current teardown swallows unreachable-runner and join failures and may delete evidence without census; the new tests must expose those cases. Ordinary fixture messaging is not uniformly refusing today. |
| E2E/AgentTaskLandDeliveryE2ETests.cs receipt/recovery methods; Fixtures/LandDeliveryFixture.cs startup, ReceiptAsync and AssertOnePromptAsync | Real Program, persisted queue, real native FakeGrok and complete UserPrompt -> V-8/V-9. Retain its production-runner refusal and owned resources; no provider credentials. |
| Application/InterimVerificationReadinessTests.cs including StateFixture; InterimVerificationPolicyTests.cs and helpers; InterimVerificationLandGuardTests.cs and helpers; InterimVerificationLandGitTests.cs; VerificationRoundDispatchTests.cs | Default/qualified identity, exact temporal boundaries, admission, queued loss, transaction/latch and Final before remote advance -> R-2. Dispatch tests are required additions to the provisional scope. |
| Application/VerificationRoundDeliveryTests.cs: CompletionReceipt, CompletionRecovery, BriefHandoffRecovery, CompletionReceiptWholeWire, StampIsNotReceipt, SingleLogicalNote, CompletionLinkValidatesIdentity, AssertReceivedOnceAsync; TestHelpers/C544DeliveryRig.cs and C544World.cs setup/settlement helpers | Real application queue/scanner/settlement with controlled adapter -> V-8/R-2. Receipt helper checks one complete wire-bearing UserPrompt, attempt floor, destination, notification/queue identities and spill hash. This is not proof of real provider or PTY behavior. |
| Native census exceptions below: ClaudeVerifiedDeliveryTests, OrchestratorWorkspaceLayoutCanaryTests, Card0490NativeCustody/Handoff/GuestGuard/ExecutionTests; TestDbFixtureLazyInitializationTests child gating; messaging TelegramContract/TelegramLiveChatConformance and disposable-broker setup; platform/ACL/modern-ConPTY skip branches | Exact per-row exclusions versus missing prerequisites -> V-1/V-4. No class-wide exclusion of mixed offline/live tests. |

Paths in this table are relative to tests/Antiphon.Tests or tests/Antiphon.E2E where
qualified by Scripts/, Application/, TestHelpers/ or E2E/. Existing fixture bodies were the
nearest fixtures for every new test file; new names below are implementation contracts,
not claims that those methods already exist.

**Fixture delivery required in S1-S4.** Add nine C599 classes: ReleaseGatePolicyTests,
ReleaseGateRunTests, ReleaseGateE2EEvidenceTests, ReleaseGateRegistrationTests,
ReleaseGatePublicationTests, ReleaseGateStatusTests and ReleaseGateActivationTests in
Antiphon.Tests; ReleaseGateIsolationTests and ReleaseGateReportDeliveryTests in
Antiphon.E2E. The exact 42 new method names are enumerated by the PC rows. Each method
executes all its guard rows, with one valid control and each specified invalid row.
Use named assertion messages "C599 G-n", retained per-row evidence and the actual returned
predicate/remote state/transcript. Printing a PASS string without that assertion is forbidden.

Extend ScriptHarness with bounded C599 case selection/required-row checking; invoke real
production scripts through pwsh using argument arrays and a private root. Inject clock,
process, GitHub and Windmill clients at I/O boundaries, not a replacement green predicate.
Use real temporary bare Git remotes and an independent observer clone for candidate/tag
claims. New persistent API fixtures must separate write acceptance from stored state and
GET/download visibility, support fail-before-write and commit-then-lose-response, and survive
coordinator restart. Scripts must receive private roots explicitly; no default live paths.
Registration's contract fixture is captured from the installed mc API during Code/S5 and
checked against request/response shapes, with tokens replaced by sentinels. S5 still verifies
the installed service. Existing fake-Git c487 fixtures alone cannot prove immutable refs.

ReleaseGateActivationTests reuses C544World/C544DeliveryRig and the real readiness reader,
clock and private files. Do not change the existing accepted Found/Full repair baseline
semantics: S5 deliberately chooses a Clean baseline for its pilot. New E2E fixtures reuse
owned Program/Postgres/runner/FakeGrok, a refusing external adapter, fresh DB schema and
assembly process limiter. They must demonstrate actual persisted board/DB writes and
bundle bytes served by the app. Capture cleanup receipt before deleting owned directories.
All prerequisite failures are failures/incomplete evidence, never silent exclusions.

**Eligibility census fixed by this design.** Code emits an expanded UID manifest from
actual discovery at the candidate SHA; the static census here specifies dispositions.
Do not invent a numeric expanded test count from source attributes. Every discovery UID
must have exactly one disposition with policy reason/owner, and each required UID exactly
one execution/chunk. Preserve raw categories in both JSON and diagnostic readers. The
runtime count and digest become a manifest artifact and must be equal at execution reconcile.

| Suite/host boundary | Required versus excluded |
|---|---|
| Default nightly | Preserve the existing seven suite IDs and existing default manual behavior; schema-v2 RC eligibility never changes nightly OptIn handling. |
| RC E2E | Required existing OptIn classes: AgentE2ETests, AgentTaskLandDeliveryE2ETests, BoardE2ETests, CardCliE2ETests, ChannelE2ETests, ContractSnapshotTests, DirectoryAutocompleteE2ETests, DispatchBaseWarningDeliveryE2ETests, IsolatedSessionRunnerTeardownTests, OrchestratorE2ETests, OutputDistillationCanaryGuardTests, ProjectDeleteE2ETests, ScheduleCliE2ETests, SmokeE2ETests, WorkflowDeleteTests, WorkflowOutputTests, WorktreeRetirementDeliveryE2ETests. Also require PtyBackendEnvGuardTests and TestClassificationGuardTests (19 existing required classes), plus both new C599 E2E classes (21). Run metadata/classification guards in a separate first process before SharedApp initialization. |
| RC E2E exclusions | Only DelegationPipelineE2ETests, DelegationSequencingE2ETests and OutputDistillationApplyCanaryTests: real headed provider/distiller approval, owned by their canary workflows. OptIn alone is insufficient to exclude any other E2E UID. |
| Agents live exclusions | Card0168InteractiveProbeTests, ClaudeAdapterIntegrationTests, ClaudeHerdrRealCliStubProxyCanaryTests, ClaudeRealCliStubProxyCanaryTests, CodexAdapterIntegrationTests, CodexBootWedgeProbeTests, CodexCommandLengthSessionTests, CodexHerdrRealCliStubProxyCanaryTests, CodexRealCliStubProxyCanaryTests, GrokHerdrRealCliStubProxyCanaryTests, GrokRealCliStubProxyCanaryTests, SpecialistToolPolicyTests, GrokRulesAutoCompactionCalibrationTests, GrokRulesCompactionAcceptanceTests, GrokRulesDispatchAcceptanceTests, GrokRulesHerdrAcceptanceTests, GrokRulesLiveMappedDispatchTests, GrokRulesNativeReadWireTests, SlashCommandMenuReconciliationTests: opt-in real-provider/interactive workflows. Also exclude exactly ClaudeEffortPromptCanaryTests.Real_effort_picker_preserves_xhigh_and_reaches_an_empty_composer: body invokes the live gates despite lacking class OptIn metadata. |
| Agents.Pty live exclusions | ClaudeAppendSystemPromptCanaryTests, ClaudeCompactionCanaryTests, ClaudeComposerCaptureProbeTests, ClaudeComposerRenderCanaryTests, ClaudeDangerousTests, ClaudeHeadedTests, ClaudeHookAdditionalContextCanaryTests, ClaudeInputProbeCanaryTests, ClaudeInteractionTests, ClaudeInterruptCanaryTests, ClaudeLocalCommandCanaryTests, ClaudeOverlayCanaryTests, ClaudePasteLossCanaryTests, ClaudeRemoteControlAtStartupCanaryTests, ClaudeRemoteControlMenuCanaryTests, ClaudeSignalCanaryTests, ClaudeSubmitConfirmCanaryTests, ClaudeSubmitContractLiveTests, ClaudeTrustPromptCanaryTests, ClaudeTuiModeTests, CodexCanaryTests, CodexComposerCanaryTests, CodexDoneDetectionCanaryTests, CodexMcpBootProbeTests, CodexOverlayCanaryTests, FakeVsRealClipParityTests, GrokCanaryTests, GrokNativeSessionCanaryTests, GrokQuestionPopupCanaryTests, GrokSignInCanaryTests, GrokSubmitWhileWorkingCanaryTests, GrokUsageOverlayCanaryTests, PtyInputLossExperiments, PtyPasteMarkerExperiments: separate real-provider/headed experimental workflows. |
| Mixed native classes | ClaudeVerifiedDeliveryTests.Evidence_appears_then_submit_completes_a_turn: fakeclaude/short argument row required; claude short/hugeLine/multiLine/batch rows excluded as live. Select required UID, never the whole mixed class. OrchestratorWorkspaceLayoutCanaryTests: Claude_loads_parent_CLAUDE_md_from_a_nested_checkout and Claude_sibling_import_is_dropped_without_the_forward_slash_approval excluded as live; offline Codex_prompt_input_is_bounded_at_the_nested_checkout_git_root and Grok_inspect_is_bounded_at_the_nested_checkout_git_root required. Missing their CLI prerequisite is incomplete, not automatic exclusion. |
| SessionRunner live/platform | Exclude HerdrGrokNativeSessionLiveTests, HerdrLabelFollowLiveTests, HerdrNamedTabPlacementLiveTests (live desktop); LinuxPhoneHomeRunnerTests (Linux qualification). All remaining automated Windows cases required. |
| PtyHost platform | Exclude LinuxPtyHostLauncherTests and exactly ShadowCopyStoreTests.Linux_copy_preserves_execute_mode (Linux qualification). Windows methods in mixed classes remain required. |
| CARD-0490 native/QEMU | Exclude Card0490NativeCustodyTests (4 QEMU placeholders), Card0490NativeHandoffTests (5 QEMU placeholders), Card0490NativeGuestGuardTests (7 Linux/QEMU controls). In Card0490NativeExecutionTests exclude only Input_reparse_is_rejected_before_write, Evidence_reparse_is_rejected_before_write, Launch_projection_is_direct_and_inherited, Missing_final_frame_invalidates_complete_prefix, Canceled_run_waits_for_owned_join (5 native placeholders). All deterministic host-policy methods stay required. Owner: CARD-0490 SourceLanding native qualification. |
| Child-only tests | TestDbFixtureLazyInitializationTests.Child_db_free_selection_touches_no_database, Child_mixed_consumers_initialize_once and Child_post_start_fault_is_terminal are excluded from top-level RC selection only. Required parent methods launch these exact child tests and retain results; never exclude the entire class. |
| Messaging | Require fake-server Telegram contract/conformance legs; inherited live Telegram credentials cleared, so no claim of live delivery. Require disposable Redpanda setup for InboxConsumerServiceTests and KafkaConsumerGroupObservationTests using suite-scoped ANTIPHON_BROKER_TESTS=1 and the owned endpoint; missing Docker/broker is red, never fallback to a live broker. |
| Other prerequisites | Slow is required. Windows modern ConPTY, staged FakeClaude/FakeGrok/node probes, git/pwsh, installed offline CLI probes, PDF browser, Docker and required ACL support are prerequisites. RunnerProcessProbeTests ACL skips and AgentTuiSecretProtector capability skips remain required and block RC if skipped. Client build/lint/Vitest and complete script census are required. |

Source metadata and skip-body audit supplies this census; actual expanded UID counts are
execution evidence, produced by Code/qualification. A new class, expansion not attributable
to its committed required method, changed exclusion or absent expected class fails closed.
Changes to data-source values in a known required method are included in its fresh expanded
required set and policy/run digest; they cannot disappear during reconciliation.

Boundary coverage is one-invalid-field-at-a-time against a valid control, plus interacting
pairs: credit kind x profile x trigger x branch; master due-day x newer failure x fresh RC;
busy/eligible x every handoff cut; mixed OptIn/Explicit x argument expansion x platform;
TRX/diagnostics/discovery x wrong-run identity; current-policy x published-state x master SHA.
Use exact clock equalities at age 0, 60 minutes and one tick outside, future one tick,
watchdog 20 minutes and one tick outside, London 07:59:59/08:00 and both DST transitions.
A full Cartesian product of unrelated invalid fields is excluded: a conjunctive guard's
single-field negative proves refusal, while the listed pairs cover shared-state/ordering
interactions. Test fixtures must keep all other guards valid when testing one negative.

**Executable row floors.** The 42 C599 methods below each have one TUnit method node
unless Code deliberately expands Arguments. Their internal scenario floors total **484**;
these are assertions over independent production invocations, not extra TRX pass counts.
Report internal rows separately from expanded TUnit counts. All named boundary combinations
and guard negatives remain required even if their implementation produces more than this
floor. A floor is not permission to delete a specified row.

The larger matrices are concrete: CreditIdentity covers 2 credit kinds x 2 profiles x
3 triggers x 4 refs (master, origin/master, feature, valid candidate) plus four malformed/
foreign identity rows. ApplyReadback crosses three script and three schedule writes with
four persistence cuts (before write, commit-before-response, before readback, before local
receipt save) and ready/unavailable recipient = 48, plus twelve identity/enablement rows.
Each publication handoff uses the same four cuts x two recipient states, including each
asset separately. CompletionHandoffs crosses eight application handoffs x two caller
states x crash/enqueue-failure = 32, plus five foreign/old/incomplete receipt controls.
Some cuts have no enqueue operation; inject the persistence failure at that handoff
instead, never invent a queue acknowledgment. FinalRecovery has five pre-advance cuts x
four review invalidations plus one valid recovery. Clock rows use the boundaries above.

| Exact new method | Minimum internal scenario rows |
|---|---:|
| ReleaseGatePolicyTests.C599_ProfileSchema | 4 |
| ReleaseGatePolicyTests.C599_ProfileSuites | 4 |
| ReleaseGatePolicyTests.C599_MetadataParsers | 12 |
| ReleaseGatePolicyTests.C599_EligibilityCensus | 12 |
| ReleaseGatePolicyTests.C599_ExpandedCoverage | 9 |
| ReleaseGateRunTests.C599_CreditIdentity | 52 |
| ReleaseGateRunTests.C599_CreditVerdicts | 16 |
| ReleaseGateRunTests.C599_MasterStateIsolation | 6 |
| ReleaseGateRunTests.C599_ParameterHops | 17 |
| ReleaseGateRunTests.C599_CloneOwnership | 5 |
| ReleaseGateRunTests.C599_SharedLock | 8 |
| ReleaseGateRunTests.C599_Continuation | 4 |
| ReleaseGateE2EEvidenceTests.C599_Prerequisites | 8 |
| ReleaseGateIsolationTests.C599_OwnedServices | 3 |
| ReleaseGateE2EEvidenceTests.C599_EnvironmentIsolation | 3 |
| ReleaseGateIsolationTests.C599_RefusingAdapters | 2 |
| ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts | 5 |
| ReleaseGateIsolationTests.C599_CleanupEvidence | 4 |
| ReleaseGateRegistrationTests.C599_Preview | 2 |
| ReleaseGateRegistrationTests.C599_ApplyReadback | 60 |
| ReleaseGateRegistrationTests.C599_Concurrency | 2 |
| ReleaseGateRegistrationTests.C599_ScheduleProvenance | 8 |
| ReleaseGatePublicationTests.C599_CandidateIdentity | 10 |
| ReleaseGatePublicationTests.C599_CutRecovery | 10 |
| ReleaseGatePublicationTests.C599_PublicationGate | 11 |
| ReleaseGatePublicationTests.C599_TagRecovery | 11 |
| ReleaseGatePublicationTests.C599_DraftRecovery | 10 |
| ReleaseGatePublicationTests.C599_AssetRecovery | 22 |
| ReleaseGatePublicationTests.C599_PublishRecovery | 10 |
| ReleaseGatePublicationTests.C599_ManifestAllowlist | 2 |
| ReleaseGateStatusTests.C599_StatusIdentity | 5 |
| ReleaseGateStatusTests.C599_NoActivation | 2 |
| ReleaseGateRunTests.C599_ReportLane | 3 |
| ReleaseGateReportDeliveryTests.C599_ReportReadback | 8 |
| ReleaseGateReportDeliveryTests.C599_ReportRecovery | 26 |
| ReleaseGateActivationTests.C599_Defaults | 2 |
| ReleaseGateActivationTests.C599_Admission | 18 |
| ReleaseGateActivationTests.C599_QueuedReadiness | 8 |
| ReleaseGateActivationTests.C599_RollbackLatch | 2 |
| ReleaseGateActivationTests.C599_FinalRecovery | 21 |
| ReleaseGateActivationTests.C599_CompletionHandoffs | 37 |
| ReleaseGateActivationTests.C599_MasterDueBoundary | 20 |

### Delivery inventory

These are outcome-delivery obligations, not "request sent" assertions. For every row,
retain the durable identity through producer, persistence, restart and recipient observation.

| ID / producer -> recipient | Durable identity and persistence | Recovery and decisive recipient evidence |
|---|---|---|
| DL-1 Windmill schedule -> desktop worker -> SSH native runner -> Windmill result reader | Workspace + registered script hash/path + schedule path/local slot + job UUID + nativeRunId + lane/profile/policy + full SHA. Journal correlation before launch; native completion record before result line. | Contract fixture crosses worker busy/already eligible with queue refusal, SSH failure, crash before native journal, after completion before response and lost response. Correlation cannot be manufactured from caller trigger. S5 actual master job and S6 actual RC slot job must be fetched by ID and agree with native persisted result and final destination receipts; a queued job/result-line alone proves neither test nor release completion. F-1/F-2 qualify the real queue without disrupting production. |
| DL-2 Native/coordinator summary -> lane-specific board incident/discussion | Repository/project + lane + nativeRunId (or pre-native candidate/slot ID) + summary digest + incident/discussion identity; durable pending intent before HTTP. | ReleaseGateReportDeliveryTests invokes production report script against real isolated Program/DB. Ready and temporarily unavailable recipient; reject-before-write, commit-then-lose-response, readback outage, crash before/after each durable step, stale revision and concurrent assignment. Restart same intent. GET via recipient API must contain the whole matching persisted discussion and correct revision/status; one logical report. RC recovery cannot touch master incident. Persistent failure remains pending/non-green. No fake server PUT/POST response is receipt. |
| DL-3 Coordinator -> remote candidate branch | Candidate ID, canonical repository identity, immutable candidate ref and pinned full SHA; local atomic journal before remote push. | Real bare remote plus separate observer clone; unavailable remote/ready remote crossed with before-push and accepted-push/lost-response cuts. Resume same journal, resolve remote before retry, refuse changed SHA. Recipient observer fetch verifies exact branch. No force update or implicit new candidate after ambiguous response. |
| DL-4 Publisher -> Git tag -> GitHub draft/assets -> published release | Same candidate/native run + manifest digest + UTC version tag + peeled SHA + release ID + asset digests; journal each completed transition before advancing. | Ready and temporarily unavailable GitHub fixture at each of tag push, draft creation, each asset upload and publication; fail-before-write, commit-then-response-loss, crash before/after journal save, then restart from disk. Separate recipient GET/download and remote tag peel are decisive, ending in published=false/true as appropriate. Actual S6 release URL, published state and downloaded asset hashes must agree with the tested SHA. A tag or draft by itself is insufficient. |
| DL-5 Watchdog -> transport -> authorized independent reader -> ledger receipt | Namespace + outage ID + nid + attempt + due day + native run/SHA + destination + whole-body hash. Ledger intent before send; distinct acceptance and received state. | Existing C545World covers eligible/held readers, failed sends and four crash cuts; S5 preserves F-1..F-6 and production qualification notice unchanged. Actual reader-observed whole message and ledger receipt are required; Sent, Telegram transport ack or read marker without body is not delivery. Failure/recovery retain linkage. |
| DL-6 Pilot stage/land completion -> real application queue -> caller session | Task/root/outcome + notification ID + queue ID + snapshotted destination + wire digest + attempt baseline sequence, with spill content hash where used. | C544 production settlement/scanner/queue tests and new CompletionHandoffs cross busy/already eligible with obligation save, task settlement commit, queue insert failure, queue commit/lost wakeup, rendering/spill save, attempt save, submitted prompt/receipt save failure. Restart services over same DB. End in exactly one matching complete UserPrompt above the attempt floor and correct destination; do not inject the expected transcript directly. Real FakeGrok E2E remains in the full RC roster and actual pilot caller receipt remains S5. |
| DL-7 Registration apply -> Windmill stored definitions/schedules | Workspace/path + expected old revision/hash + desired script hash and schedule definition. Preview has no writes; journal/readback separates requested/applied/qualified. | Stateful API fixture injects before-write failure, commit/lost-response and crash after every script/schedule write, with ready/503 recipient. Re-run apply must discover existing same objects, preserve enablement and refuse concurrent different revisions. S5 independently lists/reads all three actual definitions, schedule enablement and worker eligibility; enablement is followed by real job receipt in DL-1. |

DL-2 adds persisted board readback to the current report harness; DL-1/DL-7 add trusted
schedule-context and installed-API contract seams; DL-4 adds persisted remote-state fixtures.
These are explicit Code setup obligations needed to verify the already planned paths.
If implementation cannot expose these seams, return to Plan; do not mark the path verified
from logging, mocks of the final predicate, queue insertion or an acknowledged request.

Substitutes: C544DeliveryRig uses a controlled adapter and real application queue/DB, proving
application persistence and transcript matching but not native PTY/provider behavior.
LandDeliveryFixture adds native FakeGrok and real runner, not a real provider. C545World's
fake recipient chat proves independent importer logic, not Telegram routing or a human
reading it. Windmill/GitHub fixtures prove deterministic retry and identity behavior, not
registration, SSH, worker availability or public visibility. Bare Git proves Git recipient
state, not GitHub release state. Only S5/S6 live receipts close those limits.

### Proves it works now

- V-1: schema-v2 profiles and exact eligibility census | production policy/parser plus real TUnit probe | ReleaseGatePolicyTests.C599_ProfileSchema, C599_ProfileSuites, C599_MetadataParsers, C599_EligibilityCensus, C599_ExpandedCoverage | seven/eight suite sets, explicit dispositions, identical parser UID sets, every required expanded row executed once; every negative row refuses.
- V-2: separate master/RC credit | production wrapper/predicate/state files | ReleaseGateRunTests.C599_CreditIdentity, C599_CreditVerdicts, C599_MasterStateIsolation, C599_ParameterHops | full identity reaches leaf; green control for each lane, invalid combinations non-green, all four master files byte-identical after RC success/failure/recovery.
- V-3: owned clone and shared serialization | production scripts with real local child processes and Git | ReleaseGateRunTests.C599_CloneOwnership, C599_SharedLock, C599_Continuation; ReleaseGatePublicationTests.C599_CandidateIdentity | exactly one lane enters; no live-age stealing, mismatched continuation or changed ref; all children joined before release.
- V-4: complete E2E execution evidence | production executor/probe and real isolated E2E fixtures | ReleaseGateE2EEvidenceTests.C599_Prerequisites, C599_EnvironmentIsolation, C599_ExecutionArtifacts; ReleaseGateIsolationTests.C599_OwnedServices, C599_RefusingAdapters, C599_CleanupEvidence | actual prereqs/DB/bundle/runner/browser plus current UID/TRX/diagnostic/cleanup receipts; no skipped, absent or unowned-resource green.
- V-5: registration and trusted scheduled identity | production registration script with persistent installed-schema fixture | ReleaseGateRegistrationTests.C599_Preview, C599_ApplyReadback, C599_Concurrency, C599_ScheduleProvenance | preview zero writes, exact stored readback, repeat idempotent, disabled creation, changed/auth-invalid context refuses.
- V-6: immutable candidate and publication | real Git + production coordinator + stateful remote fixture | all eight ReleaseGatePublicationTests methods in PC inventory | one pinned SHA, one create-only branch/tag/release identity across every crash/ambiguous response; only verified published assets yield success.
- V-7: exact running release identity | production status script and isolated version endpoint | ReleaseGateStatusTests.C599_StatusIdentity, C599_NoActivation | exact SHA/tag/manifest agreement, newer SHA unreleased, unavailable service unknown, zero restart/deploy/kill operations.
- V-8: qualified master backstop and bounded pilot | live S5 acceptance | registration readbacks; real manual and scheduled master runs; unchanged F-1..F-6 and qualification notice; committed trusted receipt; one opted-in pilot ending in Final/Full Review and exact-SHA land with complete caller receipt | no activation credit from offline doubles, RC green or Interim Review.
- V-9: scheduled full RC publication | live S6 acceptance | manual full eight-suite qualification then both real 08:30/16:30 London jobs, at least one full scheduled green and published release with downloaded assets | explicit outcome for both jobs and exact tested published SHA; absent slot or smoke-only execution fails acceptance.

### Guards the regression

- R-1: existing nightly/watchdog/master semantics | three existing Script test classes and both standalone run/tests harnesses | real required result rows, run/due identity, strict green, ASCII and isolated tree remain valid. Stubs are regression inventory only, not new gate evidence.
- R-2: default/identity/queued-readiness/Final latch and completion delivery | four InterimVerification classes, VerificationRoundDispatchTests, new ReleaseGateActivationTests and three named VerificationRoundDeliveryTests methods | no unauthorized task/session/remote advance; rollback preserves latch; invalid Final cannot land; complete matching UserPrompt receipt required.
- R-3: real browser and resource isolation | SmokeE2ETests, ReleaseGateIsolationTests, IsolatedSessionRunnerTeardownTests | browser health plus persisted DB/bundle and owned runner proof; cleanup/census/join receipts; stale bundle and production endpoint refuse.
- R-4: lane-isolated report delivery/recovery | standalone test-nightly-report, ReleaseGateRunTests.C599_ReportLane, ReleaseGateReportDeliveryTests.C599_ReportReadback/C599_ReportRecovery | RC cannot revise master incident; no board receipt means no report credit; assignment/revision preserved and restarted intent delivers exactly once.

### Guard inventory

The inventory includes newly introduced guards and inherited admission/delivery guards
relied on by this activation. Each independently bypassable field/stage has its own
control. These are local CARD-0599 IDs; inherited CARD-0544/0545 IDs and F-1..F-6 are unchanged.
There are no untested safety guards in the commissioned scope. New methods are required
Code deliverables; Review must reject missing methods/rows before land.

- G-1: D-5: Supported schema only. | PC-1
- G-2: D-5: Known selected profile only. | PC-2
- G-3: D-5: Entire policy participates in hash. | PC-3
- G-4: D-5: Nightly seven-suite contract. | PC-4
- G-5: D-5: RC requires E2E in addition to nightly. | PC-5
- G-6: D-5: Diagnostic subset cannot earn green. | PC-6
- G-7: D-5: Raw metadata survives both discovery parsers. | PC-7
- G-8: D-5: RC automated OptIn is required. | PC-8
- G-9: D-5: Default nightly manual exclusion retained. | PC-9
- G-10: D-5: Live/platform/QEMU exclusions are exact. | PC-10
- G-11: D-5: New UID needs committed disposition. | PC-11
- G-12: D-5: Expanded rows and inherited tests are counted. | PC-12
- G-13: D-5: No missing required UID. | PC-13
- G-14: D-5: No required skip. | PC-14
- G-15: D-5: No duplicate chunk membership. | PC-15
- G-16: D-5: Unknown outcomes fail closed. | PC-16
- G-17: D-5: Nonzero required execution. | PC-17
- G-18: D-2: Credit kind allowlist. | PC-18
- G-19: D-2: Master credit branch identity. | PC-19
- G-20: D-2: Master scheduled provenance. | PC-20
- G-21: D-2: Master nightly profile identity. | PC-21
- G-22: D-2: RC profile identity. | PC-22
- G-23: D-2: RC trigger identity. | PC-23
- G-24: D-2: RC ref syntax and timestamp validation. | PC-24
- G-25: D-2: RC pinned full SHA identity. | PC-25
- G-26: D-2: Run identity correlation. | PC-26
- G-27: D-2: Policy identity correlation. | PC-27
- G-28: D-2: Coverage must be boolean true. | PC-28
- G-29: D-2: Tests must be boolean true. | PC-29
- G-30: D-2: Report must be boolean true with receipt. | PC-30
- G-31: D-2: Native exit must be zero. | PC-31
- G-32: D-2: Evidence is completed and fresh. | PC-32
- G-33: D-2: RC state never writes master state. | PC-33
- G-34: D-3: Profile survives every wrapper hop. | PC-34
- G-35: D-3: Expected SHA survives every wrapper hop. | PC-35
- G-36: D-3: Canonical checkout is forbidden. | PC-36
- G-37: D-3: Linked worktree is forbidden. | PC-37
- G-38: D-3: Unowned clone root is forbidden. | PC-38
- G-39: D-4: Shared lock is atomic across lanes. | PC-39
- G-40: D-4: Live owner cannot be stolen by age. | PC-40
- G-41: D-4: PID reuse is distinguished by start identity. | PC-41
- G-42: D-4: Continuation is authenticated. | PC-42
- G-43: D-4: Continuation expected SHA identity. | PC-43
- G-44: D-4: Lock persists through owned child join. | PC-44
- G-45: D-4: Busy RC defers without retry work. | PC-45
- G-46: D-6: Pinned client-build receipt required. | PC-46
- G-47: D-6: Bundle digest is verified. | PC-47
- G-48: D-6: Build/lint prerequisites must succeed. | PC-48
- G-49: D-6: Matching browser install/launch required. | PC-49
- G-50: D-6: Actual owned Docker/runner startup required. | PC-50
- G-51: D-6: Production runner destination forbidden. | PC-51
- G-52: D-6: Inherited live credentials/approvals removed. | PC-52
- G-53: D-6: Ordinary fixture cannot reach live broker/provider. | PC-53
- G-54: D-6: Fresh TRX belongs to current execution. | PC-54
- G-55: D-6: Execution diagnostics required. | PC-55
- G-56: D-6: Discovery and terminal UID identities agree. | PC-56
- G-57: D-6: Failed fixture is not a skip/green. | PC-57
- G-58: D-6: Runner final census required. | PC-58
- G-59: D-6: Owned child termination is joined. | PC-59
- G-60: D-6: Leak evidence is retained on failure. | PC-60
- G-61: D-7: Preview is read-only. | PC-61
- G-62: D-7: Registration path allowlist. | PC-62
- G-63: D-7: Optimistic script revision/hash check. | PC-63
- G-64: D-7: Exact script readback required. | PC-64
- G-65: D-7: Schedule timezone readback checked. | PC-65
- G-66: D-7: Worker tag and availability checked. | PC-66
- G-67: D-7: Schedules initially disabled. | PC-67
- G-68: D-7: Repeated apply preserves enablement/idempotency. | PC-68
- G-69: D-7: Auth failure fails closed. | PC-69
- G-70: D-7: Schedule authority comes from job context. | PC-70
- G-71: D-3: Master is pinned only once per candidate. | PC-71
- G-72: D-3: Journal precedes candidate remote push. | PC-72
- G-73: D-3: Candidate branch is create-only. | PC-73
- G-74: D-3: Pretest checkout matches pin. | PC-74
- G-75: D-3: Pretest remote ref matches pin. | PC-75
- G-76: D-3: Prepublish checkout matches pin. | PC-76
- G-77: D-3: Prepublish remote ref matches pin. | PC-77
- G-78: D-3: Skip requires an already published release. | PC-78
- G-79: D-8: Publication consumes full-profile green. | PC-79
- G-80: D-8: Manifest native run identity. | PC-80
- G-81: D-8: Manifest bundle digest identity. | PC-81
- G-82: D-8: Tag allocation is journaled before network. | PC-82
- G-83: D-8: Tag is immutable. | PC-83
- G-84: D-8: Remote peeled tag equals tested SHA. | PC-84
- G-85: D-8: Draft creation verifies remote tag. | PC-85
- G-86: D-8: Ambiguous draft create resumes same release. | PC-86
- G-87: D-8: Asset bytes are read back and verified. | PC-87
- G-88: D-8: Published state is read back. | PC-88
- G-89: D-8: Ambiguous publish resumes same tag/release. | PC-89
- G-90: D-8: Public assets use an allowlist. | PC-90
- G-91: D-8/D-9: Capabilities come from candidate build identity. | PC-91
- G-92: D-9: Status matches exact running SHA. | PC-92
- G-93: D-9: Status requires tag/manifest/published agreement. | PC-93
- G-94: D-9: Unavailable release service is unknown. | PC-94
- G-95: D-9: Release/status never activates local stack. | PC-95
- G-96: S6/R-4: Report incident identity includes lane. | PC-96
- G-97: S6/R-4: Board write acceptance is not report receipt. | PC-97
- G-98: S6/R-4: Report run receipt identity. | PC-98
- G-99: S6/R-4: Report intent survives crash before HTTP. | PC-99
- G-100: S6/R-4: Lost board response does not duplicate report. | PC-100
- G-101: S6/R-4: Recovery respects board revision and assignment. | PC-101
- G-102: S6/R-4: Failed reporting stays non-green durably. | PC-102
- G-103: D-3: Trigger survives every wrapper hop. | PC-103
- G-104: D-3: Candidate ref survives every wrapper hop. | PC-104
- G-105: D-7: Schedule cron readback checked. | PC-105
- G-106: D-7: Schedule script target readback checked. | PC-106
- G-107: D-7: Schedule worker tag readback checked. | PC-107
- G-108: D-8: Manifest repository identity. | PC-108
- G-109: D-8: Manifest SHA identity. | PC-109
- G-110: D-8: Manifest policy digest identity. | PC-110
- G-111: D-8: Manifest script digest identity. | PC-111
- G-112: D-8: Manifest build digest identity. | PC-112
- G-113: S6/R-4: Report lane receipt identity. | PC-113
- G-114: S6/R-4: Report project receipt identity. | PC-114
- G-115: S6/R-4: Report whole body digest. | PC-115
- G-116: D-1: Shipped settings remain disabled. | PC-116
- G-117: D-1: Task repository is qualified. | PC-117
- G-118: D-1: Receipt repository matches configuration. | PC-118
- G-119: D-1: Monitor repository matches configuration. | PC-119
- G-120: D-1: Task project matches qualification. | PC-120
- G-121: D-1: Receipt project matches qualification. | PC-121
- G-122: D-1: Read failures cannot admit Interim. | PC-122
- G-123: D-1: Receipt schema supported. | PC-123
- G-124: D-1: Receipt contains recipient evidence. | PC-124
- G-125: D-1: Receipt contains outage/recovery evidence. | PC-125
- G-126: D-1: Scheduled qualification run evidence required. | PC-126
- G-127: D-1: Qualification artifact is committed full identity. | PC-127
- G-128: D-1: Readiness policy hash matches. | PC-128
- G-129: D-1: Readiness script hash matches. | PC-129
- G-130: D-1: Windmill and native run identities correlate. | PC-130
- G-131: D-1: Monitor age at most 60 minutes. | PC-131
- G-132: D-1: Future monitor rejected. | PC-132
- G-133: D-1: Monitor Healthy is true. | PC-133
- G-134: D-1: Monitor ReadyForDeferral is true. | PC-134
- G-135: D-1: Readiness booleans are typed. | PC-135
- G-136: D-1: Receipt and monitor watchdog identity agree. | PC-136
- G-137: D-1/S4: Explicit appsettings and FullOnly defaults. | PC-137
- G-138: D-1: Per-card opt-in gates Interim. | PC-138
- G-139: D-1: Baseline lineage gates Interim. | PC-139
- G-140: D-1: Committed cumulative selection gates Interim. | PC-140
- G-141: D-1: Queued readiness is rechecked at launch. | PC-141
- G-142: D-1: Queued policy/lineage is rechecked at launch. | PC-142
- G-143: D-1: Existing owner latch survives disabling admission. | PC-143
- G-144: D-1: No land evidence for latched owner refuses. | PC-144
- G-145: D-1: Interim round cannot authorize land. | PC-145
- G-146: D-1: Interim scope cannot authorize land. | PC-146
- G-147: D-1: Final has Full ordinary scope. | PC-147
- G-148: D-1: Final is Clean/completed. | PC-148
- G-149: D-1: Final subject is land owner. | PC-149
- G-150: D-1: Final SHA is exact source. | PC-150
- G-151: D-1: Final ref is exact source. | PC-151
- G-152: D-1: Final repository is exact source. | PC-152
- G-153: D-1: Superseded Final cannot authorize land. | PC-153
- G-154: D-1: Recovery revalidates Final before remote advance. | PC-154
- G-155: S5/DL-5: Notification intent precedes send. | PC-155
- G-156: S5/DL-5: Acceptance alone cannot mark received. | PC-156
- G-157: S5/DL-5: Readback requires whole body. | PC-157
- G-158: S5/DL-5: Held reader suppresses premature retry. | PC-158
- G-159: S5/DL-5: Reader observation precedes ambiguous resend. | PC-159
- G-160: S5/DL-5: Recovery references original failure. | PC-160
- G-161: S5/DL-5: Delivery continues during Windmill failure. | PC-161
- G-162: S5/F-5: Crash recovery retains notification identity. | PC-162
- G-163: S5/DL-6: Completion obligation survives enqueue failure. | PC-163
- G-164: S5/DL-6: Busy recipient retains queued completion. | PC-164
- G-165: S5/DL-6: Transport ack is not session input receipt. | PC-165
- G-166: S5/DL-6: Session receipt binds attempt floor. | PC-166
- G-167: S5/DL-6: Session receipt binds destination. | PC-167
- G-168: D-1: Interim role/workspace eligibility. | PC-168
- G-169: D-1: Baseline has Full scope. | PC-169
- G-170: D-1: Baseline stage provenance. | PC-170
- G-171: D-1: Baseline owner identity. | PC-171
- G-172: D-1: Baseline card identity. | PC-172
- G-173: D-1: Baseline repository identity. | PC-173
- G-174: D-1: Baseline project identity. | PC-174
- G-175: D-1: Selection path is owned/in-repository. | PC-175
- G-176: D-1: Admission and Final latch are atomic. | PC-176
- G-177: D-1: Queued card policy is rechecked. | PC-177
- G-178: S5/DL-5: Recipient destination is authorized. | PC-178
- G-179: S5/DL-5: Receipt binds notification identity. | PC-179
- G-180: S5/DL-5: Receipt binds attempt identity. | PC-180
- G-181: S5/DL-5: Receipt binds native run identity. | PC-181
- G-182: S5/DL-5: Receipt binds attempt observation floor. | PC-182
- G-183: S5/DL-5: Enqueue failure retains same notification. | PC-183
- G-184: S5/F-6: Readiness requires current watchdog heartbeat. | PC-184
- G-185: S5/D-2: London daily deadline rather than rolling RC age. | PC-185
- G-186: S5/D-2: Newer failed master prevents yesterday bridge. | PC-186
- G-187: D-7: Schedule context job identity correlates result. | PC-187
- G-188: D-7: Schedule context path/slot correlates result. | PC-188
- G-189: S5/DL-5: Receipt readback is from authorized peer. | PC-189
- G-190: S5/DL-5: Receipt outage identity. | PC-190
- G-191: S5/DL-5: Receipt due-day identity. | PC-191
- G-192: D-1: Manual qualification run evidence required. | PC-192
- G-193: D-4: Continuation profile identity. | PC-193
- G-194: D-8: Tag object must be annotated. | PC-194
- G-195: D-8: Release creation is draft-only. | PC-195
- G-196: S6/R-4: Report repository receipt identity. | PC-196
- G-197: D-7: Registration workspace allowlist. | PC-197
- G-198: D-3: Clone path cannot escape by reparse point. | PC-198
- G-199: D-3: Skip requires exact current master SHA. | PC-199
- G-200: D-3: Skip requires current release policy. | PC-200
- G-201: D-1: Baseline task actually completed. | PC-201
- G-202: S5/DL-6: Session receipt links exact obligation. | PC-202
- G-203: S6/R-4: Incident assignment is preserved. | PC-203
- G-204: S5/F-1: SSH failure is detected outside Windows. | PC-204
- G-205: S5/F-2: Queued worker outage reaches deadline. | PC-205
- G-206: S5/F-3: Windmill outage detected while API is down. | PC-206

### Positive controls

Code writes the tests and runs V/R; ordinary Review judges the implementation and this
inventory before land. Mutation runs each PC after land as break/red/restore/green, using
the exact method below and a fresh results directory. Change production code/configuration
only; do not weaken a test, timeout, fixture expectation or prerequisite. Every mutation is
a syntactically valid, buildable defect. For script/config controls preserve valid PowerShell/
JSON and the entrypoint contract. On restoration refresh timestamps and rebuild whenever
compiled production code changed.

The exact filter for a listed Class.Method is /*/*/Class/Method, replacing Class and Method
with that row's literal names, with no wildcard suffix, class-wide run or UID/tree mixing.
Use dotnet run --project tests/Antiphon.Tests (or tests/Antiphon.E2E for the two E2E classes),
--property:OutputPath=bin-c599-pc/ and --treenode-filter after --. Each red must execute that
method and fail its specified outcome assertion. Zero tests, compiler/fixture failures,
transport unavailability and a different assertion are invalid evidence. Existing assertion
wording below identifies the current body; new assertions carry their literal C599 G-n
message. Distinct controls may intentionally target different rows of the same exact method;
each still needs its own mutation, red evidence and restored green.

These are executable commissioning specifications: 42 new methods must land in Code before
Mutation can execute them. TestDesign does not claim nonexistent tests already passed.
No external executor may access a sourced mutation snapshot. Keep sourced mutation records
in the commissioned external evidence root and restore every mutation; no snapshot commits.

- PC-1: break G-1 by accept schemaVersion=99; expect `ReleaseGatePolicyTests.C599_ProfileSchema` red at assertion `C599 G-1`: unsupported schema refuses. Cycle estimate: 3 min.
- PC-2: break G-2 by fall back to nightly for unknown profile; expect `ReleaseGatePolicyTests.C599_ProfileSchema` red at assertion `C599 G-2`: unknown profile refuses. Cycle estimate: 3 min.
- PC-3: break G-3 by omit exclusions from canonical policy hash; expect `ReleaseGatePolicyTests.C599_ProfileSchema` red at assertion `C599 G-3`: changing only exclusion changes hash and invalidates prior evidence. Cycle estimate: 3 min.
- PC-4: break G-4 by remove messaging from nightly required suites; expect `ReleaseGatePolicyTests.C599_ProfileSuites` red at assertion `C599 G-4`: nightly required set equals the seven committed suite IDs. Cycle estimate: 3 min.
- PC-5: break G-5 by omit e2e from rc required set; expect `ReleaseGatePolicyTests.C599_ProfileSuites` red at assertion `C599 G-5`: rc required set equals nightly union e2e. Cycle estimate: 3 min.
- PC-6: break G-6 by set complete when selected suites are a proper subset; expect `ReleaseGatePolicyTests.C599_ProfileSuites` red at assertion `C599 G-6`: subset completeGreen is false. Cycle estimate: 3 min.
- PC-7: break G-7 by discard OptIn metadata in diagnostic parser; expect `ReleaseGatePolicyTests.C599_MetadataParsers` red at assertion `C599 G-7`: JSON and diagnostic dispositions and raw categories agree for every probe UID. Cycle estimate: 3 min.
- PC-8: break G-8 by exclude all OptIn in central RC resolver; expect `ReleaseGatePolicyTests.C599_EligibilityCensus` red at assertion `C599 G-8`: automated OptIn UID is required and executes. Cycle estimate: 3 min.
- PC-9: break G-9 by include OptIn-only probe in default nightly; expect `ReleaseGatePolicyTests.C599_EligibilityCensus` red at assertion `C599 G-9`: nightly disposition stays excluded with reason. Cycle estimate: 3 min.
- PC-10: break G-10 by exclude whole mixed ClaudeVerifiedDeliveryTests class; expect `ReleaseGatePolicyTests.C599_EligibilityCensus` red at assertion `C599 G-10`: fakeclaude/short row remains required and live argument rows alone excluded. Cycle estimate: 3 min.
- PC-11: break G-11 by assign unknown class excluded by default; expect `ReleaseGatePolicyTests.C599_EligibilityCensus` red at assertion `C599 G-11`: unknown-class census is incomplete. Cycle estimate: 3 min.
- PC-12: break G-12 by collapse all argument UIDs to method name; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-12`: removing the second argument or inherited UID makes coverage incomplete. Cycle estimate: 3 min.
- PC-13: break G-13 by ignore required-minus-executed set; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-13`: missing-required coverage is false. Cycle estimate: 3 min.
- PC-14: break G-14 by count skipped required nodes as passed; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-14`: required-skipped coverage is false. Cycle estimate: 3 min.
- PC-15: break G-15 by deduplicate overlaps without rejecting; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-15`: duplicate UID in two chunks is rejected. Cycle estimate: 3 min.
- PC-16: break G-16 by map unknown outcome to Passed; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-16`: unknown terminal outcome rejects. Cycle estimate: 3 min.
- PC-17: break G-17 by permit empty required set to complete; expect `ReleaseGatePolicyTests.C599_ExpandedCoverage` red at assertion `C599 G-17`: zero-required run cannot be green. Cycle estimate: 3 min.
- PC-18: break G-18 by map unknown credit kind to rc-release; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-18`: unknown credit kind refuses. Cycle estimate: 3 min.
- PC-19: break G-19 by remove master/origin-master ref check; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-19`: feature and rc refs cannot earn master credit. Cycle estimate: 3 min.
- PC-20: break G-20 by allow manual master trigger; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-20`: manual master cannot refresh readiness. Cycle estimate: 3 min.
- PC-21: break G-21 by allow rc profile in master-scheduled credit; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-21`: crossed master/profile refuses. Cycle estimate: 3 min.
- PC-22: break G-22 by allow nightly profile for rc-release; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-22`: RC without full rc profile refuses. Cycle estimate: 3 min.
- PC-23: break G-23 by ignore trigger in rc-release predicate; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-23`: manual/scheduled trigger cannot masquerade as rc. Cycle estimate: 3 min.
- PC-24: break G-24 by accept arbitrary release/ suffix; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-24`: invalid date, suffix and mutable ref refuse. Cycle estimate: 3 min.
- PC-25: break G-25 by drop pinned SHA equality; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-25`: wrong or abbreviated SHA refuses. Cycle estimate: 3 min.
- PC-26: break G-26 by ignore nativeRunId mismatch; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-26`: foreign-run summary cannot earn credit. Cycle estimate: 3 min.
- PC-27: break G-27 by ignore summary policy hash mismatch; expect `ReleaseGateRunTests.C599_CreditIdentity` red at assertion `C599 G-27`: stale-policy summary cannot earn credit. Cycle estimate: 3 min.
- PC-28: break G-28 by coerce nonempty strings for coverageComplete; expect `ReleaseGateRunTests.C599_CreditVerdicts` red at assertion `C599 G-28`: false, missing, string true and numeric one refuse. Cycle estimate: 3 min.
- PC-29: break G-29 by ignore testsPassed; expect `ReleaseGateRunTests.C599_CreditVerdicts` red at assertion `C599 G-29`: red tests cannot earn credit. Cycle estimate: 3 min.
- PC-30: break G-30 by default reportDelivered to true; expect `ReleaseGateRunTests.C599_CreditVerdicts` red at assertion `C599 G-30`: NoReport and missing receipt cannot earn credit. Cycle estimate: 3 min.
- PC-31: break G-31 by ignore native exit code; expect `ReleaseGateRunTests.C599_CreditVerdicts` red at assertion `C599 G-31`: exit one with all flags true refuses. Cycle estimate: 3 min.
- PC-32: break G-32 by accept prior-run completedAt; expect `ReleaseGateRunTests.C599_CreditVerdicts` red at assertion `C599 G-32`: prior and future evidence cannot earn credit. Cycle estimate: 3 min.
- PC-33: break G-33 by resolve RC state root to master state root; expect `ReleaseGateRunTests.C599_MasterStateIsolation` red at assertion `C599 G-33`: all four master files remain byte-identical after RC green, red and recovery. Cycle estimate: 3 min.
- PC-34: break G-34 by omit Profile from self-reexec arguments; expect `ReleaseGateRunTests.C599_ParameterHops` red at assertion `C599 G-34`: captured production leaf receives rc. Cycle estimate: 3 min.
- PC-35: break G-35 by omit ExpectedSha from core-to-tests arguments; expect `ReleaseGateRunTests.C599_ParameterHops` red at assertion `C599 G-35`: each production hop receives full expected SHA. Cycle estimate: 3 min.
- PC-36: break G-36 by allow canonical checkout path; expect `ReleaseGateRunTests.C599_CloneOwnership` red at assertion `C599 G-36`: canonical-root refuses before fetch/build and sentinel is unchanged. Cycle estimate: 3 min.
- PC-37: break G-37 by accept .git file as owned clone; expect `ReleaseGateRunTests.C599_CloneOwnership` red at assertion `C599 G-37`: linked-root refuses before fetch/build and sentinel is unchanged. Cycle estimate: 3 min.
- PC-38: break G-38 by skip ownership-marker validation; expect `ReleaseGateRunTests.C599_CloneOwnership` red at assertion `C599 G-38`: unowned root refuses without mutation. Cycle estimate: 3 min.
- PC-39: break G-39 by use separate shared-lock filename for rc; expect `ReleaseGateRunTests.C599_SharedLock` red at assertion `C599 G-39`: two concurrently started child contenders have max critical-section occupancy one. Cycle estimate: 3 min.
- PC-40: break G-40 by delete live lock when old timestamp exceeds budget; expect `ReleaseGateRunTests.C599_SharedLock` red at assertion `C599 G-40`: old live owner stays owner and second child does not build. Cycle estimate: 3 min.
- PC-41: break G-41 by match stale owner by PID alone; expect `ReleaseGateRunTests.C599_SharedLock` red at assertion `C599 G-41`: same PID with wrong start identity follows dead-owner recovery. Cycle estimate: 3 min.
- PC-42: break G-42 by accept arbitrary continuation token; expect `ReleaseGateRunTests.C599_Continuation` red at assertion `C599 G-42`: wrong continuation cannot enter the owned critical section. Cycle estimate: 3 min.
- PC-43: break G-43 by replace saved expected SHA with caller SHA; expect `ReleaseGateRunTests.C599_Continuation` red at assertion `C599 G-43`: mismatched-SHA resume refuses before tests. Cycle estimate: 3 min.
- PC-44: break G-44 by release lock before child exits; expect `ReleaseGateRunTests.C599_SharedLock` red at assertion `C599 G-44`: second lane cannot start while first child is alive. Cycle estimate: 3 min.
- PC-45: break G-45 by enqueue a retry on contention; expect `ReleaseGateRunTests.C599_SharedLock` red at assertion `C599 G-45`: one deferred-busy record, zero native launches and zero retry queue entries. Cycle estimate: 3 min.
- PC-46: break G-46 by accept bundle on mtime alone; expect `ReleaseGateE2EEvidenceTests.C599_Prerequisites` red at assertion `C599 G-46`: fresh-mtime wrong-SHA bundle refuses. Cycle estimate: 3 min.
- PC-47: break G-47 by ignore bundle digest mismatch; expect `ReleaseGateE2EEvidenceTests.C599_Prerequisites` red at assertion `C599 G-47`: modified dist bytes refuse. Cycle estimate: 3 min.
- PC-48: break G-48 by ignore npm lint nonzero exit; expect `ReleaseGateE2EEvidenceTests.C599_Prerequisites` red at assertion `C599 G-48`: lint failure prevents E2E launch and green. Cycle estimate: 3 min.
- PC-49: break G-49 by accept failed Chromium launch as availability; expect `ReleaseGateE2EEvidenceTests.C599_Prerequisites` red at assertion `C599 G-49`: missing browser prevents successful executor completion. Cycle estimate: 3 min.
- PC-50: break G-50 by swallow owned DB startup failure; expect `ReleaseGateIsolationTests.C599_OwnedServices` red at assertion `C599 G-50`: failed container or runner startup leaves gate incomplete. Cycle estimate: 10 min.
- PC-51: break G-51 by allow injected runner endpoint on port 17204; expect `ReleaseGateIsolationTests.C599_OwnedServices` red at assertion `C599 G-51`: production endpoint is rejected before any HTTP request. Cycle estimate: 10 min.
- PC-52: break G-52 by retain one sentinel provider token in child environment; expect `ReleaseGateE2EEvidenceTests.C599_EnvironmentIsolation` red at assertion `C599 G-52`: child sees none of the enumerated inherited secrets, endpoints or approvals. Cycle estimate: 3 min.
- PC-53: break G-53 by restore production messaging adapter in ordinary fixture; expect `ReleaseGateIsolationTests.C599_RefusingAdapters` red at assertion `C599 G-53`: sentinel external network/provider send count is zero. Cycle estimate: 10 min.
- PC-54: break G-54 by accept TRX from previous nativeRunId; expect `ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts` red at assertion `C599 G-54`: stale/wrong-run TRX cannot complete coverage. Cycle estimate: 3 min.
- PC-55: break G-55 by ignore absent MTP execution diagnostics; expect `ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts` red at assertion `C599 G-55`: TRX-only run stays incomplete. Cycle estimate: 3 min.
- PC-56: break G-56 by join rows by display name alone; expect `ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts` red at assertion `C599 G-56`: same-name foreign UID does not satisfy required node. Cycle estimate: 3 min.
- PC-57: break G-57 by convert fixture exception to excluded disposition; expect `ReleaseGateE2EEvidenceTests.C599_ExecutionArtifacts` red at assertion `C599 G-57`: fixture-failed required node blocks green. Cycle estimate: 3 min.
- PC-58: break G-58 by treat unavailable census HTTP as clean; expect `ReleaseGateIsolationTests.C599_CleanupEvidence` red at assertion `C599 G-58`: unreachable/missing census refuses cleanup receipt and retains diagnostics. Cycle estimate: 10 min.
- PC-59: break G-59 by ignore StopAsync join timeout; expect `ReleaseGateIsolationTests.C599_CleanupEvidence` red at assertion `C599 G-59`: still-live owned child makes cleanup incomplete. Cycle estimate: 10 min.
- PC-60: break G-60 by delete run directory when census did not complete; expect `ReleaseGateIsolationTests.C599_CleanupEvidence` red at assertion `C599 G-60`: failed cleanup retains run evidence directory. Cycle estimate: 10 min.
- PC-61: break G-61 by execute writes without Apply; expect `ReleaseGateRegistrationTests.C599_Preview` red at assertion `C599 G-61`: preview sends zero mutating HTTP requests. Cycle estimate: 3 min.
- PC-62: break G-62 by accept another script path within mc; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-62`: foreign script path produces zero writes. Cycle estimate: 3 min.
- PC-63: break G-63 by skip expected-current-hash comparison; expect `ReleaseGateRegistrationTests.C599_Concurrency` red at assertion `C599 G-63`: concurrent script change refuses overwrite. Cycle estimate: 3 min.
- PC-64: break G-64 by trust create response without script GET; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-64`: accepted but different/missing script is not registered. Cycle estimate: 3 min.
- PC-65: break G-65 by ignore readback timezone; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-65`: wrong timezone fails registration. Cycle estimate: 3 min.
- PC-66: break G-66 by skip desktop worker eligibility check; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-66`: missing desktop worker refuses activation. Cycle estimate: 3 min.
- PC-67: break G-67 by create schedule enabled; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-67`: first apply readback shows disabled for all three schedules. Cycle estimate: 3 min.
- PC-68: break G-68 by recreate schedule on unchanged apply; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-68`: second apply preserves enabled state and one schedule per path. Cycle estimate: 3 min.
- PC-69: break G-69 by treat HTTP 401 as absent object eligible to create; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-69`: 401/403 yields no registration success or follow-on writes. Cycle estimate: 3 min.
- PC-70: break G-70 by trust caller-supplied Trigger=scheduled; expect `ReleaseGateRegistrationTests.C599_ScheduleProvenance` red at assertion `C599 G-70`: manual job with forged trigger receives no scheduled credit. Cycle estimate: 3 min.
- PC-71: break G-71 by fetch and substitute latest master immediately before tests; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-71`: master advancing after cut does not change candidate SHA. Cycle estimate: 3 min.
- PC-72: break G-72 by push branch before journal write; expect `ReleaseGatePublicationTests.C599_CutRecovery` red at assertion `C599 G-72`: crash before durable journal has made no remote ref. Cycle estimate: 3 min.
- PC-73: break G-73 by force-push differing existing candidate ref; expect `ReleaseGatePublicationTests.C599_CutRecovery` red at assertion `C599 G-73`: foreign existing branch is unchanged and candidate refuses. Cycle estimate: 3 min.
- PC-74: break G-74 by skip HEAD equality immediately before executor launch; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-74`: wrong HEAD prevents native launch. Cycle estimate: 3 min.
- PC-75: break G-75 by skip remote equality immediately before executor launch; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-75`: moved remote prevents native launch. Cycle estimate: 3 min.
- PC-76: break G-76 by skip HEAD equality immediately before publication; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-76`: changed HEAD yields zero tag/release mutations. Cycle estimate: 3 min.
- PC-77: break G-77 by skip remote equality immediately before publication; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-77`: moved remote yields zero tag/release mutations. Cycle estimate: 3 min.
- PC-78: break G-78 by skip on an unpublished prior green SHA; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-78`: unpublished green does not skip the scheduled cut. Cycle estimate: 3 min.
- PC-79: break G-79 by allow diagnostic or missing-suite green; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-79`: missing E2E verdict yields zero publication calls. Cycle estimate: 3 min.
- PC-80: break G-80 by ignore manifest nativeRunId equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-80`: foreign-run manifest cannot publish. Cycle estimate: 3 min.
- PC-81: break G-81 by omit bundle hash from manifest validation; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-81`: foreign-bundle manifest cannot publish. Cycle estimate: 3 min.
- PC-82: break G-82 by allocate fresh N after ambiguous tag push; expect `ReleaseGatePublicationTests.C599_TagRecovery` red at assertion `C599 G-82`: same journal resumes one tag name after lost response. Cycle estimate: 3 min.
- PC-83: break G-83 by force overwrite existing mismatched tag; expect `ReleaseGatePublicationTests.C599_TagRecovery` red at assertion `C599 G-83`: pre-existing wrong tag is unchanged and publication refuses. Cycle estimate: 3 min.
- PC-84: break G-84 by trust push acknowledgment without peeled-tag lookup; expect `ReleaseGatePublicationTests.C599_TagRecovery` red at assertion `C599 G-84`: acknowledged wrong tag prevents draft creation. Cycle estimate: 3 min.
- PC-85: break G-85 by omit --verify-tag from gh create; expect `ReleaseGatePublicationTests.C599_DraftRecovery` red at assertion `C599 G-85`: captured create argv contains --verify-tag. Cycle estimate: 3 min.
- PC-86: break G-86 by create second release after response loss; expect `ReleaseGatePublicationTests.C599_DraftRecovery` red at assertion `C599 G-86`: restart observes one release ID for journal tag. Cycle estimate: 3 min.
- PC-87: break G-87 by accept upload acknowledgment without download hash check; expect `ReleaseGatePublicationTests.C599_AssetRecovery` red at assertion `C599 G-87`: missing/truncated/wrong manifest or summary prevents publish. Cycle estimate: 3 min.
- PC-88: break G-88 by mark journal published from PATCH acknowledgment; expect `ReleaseGatePublicationTests.C599_PublishRecovery` red at assertion `C599 G-88`: accepted but still-draft/unreadable release is not published. Cycle estimate: 3 min.
- PC-89: break G-89 by allocate new tag after publish response loss; expect `ReleaseGatePublicationTests.C599_PublishRecovery` red at assertion `C599 G-89`: restart returns original release ID and exactly one published release. Cycle estimate: 3 min.
- PC-90: break G-90 by copy raw log property into public summary; expect `ReleaseGatePublicationTests.C599_ManifestAllowlist` red at assertion `C599 G-90`: seeded credential/path/transcript sentinels absent from both uploaded assets. Cycle estimate: 3 min.
- PC-91: break G-91 by read capabilities from current local checkout; expect `ReleaseGateStatusTests.C599_StatusIdentity` red at assertion `C599 G-91`: manifest and status show candidate capabilities, not newer checkout. Cycle estimate: 3 min.
- PC-92: break G-92 by label ancestor release as current release; expect `ReleaseGateStatusTests.C599_StatusIdentity` red at assertion `C599 G-92`: newer master SHA is unreleased, not latest released tag. Cycle estimate: 3 min.
- PC-93: break G-93 by ignore tag peeled SHA mismatch; expect `ReleaseGateStatusTests.C599_StatusIdentity` red at assertion `C599 G-93`: crossed tag/manifest or draft cannot label running build released. Cycle estimate: 3 min.
- PC-94: break G-94 by map GitHub error to no release; expect `ReleaseGateStatusTests.C599_StatusIdentity` red at assertion `C599 G-94`: unreachable/unreadable response yields unknown. Cycle estimate: 3 min.
- PC-95: break G-95 by invoke restart-apphost through process seam; expect `ReleaseGateStatusTests.C599_NoActivation` red at assertion `C599 G-95`: restart/deploy/kill invocation count is zero. Cycle estimate: 3 min.
- PC-96: break G-96 by query active incidents without lane label; expect `ReleaseGateRunTests.C599_ReportLane` red at assertion `C599 G-96`: RC red/green cannot revise or close master incident. Cycle estimate: 3 min.
- PC-97: break G-97 by return delivered after accepted POST without GET; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-97`: missing full persisted discussion body leaves reportDelivered false. Cycle estimate: 10 min.
- PC-98: break G-98 by match board receipt by card ID only; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-98`: foreign-run body is not a receipt. Cycle estimate: 10 min.
- PC-99: break G-99 by persist report intent only after send; expect `ReleaseGateReportDeliveryTests.C599_ReportRecovery` red at assertion `C599 G-99`: pre-send crash resumes original run intent to one durable discussion. Cycle estimate: 10 min.
- PC-100: break G-100 by retry POST without querying identity; expect `ReleaseGateReportDeliveryTests.C599_ReportRecovery` red at assertion `C599 G-100`: commit-then-response-loss yields one matching discussion after restart. Cycle estimate: 10 min.
- PC-101: break G-101 by force close assigned incident using stale revision; expect `ReleaseGateReportDeliveryTests.C599_ReportRecovery` red at assertion `C599 G-101`: concurrent/assigned incident is not overwritten and pending receipt remains retryable. Cycle estimate: 10 min.
- PC-102: break G-102 by clear pending report on final HTTP failure; expect `ReleaseGateRunTests.C599_ReportLane` red at assertion `C599 G-102`: restart retries same identity and green remains false until receipt. Cycle estimate: 3 min.
- PC-103: break G-103 by omit Trigger in wrapper forwarding; expect `ReleaseGateRunTests.C599_ParameterHops` red at assertion `C599 G-103`: each production hop receives rc, never default scheduled. Cycle estimate: 3 min.
- PC-104: break G-104 by omit Ref in wrapper forwarding; expect `ReleaseGateRunTests.C599_ParameterHops` red at assertion `C599 G-104`: each production hop receives exact immutable ref. Cycle estimate: 3 min.
- PC-105: break G-105 by ignore readback cron; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-105`: wrong cron fails registration. Cycle estimate: 3 min.
- PC-106: break G-106 by ignore readback script_path; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-106`: wrong script target fails registration. Cycle estimate: 3 min.
- PC-107: break G-107 by ignore readback tag; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-107`: wrong tag fails registration. Cycle estimate: 3 min.
- PC-108: break G-108 by ignore manifest repository equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-108`: foreign-repository manifest cannot publish. Cycle estimate: 3 min.
- PC-109: break G-109 by ignore manifest full SHA equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-109`: foreign-SHA manifest cannot publish. Cycle estimate: 3 min.
- PC-110: break G-110 by ignore manifest policy digest equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-110`: foreign-policy manifest cannot publish. Cycle estimate: 3 min.
- PC-111: break G-111 by ignore manifest script digest equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-111`: foreign-script manifest cannot publish. Cycle estimate: 3 min.
- PC-112: break G-112 by ignore manifest build digest equality; expect `ReleaseGatePublicationTests.C599_PublicationGate` red at assertion `C599 G-112`: foreign-build manifest cannot publish. Cycle estimate: 3 min.
- PC-113: break G-113 by ignore readback lane equality; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-113`: same-run wrong-lane body does not acknowledge report. Cycle estimate: 10 min.
- PC-114: break G-114 by ignore readback project equality; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-114`: same-run wrong-project body does not acknowledge report. Cycle estimate: 10 min.
- PC-115: break G-115 by accept body containing only identity marker; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-115`: header-only/truncated body does not acknowledge report. Cycle estimate: 10 min.
- PC-116: break G-116 by change settings Enabled default to true; expect `InterimVerificationReadinessTests.C544_DisabledByDefault` red at new InterimVerificationSettings().Enabled.ShouldBeFalse(shipped default). Cycle estimate: 6 min.
- PC-117: break G-117 by remove task repository qualification check; expect `InterimVerificationReadinessTests.C544_QualifiedRepository` red at task-repository verdict is (false, readiness_repository_unqualified). Cycle estimate: 6 min.
- PC-118: break G-118 by remove receipt repository comparison; expect `InterimVerificationReadinessTests.C544_QualifiedRepository` red at receipt-repository verdict is (false, qualification_repository_mismatch). Cycle estimate: 6 min.
- PC-119: break G-119 by remove monitor repository comparison; expect `InterimVerificationReadinessTests.C544_QualifiedRepository` red at monitor-repository verdict is (false, monitor_repository_mismatch). Cycle estimate: 6 min.
- PC-120: break G-120 by remove request project comparison; expect `InterimVerificationReadinessTests.C544_QualifiedProject` red at foreign-project verdict is (false, readiness_project_unqualified). Cycle estimate: 6 min.
- PC-121: break G-121 by remove receipt project comparison; expect `InterimVerificationReadinessTests.C544_QualifiedProject` red at receipt-foreign-project verdict is (false, qualification_project_mismatch). Cycle estimate: 6 min.
- PC-122: break G-122 by return ready on monitor read exception; expect `InterimVerificationReadinessTests.C544_ReadFailure` red at read-denied verdict is (false, monitor_unreadable). Cycle estimate: 6 min.
- PC-123: break G-123 by ignore qualification schema version; expect `InterimVerificationReadinessTests.C544_QualificationReceipt` red at unsupported-schema verdict is (false, qualification_receipt_schema_unsupported). Cycle estimate: 6 min.
- PC-124: break G-124 by remove recipientEvidenceIds validation; expect `InterimVerificationReadinessTests.C544_QualificationReceipt` red at no-recipient-evidence verdict is (false, qualification_recipient_evidence_missing). Cycle estimate: 6 min.
- PC-125: break G-125 by remove outageRecoveryEvidenceIds validation; expect `InterimVerificationReadinessTests.C544_QualificationReceipt` red at no-outage-evidence verdict is (false, qualification_outage_evidence_missing). Cycle estimate: 6 min.
- PC-126: break G-126 by ignore missing scheduledRunId; expect `InterimVerificationReadinessTests.C544_QualificationReceipt` red at no-scheduled-run verdict is (false, qualification_runs_missing). Cycle estimate: 6 min.
- PC-127: break G-127 by allow short artifact commit revision; expect `InterimVerificationReadinessTests.C544_QualificationRevision` red at short verdict is (false, qualification_revision_invalid). Cycle estimate: 6 min.
- PC-128: break G-128 by ignore monitor policy hash comparison; expect `InterimVerificationReadinessTests.C544_PolicyHash` red at different-policy-hash verdict is (false, monitor_policy_hash_mismatch). Cycle estimate: 6 min.
- PC-129: break G-129 by ignore monitor script hash comparison; expect `InterimVerificationReadinessTests.C544_ScriptHash` red at different-script-hash verdict is (false, monitor_script_hash_mismatch). Cycle estimate: 6 min.
- PC-130: break G-130 by ignore JobNativeRunId equality; expect `InterimVerificationReadinessTests.C544_RunIdentity` red at crossed-run verdict is (false, monitor_run_identity_mismatch). Cycle estimate: 6 min.
- PC-131: break G-131 by change age > limit to age >= limit; expect `InterimVerificationReadinessTests.C544_MonitorAge` red at age-60m verdict.Ready.ShouldBe(true). Cycle estimate: 6 min.
- PC-132: break G-132 by remove future timestamp guard; expect `InterimVerificationReadinessTests.C544_FutureMonitor` red at one-tick-future verdict is (false, monitor_future). Cycle estimate: 6 min.
- PC-133: break G-133 by ignore Healthy false; expect `InterimVerificationReadinessTests.C544_MonitorVerdict` red at unhealthy verdict is (false, monitor_unhealthy). Cycle estimate: 6 min.
- PC-134: break G-134 by ignore ReadyForDeferral false; expect `InterimVerificationReadinessTests.C544_MonitorVerdict` red at not-ready-for-deferral verdict is (false, monitor_not_ready_for_deferral). Cycle estimate: 6 min.
- PC-135: break G-135 by deserialize string true as boolean true; expect `InterimVerificationReadinessTests.C544_MonitorVerdict` red at string-true-healthy verdict is (false, monitor_malformed). Cycle estimate: 6 min.
- PC-136: break G-136 by skip watchdog instance equality; expect `InterimVerificationReadinessTests.C545_WatchdogInstance` red at mismatch verdict is (false, monitor_watchdog_mismatch). Cycle estimate: 6 min.
- PC-137: break G-137 by ship InterimVerification.Enabled=true in appsettings; expect `ReleaseGateActivationTests.C599_Defaults` red at assertion `C599 G-137`: bound production configuration Enabled is false and new card policies are FullOnly. Cycle estimate: 6 min.
- PC-138: break G-138 by ignore FullOnly role policy; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-138`: FullOnly request refuses Interim before queue insert. Cycle estimate: 6 min.
- PC-139: break G-139 by accept absent baseline outcome; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-139`: missing baseline refuses Interim before queue insert. Cycle estimate: 6 min.
- PC-140: break G-140 by accept dirty selection file; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-140`: uncommitted cumulative selection refuses Interim before queue insert. Cycle estimate: 6 min.
- PC-141: break G-141 by skip dispatch readiness recheck; expect `ReleaseGateActivationTests.C599_QueuedReadiness` red at assertion `C599 G-141`: stale/read-failed queued task creates zero child sessions and remains held. Cycle estimate: 6 min.
- PC-142: break G-142 by skip dispatch baseline ancestry check; expect `ReleaseGateActivationTests.C599_QueuedReadiness` red at assertion `C599 G-142`: revoked policy or lost baseline creates zero child sessions. Cycle estimate: 6 min.
- PC-143: break G-143 by clear RequiresFinalReview when settings disabled; expect `ReleaseGateActivationTests.C599_RollbackLatch` red at assertion `C599 G-143`: rollback rejects Interim land with existing persisted latch. Cycle estimate: 6 min.
- PC-144: break G-144 by bypass missing Final evidence when latched; expect `InterimVerificationLandGuardTests.C544_NoEvidenceRefuses` red at ConflictException code equals LandApproval.FinalReviewRequiredCode. Cycle estimate: 6 min.
- PC-145: break G-145 by allow commissioned Interim round; expect `InterimVerificationLandGuardTests.C544_InterimApprovalRefuses` red at interim-round ConflictException code equals ScopeIneligibleCode. Cycle estimate: 6 min.
- PC-146: break G-146 by allow completed Interim scope; expect `InterimVerificationLandGuardTests.C544_InterimApprovalRefuses` red at interim-scope ConflictException code equals ScopeIneligibleCode. Cycle estimate: 6 min.
- PC-147: break G-147 by allow Final/Unknown scope; expect `InterimVerificationLandGuardTests.C544_FinalFullRequired` red at final-unknown ConflictException code equals ScopeIneligibleCode. Cycle estimate: 6 min.
- PC-148: break G-148 by allow Found final review outcome; expect `InterimVerificationLandGuardTests.C544_CleanCompletedRequired` red at found-full ConflictException code equals review_evidence_ineligible. Cycle estimate: 6 min.
- PC-149: break G-149 by remove review subject comparison; expect `InterimVerificationLandGuardTests.C544_LandOwnerIdentity` red at other-owner ConflictException code equals review_evidence_subject_mismatch. Cycle estimate: 6 min.
- PC-150: break G-150 by remove reviewed source SHA comparison; expect `InterimVerificationLandGuardTests.C544_LandShaIdentity` red at other-sha ConflictException code equals review_evidence_sha_mismatch. Cycle estimate: 6 min.
- PC-151: break G-151 by remove reviewed ref comparison; expect `InterimVerificationLandGuardTests.C544_LandRefIdentity` red at other-ref ConflictException code equals review_evidence_ref_mismatch. Cycle estimate: 6 min.
- PC-152: break G-152 by remove reviewed repository comparison; expect `InterimVerificationLandGuardTests.C544_LandRepositoryIdentity` red at other-repository ConflictException code equals review_evidence_repository_mismatch. Cycle estimate: 6 min.
- PC-153: break G-153 by ignore superseded review outcome; expect `InterimVerificationLandGuardTests.C544_SupersededFinal` red at superseded ConflictException code equals review_evidence_superseded. Cycle estimate: 6 min.
- PC-154: break G-154 by skip Final revalidation on recovered Verified phase; expect `ReleaseGateActivationTests.C599_FinalRecovery` red at assertion `C599 G-154`: remote target remains original SHA on invalidated Final at every pre-advance cut. Cycle estimate: 6 min.
- PC-155: break G-155 by send transport call before committing notification intent; expect `NightlyVerificationContractTests.C544_NotificationIntent` red at second connection sees durable intent before the transport is invoked. Cycle estimate: 6 min.
- PC-156: break G-156 by mark notification received after transport acceptance; expect `NightlyVerificationContractTests.C545_AcceptanceRecordedSeparately` red at accepted-shown state remains sent and receivedAt is null. Cycle estimate: 6 min.
- PC-157: break G-157 by accept read marker without body hash match; expect `NightlyVerificationContractTests.C545_AcceptanceRecordedSeparately` red at read-marker-without-body no receipt. Cycle estimate: 6 min.
- PC-158: break G-158 by resend unaccepted attempt despite reader held; expect `NightlyVerificationContractTests.C545_HeldReaderNoResend` red at held-lost-response one message and one attempt at 29:59. Cycle estimate: 6 min.
- PC-159: break G-159 by retry transport before reader import; expect `NightlyVerificationContractTests.C545_ReaderFirstRetry` red at lost-response/reader-sees-it no resend. Cycle estimate: 6 min.
- PC-160: break G-160 by clear recovery LinkedNid; expect `NightlyVerificationContractTests.C545_RecoveryLinksFailure` red at linked equals failureNid and one recipient receipt. Cycle estimate: 6 min.
- PC-161: break G-161 by return from tick immediately on Windmill error; expect `NightlyVerificationContractTests.C545_NoWindmillDependency` red at imported while Windmill is down. Cycle estimate: 6 min.
- PC-162: break G-162 by allocate new nid when reopening ledger after cut; expect `NightlyVerificationContractTests.C544_NotificationCrash` red at each of four cuts has one logical notification and one imported receipt. Cycle estimate: 6 min.
- PC-163: break G-163 by discard settled completion obligation on queue insert failure; expect `ReleaseGateActivationTests.C599_CompletionHandoffs` red at assertion `C599 G-163`: restart yields one complete matching UserPrompt for same obligation. Cycle estimate: 8 min.
- PC-164: break G-164 by mark completion delivered when caller is working; expect `ReleaseGateActivationTests.C599_CompletionHandoffs` red at assertion `C599 G-164`: busy phase has no receipt; eligible phase has one complete matching UserPrompt. Cycle estimate: 8 min.
- PC-165: break G-165 by confirm notification from delivery stamp before whole prompt match; expect `VerificationRoundDeliveryTests.C544_CompletionReceiptWholeWire` red at header-only, identity-only and truncated wire remain unconfirmed. Cycle estimate: 8 min.
- PC-166: break G-166 by accept identical UserPrompt below attempt baseline; expect `ReleaseGateActivationTests.C599_CompletionHandoffs` red at assertion `C599 G-166`: old complete prompt cannot confirm current attempt. Cycle estimate: 8 min.
- PC-167: break G-167 by match receipt by wire digest without session identity; expect `ReleaseGateActivationTests.C599_CompletionHandoffs` red at assertion `C599 G-167`: foreign-session prompt cannot confirm. Cycle estimate: 8 min.
- PC-168: break G-168 by allow Shared workspace for Interim; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-168`: unsupported role/workspace combination refuses admission. Cycle estimate: 6 min.
- PC-169: break G-169 by accept non-Full baseline scope; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-169`: non-Full baseline refuses. Cycle estimate: 6 min.
- PC-170: break G-170 by accept outcome commissioned as Interim; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-170`: Interim baseline cannot authorize another Interim round. Cycle estimate: 6 min.
- PC-171: break G-171 by remove baseline subject comparison; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-171`: foreign-owner baseline refuses. Cycle estimate: 6 min.
- PC-172: break G-172 by remove baseline card comparison; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-172`: foreign-card baseline refuses. Cycle estimate: 6 min.
- PC-173: break G-173 by remove baseline repository comparison; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-173`: foreign-repository baseline refuses. Cycle estimate: 6 min.
- PC-174: break G-174 by remove baseline project comparison; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-174`: foreign-project baseline refuses. Cycle estimate: 6 min.
- PC-175: break G-175 by accept escaping selection path; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-175`: outside-root selection refuses. Cycle estimate: 6 min.
- PC-176: break G-176 by commit Interim task before latch transaction; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-176`: injected transaction failure persists neither admitted task nor partial latch. Cycle estimate: 6 min.
- PC-177: break G-177 by skip dispatcher role policy reread; expect `ReleaseGateActivationTests.C599_QueuedReadiness` red at assertion `C599 G-177`: revoked FullOnly policy creates zero child sessions. Cycle estimate: 6 min.
- PC-178: break G-178 by allow changed configured destination without receipt authority; expect `NightlyVerificationContractTests.C544_AuthorizedDestination` red at invalid destination produces zero sends. Cycle estimate: 6 min.
- PC-179: break G-179 by ignore notification nid in receipt import; expect `NightlyVerificationContractTests.C544_ReceiptNotificationIdentity` red at unknown-notification receipt rejected. Cycle estimate: 6 min.
- PC-180: break G-180 by ignore attempt number in receipt import; expect `NightlyVerificationContractTests.C544_ReceiptNotificationIdentity` red at attempt mismatch import reason is unknown-attempt. Cycle estimate: 6 min.
- PC-181: break G-181 by ignore native run in receipt body validation; expect `NightlyVerificationContractTests.C544_ReceiptRunIdentity` red at decisive run row import reason is run-mismatch. Cycle estimate: 6 min.
- PC-182: break G-182 by accept reader observation earlier than attempt tolerance; expect `NightlyVerificationContractTests.C544_ReceiptAttemptFloor` red at minus-121-seconds receipt rejected while minus-120 accepted. Cycle estimate: 6 min.
- PC-183: break G-183 by delete pending notification after transport throws; expect `NightlyVerificationContractTests.C544_NotificationRetry` red at attempt 2 same nid and one received receipt. Cycle estimate: 6 min.
- PC-184: break G-184 by ignore heartbeat older than 20 minutes; expect `ReleaseGateActivationTests.C599_MasterDueBoundary` red at assertion `C599 G-184`: 20-minutes-plus-tick heartbeat yields unready. Cycle estimate: 6 min.
- PC-185: break G-185 by accept yesterday master after London 08:00; expect `ReleaseGateActivationTests.C599_MasterDueBoundary` red at assertion `C599 G-185`: 08:00 due-day boundary remains unready despite fresh RC. Cycle estimate: 6 min.
- PC-186: break G-186 by ignore newer failed scheduled master attempt; expect `ReleaseGateActivationTests.C599_MasterDueBoundary` red at assertion `C599 G-186`: 07:59:59 with newer master failure remains unready despite prior green. Cycle estimate: 6 min.
- PC-187: break G-187 by ignore returned job ID when fetching authoritative context; expect `ReleaseGateRegistrationTests.C599_ScheduleProvenance` red at assertion `C599 G-187`: different job context cannot authenticate scheduled native result. Cycle estimate: 3 min.
- PC-188: break G-188 by trust schedule path supplied in script args; expect `ReleaseGateRegistrationTests.C599_ScheduleProvenance` red at assertion `C599 G-188`: foreign path or off-slot manual context cannot earn scheduled credit. Cycle estimate: 3 min.
- PC-189: break G-189 by ignore reader peer authorization; expect `NightlyVerificationContractTests.C544_ReceiptDestination` red at peer-unauthorized import; no receipt. Cycle estimate: 6 min.
- PC-190: break G-190 by ignore outage ID equality in receipt import; expect `NightlyVerificationContractTests.C544_ReceiptRunIdentity` red at oid row import reason is identity-mismatch. Cycle estimate: 6 min.
- PC-191: break G-191 by ignore due-day equality in receipt import; expect `NightlyVerificationContractTests.C544_ReceiptRunIdentity` red at due row import reason is identity-mismatch. Cycle estimate: 6 min.
- PC-192: break G-192 by ignore missing manualRunId; expect `InterimVerificationReadinessTests.C544_QualificationReceipt` red at no-manual-run verdict is (false, qualification_runs_missing). Cycle estimate: 6 min.
- PC-193: break G-193 by accept caller profile different from saved profile; expect `ReleaseGateRunTests.C599_Continuation` red at assertion `C599 G-193`: mismatched-profile resume refuses before tests. Cycle estimate: 3 min.
- PC-194: break G-194 by accept a lightweight tag with matching SHA; expect `ReleaseGatePublicationTests.C599_TagRecovery` red at assertion `C599 G-194`: lightweight tag refuses before draft creation. Cycle estimate: 3 min.
- PC-195: break G-195 by omit --draft from gh create; expect `ReleaseGatePublicationTests.C599_DraftRecovery` red at assertion `C599 G-195`: captured create argv contains --draft and no public release before asset readback. Cycle estimate: 3 min.
- PC-196: break G-196 by ignore readback repository identity; expect `ReleaseGateReportDeliveryTests.C599_ReportReadback` red at assertion `C599 G-196`: foreign-repository body does not acknowledge report. Cycle estimate: 10 min.
- PC-197: break G-197 by allow non-mc workspace while keeping path valid; expect `ReleaseGateRegistrationTests.C599_ApplyReadback` red at assertion `C599 G-197`: foreign workspace produces zero writes. Cycle estimate: 3 min.
- PC-198: break G-198 by skip resolved-root containment check; expect `ReleaseGateRunTests.C599_CloneOwnership` red at assertion `C599 G-198`: owned-marker root through external junction refuses without external mutation. Cycle estimate: 3 min.
- PC-199: break G-199 by skip when a different SHA already has a published release; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-199`: different published SHA does not suppress new candidate. Cycle estimate: 3 min.
- PC-200: break G-200 by ignore published manifest policy hash in skip check; expect `ReleaseGatePublicationTests.C599_CandidateIdentity` red at assertion `C599 G-200`: same SHA published with older policy still requires new full qualification. Cycle estimate: 3 min.
- PC-201: break G-201 by accept Succeeded baseline with null CompletedAt; expect `ReleaseGateActivationTests.C599_Admission` red at assertion `C599 G-201`: incomplete baseline task refuses admission. Cycle estimate: 6 min.
- PC-202: break G-202 by confirm any notification sharing wire digest; expect `ReleaseGateActivationTests.C599_CompletionHandoffs` red at assertion `C599 G-202`: other-obligation queue/transcript does not confirm this notification. Cycle estimate: 8 min.
- PC-203: break G-203 by close assigned incident after successful revision reread; expect `ReleaseGateReportDeliveryTests.C599_ReportRecovery` red at assertion `C599 G-203`: assigned incident is preserved and report receipt describes the refusal. Cycle estimate: 10 min.
- PC-204: break G-204 by omit exit-255 hop-failure classification; expect `NightlyWatchdogCoreTests.C545_OutageKinds` red at windows-hop-failed/exit-255 Opened equals windows-hop-failed. Cycle estimate: 6 min.
- PC-205: break G-205 by treat queued job as a started run; expect `NightlyWatchdogCoreTests.C545_OutageKinds` red at start-overdue/queued Opened contains start-overdue. Cycle estimate: 6 min.
- PC-206: break G-206 by suppress outage opening on second unreachable tick; expect `NightlyWatchdogCoreTests.C545_OutageKinds` red at windmill-unreachable/second-tick has one opened outage. Cycle estimate: 6 min.

### Out of scope

- Changing Interim into permission to land without exact-SHA Final/Full Review, bulk opt-in,
  or removing ordinary affected-class checks from Final: rejected by D-1. Existing legacy
  unlatched caller compatibility is retained and tested; no new global land bypass.
- Live provider/headed/distiller canaries, Linux-only tests and CARD-0490 QEMU placeholders:
  excluded only by the precise census above, with their separate owners. They are not
  evidence for the automated Windows release profile.
- GitHub Actions repair is CARD-0610. An Actions red does not veto valid native evidence,
  but a native lint/test failure does. No claim that CI is green.
- RC scheduler-outage independence is not promised. Actual RC slot observation and
  lane-specific failure reporting are required; master watchdog F-1..F-6 stays required.
- Full native/eight-suite runs are excluded from ordinary Code checkpoints because S5/S6
  already require real full runs at pinned source. The Code table covers named affected
  classes, production-script contracts and real isolated recipient/browser checks. A smoke
  pass never substitutes for the later full RC profile or published-release receipt.

### Checkpoints

This is the closed ordinary Code manifest, replacing the provisional CP-1..CP-12. Every row
runs after S1-S4 commits exist, on one unchanged source identity; no editing during a run.
Use scripts/run-checkpoint.ps1 for each TUnit row, -Expect with every named class (and
method roster checks for CP-7), -MinExecuted at least the stated method floor, and fresh
results paths. For new methods, report both TUnit counts and internal matrix-row counts;
at least one executed invalid row for each mapped guard plus its valid control is required.
Changing to TUnit Arguments is allowed only if the expanded roster still covers every row.

For CP-14, build the named project once with OutputPath=bin-c599/, then run its one exact
non-TUnit install command and emit the corresponding checkpoint evidence. Each other
non-TUnit row has one exact command. Table backslash-escaped pipes are Markdown only; actual
filters use plain pipes. -NoBuild reuse is allowed only from the named CP at this same SHA.

TUnit accepts parenthesised alternation only in the class segment, never in the method
segment, so CP-7 selects its three methods with a single `C544_CompletionRe*` prefix.
`--property:OutputPath=bin-c599/` flattens the per-TFM subfolder, so the CP-14 generated
install script is at `tests/Antiphon.E2E/bin-c599/playwright.ps1`, not under `net9.0/`.
The CP-8/CP-9/CP-10 -ResultsDirectory values must be out of the repository tree:
`Test-NightlySharedTree` refuses any path under `C:\src\Antiphon` or
`C:\Antiphon\worktrees`, so an in-tree `.antiphon/...` results root false-refuses the
owned-clone cases when the harness runs from a linked worktree (CARD-0616).

| CP | After | Build | Group | Filter | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c599/` | unit | `/*/*/*/*[Category=Unit]` | R-1, R-2 | all Unit, >= 1 executed, 0 failed; expanded roster | 10 |
| CP-2 | S1-S4 | `CP-1` | profile-credit | `/*/*/(ReleaseGatePolicyTests*)\|(ReleaseGateRunTests*)/*` | V-1, V-2, V-3, R-4 | 13 C599 methods, all internal rows, 0 failed/skipped | 16 |
| CP-3 | S1-S4 | `CP-1` | e2e-evidence | `/*/*/ReleaseGateE2EEvidenceTests/*` | V-4 | 3 C599 methods, all internal rows, 0 failed/skipped | 12 |
| CP-4 | S1-S4 | `CP-1` | external-contracts | `/*/*/(ReleaseGateRegistrationTests*)\|(ReleaseGatePublicationTests*)\|(ReleaseGateStatusTests*)/*` | V-5, V-6, V-7 | 14 C599 methods, all internal rows, 0 failed/skipped | 20 |
| CP-5 | S1-S4 | `CP-1` | nightly-regression | `/*/*/(NightlyScriptsTests*)\|(NightlyVerificationContractTests*)\|(NightlyWatchdogCoreTests*)/*` | R-1 | all listed existing methods and harness rows, 0 failed | 25 |
| CP-6 | S1-S4 | `CP-1` | interim-boundary | `/*/*/(InterimVerificationReadinessTests*)\|(InterimVerificationPolicyTests*)\|(InterimVerificationLandGuardTests*)\|(InterimVerificationLandGitTests*)\|(VerificationRoundDispatchTests*)\|(ReleaseGateActivationTests*)/*` | R-2 | all existing plus 7 C599 methods and all rows, 0 failed | 20 |
| CP-7 | S1-S4 | `CP-1` | completion-receipts | `/*/*/VerificationRoundDeliveryTests/C544_CompletionRe*` | R-2 | 3 methods; receipt 8, recovery 36, whole-wire 3 internal rows | 18 |
| CP-8 | S1-S4 | `none (PowerShell harness)` | run-regression | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -ResultsDirectory C:\Antiphon\checkpoints\c599-cp8` | R-1 | complete declared harness inventory and C599 cases, 0 failed | 3 |
| CP-9 | S1-S4 | `none (harness owns isolated probe build)` | coverage-regression | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -ResultsDirectory C:\Antiphon\checkpoints\c599-cp9` | R-1, V-1, V-4 | complete declared inventory, actual probe UID roster, 0 failed | 5 |
| CP-10 | S1-S4 | `none (PowerShell harness)` | report-regression | `pwsh -NoProfile -File scripts/test-nightly-report.ps1 -ResultsDirectory C:\Antiphon\checkpoints\c599-cp10` | R-4 | all existing/new lane cases, 0 failed; stubs not receipt evidence | 3 |
| CP-11 | S1-S4 | `none` | client-install | `npm --prefix client ci` | V-4, R-3 | exit 0 at committed SHA | 3 |
| CP-12 | S1-S4 | `client/dist only` | client-build | `npm --prefix client run build` | V-4, R-3 | exit 0; run/SHA/bundle digest receipt | 2 |
| CP-13 | S1-S4 | `none` | client-lint | `npm --prefix client run lint` | V-4, R-3 | exit 0, no lint red waiver | 2 |
| CP-14 | S1-S4 | `tests/Antiphon.E2E -> bin-c599/` | browser-install | `pwsh -NoProfile -File tests/Antiphon.E2E/bin-c599/playwright.ps1 install chromium` | V-4, R-3 | isolated build and generated matching install exit 0 | 5 |
| CP-15 | S1-S4 | `CP-14` | e2e-metadata | `/*/*/(PtyBackendEnvGuardTests*)\|(TestClassificationGuardTests*)/*` | V-1, V-4, R-3 | 2 methods, 2 passed, before SharedApp in a fresh process | 2 |
| CP-16 | S1-S4 | `CP-14` | browser-smoke | `/*/*/SmokeE2ETests/*` | R-3 | 2 methods, 2 passed, 0 skipped | 8 |
| CP-17 | S1-S4 | `CP-14` | fixture-isolation | `/*/*/(ReleaseGateIsolationTests*)\|(IsolatedSessionRunnerTeardownTests*)/*` | V-4, R-3 | 3 C599 and 2 teardown methods, all rows, 0 failed/skipped | 12 |
| CP-18 | S1-S4 | `CP-14` | board-receipts | `/*/*/ReleaseGateReportDeliveryTests/*` | R-4 | 2 C599 methods, every delivery cut, 0 failed/skipped | 12 |

S5/V-8 and S6/V-9 are post-land operational acceptance, deliberately outside the ordinary
CP union (V-1..V-7 and R-1..R-4). Full-profile native runs in those slices generate their own
per-suite/per-UID counts, manifests and full receipts. Observe both RC schedule slots even
if the first succeeds; at least one must publish a full green. Existing F-1..F-6 labels and
cuts remain unchanged, followed by the real production qualification notice. Operator-only
reader login/live notice and activation retain their documented authority boundary.

Code reports one CHECKPOINT line per CP with commit, build status, literal filter/command,
executed/passed/failed/skipped counts, TRX/evidence location and rerun count. Reject zero
selected methods or missing required rows. Each harness uses a fresh private results
directory; after failure use a new suffix rather than stale files. On inherited red, run
the exact failing test at the baseline before attribution; report that extra run's reason.
Never broaden filters, retries or timeouts to get green. Run Antiphon.Tests, E2E and native
assemblies sequentially. Remove only exact owned bin-c599 directories after verifying
resolved paths remain inside this worktree. TestDesign created no such build output.

### Cost

All figures are estimates, not measurements or permission to widen timeouts.

- Ordinary Code V/R floor: **178 minutes**, the sum of CP-1..CP-18. Of this, 15 minutes
  estimates setup/build/install (CP-1 build 4, CP-9 probe build 1, CP-11 install 3,
  CP-12 bundle build 2, CP-14 E2E build/browser install 5); 163 minutes is V/R execution.
  Authoring/debugging is additional; allow 8-16 hours for the new seams and fixture repairs.
- Mutation floor: **982 minutes** for every PC's exact Class.Method filter above:
  110 script/config cycles x 3 min = 330; 74 compiled application cycles x 6 = 444;
  6 completion-delivery cycles x 8 = 48; 16 E2E cycles x 10 = 160.
  Each cycle includes break, decisive red, restore and green. Script cycles reuse the
  unchanged test assembly; compiled cycles include two isolated incremental builds at
  2 min each (384 minutes total build). Remaining mutation execution/restoration is
  598 minutes. No whole-class mutation reruns are budgeted.
- Ordinary plus every PC: **1,160 minutes** = 399 setup/build + 163 ordinary V/R +
  598 mutation execution/restoration. Per-row cycle estimates are in the PC inventory;
  sequential execution is the cost floor here. Independent shard scheduling may shorten
  elapsed time only under the testing guide's ownership/schema/limiter rules; it does
  not remove controls or reduce claimed work minutes.
- Required S5/S6 qualification is separately estimated at **840 active/run minutes**:
  four full native runs x 120 = 480 (manual/scheduled master and manual/scheduled RC);
  two additional full E2E allowances x 60 = 120; registration/host setup 90;
  F-1..F-6 and recipient readbacks 120; pilot/Final evidence 30.
  This is a planning floor, not established capacity. Longer measured suites add to it.
  Overnight master and both RC slots require at least 1-2 calendar days; busy/red slots
  extend acceptance. Total commissioning floor including these runs is **2,000 minutes**
  plus authoring, failures and calendar waits.
- Verification savings: one shared Antiphon.Tests build avoids six redundant 4-minute
  builds (24 min); one shared E2E build avoids three redundant 3-minute builds (9 min):
  **33 estimated ordinary minutes saved** versus rebuilding every TUnit group.
  The finalized 178-minute ordinary scope is **76 minutes above** the provisional 102,
  justified by missing queued-readiness, producer-to-recipient, metadata, isolation and
  recovery checks. No controls were omitted for speed.
- Product/per-change savings claimed now: **0 minutes and $0 measured**. A plan and
  unactivated receipts prove no actual saving. S5 records comparable Interim and Final
  command-correlated costs at one committed source; any eventual saving applies to
  bounded repair rounds, with Final still required.

Pre-handoff audit: bodies read; **guards=206, mapped=206, missing=0, duplicate PC
mappings=0**. Every PC specifies a buildable production defect, literal test method and
decisive assertion, including new methods commissioned to Code. All 42 new methods fall
in a checkpoint filter. Ordinary CP sum=178; all-PC sum=982. No build, test execution,
live registration, credential change or release occurred in TestDesign. Source/fixture
inspection, census and document consistency checks are the evidence for this stage.

--- next stage ---
next: code
handoff: Implement S1-S4 and 42 C599 methods using CP-1..18. Preserve master/RC separation and exact Final/Full before land. Review before land; method-scoped Mutation after land. S5 actual Windmill/master qualification/pilot and S6 both real RC slots with at least one scheduled full green published release remain required.
artifact: docs/superpowers/plans/2026-09-22-card-0599-release-gate-activation-plan.md
