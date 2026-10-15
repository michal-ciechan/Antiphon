# CARD-0907 raw-parser slice

Owner: Code task cd023562, branch feat/card-task-cd023562, base
8ffe3633357e6383b7e408bd078465acf9f5fd79. This is a bounded continuation of
[the landed design](2026-10-02-card-0907-codex-0160-screens-plan.md), under the
2026-10-15 brief's next-unfinished-slice and minimal-verification direction.

The Windows-only C907-DESKTOP-STUB-1 capture is unavailable in this Linux checkout.
No v0160 captures or earlier Code repairs exist at this base. Advance the independent
V-5 raw-parser witnesses now; the capture-first ordering is amended only for these
synthetic witnesses and the pinned vendor status panel. Do not substitute synthetic
working/done/rollout evidence. The commissioned round remains Final, incomplete for
the whole card; this is not an Interim pass or a reduced full-card gate.

## Scope and tests

Implement the four planned methods in CodexResponseAnalyzerTests (9 total) and
SubscriptionUsageParserTests (5 total). The response fallback test also drives
RunnerCodexAdapter through a fresh TurnEnd without AssistantText. Preserve raw
diagnostics, plain/SGR/OSC/erase behavior and existing hard-wrapped echo tests.
The CUF status method collects independent assertion failures, exposing both the
cursor and full vendor-panel defects in the same red run. The parser accepts only
a bounded bracketed bar of 1..80 full-block/shade glyphs, matching the pinned
vendor panel's 20 glyphs. Reject arbitrary, empty, multiline or oversized bars.
Malformed dated resets retain a parsed percentage with no absolute reset.

The v0160-status-panel fixture retains the entire pinned vendor payload, including
its v0.0.0 test banner; its header names release 0.160.0, source revision, retrieval,
transformation, geometry limitation and independent source/payload hashes. The
test loader removes the envelope and verifies the payload hash before parsing.
Inline cursor witnesses remain explicitly derived regressions.

Commit/push tests and this selection before the red run. Then fix only the two raw
consumers: Codex-only CUP/CUF separation before AnsiStripper.Clean, plus the bounded
status-bar grammar. Keep the shared helper in CodexDetectors.cs as allowed by D-4.
No startup, completion, hold, delivery or transcript behavior changes in this slice.

### Checkpoints

Original slice: CP-3 alone at the test-first commit; CP-3 and CP-6 at the repair commit.
D1 repair (Code ec8f807a, base 14f077fb578e5262b5768fc3bafaa77fb7989764):
run CP-3 and CP-7 at the test-first commit, then CP-3, CP-6 and CP-7 at the fix.
This is the brief's closed Final repair selection (CARD-1287), not whole Unit.
CP-3 now includes two new echo regressions (73 results); CP-7 includes every method
in the Grok, OpenCode and portable in-process Codex raw-response caller classes.
The new CodexAdapterRawResponseTests uses the existing stream-reader seam and private
fixture state to call the real adapter extraction path without starting a process;
it does not replace the seven Windows-only CodexAdapterLocalShellTests.
Each row uses a fresh isolated output and exact complete classes. CP-3 retains
the parent selection: 9 + 5 + 39 + 18 = 71, with the two future V-6 methods still
pending (the parent final floor remains 73). CP-6 is the brief-required extra
regression row identified by searching the changed parser identifiers, response
fields and Weekly limit literals: the runner completion consumer (7) and persisted
subscription monitor consumer (14). Direct parser guards are already in CP-3.
CodexAdapterLocalShellTests requires Windows cmd.exe and remains pending there.
No full assembly or whole Unit invocation; operator brief explicitly excludes it.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | EstimatedMinutesWindows | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---:|---|---|
| CP-3 | Raw-tests,Raw-fix,D1-tests,D1-fix | `tests/Antiphon.Tests -> bin-c907-raw/` | codex-raw | `/*/*/(CodexResponseAnalyzerTests*)\|(SubscriptionUsageParserTests*)\|(UsageLimitWallParserTests*)\|(ProviderQuotaRefusalTests*)/*` | V-5,R-3,D1 | Linux 73; Windows 73; 0 failed/skipped after repair | 73 | 6 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c907-disabled` |
| CP-6 | Raw-fix | `tests/Antiphon.Tests -> bin-c907-adjacent/` | codex-adjacent | `/*/*/(RunnerCodexAdapterTurnCompleteTests*)\|(SubscriptionUsageMonitorTests*)/*` | adjacent R-2 and persistence | Linux 21; Windows 21; 0 failed/skipped | 21 | 8 | 9 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c907-disabled` |
| CP-7 | D1-tests,D1-fix | `tests/Antiphon.Tests -> bin-c907-callers/` | raw-callers | `/*/*/(RunnerGrokAdapterTurnCompleteTests*)\|(OpenCodeAdapterTests*)\|(CodexAdapterRawResponseTests*)/*` | D1,adjacent R-2 | Linux 20; Windows 20; 0 failed/skipped after repair | 20 | 6 | 7 | true | `TUNIT_MAX_PARALLEL_TESTS=1;C804_ORPHAN_SWEEP_ROOT=c907-disabled` |

Use the checkpoint tool with committed HEAD as expected-source-sha; await terminal
exit and inspect every class and new method in fresh TRX. One leased bootstrap of
the checkpoint launcher is allowed because no launcher output exists at this base.
No repeat proof or mutation runs are commissioned. Generated evidence stays ignored.

D1 invariant: cursor separators apply only to Codex raw callers. Grok/OpenCode retain
plain AnsiStripper output, including answer bytes and question detection, through both
Grok completion paths and OpenCode completion. Codex retries echo matching against
plain-stripped text if the separated form misses a mid-word move or an existing space.
The four V0160 tests and all 67 earlier parser results must stay green. Cost: 12 minutes
test-first plus 20 minutes final, including isolated builds; no full assembly or repeat
proof is justified. Add no timeout increases or weakened assertions. The repair alone
goes next to Review; the parent card's remaining acceptance remains incomplete below.

## Remaining card obligations

V-1,V-2,V-3,V-4,V-6,V-7,V-8 and R-1,R-4,R-5 remain pending. V-5/R-3 can earn Linux
slice evidence here; their Windows receipts remain pending. CP-6 does not discharge
the full R-2 roster. Parent CP-1,2,4,5 and final expanded CP-3 remain pending on
both OSes. C907-DESKTOP-STUB-1 and native 120x30 capture/rollout acceptance require
a caller-commissioned Windows Code continuation; it must use the no-account,
loopback-only recipe and retain the single-submission budget. No credential or
interactive login is needed or authorized by this slice.

PC-1 through PC-12 remain pending for SourceLanding Mutation, including PC-5/6/9
guarding these repairs. Ordinary tests never discharge those controls. Do not
claim whole-card completion, Review readiness or landability from this slice.
Next Code completes the remaining implementation and ordinary verification, then
ordinary Review. Restart after this repair lands: server (both raw consumers execute
there); no runner restart or model-ladder change is introduced.
