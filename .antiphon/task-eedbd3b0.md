# Review CARD-1754 round 1 (Final): compaction fact load — CLEAN

Subject: origin/feat/card-task-d3524403 tip 9676822dcc4371ac019170cb56d53ec2a85920a6 (Code task d3524403-c59d-4e66-a7a9-b4a668224daa, Grok). Base edb2c20156afa4da503b1b29fb83f1f953987bd5. Range edb2c2015..9676822dc, 2 commits, 5 files (3 server, 2 tests). No migration in the diff. Verdict: CLEAN, no blockers. Advisories A-1..A-4 below.

## Execution (one leased checkpoint run at the reviewed SHA)

Run 20261107-144802-107c, plan `.antiphon/card-1754-review-plan.md` (ignored extract; no plan exists for CARD-1754), `--expected-source-sha 9676822dcc4371ac019170cb56d53ec2a85920a6`, host server2 (Debian 12, cores=24). Report: `.antiphon/checkpoints/20261107-144802-107c/report.md`.

```
source: 9676822dcc4371ac019170cb56d53ec2a85920a6 state=clean buildSource=verified
CHECKPOINT CP-1 commit=9676822dcc4371ac019170cb56d53ec2a85920a6 build=ok filter=/*/*/(CheckCompaction*)|(CompactionContinuationPolicyTests*)/* executed=82 passed=82 failed=0 skipped=0 trx=.antiphon/checkpoints/20261107-144802-107c/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=9676822dcc4371ac019170cb56d53ec2a85920a6 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=9676822dcc4371ac019170cb56d53ec2a85920a6 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 trx=.antiphon/checkpoints/20261107-144802-107c/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=9676822dcc4371ac019170cb56d53ec2a85920a6 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 19m37s  builds: 1  rows: 2 green 0 red 0 skipped   outputs: deleted bin-c1754-r1/   verdict: GREEN exit=0
```

CP-1 identities (TRX): CheckCompactionReceiptTests 27, CompactionContinuationPolicyTests 19, CheckCompactionAutomaticRestartTests 11, CheckCompactionCrashTests 9, CheckCompactionFactLoadTests 3, CheckCompactionFactEquivalenceTests 3, CheckCompactionWorkerLifetimeTests 2, CheckCompactionRecoveryPersistenceTests 2, CheckCompactionContinuationTests 2, CheckCompactionFixtureTests 1, CheckCompactionRecoveryFlowTests 1, CheckCompactionContinuationSettingsTests 1. All six new tests Passed by name. Counts match the Code report (82/82, 75/75).

Other builds: one leased `dotnet build tools/Antiphon.Checkpoints` (label c1754-review-tool, lease granted, 12 s) to run the tool from its built dll; no other build or test driver ran.

- `check-evidence-diff.ps1 -BaseRef edb2c2015… -HeadRef 9676822dc…`: commits=2 entries=0 violations=0, exit 0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master now 0e7aef0379a073c206e8398dcaed539bc33eaf9b, past the brief's 5322f3d2): tree 9aedfb0f40498017e0d432e479d8727630d6a9ae, exit 0, no conflicts.
- Scope: the commissioned closed selection (compaction classes + guard batch) ran in full. The whole Unit lane was NOT run (not commissioned; ~3 h on server2 against a 60-minute budget), so `ordinaryScopeCompleted` is Interim. The card's second acceptance (desktop re-trace, LoadFactsAsync < 3 %) is a desktop action and remains owed to the caller. SourceLanding Mutation: no PC rows exist (no plan); nothing pending from a plan.

## (1) Verdict equivalence — holds

`CheckTokenSet.Correlates` is exactly `Contains(D) || Contains(N) || Contains(marker)` for lowercase ordinal text, which is what the legacy lambda computed:
- D form: scans every 36-char window with hyphens at 8/13/18/23 and lowercase hex elsewhere, then a hash lookup. Any D-form occurrence satisfies that shape; nothing else can match the set.
- N form: maximal lowercase-hex runs, every 32-char window inside a run ≥ 32. An N-form id is always inside such a run (covers ids embedded in text, e.g. `aa<N>bb`, and the `<N>ff` seed).
- Marker: `[antiphon-task:` + 8 lowercase hex + `]` against the short-id set, identical to `DelegationReportFormatter.TaskMarker` (`[antiphon-task:{N[..8]}]`).
- IsHumanOrigin: same `RequiresTextMatch` filter and `IsCompleteIn` as before.
Uppercase D, a 31-char N prefix, an unterminated marker, null/empty text and non-prompt kinds are in the test and all come out false, as before.

The oracle is genuine: `Legacy(...)` in both test files is a verbatim copy of the base `LoadFactsAsync` lambda (`tokens.Any(token => row.Text.Contains(token, Ordinal))`, `humanMatchable.Any(IsCompleteIn)`), not the new code. `Token_lookup_matches_contains_on_many_checks_and_prompts` compares every field of every fact (48 Check ids, 52 prompts); `Judged_window_keeps_legacy_verdicts_on_a_large_transcript` compares full verdict records for: auto boundary + marker owner, embedded N owner, human-owned prompt, unmatched tool, manual boundary, progress after boundary, interrupt end followed by an uncorrelated human prompt, and an ordering conflict (40 Check ids, 61 history rows). `Seeded_transcript_policy_verdict_matches_the_legacy_fact_computation` does the same through the real `LoadFactsAsync` against Postgres.

## (2) Window contains every judged row — holds

