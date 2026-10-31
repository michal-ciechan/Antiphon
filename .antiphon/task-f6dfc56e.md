# CARD-1545 S1c sanitization fix round 1

Outcome: D-1, D-2, and D-3 are fixed on `4bf72c9e9b5a4a1dffaa42d140c4816a748492e8`. CP-20, CP-26, and CP-29 are green on that commit. OuterPublish exited 0. No live Claude ran.

## Commits

| SHA | Message |
|---|---|
| `86cee165cf76b7995077be3f620108c3aa84064e` | `test(card-1545): fail closed on raw sinks, token shapes, and scanner trust` |
| `255450b6ccc422b00cc1ae36bd2ce7d774c91cc5` | `fix(card-1545): reject token-shaped secrets and syntax-check sinks` |
| `4bf72c9e9b5a4a1dffaa42d140c4816a748492e8` | `fix(card-1545): refuse a 36-character base64 run and keep one scan anchor` |

The checkpoints below verified `4bf72c9e9b5a4a1dffaa42d140c4816a748492e8` (sourceState=clean, dirty=0). This report is a later docs commit on the same branch.

Branch: `feat/card-task-f6dfc56e`. Base: `5d719f5f080efca7d321f881af06941225eca0ed`.

## What changed

- `tests/Shared/LiveClaude/LiveClaudeSinkAudit.cs` walks method bodies. Console, TestContext, skip and Should messages, string.Format, concatenation, File writes, and Process arguments are raw when they carry a screen, home path, or other tainted value that is not a `LiveClaudeEvidence` projection. Aliases and same-method local-function forwarders are tainted. Both test projects already compiled `../Shared/LiveClaude/*.cs` and already referenced `Microsoft.CodeAnalysis.CSharp` 5.9.0. No PackageReference was added.
- `LiveClaudeEvidenceSanitizer.IsToken` keeps the 80-character bound and the identity and run lines. The charset is `[A-Za-z0-9._-]`. Credential substrings (`eyJ`, `ghp_`, `gho_`, `ghs_`, `ghu_`, `github_pat_`, `sk-`, `xox`, `AKIA`, `bearer`, `oauth`) are refused inside `IsToken`. A 32-hex run is refused. A 32-character `[A-Za-z0-9]` run is refused when the value has no `_`. The 36-character witness `qQzZ0123456789qQzZ0123456789qQzZ0123` is refused by that bound. `C680_LiveNudgeRefusesPermissionAndQuestionDialogs` contains `_`, so the base64 run is not applied to it. Field keys stay on the closed list `cli`, `backend`, `tool`, `revision`, `backendDelta`, `refused`, `submission`, `evidence`. Dropping the field-value `IsToken` check, the identity `IsToken` check, or the length check each leaks a witness the other checks do not reject.
- `scripts/lib/live-lane-evidence.ps1` unions context sentinels onto the built-in floor. The producer row walk is the only `[IO.SearchOption]::AllDirectories` token, so PC-56 still has one anchor. The evidence-tree walk recurses through `[enum]::Parse([IO.SearchOption], 'AllDirectories')`, checks file and directory names before the empty-file skip, and prefix-reads non-row files above 8MB. OuterPublish derives producers from `state.json` when that file exists. The script is ASCII and parses.

## Checkpoints

Verified commit `4bf72c9e9b5a4a1dffaa42d140c4816a748492e8`. Manifest slotWaitMinutes 70, UseAppHost false, one build per group, serial, `--total-timeout` 130m / hard 150m for the test rows and 90m / 100m for CP-29. The committed wrapper's slotWaitMinutes 25 was left unchanged.

Run `20261031-185100-b2fc` (CP-20, CP-26), verdict GREEN exit 0:

```
CHECKPOINT CP-20 commit=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f6dfc56e/.antiphon/checkpoints/20261031-185100-b2fc/rows/CP-20/run.trx slot=granted waited=165s dirty=0 source=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-20 waited=165s executed=12.823s wall=177.885s
PHASES CP-20 slotWait=270s build=31.3998421s startup=1.7508826s testsWall=10.0612637s teardown=0.4239786s hostWall=12.2361303s
CHECKPOINT CP-26 commit=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-f6dfc56e/.antiphon/checkpoints/20261031-185100-b2fc/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-26 waited=0s executed=199.549s wall=199.592s
PHASES CP-26 slotWait=90s build=397.7026212s startup=3.891085s testsWall=194.4975996s teardown=0.481545s hostWall=198.8702295s
```

Run `20261031-190839-2ea0` (CP-29), verdict GREEN exit 0. Command row counts are n/a:

```
CHECKPOINT CP-29 commit=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 build=n/a filter=pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=4bf72c9e9b5a4a1dffaa42d140c4816a748492e8 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-29 waited=0s executed=2.192s wall=2.245s
```

