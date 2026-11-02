# CARD-1545: where the card stands and the next stage on server2

Investigate task a9b3bc09, 2026-11-02, base master `10c8c2471633c7c0212294afc57348878021e183`.
Read-only: no build, test or production edit. Sources: the card and its history, the task list for
the card (`GET /api/agent-tasks?projectId=…&since=2026-10-22`, filtered by title), the reports of
tasks 185b71e2, 3ef5e873, 8cb9f7d5, 52219abc, 60a7cb17 and 91ed8fe0, the plan
`docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md` ("the plan"), and
`docs/superpowers/plans/2026-10-23-card-1545-project-selection-slice.md` ("the Oct-23 plan").

## 1. Slice table

| Item | State | Evidence |
|---|---|---|
| Oct-23 advisory project planner (S1–S6 of the Oct-23 plan; CP-1..CP-7 there) | **On master**, landed by Merge 91ed8fe0: operation `4edfabd1-b29d-49a7-840f-44d5bd86a4a8`, phase Complete, publication **Landed**, verified `a97194ee7`, remote confirmed 2026-10-24T03:05Z | `scripts/lib/rc-project-selection.ps1` (`Get-RcProjectSelection` :164), `tests/rc-project-categories.json` (7 batches), `release-selection.ps1:215` adds `projectProposal`; `:228` still sets `effectiveMode='Full'`. No Review task exists for it (Code 2dbadf60, then Merge 91ed8fe0). PC-1..PC-7 were never run (no Mutation task). |
| S1a-baseline, S1a (CP-27, CP-28, CP-25, CP-1, CP-30) | **On master** | Combined set merged at `8f721e971`, fast-forwarded to master `0493af102` by Merge 8cb9f7d5. Final Review 185b71e2 clean at reviewed `ffeaa28f0` (code `5e3dcc4c`), `reviewedSourceClean: true`, scope Full. |
| S1b-admission, S1b-outcomes (CP-2, CP-16) | **On master** | Review 3ef5e873 clean at `f41be02bd`; `LiveLaneRunnerTests.cs`, `LiveLaneOutcomeTests.cs`, `scripts/run-live-lane.ps1`, `scripts/lib/live-lane-outcome.ps1` exist on master. |
| S1c-executable, -profile, -startup-oracles, -delivery-oracles, -sanitization (CP-17, CP-23, CP-18, CP-24, CP-3, CP-19, CP-20, CP-26, CP-29) | **On master** (`0493af102`) | Same combined set; 185b71e2 lists every row green on `ffeaa28f0` with clean receipts. Also on `origin/rc/2026-11-01` (ancestry checked). |
| S1c-native acceptance (CP-4..CP-7, CP-21) | **Not started** | `tests/live-lane-checkpoints/live-claude-core-windows.yaml` on master is a 6-line stub: `nativeQualified: false`, "No executable command, build or paid row is present". |
| Post-land Mutation of S1 | **Not started; not dispatchable as is** | Land operation `cfc0cf92-8f3c-4aed-a0eb-e874b9c67a00` (task 7d513c4d) reads phase **Refused**, publication **Unconfirmed**, `remoteConfirmedAt: null`, reason `rebase_conflict`, though `0493af102` is on master. `SourceLandingAdmission.cs:31` refuses `verification_source_unconfirmed` unless `HasPublication` (`AgentTaskLandingState.cs:11-19`: needs RemoteConfirmedAt and Landed/AlreadyPresent). CARD-1637 (Backlog) owns the land gap. No `Post-land verification: CARD-1545` companion card exists (board search). |
| Post-land Mutation of the Oct-23 slice (PC-1..PC-7) | **Not started; dispatchable** | Operation 4edfabd1 has confirmed publication. Needs a companion card first. |
| S2a-1 repository closure (CP-8) | **Not started** | No `scripts/lib/live-lane-fingerprint.ps1` on master; no task in the card's list; no TestDesign doc for S2+. |
| S2a-2 environment attestation (CP-22) | **Not started** | No `live-lane-attestation.ps1`. |
| S2b receipt schema (CP-9) | **Not started** | No `live-lane-receipt.ps1`. |
| S2c trusted issuer (CP-10) | **Not started** | No `live-lane-issuer.ps1`. |
| S3a advisory cut check (CP-11) | **Not started** | No `release-live-lane.ps1`. |
| S3b authority/publication (CP-12, CP-13) | **Not started** | |
| S3c enforce/migrate C680 (CP-14) | **Not started** | |
| S4a rc-lanes shadow (CP-15) | **Not started** | No `tests/rc-lanes.json`, no `RcLaneShadowTests.cs`. |
| S4b+ later lanes | **Not started** | Plan: each needs its own TestDesign. |

