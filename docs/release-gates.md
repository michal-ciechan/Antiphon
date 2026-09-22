# Release gates: Interim rounds, RC profile and published releases

Owner document for CARD-0599. It covers the credit split between the nightly master lane
and the release-candidate lane, the RC coverage profile including automated E2E, CalVer
tags and GitHub Releases, and the dormant Interim activation configuration.

> **Deployment status: UNQUALIFIED.** S1-S4 are implemented and reviewed, but nothing here
> is registered or running. There is no Windmill RC job, no enabled RC schedule, no
> qualification receipt, no published tag and no Interim pilot. `InterimVerification.Enabled`
> ships `false`. CARD-0599's operating acceptance stays open until the S5 and S6 receipts
> exist. Do not describe the release-gate model as operational, and do not treat a checked-in
> definition file as registration evidence.

Related owners: [testing and build](testing-and-build.md), [nightly watchdog](nightly-watchdog.md),
[orchestration loop](orchestration-loop.md), [Windmill definitions](../scripts/windmill/README.md),
[HTTP operations](ops-http.md), [AppHost runbook](apphost-runbook.md).

## Two lanes, two kinds of credit

Master is still the integration branch, and its scheduled readiness contract is unchanged.
A release candidate is an immutable cut of master that is tested under a wider profile.
The two lanes earn **different credit** and write **different files**.

| | master lane | RC lane |
|---|---|---|
| Profile | `nightly` | `rc` |
| Trigger | `scheduled` | `rc` |
| Ref | `master` / `origin/master` | `release/rc-YYYYMMDDTHHMMSSZ` |
| Credit kind | `master-scheduled` | `rc-release` |
| Green written to | `C:\Antiphon\nightly\last-complete-green.json` | `C:\Antiphon\releases\candidates\<candidate-id>\complete-green.json` |
| Earns | master daily readiness (Interim admission) | permission to publish one release |

`Test-NightlyCompleteGreenPredicate` in `scripts/lib/nightly-run-impl.ps1` takes the credit
kind it is being asked about and defaults to `master-scheduled`, so every pre-existing caller
keeps its original meaning. The decision itself lives in `Get-ReleaseGateCreditVerdict`
(`scripts/lib/release-gate.ps1`). Both kinds require:

- `coverageComplete`, `testsPassed` and `reportDelivered` as **real booleans** - a JSON
  `"true"` string earns nothing;
- `exitCode` 0, a non-empty `runId` and a non-empty `policyHash`.

`rc-release` additionally requires a valid candidate ref, a full 40-hex `sha`, an
`expectedSha` equal to it, and a candidate id. Any other profile/trigger/ref combination
earns **nothing**; there is no default kind and unknown combinations fail closed.

Consequences that are deliberate, not incidental:

- **RC green never substitutes for master readiness.** An immutable older candidate says
  nothing about the current integration stream. An RC success cannot repair a failed,
  missing, stale or overdue scheduled master run.
- **RC failure does not revoke a healthy master green.** It blocks that release only.
- An RC run refuses to start against the master state root at all, and an RC run leaves
  `last-run.json`, `last-complete-green.json`, `last-monitor.json` and
  `interim-qualification-receipt.json` byte-identical.
- The once-nightly London due-date / 08:00 readiness model and the watchdog timing model
  are unchanged.

## Serialising the two lanes

Native work is expensive and must never overlap. Both entrypoints acquire one shared
verification lock at `C:\Antiphon\verification\native-run.lock` (overridable with
`-CoordinationRoot`, which the tests use for a private root) in the same order, before any
clone, build or test. Owner identity is PID + process start + run id + continuation id, and
it is carried across the self-reexec hop so a hop proves inheritance rather than stealing.

A live owner is **never** stolen on age. On contention a scheduled RC writes a
`deferred-busy` attempt, returns non-green and waits for its next slot; there is no retry
queue. The master schedule keeps its existing missed-run and readiness consequences - an RC
is never killed to make master green. Assembly-local `ParallelLimiter<ProcessSpawnLimit>`
limiters are per test project and are **not** cross-process locks; this file is.