OuterPublish on the bound context: `publicationCount=1`, `EVIDENCE-SCAN clean`, exit 0.

Earlier runs on this task, kept as history:

- `20261031-182348-09c7` on `255450b6ccc422b00cc1ae36bd2ce7d774c91cc5`: CP-20 executed=2 passed=1 failed=1 (`C1545 V-5/field-token qQzZ01234567`; CanarySinks passed). CP-26 executed=2 passed=0 failed=2 (`C1545 PC-56 anchor missing`, then `C1545 V-7/child-red`). The 36-character run was under a floor of 40, and the scanner spelled `AllDirectories` twice so PC-56's unique-anchor check threw. Both are corrected in `4bf72c9e9`.
- `20261031-184533-9c53` on `4bf72c9e9`: exit 2, executed=0. Slot reason `refused: pre-existing-build-source` because the previous red outputs were still in the isolated directories. Those directories were removed by literal path and the green run above replaced them.

## Mutants

Each line is the in-suite cycle `C1545 <id> assertion-red; restored-green`. CP-20 printed the sink cycles on three audited methods (`Control_auto_connects_a_bridge_session_before_the_first_user`, `Submitting_via_two_writes_completes_a_turn`, `C514_Idle_menu_clearance_preserves_bridge_and_converts_prompt`).

Review survivors, now red then restored:

| ID | Witness | Result |
|---|---|---|
| SM-S1 | `Console.WriteLine(runner.SnapshotScreen())` | red `raw-console`, restored |
| SM-S2 | `throw new SkipTestException(runner.SnapshotScreen())` | red `raw-skip`, restored |
| SM-S2b | `Console.WriteLine("cwd=" + HOME)` | red `raw-console`, restored |
| SM-S4 | drop the field-value `IsToken` check | red, restored |
| SM-S5 | drop the CaseId/TestId/Kind `IsToken` check; leak is `ghp_` | red, restored |
| SM-S6 | drop the 80-character bound; leak is `a.` repeated 45 times | red, restored |
| SM-C1 | `Console.Out.WriteLine(snapshot.RenderedScreen)` | red, restored |
| SM-C1b | `Console.Error.WriteLine(after.RenderedScreen)` | red, restored |
| SM-C2 | `throw new SkipTestException(snapshot.RenderedScreen)` | red, restored |

`ProveDiagnosticMutants` prints the literal label `PC-42` for every C680 cycle. The TRX suffix identifies the sink: `Console.Out.WriteLine(snapshot.RenderedScreen)`, `Console.Error.WriteLine(after.RenderedScreen)`, and `throw new SkipTestException(snapshot.RenderedScreen)`.

Controls that already went red still go red:

| ID | Witness | Result |
|---|---|---|
| SM-S0 | `Console.WriteLine("SCREEN:\n" + runner.SnapshotScreen())` (in-suite PC-38) | red `raw-console`, restored |
| SM-S3 | `Append(capture.Screen)` (PC-34) | red `payloadHasScreenSentinel`, restored |
| SM-S7 | `Append(capture.Link)` (PC-33) | red `payloadHasSentinel`, restored |
| SM-C0 | C680 `Console.WriteLine("SCREEN:\n" + tool)` (PC-42) | red, restored |

Also red then restored: PC-35, PC-36, PC-37, PC-39, PC-40, PC-41, PC-43 through PC-47 (`unsafe-run-id`), PC-48 through PC-72, duplicate-uid, and the extra sink witnesses SM-S1a (alias), SM-S1w and SM-S2w (same-method forwarders), SM-Out, SM-Err, SM-Fmt, SM-File, SM-Proc, SM-Ctx. PC-57 covered json, xml, ansi, and osc8. PC-69 covered context-write, artifact-read, receipt-write, summary-write, and publish-write. PC-71 covered nonzero exits 1, 2, 3, and 8.

D-3 scanner witnesses, all inside `ArtifactGateRequiresPresence` which passed:

- Context `sentinels:["NOPE-XYZ"]` over a nested secret: exit 2 `sentinel-present`.
- Empty `sentinels` list over the same secret: exit 2 `sentinel-present`.
- SM-D3-floor replaces the needle floor with the custom list: red exit 0 `EVIDENCE-SCAN clean`, restored exit 2 `sentinel-present`.
- Zero-length file whose name is the secret sentinel: exit 2 `sentinel-present`.
- Directory whose name is the screen sentinel: exit 2 `sentinel-present`.
- `ProveOmittedProducer`: a producers list that points at a clean row while `resultsRoot/run-omitted/rows/CP-20/console.log` contains the secret. Scan exit 2 and OuterPublish exit 2.
- PC-56 replaces the single `AllDirectories` token with `TopDirectoryOnly`: red exit 0 `EVIDENCE-SCAN clean` (nested `leak.txt` missed), restored exit 2 `sentinel-present`.

