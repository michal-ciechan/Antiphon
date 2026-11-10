# CARD-1508 Review round 1 (task 7e1aae7e)

Verdict: one blocker. The fatal pre-prompt sentence is classified and appended for the captured frame, and the named class runs are green. `CodexStartupScreen.Classify` also returns `AccountBootstrap` for a live composer or a raw tail that merely contains the three phrases, and Codex pointer evidence then refuses to read that composer.

Reviewed source: `e25cebdf5f2c357c36f515de3836389c8379db97` on `feat/card-task-64e5af25`. Code owner `64e5af25-be6e-45e2-ac8d-64f9e7c79f8a`. Base `ac6d00022e940ee3b90584c7d313567b4491c498`. No `### Checkpoints` table; Code said so. This pass re-ran the named classes, the documented guard filter, the impacted rows, evidence-diff, and merge-tree. PCs stay pending.

## B-1 AccountBootstrap is not confined to a fatal pre-prompt frame

Where: `src/Antiphon.Agents.Pty/CodexStartupReadiness.cs` (`Classify`, the `IsFatal` return before the composer checks) and `src/Antiphon.Agents.Pty/CodexPointerComposerEvidence.cs` (`TryReadComposer` accepts only `Unknown` or `Ready`). `IsFatal` is true when either the rendered screen or the raw tail contains all three of `account/read failed during TUI bootstrap`, `workspace routing discovery timed out`, and `-32603`.

Failure: A healthy Codex composer whose text is that sentence classifies as `AccountBootstrap`. `CodexPointerComposerEvidence.IsComplete` is false and `MatchingPrefixLength` is 0. `SessionMessageQueueService.ObservePointerAsync` therefore never sees a complete composer, and `SubmitConsumedPointerAsync` ends `NoComposerEvidence`. The same chrome with the idle hint `Ask Codex to do anything` is still pointer-complete. A ready rendered screen whose raw tail alone holds the sentence is also `AccountBootstrap` and not ready.

Why: Before this enum value, a non-idle composer that quoted the sentence fell through to `Unknown`, which pointer evidence still reads. The new reason is produced first and the pointer gate treats every other reason as "do not read the composer." That changes delivery, not only the failure text. Sign-in still wins (variants 07 and 20). A bare `-32603` and a usage-limit `-32603` above an idle composer stay `Ready` (variants 08 and 09). No switch on `CodexStartupReason` throws or maps the new value. `AuthenticationRequired` is still only `LaunchBlock == ProviderSignInRequired`. This path leaves `FailureCode` and `LaunchBlock` unset. Retry, quota hold, and pool routing do not read the enum.

Fix: Keep `AccountBootstrap` for a fatal pre-prompt frame that is not a live composer. A composer that only quotes the sentence must stay readable by pointer evidence (`IsComplete` true when the composer body equals that sentence). A ready rendered screen must stay `Ready` when the three phrases are only in the raw tail. Add those two tests. The existing "fatal line above a ready composer stays not ready" startup case can remain.

Executed at the reviewed SHA against `Antiphon.Agents.Pty.dll` (20 screens, lease `36430463-3ad3-48b3-9689-3d3e25c32a18`, held 6s). Variant 10: composer contains the sentence, reason `AccountBootstrap`, pointer complete false, prefix 0. Variant 11: the sentence in an agent message above an idle composer is also `AccountBootstrap`. Variant 17: ready rendered screen plus the sentence only in raw is `AccountBootstrap`. Variant 18 control: the idle screen is `Ready` and pointer-complete.

## What holds for the captured frame

The exact and wrapped pre-prompt frames classify as `AccountBootstrap` and are not ready. Missing any one of the three phrases does not. The real sentence's outcome is one line, 217 characters: `AccountBootstrap: Error: account/read failed during TUI bootstrap: account/read failed: workspace routing discovery timed out (code -32603); elapsedMs=43954; capture=/tmp/antiphon-codex-startup/example.txt; exitCode=1`. Diagnostic length 121. It does not contain the welcome banner, an email, a token, or a Codex home path. The capture path is the startup-capture file. The adapter log keeps `reason=AccountBootstrap` and the path; the screen sentence is not logged (`CodexAccountBootstrapFailureTests`). `LaunchInteractiveAsync` appends that clause once onto the process-exit `FailureReason` and clips at 2000. `AgentTaskLiveness.ClassifyFailure` copies it into the task reason with `FailureCode` null, and `DelegationReportFormatter.BuildCompletionNote` carries the sentence. No migration rewrites old failed rows.

## Advisory

A-1. `Flatten` strips ESC and leaves the CSI payload. Variant 13 (`(code \x1b[31m-32603\x1b[0m)`) still classifies, and the clause contains `[31m` (`code [31m-32603`). The 2026-10-22 sentence has no ANSI. Does not hold the land.

## Verification

No plan checkpoint rows. Leased runs at `e25cebdf5f2c357c36f515de3836389c8379db97`, `UseAppHost=false`, `TEMP=/tmp`, native output.

| Run | Lease | waited | held | Result |
|---|---|---|---|---|
| Pty build | 4f40cc39-d719-4692-9691-759bb04b2bc9 | 45s | 40s | 0 errors |
| CodexAccountBootstrapTests, CodexStartupReadinessTests, CodexReadyWaitTests | 3540509c-ac24-4f7d-8473-3ce68910ea1c | 120s | 3s | 59 passed, 0 failed |
| PC-1 rebuild and `Exact_pre_prompt_frame_is_account_bootstrap` | build 582a4406-b1dd-4190-b419-3d06bf8e4638; run bb00b3a1-7302-4928-9328-5cbf80b1e0fc | 0s; 0s | 12s; 4s | 1 failed, reason Unknown. Source restored |
| Antiphon.Tests build | 0a454713-ac18-4f56-8f08-209d0c6b8e3b | 0s | 359s | 0 errors |
| CodexAccountBootstrapFailureTests, RunnerCodexAdapterReadyTests | f5d68ea5-dc21-4a1f-9180-8f6b91c48c03 | 90s | 13s | 15 passed, 0 failed |
| AgentSessionLaunchFailureTests | 5f0bc7a0-4d09-49aa-9342-ce84a22488ec | 0s | 84s | 76 passed, 0 failed |
| Documented nine-class guard filter | ebb450ea-3866-4b1a-85f8-f313f6d1d2fb | 45s | 173s | 78 passed, 0 failed |
| Card1277PtyHostFixtureTests (impacted pty glob) | 6881d216-e76b-4666-b7d6-74b109f3e190 | 60s | 27s | 3 passed, 0 failed |

Code reported guards 135 for ten classes. The documented filter in `docs/testing-and-build.md` is the nine classes above; 78 passed matches that set. TestLaneCategoryGuardTests is inside it.

Evidence-diff `ac6d00022..e25cebdf5`: commits=2 entries=0 violations=0. `git merge-tree --write-tree origin/master HEAD` against `b8812bcb12ba309092007e216251284466181b5d` exited 0, tree `ecda2f94e79eff040b77aefe6e16e3569ddf8157`. `scripts/impacted-classes.ps1` on that range: kinds `pty-host-fixture`, `test-lane-category`. Downgrade/migration rows: 0. No new input wait. Ready-wait still ends on timeout, exit, or ready; this reason sends no key. PC-2 and PC-3 were not re-run. PC-1, PC-2, and PC-3 stay pending for SourceLanding Mutation.

`reviewedSourceClean` is false: these are leased TRX runs, not a checkpoint-tool receipt with verified build provenance.
