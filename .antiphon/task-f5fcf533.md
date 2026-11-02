# CARD-1545 S2a-1 Final Review round 1 (task f5fcf533)

**Outcome:** Clean. Reviewed source `ec25d5f1aaa90f38796b53cf7126fb36c015c932` on `feat/card-task-5ef0b4bc` (red `85fef1e5b4f9207752d7863cf2afb711711acc87`, tested `a00adc043d8257ba79307983f97efd3129aebd05`, report `ec25d5f1aaa90f38796b53cf7126fb36c015c932`). Base `809e7b7a916a303f16ba124c082d91b7f9cec508`. Diff reviewed: `809e7b7a916a303f16ba124c082d91b7f9cec508..a00adc043d8257ba79307983f97efd3129aebd05`, plus the report commit. No defect. S2a-1 cannot report the lane as not due.

## Fail-safe

`Get-LiveLaneStatus` (`scripts/lib/live-lane-fingerprint.ps1` lines 312-320) always returns `state=DUE`, `effectiveMode=Full`, `activation=shadow-only`, `gateCredit=false`, `executionEvidence=false`. Fingerprint `complete` maps to reason `no-receipt-validator` and is still DUE. Fingerprint `unknown` maps to `unknown-input` and is still DUE. `scripts/live-lane-status.ps1` lines 16-22 write that document on success (exit 0). The catch path writes `state=INVALID` with the same shadow fields and `gateCredit=false` (exit 2) for `manifest-missing`, `declaration-invalid`, `option-not-available:<name>`, an unmatched exception (`status-failed`), and an unreadable git object (`git-read-failed`). `-Attestation`, `-History`, and `-ReusePolicy` refuse. No path emits covered, unchanged, not due, or gate credit.

Cannot-prove cases:

- Undeclared input, path outside declared inputs, empty declared root or `contractFiles` pattern, symlink (`120000`), gitlink (`160000`), namespace outside the closure, undeclared runtime literal, instruction import outside the walk: fingerprint `unknown`, status still DUE.
- New file, deleted file, or edited file under a hashed input: the git blob id changes, so the digest changes. The lane stays DUE either way.
- CRLF/LF: the digest hashes committed blob ids. It does not normalize newlines. The same commit hashes equal on a CRLF working tree and an LF working tree. A commit that stores CRLF is a different blob, so the digest changes and the lane stays DUE. `.gitattributes` has `* text=auto` and `*.ps1 text eol=lf`. `ConvertTo-TestSelectionLfText` parses `@` imports only.
- Case-insensitive collision of two tracked paths: both blob ids are hashed (ordinal dictionary). This tree has no case-only path pair (`git ls-tree -r --name-only` folded and uniqued). A case-variant instruction leaf outside the inventory (`Claude.md`, `.Claude/`) is neither hashed nor marked unknown. That gap is absent here and cannot grant credit.
- Glob matching nothing: `declared-root-empty:` (roots and each `contractFiles` pattern). Glob matching too much: those paths are hashed, so the digest moves when they change.

## Complete inputs

`tests/live-lane-inputs.json` for `live-claude-core-windows` declares the contract files (`tests/live-lanes.json`, `tests/live-lane-inputs.json`, `scripts/lib/live-lane-*.ps1`, `scripts/live-lane-status.ps1`, `scripts/run-live-lane.ps1`), minimum roots (Pty, PtyHost, PtyHost.Client, PtyHost.Protocol, SessionRunner, SessionRunner.Contracts, `server/`, `tests/Shared/`), runtime roots (`scripts/`, `tests/live-lane-checkpoints/`, `.claude/`), the six namespace roots, and the instruction patterns. `scripts/` as a runtime root hashes the S1 scripts the manifest dot-sources, including `nightly-*.ps1`.

Read by the lane and not in the digest (advisory, status stays DUE):

- `tests/test-execution-policy.json` — S1 census in `scripts/lib/live-lane-manifest.ps1`. `scripts/lib/live-lane-run.ps1` does not read it.
- `tests/rc-project-categories.json` — `Read-RcProjectGraph`. Catalog faults are observations (Q-8), not digest inputs.
- `tests/test-selection-policy.json`.
- Claude executable identity, model, and OS managed policy — S2a-2 (`scripts/lib/live-lane-attestation.ps1`), not this declaration.

Real-tree census at this tip: fingerprint `unknown`, digest `5182a261ae449f583937ca6f6a7d096c049e162ff706f94b9f1b9f4c5c8a2804`, inventory 5229, closure 38, 200 reasons (188 `namespace-outside-inputs`, 12 `undeclared-runtime`). Status document: DUE, Full, shadow-only, gate false, execution false, reason `unknown-input`.