No branch carries unlanded S2+ work: the card's task list has no task after S1 except the land/close
tasks, and `git log --all` over the S2/S3/S4 file names returns nothing across the 4,708 fetched `origin` refs.

## 2. Where each item can run

| Item | server2 / Linux | Needs Windows or desktop | Waits for operator |
|---|---|---|---|
| S2a-1, S2a-2, S2b, S3a, S3b, S4a | Yes: PowerShell libraries plus `Antiphon.Tests.Scripts` TUnit rows with owned fake inputs (plan, Verification design: "All new portable test classes carry Unit metadata, use owned fake inputs"). The whole S1 series and the Oct-23 slice were coded and reviewed on server2. | No for ordinary rows. The plan wants extra native Windows observations for V-2a and V-7 only. | S2a/S2b: **D-6** reuse policy. The plan says "operator may defer S2 fingerprint code if choosing always-rerun". Recommended default (plan): reuse for at most 24 h with equal inputs, same line, host and account, plus fresh probes. Under always-rerun, S2 is diagnostic only. S3a: **D-5**, default warning-only during shadow. S4a: none (D-10 already says shadow-only). |
| S2c issuer (CP-10) | Mock-key verifier logic only | Issuer custody on a host | **D-9** signer. Default: diagnostic receipts now, no signer. The plan says to commission a separate design if no issuer boundary exists. |
| S3c (CP-14) | Code is portable | Needs a native lane green first | D-5, D-6, D-9, plus a green native lane and shadow agreement (plan: "Required dependencies for S3 enforcement") |
| S1c-native CP-4..CP-7 | No | Yes. Eligible authenticated Windows host, ConPTY, `ANTIPHON_HEADED_TESTS=1` through `run-live-lane.ps1` only. Full clears that flag (`tests/test-execution-policy.json` safeEnvironment.clear; `nightly-policy.ps1` Get-NightlySafeChildEnvironment, per plan Ground truth). | **D-3** host/account (default: separate `CLAUDE_CONFIG_DIR` on an eligible Windows host, never the runner's profile). **D-4** spend (default: four intended prompts, stop at six, 25-minute ceiling, Haiku in the lane context). **D-11** startup premise (default: an absent control is INCONCLUSIVE). |
| S1c-native CP-21 | No: `ClaudeSubmitContractTests.cs:88` skips "ConPTY only on Windows" | Windows, no provider | None. These two methods have only `Category("Pty")` and no rc exclusion (the policy names only `ClaudeSubmitContractLiveTests`, :300), so Windows Full already runs them as required tests. As a lane row they still need the native manifest. |
| Mutation of the Oct-23 slice | Yes (`-Runner server2 -SourceLanding 4edfabd1…`, linux-cgroup-v1, `orchestration-loop.md` §Mutation producers) | No | None |
| Mutation of S1 | Probably yes once admitted; most PCs are portable wiring rows | CP-4..7 PCs no | Blocked on confirmed publication of operation cfc0cf92 (CARD-1637 or an operator decision on how to confirm it). |

## 3. Next stage to dispatch now

**Role: TestDesign** (ClaudeCode High, `-Runner server2 -Worktree`), base `-StartRef origin/master`
(`10c8c2471633c7c0212294afc57348878021e183` at investigation time). Each remaining slice needs a
TestDesign before Code. The plan says "TestDesign must validate current importer syntax, exact expanded rosters, supported deadline
settings, baseline-compatible red assertions and native prerequisites before Code". Only S1 had
TestDesigns (6a22c243, 8b284c8f, 1206b8f3).

- **Scope (two sentences):** Write the test design for S2a-1 (CP-8, V-5a), S2a-2 (CP-22, V-5b) and S2b
  (CP-9, V-6), the provider-free fingerprint, attestation and receipt chain, against master as it
  stands after the S1 land. The design should turn the plan's proposed methods into exact rosters, red
  witnesses and an importable `### Checkpoints` table, and it should record D-6 as provisional.
- **Inputs:** the plan (sections Input fingerprint and lane state, Implementation slices, Verification
  design V-5a/V-5b/V-6, Checkpoints, Performance budget). The S1 TestDesign docs
  `docs/superpowers/plans/2026-10-31-card-1545-s1b-test-design.md` and `…-s1c-test-design.md` for format.
  Master code `scripts/lib/rc-project-selection.ps1` (`Read-RcProjectGraph` :38, reused by S2a-1),
  `scripts/lib/live-lane-manifest.ps1`, `scripts/lib/live-lane-run.ps1`, `scripts/lib/live-lane-outcome.ps1`
  and `tests/Shared/LiveClaude/LiveClaudeProfile*.cs`, `LiveClaudeExecutable*.cs` (the S1 attestations S2a-2 must consume).
  Review 185b71e2's report (`.antiphon/task-185b71e2.md` on `origin/feat/card-task-185b71e2`).
- **Rows involved:** CP-8 (7 methods), CP-22 (6), CP-9 (6), all `tests/Antiphon.Tests` builds. TestDesign itself runs no rows.
- **Duration:** S1 TestDesigns took 17–23 min (Codex Frontier). Estimate 30–45 min for three slices.
  Later Code: the plan estimates 45–60 min per slice. S1 Code tasks actually took 24–335 min.
- **Risks:** (a) D-6. If the operator later picks always-rerun, S2 becomes diagnostic only. The rollback
  is "force due", so the risk is wasted spend, not a broken gate. (b) The audit 60a7cb17 found 16% of
  CARD-1545-series defects came from parallel slices clashing. Design the three as one sequential
  Code chain, not three parallel Code tasks. (c) S2a-1's real inputs are the live roots
  (`src/Antiphon.SessionRunner/`, `server/`, …). Tests must use owned fixtures so in-flight edits there
  do not move the expected values.
- **Conflict with in-flight work (2026-11-02):** In-flight tasks are 00abf1bf (CARD-0824 slot pool, runner/dispatcher),
  253f7e9d (CARD-1624 Review), bd1e86ea (CARD-1514 landing fence) and 250a07fb (CARD-1514 Windows Full run).
  The proposed S2 files are all new: `scripts/lib/live-lane-{fingerprint,attestation,receipt}.ps1`,
  `scripts/live-lane-status.ps1`, `scripts/test-live-lane-{fingerprint,attestation,receipt}.ps1`, and
  `tests/Antiphon.Tests/Scripts/LiveLane{Fingerprint,Attestation,Receipt}Tests.cs`. Antiphon.Tests
  compiles `Scripts/` by glob, so `Antiphon.Tests.csproj` needs no edit. **No file overlap was found.**
  Possible shared-file touches for Code to check: `tests/test-execution-policy.json` or the test-classification roster
  (classification of new Unit classes), and `server/Application/Services/LandVerificationBuildGraph.{evaluation,manifest}.txt`
  (the land-graph digest, re-pinned by S1 in 4c0cdfe3a). The landing-fence work may also re-pin these.
  Run land-graph `-Check` before land.

**Second choice, independent of D-6: TestDesign for S4a** (CP-15, V-12, `tests/rc-lanes.json`,
`RcLaneShadowTests`). S4a is shadow-only and closest to the card's title (per-app batches with
downstream dependents). The plan states no dependency on S2/S3 for it. It **modifies**
`rc-project-selection.ps1`, which S2a-1 reuses, so its Code must follow S2a-1's Code, or be checked
against it with merge-tree. Its TestDesign may run in parallel with the one above.

