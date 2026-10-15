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

## Repair verification completed

The raw-parser slice is complete. All 92 Linux results passed at the exact committed
repair SHA **d3f6300d0752328dc71b48cb10e662391b2e7581**, with no failures or skips.
This section updates the pending repair status above; the historical build failures
and deliberate test-first assertion outcomes remain unchanged. The later evidence
publication commit changes this Markdown report only; no build/test result is
re-attributed to that later SHA and no unnecessary post-green repeat is run.

Run 20261015-164338-f6b2 returned terminal exit 0 after wait confirmed executor exit.
The fresh TRX files were inspected by class, new method, outcome and nonzero counts.

| Checkpoint | Actual complete class counts | Outcome |
|---|---|---|
| CP-3 | CodexResponseAnalyzerTests 9; SubscriptionUsageParserTests 5; UsageLimitWallParserTests 39; ProviderQuotaRefusalTests 18 | 71 passed, 0 failed, 0 skipped |
| CP-6 | RunnerCodexAdapterTurnCompleteTests 7; SubscriptionUsageMonitorTests 14 | 21 passed, 0 failed, 0 skipped |

All four V0160 methods passed, including CUP/CUF/plain/SGR/erase/OSC raw answer
variants through the actual runner fallback, original raw diagnostic preservation,
fixed UTC reset, vendor bar and malformed input cases. CP-6 covers the full named
completion and persistence classes, not just their newest tests. No existing test
failed in any completed test invocation.

```text
CHECKPOINT CP-3 commit=d3f6300d0752328dc71b48cb10e662391b2e7581 build=ok filter=/*/*/(CodexResponseAnalyzerTests*)|(SubscriptionUsageParserTests*)|(UsageLimitWallParserTests*)|(ProviderQuotaRefusalTests*)/* executed=71 passed=71 failed=0 skipped=0 trx=/work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-164338-f6b2/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d3f6300d0752328dc71b48cb10e662391b2e7581 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=d3f6300d0752328dc71b48cb10e662391b2e7581 build=ok filter=/*/*/(RunnerCodexAdapterTurnCompleteTests*)|(SubscriptionUsageMonitorTests*)/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-cd023562/.antiphon/checkpoints/20261015-164338-f6b2/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=d3f6300d0752328dc71b48cb10e662391b2e7581 sourceState=clean buildSource=verified
```

Receipt qualification command:

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261015-164338-f6b2/report.json -ExpectedSourceSha d3f6300d0752328dc71b48cb10e662391b2e7581 -Rows CP-3,CP-6
```

Exit 0; unedited validator result:

```text
CHECKPOINT SOURCE VALID source=d3f6300d0752328dc71b48cb10e662391b2e7581 rows=2
```

Both source snapshots bind the same SHA and fingerprint
a65c198c276b22fc74991f20bf6820bfc05b7f8d3a5bfd3c49ce3acf4546e96c; dirty=0,
sourceState=clean, buildSource=verified, cleanupRefusals empty. Both build and test
leases were granted with waited=0s; no unleased or skipped-slot driver ran. The
failed-build checkpoint lines say slot=skipped for their unexecuted test rows; the
actual build leases were granted with waited=0s in each stored report.

Reported checkpoint wall costs, retained without relabeling: first build failure
171.7623722s; second build failure 125.1380035s; intended red 398.334524s; repair
group 696.6755713s. Total 1391.910471s (23m12s), plus the 7s leased bootstrap and
foreground wait/cleanup overhead. Repair builds were 312.4276938s and 264.6393308s;
test hosts were 6.1343857s and 76.5859195s. There were no repeat-proof/loaded runs.

The checkpoint deleted both final compiler-output roots. Earlier completed red/
failed output roots were removed manually after ownership inspection, and the
owned bin-c907-tool launcher output was removed after the final wait. Ignored
checkpoint receipt/log/TRX directories are retained, including build-log directories
whose names happen to begin bin-c907; those are evidence, not compiler output.

## Final ordinary coverage and handoff

| ID | Actual outcome in this slice | Still required |
|---|---|---|
| V-1 | Not run | All native/historical readiness fixture coverage |
| V-2 | Not run | Modal, compaction and screen-lifecycle coverage |
| V-3 | Not run | New ready/submit adapter methods |
| V-4 | New methods not run; 7 existing completion cases passed | Captured rollout/compaction methods and working lifecycle |
| V-5 | Four new methods red before repair, green on Linux after repair | Windows execution |
| V-6 | New vendor-wall methods not implemented; 57 existing wall/classifier cases passed | Vendor wall/error assertions and Windows |
| V-7 | Not run | Captured TUI JSONL, normalizer and tailer smoke |
| V-8 | Not run | Normalized vendor error through real hold/recovery acceptance |
| R-1 | Not run | Full selected Pty roster on both OSes |
| R-2 | Partial Linux: full 7-method completion class passed | Remaining adapter/working classes and Windows |
| R-3 | All 67 existing Linux parser results passed after repair | Windows |
| R-4 | Not run | Runner transcript/tailer/quota classes on both OSes |
| R-5 | Not run | Full quota acceptance class on both OSes |

C907-DESKTOP-STUB-1, every Windows row, seven Windows-only
CodexAdapterLocalShellTests cases, parent CP-1/2/4/5 and the two future V-6 methods
that expand parent CP-3 to 73 remain pending. Nothing deferred is counted as passed.
The whole-card Final gate remains incomplete. The parent requires the full native
120x30, captured-stub-response provenance and rollout evidence; this vendor-derived
status fixture cannot discharge that requirement. Whole Unit was not run under the
operator brief's explicit minimal-verification instruction.

All PC-1 through PC-12 and variants remain pending for later SourceLanding Mutation.
Mutation owns deliberate mutants, red/restore/green cycles and missing-control
discovery. No production mutation was executed here. No Review or landing is
requested for an incomplete whole-card gate: next Code completes remaining
implementation/ordinary V/R, then ordinary Review.

Additional literal-search triage: SubscriptionUsageHttpTests only seeds and projects
stored values; it never invokes the changed parser. FakeAgentProtocolAdapter and
GrokQuestionPopupTests use Grok popup text. These incidental Weekly limit matches
were inspected and excluded from the affected-consumer row. No live Grok canary
was run. The final exact affected integration selection remains CP-6 above.

Git compatibility checked after fetching origin/master at
a30b7f4297400f010ce70188ac99ebe55be51931: merge-tree --write-tree origin/master
HEAD returned exit 0, tree b32f529fc815956f27e08e27cd5371890843d0c3 at the tested
repair commit. This is a conflict check, not acceptance or landing authorization.
The caller receives the final publication SHA and full-base evidence-history check
in the task report after this evidence-only commit is pushed.

Restart: **server**, after eventual landing/activation. This slice changes server
raw consumers and does not require a runner restart. No service was restarted here.