## The RC coverage profile

`tests/test-execution-policy.json` is schema **v2**. It keeps `defaultSuites` as the
compatible nightly default and adds `profiles`:

- `profiles.nightly.requiredSuites` - the seven existing suites. E2E stays out, and
  `OptIn` / `Explicit` still excludes exactly as before. **The default nightly lane is
  unchanged.**
- `profiles.rc.requiredSuites` - those seven **plus `e2e`**.

The selected profile is authoritative for required suites. An unknown profile name, or `rc`
asked of a schema-v1 policy, is a hard refusal rather than a silent fall back to the seven.
The whole policy, profile dispositions and exclusions included, is covered by `policyHash`;
a stale hash refuses.

`rc` means the full **automated Windows profile**: all Unit, Integration and Slow cases
eligible for this host, the native suites, disposable-broker messaging, client
build/lint/Vitest, the script census and automated E2E. It is *not* every test on every
platform. Explicitly outside it, each with a reason and an owner recorded in the policy:
live provider and headed canaries, separately approved distiller tests, QEMU SourceLanding
controls (CARD-0490) and Linux-only lanes (CARD-0590).

### OptIn is not exclusion in a profile lane

In the `rc` profile, `OptIn` alone does **not** exclude a discovery node. Only a declared
`profiles.rc.exclusions` row - class, or class plus named methods - excludes, and every row
must carry a `reason` and an `owner` or the policy refuses to load. Raw discovery categories
are preserved on the node and reported; they no longer decide.

The consequence is the point: a new or unclassified case **stays required** and fails the
coverage census rather than silently inheriting manual status. `Get-NightlyDiscoveryCensus`
gives every UID exactly one disposition and emits required/excluded counts plus a digest that
becomes a manifest artifact. A required skip, a missing UID, a duplicate chunk membership, a
missing TRX, an unknown outcome, **zero executed required cases** or stale evidence all block
release. A zero-required-UID run is never green in a profile lane.

### Automated E2E execution

`e2e` is a runnable suite disposition for `Profile=rc`; the nightly lane continues to leave
it out. Native ownership is derived from the suite definition (project + assembly), so `e2e`
serialises and produces TRX, discovery and execution diagnostics like every other native
suite, rather than needing a second hard-coded id list.

Before any E2E chunk runs: npm ci / build / lint at the pinned SHA, the E2E project build,
`playwright.ps1 install chromium` from the generated script, and a client bundle present.
A missing browser, unavailable Docker, stale bundle, fixture failure or unavailable owned
runner is **red or incomplete, never a skip**.

E2E uses the existing `AntiphonAppFixture`, `PlaywrightFixture` and `IsolatedSessionRunner`:
the app's runner endpoint is its own loopback random port, never production 17204; the
database is disposable; scratch repos are owned. Headed and live environment variables stay
cleared, as do inherited task tokens, production API overrides and distiller approval paths.
Normal E2E must not reach a live broker or an authenticated provider to satisfy coverage.

Replaces the earlier claim that `-Suites e2e` runs E2E: it did not. `nightly-tests-impl.ps1`
skipped `mode=manual` unconditionally and `$NightlyNativeGroups` omitted E2E, so the
selection was accepted and then dropped. Use `-Profile rc`.

## Cutting, testing and publishing a candidate

| Script | Job |
|---|---|
| `scripts/release-candidate.ps1` | Pin `origin/master` once in a producer-owned clone and push the create-only candidate branch. |
| `scripts/release-cut.ps1` | The lane entrypoint: cut, run the `rc` profile, publish only on RC complete-green. |
| `scripts/publish-release.ps1` | CalVer tag plus GitHub Release, bound to the tested SHA. |
| `scripts/release-status.ps1` | Is the running build a published release? Read-only. |
| `scripts/register-release-gates.ps1` | Idempotent Windmill registration front door. |

