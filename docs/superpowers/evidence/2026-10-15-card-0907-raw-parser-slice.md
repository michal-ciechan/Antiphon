# CARD-0907 raw-parser slice evidence

Code owner / original landing task: cd023562-f0f0-4238-84b4-063f9d03077b.
Branch: feat/card-task-cd023562. Worktree: /work/worktrees/task-cd023562.
Task base: 8ffe3633357e6383b7e408bd078465acf9f5fd79.
Plan: [bounded slice](../plans/2026-10-15-card-0907-raw-parser-slice.md), under
[the parent design](../plans/2026-10-02-card-0907-codex-0160-screens-plan.md).

## Scope and current outcome

Four new V-5 regression tests reproduced the actual defects before production edits:
cursor-separated question-bearing echo survived; answer words joined; CUP/CUF weekly
status text failed to parse; the independent vendor progress bar also failed to parse.
All 67 pre-existing CP-3 results passed. Production repair preserves CUP/CUF separators
only in the Codex raw consumers and permits a 1..80 glyph full-block/shade bar. Shared
ANSI stripping, other providers, runtime deadlines, transcript authority and raw
diagnostics retain their existing behavior. Final repair verification is pending in
this commit; a later append records its actual outcome.

The Windows capture remains unavailable from the assigned Linux checkout. This slice
is useful independent work, not full CARD-0907 acceptance. No provider session, model
call, account access, capture submission, deployment or restart was performed.
GET /api/runner-defaults and GET /api/session-runners were read: global default server2;
desktop advertises Windows and availability; this task has no desktop filesystem.
No host placement settings were changed.

## Fixture provenance

