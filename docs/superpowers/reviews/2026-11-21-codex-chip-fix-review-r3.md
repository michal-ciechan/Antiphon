# Review r3: Codex 0.170 warning-chip pointer fix (CARD-1514, rc fix 3)

Review task `dfeb678e`. Subject: `origin/feat/card-task-589abe80` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`, Code owner `589abe80` (Grok). Round 3:
blockers and regressions only. Range `b02b4ea7ae80ad8eff3445a82d5477b80107bf44..bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`
(`94950a17c` red test, `b963a3e8b` fix, `bd34a7ff6` plan).

**Verdict: CLEAN. No blockers, no regressions.** One advisory (A-1).

## Runs (server2, native output, UseAppHost=false, TEMP=/tmp, ANTIPHON_LINUX_COMPILER_POLICY=0)

| Run | Lease | Load at start | Result |
|---|---|---|---|
| `dotnet build tests/Antiphon.Agents.Pty.Tests` at `bd34a7ff6` | `ef27a153-fe43-4101-b28c-7055e9a1dd6b` (held 37 s) | 3.00 | exit 0 |
| `--no-build` run, CP-1 filter `(CodexAccountBootstrapTests*)\|(CodexStartupReadinessTests*)\|(CodexV0160StartupTests*)\|(CodexReadyTrackerTests*)\|(CodexReadyWaitTests*)`, `--timeout 570s` | `cbd2dd23-646e-461b-a38f-9b247e379e5b` | 5.65 | total 83, failed 0, succeeded 83, skipped 0 |
| dll probe (no build): 3 measured grids + 13 adversarial variants | n/a | n/a | 16/16 as expected |
| `check-evidence-diff.ps1 -BaseRef b02b4ea7a -HeadRef bd34a7ff6` | n/a | n/a | commits=3 violations=0, exit 0 |
| CP-3 pin: `test-live-lane-run.ps1 -PrintInputDigest` | n/a | n/a | `15137e48…9365` equals `PinnedInputDigest` |
| scratch `git rebase origin/master` (detached, not pushed) | n/a | n/a | clean, 3 commits replayed |

Not run here: CP-2 (nine-class guard batch, `Antiphon.Tests`) and CP-4 (land-graph
`-Check`, needs its evaluator build). The brief's operator CPU rule allowed ONE build
(the Pty Codex classes). Both rows need an extra build. The diff touches no
`tests/Antiphon.Tests`, no scripts, no `tests/derived-impact-index.json`. It adds two
methods to an already classified class. The author reported CP-2 84/84; this round
did not re-witness it. That is why the evidence block says Interim.

## (a) False Enter

The new step only changes which row is taken as the footer. After the step the row must
still pass `IsModelEffortCwdFooter` and `TryReadFooterOnlyModel`. The composer walk and
`ComposerDeliveryEvidence.WholeBodyEquals` are unchanged, so the full pointer is still
required. `TryReadBottomComposer` is unchanged.

Probe: `Antiphon.Agents.Pty.dll` from the build above, loaded in pwsh, calling
`CodexPointerComposerEvidence.IsComplete` and `MatchingPrefixLength`. The measured grids
are the two `text` blocks of
`docs/investigations/2026-11-21-codex-0170-delivery-verification-failed.md` at
`243f9924bc7333a4f7fc07b5eae4bf1ddfb72101`. The bodies are the composer rows, joined.

| Case | Expect | complete | prefix |
|---|---|---|---|
| M1 measured Sol, padded to 30 rows | true | true | 532/532 |
| M2 measured Astra, padded to 30 rows | true | true | 531/531 |
| M3 measured Sol, rows as in the note | true | true | 532/532 |
| A1 Sol, last wrap row missing (partial pointer) | false | false | 478/532 |
| A2 body differs by one character | false | false | 532/533 |
| A3 chip row in the transcript area, unknown last row | false | false | 0 |
| A4a GPT footer carries a chip AND a chip-only row below, full pointer | true | true | 532/532 |
| A4b same, partial pointer | false | false | 478/532 |
| A5 modal with `warning` and `f2 to view`, no footer above | false | false | 0 |
| A5b `warning: f2 to view details` under the composer, no footer | false | false | 0 |
| A6 chip row with a shortcuts row below it | false | false | 0 |
| A7 pointer only in history, empty current composer, chip last | false | false | 0 |
| A8 chip-only row containing `GPT-` (no step) | false | false | 0 |
| A9 empty composer, chip last | false | false | 0 |
| A10 chip row alone | false | false | 0 |
| A11 blank rows between footer and chip, full pointer | true | true | 532/532 |

The measured prefixes (532 Sol, 531 Astra) equal the note. Every true case has the
whole pointer in the current composer. Every partial, stale or footer-less case stays
incomplete. The existing negatives (`Codex_0170_shortcuts_row_without_composer_text_is_not_complete`,
the 0.160 composer-evidence cases) are in the 83 green. Their test bodies are unchanged
in the range.

## (b) Fixtures and the red witness

The new test's last two rows are verbatim from the measured screens: the
`GPT-6.2-Sol high · /work/worktrees/ws-03-d3` and `GPT-6.2-Astra high · /work/worktrees/ws-02-d3`
footers, and 94 spaces plus `⚠ 2 warnings · f2 to view` (119 columns). The `›` head
rows are verbatim. The composer is abbreviated (see A-1). The probe above runs the full
measured grids, so the fix is witnessed on the real shape.

Red reason: at `94950a17c` the reader is the `b02b4ea7a` reader. The chip row is not an
`IsAgentShortcutsRow`, passes `IsModelEffortCwdFooter` and fails `TryReadFooterOnlyModel`.
So `TryReadComposer` returns false and the prefix is 0. That matches the author's
recorded red (expected 176, actual 0) and the note's diagnosis. This round did not
rebuild `94950a17c` (one-build rule).

## (c) Merge and land

`git merge-tree --write-tree origin/master bd34a7ff6` conflicts in
`CodexPointerComposerEvidence.cs` and `CodexAccountBootstrapTests.cs`. Master
`0ce5e409c` already holds the earlier stack as `c2334fdc7`, `5b7784d39`, `d980e90c6` and others.

Scratch detached `git rebase origin/master` (not pushed): git skipped `41415c404`,
`9adbf3ba1` and `b02b4ea7a` as previously applied. It replayed the three new commits
cleanly as `81aed6444`, `1142b3feb`, `0a9c468a5`. `git range-diff` shows all three as
`=` (patch-identical). The rebased blobs of `CodexPointerComposerEvidence.cs`,
`CodexAccountBootstrapTests.cs` and `CodexStartupReadiness.cs` equal the reviewed
`bd34a7ff6` blobs. `src/Antiphon.Agents.Pty` and `tests/Antiphon.Agents.Pty.Tests` have
no diff between `b02b4ea7a` and `origin/master`. So the 83/83 run verified the bytes
that will land.

A normal rebase-based land by owner `589abe80` with
`-ExpectedSourceSha bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` should succeed. No replay
task is needed. This holds unless the land path refuses on the raw merge-tree pre-check
instead of rebasing. If it does, the fallback is a replay of the three commits onto master.

## Advisory

- A-1 (advisory, not a blocker). The test fixtures keep the measured footer, chip row and
  `›` head row. They replace the five measured wrap rows with one spliced row
  (`BRIEF IS NOT IN THIS MESSAGE. inbox/<id>.md`), and they drop the header rows 2 to 5.
  The comment calls the grid "measured". A follow-up could embed the full 30-row captures
  verbatim. This round's probe M1 to M3 already covers them.