**Also dispatchable now (not a CARD-1545 Code slot):** a Mutation for the Oct-23 slice, after filing its
companion card and with `-SourceLanding 4edfabd1-b29d-49a7-840f-44d5bd86a4a8`. It covers PC-1..PC-7 from the Oct-23 plan.

## 4. Draft brief (TestDesign, paste-ready)

> TestDesign for CARD-1545 S2a-1, S2a-2 and S2b (fingerprint, environment attestation, receipt) on
> server2 from origin/master. Goal: one test-design doc
> `docs/superpowers/plans/2026-11-02-card-1545-s2-test-design.md` that turns plan V-5a/V-5b/V-6 into
> exact method rosters, assertion-red witnesses, and an importable `### Checkpoints` table (CP-8, CP-22,
> CP-9, one After group per slice, `tests/Antiphon.Tests -> bin-live-<x>/`, exact
> `/*/Antiphon.Tests.Scripts/<Class>/*` filters, Min = method count). Validate the table with
> `dotnet run --project tools/Antiphon.Checkpoints -- import --plan <doc>` only (no run). Invariants: provider-free, owned
> fake inputs only, no live Claude/ConPTY/credential; S2a-1 reuses `Read-RcProjectGraph` and does not
> modify `rc-project-selection.ps1`; S2a-2 consumes S1's executable/profile/settings attestations and
> does not rebuild launch machinery; the fingerprint excludes candidate SHA, time, run id and scratch
> paths; unknown input refuses reuse; only GREEN updates last-green and a newer red revokes. Keep D-6
> PROVISIONAL (24 h bounded reuse) and state what changes if the operator chooses always-rerun. Specify
> a sequential Code chain S2a-1 → S2a-2 → S2b, each with red-first witnesses and its own CP row. Name
> every shared file Code would touch (policy/classification roster, land-graph digest). Stop and report
> blocked if any V-5a/V-5b/V-6 oracle needs a native Windows observation or an operator decision
> beyond D-6, or if the plan's proposed method names collide with existing classes. No production
> code, no build, no test run; commit and push the doc only.