The complete status snapshot payload is vendor-derived, not a local terminal capture.
[Immutable vendor source](https://raw.githubusercontent.com/openai/codex/a956835d020762cb2b570053af06f643a11c0ecc/codex-rs/tui/src/status/snapshots/codex_tui__status__tests__status_snapshot_includes_credits_and_limits.snap),
retrieved 2026-10-15. Source SHA256
3f7762c4e006f0934b118408bd264469693ff0204a73251cc0ecab4678fc689f;
payload SHA256 a691faeafa5d9e59b47a78832cb3c5e987bed37b0feba5cfbb97d181856eed68.
Only snapshot frontmatter was removed; the vendor's v0.0.0 banner, model, workspace
placeholder and panel are retained. No native geometry is claimed. The private test
loader removes the metadata envelope and verifies its hash before real parser calls.
The expected answer, remaining percentage and UTC reset are independent literals,
not fixture self-comparisons. Cursor input witnesses are the parent plan's derived
literals.

## Test-first history

One declared bootstrap: build-slot.ps1 -Label c907-checkpoint-launcher -- dotnet build
tools/Antiphon.Checkpoints --property:OutputPath=bin-c907-tool/ --nologo.
Exit 0, slot=granted waited=0s, maxcpucount=6, lease
9e0a58af-7df1-47d6-a118-71d0f5ccfa8c, held 7s. No launcher was initially built.
This is the sole non-checkpoint build driver. No full assembly or Unit-lane run, per
the operator brief; no live canary, no repeat-proof runs, and no deliberate mutants.

The initial two checkpoints failed compilation of newly authored Shouldly assertions.
These are introduced test-authoring failures, repaired in pushed commits, never
credited as guarded-defect reds. First: unsupported aggregate helper and labeled
ShouldContain overload. Second: labeled ShouldNotContain overload inside the now
bound aggregate. Production source was unchanged in both. Both fresh failed output
directories were inspected as owned, untracked output from this task and removed
manually after terminal executor exit; the original refusal records are preserved.

The successful test-first build at 2ff6f12c087b28cd3635c05616211c1237870800 ran
exactly CodexResponseAnalyzerTests=9 (2 failed), SubscriptionUsageParserTests=5
(2 failed), UsageLimitWallParserTests=39 and ProviderQuotaRefusalTests=18. Fresh TRX
was read, including every new method and failure label:

| Method | Actual first detecting label / result |
|---|---|
| V0160_cursor_positioned_echo_is_excluded | C907-echo-excluded; question mark survived |
| V0160_fallback_preserves_answer_word_boundaries | C907-answer-word-boundaries-cup; C907alphabeta. |
| V0160_cup_status_retains_remaining_and_reset | C907-cup-status-parsed; Unparsed |
| V0160_cuf_status_retains_remaining_and_reset | Aggregate: C907-cuf-status-parsed, C907-vendor-panel-parsed, C907-malformed-date-percentage; Unparsed in each |

The red receipt has clean matching start/end SHA and fingerprint, dirty=0 and
verified build binding. A failing test run is not a green receipt. It retained its
owned output directory; remove that directory after the completed run before the
fresh repair build. No tests were skipped and no existing test failed.

## Unedited checkpoint lines

Run 20261015-162815-b38b; evidence root /work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-162815-b38b/.

```text
CHECKPOINT CP-3 commit=4c2434113a3770a0b1695be7de1c55f18d2eff7d build=failed filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=4c2434113a3770a0b1695be7de1c55f18d2eff7d sourceState=clean buildSource=unknown
```

Run 20261015-163218-f087; evidence root /work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-163218-f087/.

```text
CHECKPOINT CP-3 commit=d98c9fc124e5519a00d139e2a7aa93db812d8e0d build=failed filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=d98c9fc124e5519a00d139e2a7aa93db812d8e0d sourceState=clean buildSource=unknown
```

Run 20261015-163515-c109; evidence root /work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-163515-c109/.

```text
CHECKPOINT CP-3 commit=2ff6f12c087b28cd3635c05616211c1237870800 build=ok filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=71 passed=67 failed=4 skipped=0 trx=/work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-163515-c109/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=2ff6f12c087b28cd3635c05616211c1237870800 sourceState=clean buildSource=verified
```

## Required verification status before repair run

V-1: pending; V-2: pending; V-3: pending; V-4: pending; V-5: four intended reds
confirmed, repair green pending; V-6: new vendor-wall methods pending (existing
39+18 parser cases green); V-7: pending; V-8: pending.
R-1: pending; R-2: parent roster pending (CP-6 adjacent subset scheduled); R-3: all
67 existing Linux parser cases passed at the test-first SHA, repair rerun pending;
R-4: pending; R-5: pending. All Windows rows and C907-DESKTOP-STUB-1 remain pending.
The parent CP-3 final floor of 73 still requires the two V-6 additions; this slice's
71-result selection does not replace it. CP-1,2,4,5 remain unexecuted here.

PC-1 pending; PC-2 pending; PC-3 pending; PC-4 pending; PC-5 pending; PC-6 pending;
PC-7 pending; PC-8 pending; PC-9 pending; PC-10 pending; PC-11 pending; PC-12 pending.
All variants and missing-control discovery belong to later SourceLanding Mutation.
No PC is discharged by the ordinary red-first run.

Identifier/literal searches covered every changed code/test/fixture and both plan
paths: CodexResponseAnalyzer, ExtractResponse, IsAskingQuestion, CodexRemaining,
SubscriptionUsageParser, v0160-status-panel, C907 labels, Weekly limit and the
response/completion fields. Direct existing guards are in CP-3; the caller/persistence
classes RunnerCodexAdapterTurnCompleteTests (7) and SubscriptionUsageMonitorTests
(14) form the extra CP-6 row. CodexAdapterLocalShellTests has 7 Windows-only cmd.exe
cases and remains pending for Windows. Grok popup/canary literal matches do not
consume the changed Codex-specific path; no live canary is authorized here. No
existing tests consume the new plan paths or fixture name.

Final/whole-card gate: incomplete. Next Code completes capture-dependent work and
remaining ordinary V/R, then ordinary Review. Restart after this repair lands:
server. No runner behavior changed; no runner restart is needed for this slice.