`Evaluate` reads text only through kind-gated helpers: `IsAutoCompactBoundary`/`IsManualCompactBoundary` (CompactBoundary rows only), `IsInterruptPrompt` and `IsCompactionContinuationPrompt` (UserPrompt only). The new loader keeps: every CompactBoundary row's text; every UserPrompt whose text passes `IsInterruptPrompt` (SQL pre-filter `strpos("Text",'[Request interrupted')>0` is a superset of `TrimStart().StartsWith`, refined in memory); every UserPrompt with `Sequence > start`, where `start = CompactionContinuationPolicy.OpenStartExclusive(...)` runs the same `NewestOwnedBoundarySequence` + `LastEffectiveEndIndex` that `Evaluate` runs, over all rows (slim projection carries Sequence/Kind/Timestamp/CreatedAt/Uuid/ToolUseId). Those two functions need only boundary text, interrupt text and timestamps, all present at that point. `open = ordered.Skip(endIndex+1)` is exactly `Sequence > start` because `IX_TranscriptEntries_AgentSessionId_Sequence` is unique. `endIndex < 0` maps to `long.MinValue` and loads every prompt. `HasOrderingConflict`/`PredatesOwnership`/`TryWaitStart` read no text; the verdict record carries sequences and NativeIds only. `FindOwningCheckAsync` reads `prompt.Text` for `OrdinaryPromptSequence`, which is inside the open window. No other consumer reads fact text (grep of the service: lines 515/591/787/807 are separate queries).

## (3) Fact cache — cannot serve stale facts here; not a blocker

Key = SHA-256 of (acceptedStartedAt ticks, row count, max sequence, sum of `length("Text")`, sorted Check ids, sorted sent human bodies). Facts are stored per session under that key; a mismatch reloads. The stamp is read before the load, so a cached array can never be older than its key.

Transcript rows are immutable in this code base: the only writers are `AgentSessionRuntime` inserts (the `ApplyPersistStub` rewrite at AgentSessionRuntime.cs:1800 runs after `ChangeTracker.Clear()` on an entity that failed to insert and is re-added as a new row, so it never updates a stored row) and `DataRetentionService.cs:372`, a whole-session `ExecuteDelete` (count drops to 0, key changes). No `Update`/`Attach`/`ExecuteUpdate`/raw SQL/migration touches TranscriptEntries, and no API route writes them. Catch-up imports with lower sequences change the count. Human bodies and the Check set are in the key, so a sent queue row or a new Check task reloads. The conceded same-length in-place edit therefore has no producer, and a wrong or missed auto-stop from a stale fact is not reachable. Thread-safety: static `ConcurrentDictionary`, immutable entries, readonly record struct facts; the array is shared read-only. Growth: bounded at 256 sessions, cleared wholesale above that; entries for ended sessions linger until then (see A-3).

## (4) Witnesses — real

Red commit b374ea09c keeps the legacy loader (made `LoadFactsAsync` internal only) and adds the three load witnesses plus the source witness; at that commit `tokens.Any(` is present, every row's text is projected and nothing is cached, so the three claimed reds are red by construction. The fix commit changes `TextProjectionIsRestricted` from `IndexOf(" WHERE ")` to `\bWHERE\b` (EF emits a newline before WHERE), a helper correction, not a loosened assertion. I did not execute the red commit myself (cold build budget); the tip's source has no `tokens.Any(`/`SelectMany(id => new[]` residue and the checkpoint receipt is `sourceState=clean`, so the mutants are restored. `Rows_outside_the_judged_window_do_not_return_transcript_text` would be red if any TranscriptEntries query projected `"Text"` without a Kind/Sequence WHERE, or if historical text came back; `Unchanged_session_does_not_project_transcript_text_again` is red without the cache. None is a self-compare.

## (5) Regressions elsewhere — none found

`CompactionContinuationPolicy` gains only the pure `OpenStartExclusive`. `LoadFactsAsync` signature change is applied at all three call sites with the scope's own `AcceptedStartedAt`. `.NET` `AlternateLookup<ReadOnlySpan<char>>` requires net9+, which the build uses. Query count on a cache miss rises from 3 to 8 small queries, on a hit to 5 aggregates/ids; bytes transferred drop from the whole transcript to the judged window.

## Advisories (no Review rerun needed)

- A-1 Stamp cost: `SUM(length("Text"))` makes Postgres detoast and scan every row's text of the session on every pass (per-minute gate per seat; ungated per tick for unresolved episodes in `AdvanceAsync`). Rows are append-only, so count + max sequence already detects every change the sum could; dropping the length sum (or replacing it with `MAX("CreatedAt")`) removes the last whole-transcript scan from the path. The dev Postgres shares the desktop host, so the re-trace may show this as DB CPU instead of server CPU.
- A-2 Two implementations of one window rule: `CompactionTranscriptFacts.BuildJudged`/`KeepText` is used only by the in-memory equivalence suite; the service's `LoadJudgedFactsAsync` restates the rule in SQL. The DB path is proven by one seeded shape (TurnEnd end, auto boundary). A second DB shape with an interrupt as the effective end would bind the SQL `Sequence > start` and the interrupt pre-filter directly.
- A-3 Cache eviction: entries for ended sessions stay until 256 sessions accumulate, then everything is cleared; a per-entry `Clear` on session end or a small LRU would keep steady-state memory to live seats. The `Unchanged_session…` witness also assumes no concurrent `Store` crosses 256 between its two calls (negligible in the current suite).
- A-4 `Fact_load_does_not_token_scan_every_check_id` is a source-text guard (walks up to Antiphon.sln from the test output); it cannot catch a dll-swap mutant and would fail if test output were ever relocated outside the repo. The two behavioural witnesses carry the real guard.

Owed to the caller: desktop re-trace for the card's CPU acceptance; SourceLanding Mutation pass (no PC rows to seed). Restart: none.