**Cut.** The candidate ref is exactly `release/rc-YYYYMMDDTHHMMSSZ`; `release/current` and
any other shape refuse. The full SHA is pinned **once** and written to the candidate journal
**before** the remote push. There is no force push, no repair commit on the candidate, and
no moving a tested branch. An existing remote candidate ref at a different SHA is a hard
refusal. The same journal/ref/SHA is an idempotent resume, and a lost push response is
resolved by re-reading the remote rather than pushing again under a new identity. The
producer clone must be owned: the canonical checkout, a linked worktree and an unmarked
clone are all refused.

**Test.** `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>` runs
against `C:\Antiphon\releases\checkout` and the candidate's private state root. `-Profile`,
`-ExpectedSha`, `-CandidateId`, `-ReleaseRoot` and `-CoordinationRoot` survive the
wrapper -> core -> self-reexec -> tests hops. Before tests and again before publication, the
checkout HEAD, candidate journal and remote candidate ref are compared to the pinned SHA. A
moved ref fails; it is never retested implicitly under its old identity.

**Fix.** RC findings are fixed through ordinary Code / Review / land onto master, then a
**new** candidate is cut and its full profile runs from scratch. The failed candidate's
evidence is kept and linked to its successor. A scheduled cut is skipped when that exact
master SHA already has a published release under the current profile and hash. A partial run
or a green under a different policy is never reused.

**Publish.** Only on RC complete-green, and only after the gate re-validates the candidate
journal, profile/policy hash, every required suite result, report delivery and the remote
candidate SHA. Diagnostic, `-NoReport` and seam-driven runs are refused outright. The
sequence, each transition journalled **before** the next begins:

1. reserve `vYYYY.MM.DD.N` (UTC cut date, positive daily sequence) in the publication journal
2. create the local annotated tag at that SHA
3. push that tag alone, never with `--force`
4. verify the peeled remote target equals the tested SHA
5. `gh release create <tag> --verify-tag --draft --notes-file <file>`
6. upload `release-manifest.json` and the sanitized verification summary
7. read tag and asset digests back
8. publish the draft, then persist the release id and URL

A retry resumes the same journal. Same tag/SHA/manifest is success or recovery; any mismatch
is a hard refusal. A tag with no published release stays **pending**, not released, and so
does a draft missing its assets. Tags are never deleted or recreated and another release's
evidence is never overwritten. After an ambiguous timeout, remote state is re-read before
another write; a crash after publication but before local acknowledgement recovers the
existing release id rather than allocating another `N`.

The manifest carries only allowlisted fields - schema version, repository, tag, candidate
ref and full SHA, native run and Windmill job/slot ids, UTC start/end, policy/profile/script
hashes, build and bundle hashes, per-suite discovered/required/executed/passed/failed/
skipped/excluded counts, exclusion reasons, summary digest and build-reported capabilities.
Capabilities are read from the candidate's own isolated server identity probe, never from the
production server and never from a copied constant. Raw logs, credentials, hostnames and
transcripts never reach a public asset.

Release notes name the commits since the prior published tag, card references where present,
test counts, profile and exclusions, evidence identities and the previous release. The first
release says there is no earlier release.

## Release identity is not local activation

`/api/version.version` remains the built SHA and `capabilities` remain feature probes; no new
web route and no change to NuGet or SDK assembly versioning. `scripts/release-status.ps1`
looks the running SHA up by **exact** match against published release manifests and returns
one of:

- `released` plus the matched tag and URL,
- `unreleased integration build`,
- `unknown` when publication or GitHub data is unavailable.

An ancestor tag, or simply the newest release, is never reported as a newer running SHA's
identity. Creating a release never restarts AppHost or the runner and never checks a tag out
in the main worktree. Updating master and restarting normally may correctly leave the dev
stack ahead of the latest release. If an operator later deploys a release, require
`/api/version` SHA equality to that tag's peeled SHA and the intended capabilities, using the
[canonical restart runbook](apphost-runbook.md).

## GitHub Actions is not a release prerequisite

