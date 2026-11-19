# Review c0547f8f: CARD-2004 round 1 (Final/Full)

Subject: origin/feat/card-task-6852e148 tip 25a393a11a0be7d7e02fb86519d7394bc07d14c3, range
c84185e9a424a4cc74011c7d3884e382d3bf9992..25a393a11a0be7d7e02fb86519d7394bc07d14c3 (3 commits). Author: Grok. Reviewer: Claude Opus.

Verdict: CLEAN. No blockers. Advisories below; none holds the land.

## Security judgement

1. Raw header echo. The only read of `X-Antiphon-Caller` is `AgentTaskEndpoints.cs:273`
   (`polls.Record(...)`). `TryRegistered` refuses null/whitespace and anything over 256 chars
   before Trim/ToLowerInvariant (so a large header is not copied), then requires an ordinal
   match in `RegisteredLabels`. A miss returns `""`/false and falls to the server label; the
   header is never stored, thrown or logged. No HTTP logging/Serilog request logging/header
   enrichment is configured in server (`AddAspNetCoreInstrumentation()` with defaults does
   not capture request headers). The stored label is ordinal-equal to a registered entry, so
   a case variant or a Unicode fold (e.g. Kelvin sign) cannot store bytes other than the
   canonical label.
2. `capability:<name>`: `caller.CapabilityName` is `DelegationCapabilities.Name`, read only
   after the bearer token hash matches an unrevoked row (`AgentTaskService.cs:256-277`). Not
   client-supplied. Shape-checked (letter + 63 of `[a-z0-9._-]`, not guid, not 32+ hex, not
   `unknown`), capped at 64 in its own dictionary.
3. Memory bound: `_server` admits only `session`, `capability`, and `kind:` + an
   `AgentKind` name (closed set built from `Enum.GetNames`). `_client` can only hold the six
   registered labels. `_capabilityNames` is capped at 64. All bounded; no client input grows any set.
4. Test witnesses: mutant "remove `if (!RegisteredLabels.Contains(text)) return false;`" built
   (build slot lease 727d5ba5, 199 s) and ran `/*/*/AgentTaskGetPollMetricsTests/*`:
   total 11, failed 7 (C1972_script_header..., C1972_absent_identity..., C1972_unsafe_header...
   [Unknown should be 4 but was 0], C1972_distinct_labels_stop_at_64, C2004_registered_labels...,
   C2004_junk_headers..., C2004_unknown_header...). Reverted (`git diff` empty). The old
   self-comparing `Replace(...).ShouldNotContain` pair is replaced by `CountOf(...)==1`, which
   fails when the call is removed or duplicated. `C2004_list_route_is_not_counted` fails if
   `polls.Record(` or `AgentTaskGetPollMetrics` appears in the list GET region.
5. Docs: docs/logs.md and docs/ops-http.md match the code (registered table, own slots for
   kind/session/bare capability, 64 caps, overflow, list route not counted). Polling scripts
   and `useAgentTask` were left unchanged on purpose (plan and docs say so).

## Checkpoint run (tool), reviewed SHA 25a393a11a0be7d7e02fb86519d7394bc07d14c3

Run 20261119-140717-9ed5, plan docs/superpowers/plans/2026-11-19-card-2004-checkpoints.md, verdict GREEN exit=0, wall 15m19s, 1 build, unlisted: none.
`validate --evidence .../report.json --expected-source-sha 25a393a1...`: `CHECKPOINT SOURCE VALID source=25a393a11a0be7d7e02fb86519d7394bc07d14c3 rows=8`.

| CP | executed | passed | failed | skipped |
|---|---|---|---|---|
| CP-1 AgentTaskGetPollMetricsTests | 11 | 11 | 0 | 0 |
| CP-2 DbRateSampler/DbCallSiteMetrics | 11 | 11 | 0 | 0 |
| CP-3 TestClassificationPolicyTests | 42 | 42 | 0 | 0 |
| CP-4 DerivedImpactIndexTests | 27 | 27 | 0 | 0 |
| CP-5 guard batch (7 classes) | 34 | 34 | 0 | 0 |
| CP-6 SlowTestRegistryMergeTests | 5 | 5 | 0 | 0 |
| CP-7 land graph -Check | exit 0 (approved) | | | |
| CP-8 check-evidence-diff | exit 0, commits=3 violations=0 | | | |

Unlisted runs (reason: brief-required mutation witness): one `dotnet build tests/Antiphon.Tests`
and one `dotnet run --no-build` filter run, each under its own build-slot lease, against the
mutated tree (not the reviewed SHA); outcome above.

Other checks: `git merge-tree --write-tree c93364585afe99a99c770d23ec8ff7d2dc23bfa0 25a393a11` clean (tree e208ce007a777df8ed56efec85f298a7e942fb86); `check-evidence-diff -BaseRef c84185e9a -HeadRef 25a393a11` commits=3 entries=0 violations=0.

PCs: none in the plan; mutation execution waived (operator 2026-11-12), not discharged.
Producer-to-recipient / queue / transcript: not applicable (an in-process counter, no session delivery).
Changed input waits: none.

## Advisories (cards, not land blockers)

- A-1. `TryRegistered` stores the lowercased input string rather than the set's own instance.
  It is ordinal-equal, so harmless; returning the canonical instance (`RegisteredLabels.TryGetValue`)
  would make the "canonical string" property self-evident. Note `ToLowerInvariant` folds
  U+212A KELVIN SIGN to `k`, so `checKpoint-task.ps1` counts as `checkpoint-task.ps1`; harmless.
- A-2. An `AgentKind` value outside the enum (e.g. an int not defined in the enum read from the DB) builds
  `kind:<number>`, which is not in `KindLabels` and lands in the `_client` store (cap 64). Server
  data, bounded; only attribution is affected.
- A-3. Card A-3 (per-request string building, lock instead of the lock-free pattern) is unchanged.
- A-4. The 256-char pre-trim refusal is not in the ops-http prose.
- A-5. Follow-up from the card still open: make the scripts and `useAgentTask` send the registered label.
