# Windows rc gate on the latest tip, failed groups first

Evidence only. No run was started, stopped, or written. Fetched `origin/master` and `origin/rc/2026-10-27` on 2026-10-27; both tips matched the brief before and after fetch. Desktop logs are not mounted on this Linux runner (`/mnt` empty; no `8f055f87` or `105792f7` file anywhere under `/work`).

## Outcome

Let the live full Windows qualification finish. It is already the latest rc tip. There is no supported way to run previously failed groups first and still earn rc credit. Pointing `rc/2026-10-27` at latest master is not a fast-forward, and the only product code that move would drop is the rc-only Claude 2.1.310 hint parser.

## 1. `origin/master` versus `origin/rc/2026-10-27`

| | SHA | Tree |
|---|---|---|
| `origin/master` | `0794458dbb5c381116eb00ae6e894315ffc030d5` | `10ee1a52cf23a4da21bddde7e5353fcc14cfdfd1` |
| `origin/rc/2026-10-27` | `f84228f285dd3e4ad7b5dc8260cbc9f819256f50` | `f607b0de3ea76286bb395432f64855c74ec73736` |
| merge-base | `a97194ee7a4adf215454fd82c453ba46362596ee` | subject `docs(evidence): record CARD-1545 continuation checkpoint and remaining gates`, committer 2026-10-24 03:58:17 +0100 |

`git rev-list --left-right --count origin/master...origin/rc/2026-10-27` is **600 537**: 600 commits reachable only from master, 537 reachable only from rc. Neither tip is an ancestor of the other (`git merge-base --is-ancestor` exit 1 both ways).

`git rev-list --left-only/--right-only --cherry-pick --count` on the same symmetric range: **73** master commits have no patch-id twin on the rc, and **10** rc commits have no patch-id twin on master. The other **527** commits on each side are patch-equivalent to a commit on the other side (600 − 73 = 537 − 10 = 527). The two tip commits, `0794458d` and `f84228f2`, both subject `fix: pin the added C467 retained-evidence E2E case`, have the same stable patch-id `e485885b65e9e1b19183e2335ed1a8b02966619b`.

Commits touched by the 600 master-only SHAs, overlapping counts (a commit can touch several areas): tests 570, scripts 121, `.antiphon` 42, server 39, `docs/investigations` 38, `docs/superpowers` 28, src 27, `docs/testing-and-build.md` 7, tools 5, `docs/orchestration-loop.md` 4, client 4, `docs/session-runtime-invariants.md` 3, `docs/test-performance-standards.md` 2, `docs/release-gates.md` 2, and one each of `docs/docker-stack.md`, `Dockerfile`, `AGENTS.md`, `.github`, `.gitattributes`.

The 73 patch-unique master commits are documentation and task reports, plus one test commit whose tip blob matches the rc anyway:

- `.antiphon` reports and `docs/investigations` / `docs/superpowers` / `AGENTS.md` / `docs/orchestration-loop.md` / `docs/testing-and-build.md`. Short SHAs, newest first: `61c85fc84`, `718bd29f4`, `9e9518fd0`, `b6db2de3d`, `7b7aea79b`, `30a366174`, `2b3ed0ed8`, `44de6c6d5`, `8364e9c7d`, `60d7458ea`, `812486dd7`, `f0dbd4092`, `edfe601ce`, `faae4a9f9`, `308bdd8be`, `d78b58095`, `95d0bad51`, `3d9d25676`, `1870edcd6`, `15eae4779`, `3b7e33bee`, `7b132e411`, `9a3e6c7a5`, `151ce388f`, `c37155d60`, `98215c7e5`, `317fb964f`, `888660aa1`, `2314598c3`, `0669d2473`, `e5a867361`, `101fb0a86`, `bc8eedfda`, `e49be26dd`, `e6211673d`, `f2b167a40`, `e4da6e28d`, `4362e3ece`, `f903edacf`, `f5456c266`, `6889e0cb9`, `49c700fa4`, `f0911378c`, `c08244968`, `2d06d0a46`, `1a5651eab`, `a0b51f75f`, `f0c1c658a`, `0d51344da`, `d1c95c652`, `785d6a636`, `fda635c66`, `a640c8614`, `9376a1daa`, `acb6f2e60`, `1603a6f18`, `256213bf8`, `263aa2c6c`, `1c937a427`, `a7895d00b`, `dc1a21845`, `a28f1faa9`, `5b6a1f9cb`, `be55a999b`, `0e64a6aba`, `e0893861e`, `ec5123c39`, `c2e35da7c`, `e970c2c9f`, `ca250de0b`, `03f9c664c`, `9ff567994`.
- `583a36b0b` `test(CARD-1574): expose regrowth and malformed-byte readiness holes` also edits `tests/Antiphon.SessionRunner.Tests/ResumeReceiptReadinessTests.cs` and `docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md`. At both tips those blobs are `b661836e14377e1314fcefd2d10b6ca3d7ef001e` and `5313a33796fcac92371a28efd0692159d407610a`. The rc twin `125c720b9` has a different patch-id and the same tip bytes.