Neither candidate admission nor publication depends on Actions being green; they depend on
this profile's fresh native evidence. CARD-0610 owns Actions repair, and the missing CI
signal is stated in initial release notes and qualification. That separation does **not**
waive an overlapping defect: the same client lint failure that reddens CI also reddens this
harness, and it blocks qualification and release until it is repaired on master. There is no
baseline-red waiver, retry loop or exclusion created solely to get past a failure. Linux
coverage stays owned by CARD-0590 / CARD-0605.

## Registration (operator-run, S5/S6)

`scripts/register-release-gates.ps1` is preview-only by default and writes nothing. It takes
an untracked `-Profile <json>` with `windmillBaseUrl`, `workspace` (must be `mc`),
`tokenFile`, `repositoryPath` and `projectId`; an inline token, password or secret in that
profile is refused. The token is read from the operator-placed file and is never printed,
logged or embedded in a script payload, and this script creates no token.

```powershell
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply
pwsh -NoProfile -File scripts/register-release-gates.ps1 -Profile C:\Antiphon\nightly\release-gates-deploy.json -Apply -EnableSchedule readiness
```

Registration uses the installed Windmill HTTP API, never a database write. Scripts are
created and read back by hash and content; schedules are always **created disabled**, and
`-EnableSchedule <nightly|readiness|rc>` (which requires `-Apply`) turns on exactly one. An
idempotent reapply discovers the existing objects, preserves an already-enabled matching
schedule and refuses cron/timezone drift; disabling is always explicit and never an accident
of the disabled creation payload. Enable `nightly` after its manual green and `rc` after RC
qualification. Qualification records the before/after/readback of each command, including a
second apply proving no duplicates.

Trigger provenance is derived from the runner's own schedule context (`WM_SCHEDULE_PATH`
matching the expected schedule path); otherwise the wrapper passes `manual`. An API caller's
`trigger` string cannot manufacture scheduled credit, and a missing schedule context refuses
scheduled credit rather than defaulting to it. RC uses `trigger=rc` plus separate
invocation-source, job and slot fields; there is no master-credit alias.

Planned schedules: master `0 30 0 * * *`, readiness `0 */30 * * * *`, RC
`0 30 8,16 * * *` - all `Europe/London`. Two RC cuts a day are *attempts*, not a guarantee
of two successful releases.

Secret placement, interactive Telegram login and authorization for live notices remain the
operator's, as the [custody rule](nightly-watchdog.md#custody) requires. A Code or Review
delegate does not run these commands against the live workspace.

## Interim verification rounds

`server/appsettings.json` now ships the section explicitly:

```json
"InterimVerification": {
  "Enabled": false,
  "CanonicalRepositoryPath": null,
  "ProjectId": null,
  "StateRoot": null,
  "MonitorFreshMinutes": 60,
  "MaxFileBytes": 65536
}
```

`FullOnly` remains the card default. After qualification the deployed server gets the real
values, through the existing `antiphon-server` user-secrets store under the actual AppHost
service account, or the corresponding `InterimVerification__...` environment on a deployment
without user secrets:

```json
"InterimVerification": {
  "Enabled": true,
  "CanonicalRepositoryPath": "C:\\src\\Antiphon",
  "ProjectId": "<actual Antiphon project GUID>",
  "StateRoot": "C:\\Antiphon\\nightly",
  "MonitorFreshMinutes": 60,
  "MaxFileBytes": 65536
}
```

`Antiphon.AppHost/Program.cs` launches the server as Development but does **not** forward an
arbitrary AppHost configuration section, so putting these values only in AppHost appsettings
does not activate the server. Verify effective behaviour with a real pilot admission and
refusal plus recorded deployment identity. Resolve the project through the board/project API,
not a hard-coded GUID and not the board id; validate the configured canonical path against
the task's repository identity before opt-in; keep Windows backslashes. The configured
canonical path is the qualified repository, not the isolated nightly clone.

### What Interim actually saves