## 5. Stale or contradicted premises

- The card description cites `release-selection.ps1:213` for change-aware-unqualified. Master now sets
  `effectiveMode='Full'` at `:228` and adds an advisory `projectProposal` (:207-:228). Still Full; only the line moved.
- The plan says "S1b-admission and S1b-outcomes are already in TestDesign/Code". They are landed.
  The plan's S1 order is complete apart from native acceptance.
- The close report 52219abc says CP-21 is "probably covered by Full". It is required in the Pty suite on
  Windows (not excluded; Windows-only skip at `ClaudeSubmitContractTests.cs:88`). The lane CP-21 row still
  does not exist, because the native manifest is a stub.
- The 8cb9f7d5 report calls master "fast-forwarded", and the land record still shows Refused/Unconfirmed.
  So any brief that says "Mutation after land" for S1 rests on a false premise until publication is
  confirmed. One possible mechanism, read but not reproduced: `AgentTaskLandingProtocol.cs:247` checks
  containment against the operation's pinned `TargetBeforeSha` (pre-push master `df92aa7b7`). A same-SHA
  re-post of that operation therefore cannot see the branch inside the new master, and it rebases again (:269-270).
- The Oct-23 plan says CP-3 R-1 is red because Antiphon.TestTiming.Probe and c1375-probe have no
  dispositions. Both now appear in `tests/test-execution-policy.json` (:1505, :1545), so this is likely
  resolved. Not re-run.
- In the S1 series the Code work ran as role `Custom`, not `Code`, so stage-based pipeline views
  under-count CARD-1545 Code.
- Final Review 185b71e2 left four advisories: re-seed with a hand-edited fixture, no diagnostic for a
  removed declared method, unnamed sink shapes, and CP-30 `undeclared-new-uid` on later trees. None has
  a card (board search on "CARD-1545": only 1623, 1635 and 1637 mention it).

## Unknowns

- Whether any operator decision on D-3..D-11 exists outside the card text. None is recorded on the
  card or in its history.
- Whether Windows Full on rc/2026-11-01 (250a07fb) actually executed CP-21's two methods green. Not checked.
- Whether new glob-compiled test files change the land-graph digest. Not measured.

Not done, noted: confirm or re-land operation cfc0cf92 so the S1 Mutation can be admitted (CARD-1637 scope).