The 10 patch-unique rc commits:

| SHA | Subject |
|---|---|
| `105792f7d22f297746cdb338976f52e424b76fa7` | docs: record rc top-up 4 mapping and verification |
| `cdfd8366fa252d5c87997bef10352f2650f96d53` | fix(pty): accept the wrapped /rc badge with its connection status |
| `c90f8047b299aa3c263ff1a1a33536bbe48f02fc` | fix(pty): recognise Claude 2.1.310 weekly-usage and paste-expand hint rows |
| `de9a8fba48dcf3c5ff84114fa23d73122aa5ff97` | fix(pty): accept Claude's wrapped /rc hint badge under the composer |
| `afdeebdc1` | docs: record rc top-up 3 mapping and build evidence |
| `a13e544e2` | docs: record final batch C rc top-up mapping and build |
| `f3aa3b43e` | docs: record rc integration top-up picks and build evidence |
| `2b7383b24` | docs: align restored resume-readiness plan for rc integration |
| `125c720b9` | test(CARD-1574): expose regrowth and malformed-byte readiness holes |
| `665bf12b1` | docs: record prepared rc integration pick candidate |

`git diff --name-status origin/rc/2026-10-27 origin/master` is 68 paths: 56 added on master, 5 present only on the rc (all `.antiphon/task-*.md`), 7 modified. The only product files are `src/Antiphon.Agents.Pty/ClaudeScreen.cs` and `tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs`. On the rc, `MaxHintRowsBelowComposer` is 4 and `IsWrappedHintCommandRow` accepts a wrapped `/rc` or `/effort` badge plus the weekly-usage and paste-expand rows (`ClaudeScreen.cs` as of `f84228f2`). Master still has the constant 3 and does not have those helpers. The three pty commits above are ancestors of both `105792f7` and `f84228f2` (`merge-base --is-ancestor` exit 0). They are not on master (`git log origin/master --grep='wrapped /rc'` is empty; master's last `ClaudeScreen.cs` touches are CARD-0650).

Identical blobs at both tips: `scripts/nightly-run.ps1` `5ea7d245c9b185be76162a604bfd72611524ba73`, `scripts/lib/nightly-tests-impl.ps1` `195e7e8b0129114af2009822e94b1f0ab6ac6aa6`, `docs/release-gates.md` `eb88ab337428412019b484e18053f8d5b6c0aeac`. `server/`, `client/`, and `scripts/` do not differ. The modified docs are `AGENTS.md` (one link to the CARD-1514 repair loop), `docs/orchestration-loop.md` (that loop, added on master), `docs/testing-and-build.md` (C487/C578 fixture note), and `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md`.

### How an rc line is cut, and what a master-based rc would mean

`docs/release-gates.md:22-26` says the release line is a long-lived `rc/YYYY-MM-DD` branch. Bug and test fixes for code already on that line land on it. Features and refactors stay master-only. Every rc-line land is merged to master with a merge commit. `docs/release-gates.md:424-434` says a cut journals an existing remote `rc/YYYY-MM-DD` tip, does not create the branch, and does not force-push. `docs/orchestration-loop.md:624` states the practised port: fixes go to master and to the rc line by a reviewed cherry-pick that makes a new rc tip. Observed history matches the cherry-pick sentence, not a fast-forward or a re-cut: top-up records such as `.antiphon/task-ae45156c.md` and `.antiphon/task-3b5896b8.md` are ordered `git cherry-pick -x` maps, and `.antiphon/task-12e8a22a.md` says the master lineage must not be used as an rc fast-forward.

`docs/release-gates.md:456-459`: a fix keeps the failed run's evidence, and the line's new tip is tested from scratch. A partial run, or a green under a different policy, is never reused.

"rc based on latest master" is not a fast-forward of `rc/2026-10-27` to `0794458d`. A reset of that ref onto master is a force update the cut refuses, and the resulting tree deletes the three pty commits. A merge of master into the rc is a new SHA. It must be qualified from scratch. Resolving `ClaudeScreen.cs` toward master drops the 2.1.310 hint parser; resolving it toward the rc keeps the parser and adds master's documentation. It does not add other product, test, or script code, because those trees already match.

The risk of treating the 600 master SHAs as unqualified product is smaller than the SHA count suggests: 527 are patch-twins of rc commits already on the line under test. The 73 unique master patches are records, except `583a36b0b`, whose test blob already matches. The real product risk of a master reset is losing `de9a8fba4`, `c90f8047b`, and `cdfd8366f`.

## 2. Order and selection

No parameter on `scripts/nightly-run.ps1:82-117` selects groups, chunk order, or previously failed classes. `SelectionMode` is only `Full`, `Shadow`, or `ChangeAware`. `-Suites` can name suites. `-ContinueRunId` continues the same run's lock hop (`scripts/lib/nightly-common.ps1` compares the existing `runId` to `ContinueRunId`); it does not reorder chunks.

`scripts/lib/release-selection.ps1:1-2` says every admitted attempt executes Full. `ChangeAware` adds fallback reason `change-aware-unqualified` and still stores `effectiveMode=Full` and `coverageScope=Full` (`release-selection.ps1:218-229`). `Resolve-ReleaseSelectionMembers` (`release-selection.ps1:376-379`) returns the full member list for that mode and throws `selected-execution-unqualified` otherwise. `scripts/lib/rc-project-selection.ps1:2` is advisory project batches and is never execution or qualification authority. `scripts/select-test-rosters.ps1` writes a selection JSON; it does not launch tests or set group order.

When the rc profile has exclusions, native suites are repacked by `Resolve-NightlySuiteChunks` into `Get-NightlyProfileLaunchPlan` (`nightly-tests-impl.ps1:450-463`). `ConvertTo-NightlyProfileLaunchGroups` (`nightly-tests-impl.ps1:316-427`) walks namespaces in sorted order, packs with first-fit largest-first (`Split-NightlyItemsBySeconds`, `nightly-tests-impl.ps1:216-218`), numbers `all-group-N` (`nightly-tests-impl.ps1:408`), then appends oversized splits and exact leaves. The suite loop launches that array in order (`nightly-tests-impl.ps1:1124`). Those group numbers are planner indices. `docs/investigations/2026-10-23-rc-5a126f6fc-qualification-groups-15-20-debug.md:36` already records that group numbers are not stable identities. The same planner blob is on master and on this rc, so a new run of either tip uses this order.

A `-Suites` subset that omits a required suite sets `coverageComplete` false with reason `partial-selection` (`nightly-tests-impl.ps1:831-834`). `Invoke-NightlyExactRoster` (`nightly-tests-impl.ps1:1521-1522`) cannot be promoted to Full or used to activate ChangeAware. rc-release credit needs real booleans `coverageComplete`, `testsPassed`, and `reportDelivered`, exit 0, and a pinned candidate SHA (`docs/release-gates.md:78-86`). Linux rc execution is `coverageScope=PlatformDiagnostic` and cannot earn that credit (`nightly-tests-impl.ps1:1433-1437`, `docs/release-gates.md:230-234`). `docs/nightly-watchdog.md` has no group-order or failed-group switch.

A chosen order, or a rerun of only the previously failed groups, is a targeted gate. It is not a full qualification. The CARD-1514 repair loop (`docs/orchestration-loop.md:650-687`) is that targeted path: Debug on server2 while the Windows run continues, then a later from-scratch qualification of the next rc tip. Those Linux repairs do not qualify the running SHA.

## 3. Run `8f055f87`

Not reconstructable from this host. `git grep` of `8f055f87` and `79b742de` on both tips is empty. The candidate commit is on the rc and not on master:

- `105792f7d22f297746cdb338976f52e424b76fa7`, subject `docs: record rc top-up 4 mapping and verification`, committer 2026-10-26 08:53:57 +0000
- ancestor of `origin/rc/2026-10-27` (exit 0), not of `origin/master` (exit 1)
- 107 rc commits after it (45 dated 2026-10-26, 62 dated 2026-10-27), which are the later cherry-picked top-ups now inside `f84228f2`

The brief's counts (140 failures, 65 classes, 179 required cases with no terminal evidence, two timed-out chunks) are not in any committed summary. A Windows read-only pass must read, and not modify:

- `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87\summary.json` (`nightly-tests-impl.ps1:848` and `:1499`)
- `antiphon-all-group-N.trx` and sibling `{suite}-{chunk.id}.trx` files in that log directory (`nightly-tests-impl.ps1:1129`)
- `timing\groups-planned.json` and `timing\groups-executed.json` (`scripts/lib/test-phase-evidence.ps1:195-200` and `:772`; the timing root falls back to `<log>\timing`)
- chunk timing rows whose `Why` names the two timeouts

Until that read, nothing here can say which of the 65 classes were fixed by a later top-up, which are obsolete fixtures, or which are still open. The fixes that did land after `105792f7` are already in the candidate the live run is testing. They are not a class-level disposition of `8f055f87`.

## 4. Stopping `79b742de`

The supported procedure is the manual emergency stop in `docs/release-gates.md:153-197`. There is no automatic stop command (`docs/release-gates.md:119-120`, CARD-1500 D-1). Do not run it from this task. On the desktop, in order:

1. Identify candidate `rc-2026-10-27-f84228f285dd`, SHA `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`, run `79b742de44354190bb55fd35914496f3`, supervisor PID 145140, and the exact state, coordination, and checkout paths from the launch record. Run `scripts/rc-qualification-status.ps1` on that host (`docs/release-gates.md:122-127`). Preserve lock bytes. Do not guess a supervisor from the lock PID.
2. Prove the tree with `Get-CimInstance Win32_Process` from the recorded nightly lock owner up to that supervisor. Abort on a reused PID, missing ancestry, or an unrelated child. Do not kill every `pwsh` or `dotnet`.
3. Reobserve PID and start, then `taskkill /PID <verifiedSupervisorPid> /T /F`. Census for 30 seconds. An incomplete census retains the locks. Do not kill a reused PID. Do not substitute the native-lock owner PID for the supervisor.
4. Only after that owner and this run's children are gone, and only while new native starts are held, `Move-Item` the exact `C:\Antiphon\verification\native-run.lock` to a new same-volume evidence path without `-Force`. `Enter-ReleaseGateNativeLock` does not auto-reclaim (`docs/release-gates.md:191`).
5. Record the stop. Do not rewrite `last-run.json`, green files, journals, or reports. Do not delete `run.lock` and `native-run.lock` as a pair (`docs/release-gates.md:192-196`). A later qualification is a new attempt. Do not use `release-cut.ps1` as the restart: it publishes on green (`docs/release-gates.md:197`).

What remains is an incomplete run. `docs/release-gates.md:456-459` says that partial evidence is not reused for the next tip. The in-progress chunks do not credit a different SHA either: the gate pins `ExpectedSha` and refuses a moved ref (`docs/release-gates.md:436-441`).

This run is the full rc profile of the current tip, supervisor still recorded as live in the brief. It is not an abandoned run. `docs/orchestration-loop.md:626` allows an orchestrator to stop an abandoned or uncreditable qualification through this path. `docs/orchestration-loop.md:655-657` forbids repair agents from stopping the live qualification. Stopping `79b742de` is safe only in the sense that the procedure above is defined. It is not required for a gate of `f84228f2`, and it throws away the only in-progress full credit for that SHA. There is no pause-and-reorder.

## 5. Plan

Let `79b742de` finish on `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`. That is the cheapest full Windows gate of the latest rc tip. Keep dispatching CARD-1514 Debug tasks for groups as they fail, on server2, without touching the desktop run (`docs/orchestration-loop.md:650-687`). After that run settles, cherry-pick any still-open fixes onto the rc line the way top-ups 5–9 were ported, then qualify the new tip from scratch in planner order.

Do not reset or merge `rc/2026-10-27` onto `0794458d` as part of this gate. Do not stop `79b742de` in order to run failed groups first. That order is not a qualification mode, old group numbers will not match the next plan, and the stop discards the current attempt.

Decisions the orchestrator must confirm:

1. Accept `f84228f2` as the candidate under test. The alternative, a new SHA that literally contains master, is a new from-scratch run and drops or must re-apply `de9a8fba4` / `c90f8047b` / `cdfd8366f`.
2. Leave supervisor PID 145140 running. Stop it only through `docs/release-gates.md:153-197` if decision 1 is rejected.
3. Commission a Windows read-only inventory of `2026-10-26-0946-8f055f87` if a class list is still required. It does not change decisions 1 or 2. Group ids from that run are not launch selectors for `f84228f2`.

## Not done, noted

No code change was designed; the planner has no failed-first switch, and adding one would still be a different policy unless the rest of the rc profile then ran.

## Remaining uncertainties

- The 65 class names, 179 missing-terminal cases, and two timeout chunk ids of run `8f055f87` are only on the desktop path above.
- This investigation did not observe PID 145140 or read `native-run.lock`. Liveness is the brief's launch record, not a new probe.
- Patch-id twins can differ in commit metadata and still match at the tip, as `583a36b0b` and `125c720b9` do. The tree diff is the net content comparison.