The shipped Interim profile reduces **repeated repair-round verification**. It does not
reduce the final ordinary verification required to land a change, and it is not a weaker
landing policy.

- First rounds are Final. A Clean Final baseline is established first.
- An Interim round selects cumulative changed cases since that baseline (earlier repair cases
  included), unresolved-finding tests and named adjacent smoke, and records every deferred ID.
  Deferred IDs are never marked passed.
- Whole-Unit and affected-class ordinary V/R stay in Final.
- Unbounded shared or native impact stays Final.
- Unnamed broad integration/native work and automated E2E run periodically on RCs; a named
  ordinary check is never silently reclassified as "covered by the RC".
- The land latch is unchanged: an exact-SHA Final / Full Review still precedes land.

No claim that activating Interim moves all affected Slow or native tests from Final Review to
the RC gate is valid. Product savings measured so far: **zero minutes, zero dollars**. A plan
and unactivated receipts prove no saving. S5 records comparable Interim and Final
command-correlated costs at one committed source; broader opt-in is a later evidence-based
decision.

### Required before `Enabled=true`

| Artifact | Required content |
|---|---|
| `docs/investigations/<date>-card-0487-nightly-qualification.md` | Accepted CARD-0545 S6 result; deployed code, script/policy hashes, manual full unattended green, later real scheduled full unattended green, all seven suite counts, job/native-run correlation, notification and outage/recovery receipts. |
| `C:\Antiphon\nightly\watchdog-deploy.json` + operator watchdog env/session | Qualifying independent host, correct instance, reachable snapshot; custody local, secrets never committed. |
| `C:\Antiphon\nightly\readiness-config.json` | Expected script/policy hashes, watchdog URL/instance, canonical repository and actual project. |
| `C:\Antiphon\nightly\interim-qualification-receipt.json` | Schema 1; canonical repository/project; committed qualification artifact path and full commit; policy/script hashes; manualRunId, scheduledRunId, scheduledJobId; non-empty recipientEvidenceIds and outageRecoveryEvidenceIds; watchdogInstanceId. |
| `last-complete-green.json` + `last-monitor.json` under the same root | Real scheduled master green for the valid London due day; fresh Healthy/ReadyForDeferral booleans; matching repository/project/hash/native-run/job/watchdog identities. Freshness stays 0-60 minutes; future timestamps rejected. |
| `docs/investigations/<date>-card-0544-verification-pilot.md` | Baseline/subject/selection/card revision, admission and refusal evidence, deferred cases actually run in Final, exact-SHA land, rollback check, measured costs. |
| `docs/investigations/<date>-card-0599-release-gate-qualification.md` | Registration readbacks, full automated RC roster and counts including E2E, two real schedule-slot observations, published tag/release/manifest, exact-SHA status lookup, all exclusions and limits. |

Commit the qualification document **before** writing its full commit SHA into the trusted
receipt. Do not bootstrap readiness by forging `Healthy=true`, accepting a send
acknowledgement as a receipt, or substituting a scheduled RC for the scheduled master
acceptance run.

## Failure handling and rollback

- **Failed RC:** retain evidence and the incident, publish nothing, fix through master, recut.
- **Failed publication after green:** resume the same journal. Never change tested source.
- **Lost master readiness:** Interim admission and queued launch fail closed; existing owner
  latches persist and require Final. RC green cannot restore eligibility.
- **Rollback:** set pilot policies `FullOnly`, `Enabled=false`, and disable RC scheduling and
  publication. Keep the master and readiness jobs and the watchdog running for evidence.
  Preserve tags, releases, candidate journals and qualification receipts - rollback is never
  destructive.
- **Changed script, profile or hash:** an old qualification does not silently apply. Requalify
  identity and readiness before further Interim admission. Old releases keep their original
  manifest.
- RC job failure is visible in Windmill and in a dedicated `release-gate` board incident. The
  independent watchdog continues to monitor **master only**; there is no independent RC
  scheduler-outage notification. An RC incident can never update or close a master nightly
  incident, and a reporting failure is a failed job with a local durable record, not green.
