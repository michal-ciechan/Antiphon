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

### Cost

Provisional ordinary Code floor: **102 minutes**, the sum of the CP table below, plus roughly
4-8 hours authoring. These are planning estimates, not measured suite costs. TestDesign must
recost after reading the existing wrapped harness roster and any E2E isolation repairs.
Full-suite qualification is separate: allow the inherited 2-6 hour observation estimate per
full run, an unmeasured E2E allowance, a subsequent overnight master boundary and two RC slots
(at least 1-2 calendar days). Existing timeouts remain failure boundaries, not larger budgets.
No speedup or current full-suite duration was established in this Plan.

### Checkpoints

**Provisional closed-list shape for TestDesign to finalize, not Code authorization.** All
rows follow committed S1-S4 so reused outputs have identical source. Use fresh result roots,
`run-checkpoint.ps1` for TUnit, per-CP expanded counts and expected class identities; no UID/tree
selector mixing. Table `\|` escapes are Markdown only: actual OR filters use plain `|`.
S5/V-8 and S6/V-9 are explicitly separate post-land operational acceptance, not omitted Code rows.

| CP | After | Build | Group | Filter / exact command group | Covers | Expect | Min |
|---|---|---|---|---|---|---|---|
| CP-1 | S1-S4 | `tests/Antiphon.Tests -> bin-c599/` | unit | `/*/*/*/*[Category=Unit]` | ordinary Unit floor | >= 1 executed, 0 failed; record expanded roster | 8 |
| CP-2 | S1-S4 | CP-1 | profile-credit | `/*/*/(ReleaseGatePolicyTests*)\|(ReleaseGateRunTests*)/*` | V-1, V-2, V-3 | all listed, 0 failed | 8 |
| CP-3 | S1-S4 | CP-1 | e2e-evidence | `/*/*/ReleaseGateE2EEvidenceTests/*` | V-4 | all listed, 0 failed | 10 |
| CP-4 | S1-S4 | CP-1 | external-contracts | `/*/*/(ReleaseGateRegistrationTests*)\|(ReleaseGatePublicationTests*)\|(ReleaseGateStatusTests*)/*` | V-5, V-6, V-7 | all listed, 0 failed | 8 |
| CP-5 | S1-S4 | CP-1 | nightly-regression | `/*/*/(NightlyScriptsTests*)\|(NightlyVerificationContractTests*)\|(NightlyWatchdogCoreTests*)/*` | R-1 | all listed, 0 failed | 25 |
| CP-6 | S1-S4 | CP-1 | interim-boundary | `/*/*/(InterimVerificationReadinessTests*)\|(InterimVerificationPolicyTests*)\|(InterimVerificationLandGuardTests*)\|(InterimVerificationLandGitTests*)/*` | R-2 | all listed, 0 failed | 10 |
| CP-7 | S1-S4 | Client build prerequisites | e2e-prerequisites | In `client`: `npm ci`, `npm run build`, `npm run lint`, serially; write run/SHA/digest receipt | V-4, R-3 | all commands exit 0, fresh bundle | 5 |
| CP-8 | S1-S4 | `tests/Antiphon.E2E -> bin-c599/`; generated `playwright.ps1 install chromium` before execution | browser-smoke | `/*/*/SmokeE2ETests/*` | R-3 | 2 executed, 2 passed, 0 skipped | 10 |
| CP-9 | S1-S4 | CP-8 | fixture-isolation | `/*/*/(ReleaseGateIsolationTests*)\|(IsolatedSessionRunnerTeardownTests*)/*` | V-4, R-3 | all listed, 0 failed, 0 required skipped | 10 |
| CP-10 | S1-S4 | none (PowerShell fixture harness) | run-regression | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -ResultsDirectory .antiphon/c599-cp10` | R-1 | complete declared harness inventory, 0 failed; fresh directory | 2 |
| CP-11 | S1-S4 | none (harness owns its isolated probe build) | coverage-regression | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -ResultsDirectory .antiphon/c599-cp11` | R-1 | complete declared harness inventory, 0 failed; fresh directory | 5 |
| CP-12 | S1-S4 | none (PowerShell fixture harness) | report-regression | `pwsh -NoProfile -File scripts/test-nightly-report.ps1 -ResultsDirectory .antiphon/c599-cp12` | R-4 | all declared existing and new lane cases, 0 failed; fresh directory | 1 |

Remove the exact producer-owned alternate outputs after runs, verifying absolute paths stay
inside this worktree. Execute Antiphon.Tests and E2E/native projects sequentially. On red,
reproduce named inherited failures at the baseline before attribution; fixes require a commit
before rerunning only affected CP rows. Any additional run needs its stated reason.

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

--- next stage ---
next: test-design
handoff: Finalize V/R controls, profile eligibility census and CP-1..12 for this activation-first design: separate master/RC credit, automated E2E including OptIn accounting, immutable RC publication and actual Windmill qualification. Preserve Final-before-land and carry S5/S6 as required operational acceptance.
artifact: docs/superpowers/plans/2026-09-22-card-0599-release-gate-activation-plan.md