Namespace split: `tests/Antiphon.SessionRunner.Tests` 160 (158 plus `Fixtures/RunnerRestart` 2) and `tests/Antiphon.PtyHost.Tests` 28 (25 plus `FrozenV1` 3). Those projects are outside the closure and reuse declared product namespaces.

Undeclared-runtime (12): nine tracked `docker/**` scripts (`docker/git-minimum-version.sh`, six under `docker/session-runner-grok/`, `docker/stack/init-state.sh`, plus `docker/session-runner-grok/seed-claude-onboarding.mjs`), `tests/Antiphon.SessionRunner.Tests/Fixtures/CodexVersionChild.ps1`, and the fixture strings `file.ps1` and `outside.ps1`. Those two strings come from `tests/Antiphon.Tests/Application/InternalDecisionPolicyTests.cs` (`../file.ps1`) and `AgentTaskDecisionQuestionApiTests.cs` (`../outside.ps1`). They resolve to repo-root names, outside every covering root. One reason each after dedupe. `//server/share/file.ps1` in the same argument list is rewritten under `scripts/` and prefix-covered. It is a test argument, not a tracked file.

## Orchestrator question

Recommend (a): a data-only follow-up that adds `docker/` and `tests/Antiphon.SessionRunner.Tests/` plus `tests/Antiphon.PtyHost.Tests/` to the declaration. Do it in S2a-2 or as its own data change. Do not land it inside this slice.

(a) hashes those tracked trees. It does not hide existing content, and it does not by itself make the fingerprint `complete`: `file.ps1` and `outside.ps1` stay outside the new roots. It would be wrongful only if the same change also exempted those fixture strings and then treated `complete` as a full input set. Do not exempt them. Prefix cover already treats an untracked name under a declared root as covered; extending that to `docker/` matches what `scripts/` already does. The nine current docker reasons are tracked paths, so (a) hashes them.

(b) changes the meaning of `namespace-outside-inputs`. The design's `tools/Stray` witness (no csproj) would stay red. Reject for this follow-up.

(c) is safe and leaves the lane unknown until S4a. Prefer (a) so the shadow digest starts moving when those trees change.

This is advisory. State stays DUE and `gateCredit` stays false until a later slice adds a receipt validator.

## Design deviations

None weaken a witness or a guard. Not D-n.

1. content-glob uses `Content Include="../Shared/Data/*.json"` and edits `tests/Shared/Data/a.json` then adds `b.json`. `tests/Shared/Data/` is not a declared root or a closure project directory. PC-16 can go red.
2. test-namespace uses `namespace Antiphon.Tests.Helpers`, which matches namespace root `Antiphon.Tests` and not product root `Antiphon.Agents.Pty`. The design's `Antiphon.Agents.Pty.Tests.Helpers` also prefix-matches `Antiphon.Agents.Pty`, so PC-34 could not go red as written.
3. domain copies `fixture-lane-2` only in the declaration. The manifest has no lane id. PC-5 (omit `laneId`) still goes red. Digests differ by `laneId`.
4. import-cycle uses `@b.md` / `@a.md` because imports resolve relative to the importing file. The design's `@docs/b.md` from `docs/a.md` would look up `docs/docs/b.md`. Visited set, depth 8, 30s budget.
5. undeclared-runtime control uses `"scripts/lib/tool.ps1"` and `"lib/tool2.ps1"` (maps to `scripts/lib/tool2.ps1`). A bare `tool2.ps1` always maps to `scripts/tool2.ps1`, outside the fixture roots. The positive case still asserts both undeclared reasons.
6. catalog-only and fault-outside-closure assert the observation, so they are red on the fail-open stub. The design called them stub-green.
7. Status takes `-RepositoryRoot`. The library exports `Get-LiveLaneStatus`, so most state checks are in-process. Three child pwsh launches cover shadow-only, missing-manifest, and declaration-schema. The script is 23 lines and prints the library document unchanged. Refusal paths are not re-executed through the script process (advisory).
8. `declared-root-empty:` also applies to each `contractFiles` pattern.
9. A runtime literal is covered when it is in the inventory or its resolved path starts with a declared minimum/runtime root or a closure project directory, even if the file does not exist. Paths outside those roots stay unknown. The `//server/share/file.ps1` rewrite is the extra silent cover and hides no tracked file.
10. The ignored CP-8 row pin is 8 minutes. The design says 4. The author's measured row was 257s, about 1.87 times 4 minutes. Method budgets stay 60s and 120s. A fresh import without the pin derives 30 minutes for CP-8. This review passed `--row-timeout 20m`, so that derived row did not cancel the 25-minute total.

## Performance

`StableCanonicalInputs` budget is 120s.