## Declined detections

1. BCL exceptions whose message is built inside the runtime (`File.ReadAllText` throwing `FileNotFoundException` with the full path) at the canary helper. The throw is not in an audited method body.
2. `SkipTestException(e.Message)` when the message expression is not screen or home tainted (`RequireExecutable` in the watchdog).
3. Interpolated skips whose holes are tool names (`Native qualification requires {name}`).
4. `ProcessStartInfo` of the admitted executable path when the expression is not tainted.
5. `File.WriteAllTextAsync` fixture writes whose content is a literal or `JsonSerializer.Serialize`.
6. Dataflow through fields, ref or out parameters, lambdas invoked in another method, reflection, and dynamic.
7. A `StringBuilder` accumulated and then written, unless the `Write` argument itself is tainted or a non-literal Console argument.
8. A wrapper defined in another file that is not in the scanned tree. Same-method local functions are detected.
9. A secret assembled character by character when it is not one tainted expression or a `+` chain.
10. Content past the 8MB cap. Row files over 8MB still return `unreadable`. Other files are prefix-read for the first 8MB.
11. A base64url value whose `_` breaks the run below 32 and which has no known prefix. That exemption keeps `C680_LiveNudgeRefusesPermissionAndQuestionDialogs` a valid token.

Healthy sinks the audit leaves green: `throw new SkipTestException(e.Message)` with an untainted message; interpolated skips with untainted holes; literal and JSON fixture writes; `ShouldBe` and `ShouldContain` with literal messages; `InvalidOperationException` and `TimeoutException` whose holes are not tainted; `ProcessStartInfo` of the admitted path; `ClaudeScreen.IsSettled(before.RenderedScreen, after.RenderedScreen)`; a menu snapshot used only as the receiver of `Contains`; `MenuPresent(after).ShouldBeFalse(LiveClaudeEvidence.Assertion(...))`; settings `ShouldBeTrue` with a literal message; `throw new SkipTestException(LiveClaudeEvidence.Skip(...))`; `File.WriteAllText(SettingsPath, OffSettingsJson)` in the canary sandbox constructor, which is outside the audited method bodies; `done.ShouldBeTrue($"[{backend}] ...")` whose hole is not tainted; the C514 literal skip `pending native acceptance`.

## Evidence diff, merge-tree, land graph

`scripts/check-evidence-diff.ps1`:

- `cbe3e6b42401db766f4cee38bc9b34f5b9d314af..4bf72c9e9b5a4a1dffaa42d140c4816a748492e8` commits=56 entries=8 violations=0
- `5d719f5f080efca7d321f881af06941225eca0ed..4bf72c9e9b5a4a1dffaa42d140c4816a748492e8` commits=3 entries=0 violations=0

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`. origin/master is `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`. Exit 1. One add/add conflict, in `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`. Stage 2 blob `413074d8f03fa2a40c6432aa2c1d16c8881f6a65` (master). Stage 3 blob `542b9380cdd724fb8811d9812b8d0a0dd7e2f6de` (this branch). Not merged.

`scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`):

```
digest=BAA62938B0DA86DC5950DA5BAA20EFA021BF6E29C949C785CCB3B8E1832C4365 count=68
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

Exit 1. The refusal is the digest alone. The Merge's `-Update` must record `BAA62938`, not `9D7BBFDE`. This task did not run `-Update`.

## Left for Final review

ordinaryScope is Interim. The whole Unit lane did not run. Live-only UIDs and every PC stay pending. S1a defects D-4, D-5, and D-6 were not touched. Advisories A-1 through A-8 were not treated as defects. A-4 is covered by the evidence-tree scan. A-6's wrapper slot wait of 25 minutes was left as committed; this run used a gitignored manifest with slotWaitMinutes 70.

Merge order after Final review: chain `2c550140e8d1f96f50dfa2080461150a966e906b`, then this sanitization branch, then S1a `9a05dbfcd4b49fdbed098a520b28c7e7d91cc46c`. Resolve the plan add/add once with blob `542b9380cdd724fb8811d9812b8d0a0dd7e2f6de`.

Isolated output directories removed by literal path: `tests/Antiphon.Agents.Pty.Tests/bin-s1c-fix-f6dfc56e-pty`, `tests/Antiphon.Tests/bin-s1c-fix-f6dfc56e-c680`, `tools/Antiphon.Checkpoints/bin-c1545-checkpoints`, `src/Antiphon.HostCleanup/bin-c1545-checkpoints`.