| Run | Time | Result |
|---|---|---|
| Author CP-8 at `a00adc043` | 88.0s | passed |
| This review CP-8 method (`20261102-104029-a64f`) | 52.911s | passed |
| Rerun 1 wall / TUnit | 67.355s / 65.196s | passed, exit 0 |
| Rerun 2 wall / TUnit | 56.413s / 55.239s | passed, exit 0 |

Both reruns are under half of 2x the 120s budget. No flake. Leave the 120s budget. A cache or a wider timeout is a follow-up only. The first timing attempt built `tests/Antiphon.Tests` (0 errors, 1482 pre-existing warnings, slot `92ddfc77-c2e8-418a-87d2-e7795f37c828`, waited 0s) and then exited 127 because `/usr/bin/time` is not installed. No test method started on that attempt. The two reruns used `date` against that same `UseAppHost=false` Debug assembly, under slot `78a68117-15cf-4df3-a5b8-65915db911ef` (waited 91s, position 3 of 4).

## Verification

CHECKPOINT CP-8 commit=ec25d5f1aaa90f38796b53cf7126fb36c015c932 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f5fcf533/.antiphon/checkpoints-f5fcf533/20261102-104029-a64f/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=ec25d5f1aaa90f38796b53cf7126fb36c015c932 sourceState=clean buildSource=verified

CHECKPOINT-TIMING CP-8 waited=0s executed=164.750s wall=164.797s. One row, one build, output `bin-live-fingerprint/` removed on green. `dotnet exec` of the checkpoint tool `validate --evidence .antiphon/checkpoints-f5fcf533/20261102-104029-a64f/report.json --expected-source-sha ec25d5f1aaa90f38796b53cf7126fb36c015c932 --rows CP-8` printed `CHECKPOINT SOURCE VALID source=ec25d5f1aaa90f38796b53cf7126fb36c015c932 rows=1` (exit 0).

Land graph `-Check` (no `-Update`) approved. `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, then `approved`, exit 0. Tool path was the native Debug DLL built in the test slot (`tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`). The auditor has no `DEBUG` conditionals. No `OutputPath`.

`scripts/check-evidence-diff.ps1 -Repository . -BaseRef 809e7b7a916a303f16ba124c082d91b7f9cec508 -HeadRef HEAD`: `EVIDENCE result commits=3 entries=1 violations=0`. The one entry is the tracked code report `.antiphon/task-5ef0b4bc.md`.

`git fetch origin` then `git merge-tree --write-tree` (exit 0, no conflicts):

- `origin/master` `3a49b60133327f4441f06698ef3369b1d277d049` -> tree `90db5dbc124156983b731c4a3179986b86a9c6d1`
- `origin/rc/2026-11-01` `e114313d36ef0892676c4e0f4be2c956808c7978` -> tree `90db5dbc124156983b731c4a3179986b86a9c6d1`

Same result tree. Master-only feature. The rc difference is informational. Do not backport.

OS-witness lint exit 0 (`/tmp/os-witness-f5fcf533.json`). No OS candidates in the five new files. `scripts/lib/live-lane-fingerprint.ps1` matches the startup census, so `restartImpact.required` is true and the Windows native obligation is pending: nothing in AppHost or `restart-apphost.ps1` loads it, and no Windows rehearsal was run. Pending native is advisory. This slice does not need `-Platform Windows`. The pending record is not an rc-pick authorization.

Unit lane: not run, and not claimed. The plan's S2a-1 closed list is CP-8. This brief's commissioned checks are CP-8, the two timings, the land-graph check, the evidence diff, and the two merge trees. The generic Unit-lane sentence is capped by that list. Code deferred the lane and did not pass it. This review does not pass it either.

PCs PC-1 through PC-41 stay pending. V-8..V-19 and R-4..R-8 belong to S2a-2 and S2b.

Worker run-rule breaches, informational: one harness probe outside the build-slot gate, and one `rm -rf` with a variable on that session's own scratch folder.

## Defects

None.

## Advisory (do not rerun Review, do not open a Code repair for these)

- Follow-up (a) above. Leave `file.ps1` and `outside.ps1` unknown.
- Policy file, rc catalog, and selection policy are outside the digest. Catalog changes do not change the hashed closure.
- Case-variant instruction leaves outside the inventory are silent. None exist in this tree.
- `//server/share/file.ps1` prefix-covers under `scripts/`.
- Script-process refusals are covered by the library and three child launches, not by a second script-process pass.
- CP-8 ignored pin is 8 minutes. Method budgets unchanged.
- Windows native obligation for `scripts/lib/live-lane-fingerprint.ps1` is pending. Master land does not need the rehearsal.
- Author's two run-rule breaches, as above.
