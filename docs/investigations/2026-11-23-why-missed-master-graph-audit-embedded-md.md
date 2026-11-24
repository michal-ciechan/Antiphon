# Why master's land-graph audit missed the embedded seed markdown (2026-11-23)

Debug task 02f9ccec, branch `feat/card-task-02f9ccec`, base master `c04934e50`.

## Symptom

`scripts/audit-land-verification-graph.ps1 -Check` at master `c04934e50` exits 1:

```
definition file item selects a markdown or text document the audit does not allow; -Update cannot approve it:
server/Antiphon.Server.csproj EmbeddedResource Include=Application\Review\Checks\Seed\**\*.md
  -> server/Application/Review/Checks/Seed/concurrency/SEED-0004.md
audit refused: land graph pre-check refused
```

The rc cut gate needs `-Check` exit 0 at the pin, so this blocks the cut.

## Two separate faults

1. **Pre-check refusal (fixed on this branch).** `34b0f5c16` added an `EmbeddedResource` of
   `Application\Review\Checks\Seed\**\*.md` to `server/Antiphon.Server.csproj`. The pre-check
   (`tools/Antiphon.LandGraphAudit.Core/LandGraphPrecheck.cs`) refuses any markdown or text document a
   definition-file item selects unless its resolved pattern is on `AllowedDocumentPatterns`. The seeds are
   product data compiled into `Antiphon.Server`, the same kind of input as `server/Bundles/*.md`, which
   is already on that list. The fix adds the exact resolved pattern
   `server/Application/Review/Checks/Seed/**/*.md`. The match is by whole pattern, so a wider glob, a
   sibling directory, another extension or the same text in another project still refuses.
2. **Stale land-graph record (not fixed; needs an operator decision).** Master's recorded manifest
   digest has not matched master's own definition rows since the review-api series landed.

## Timeline (master first-parent order, digests are the first 8 hex of the manifest fingerprint)

| Commit | Committed (UTC) | Tree digest | Recorded | Note |
| --- | --- | --- | --- | --- |
| `a3fa41468` | 11-22 21:15 | F7476276 | F7476276 | last commit where the record matches the tree |
| `bea5d918b` | 11-22 21:15 | 232CCFBE | F7476276 | review-api series: `pr-review.yaml` EmbeddedResource in the server csproj |
| `34b0f5c16` | 11-22 21:15 | 11E7E8FB | F7476276 | review-api series: the seed-check EmbeddedResource |
| `18a1fbb10` | 11-23 00:20 | CD086842 | F7476276 | later batch changes a definition file |
| `f3ac671e2` | 11-23 00:20 | CD086842 | A95B349D | "refresh land-graph records" recorded a digest no master tree has |
| `3aae33ea8` | 11-23 09:25 | 39B06B3A | 5E0AD924 | same again; its message names `-Base fdc57daa4`, a commit not in master's history |
| `c04934e50` | master tip | 39B06B3A | 5E0AD924 | `-Check`: pre-check refusal, then (after the fix) "manifest does not match" |

The review-api series is 35 commits all committed at 2026-11-22T21:15, which is one direct push of a
rebased series. Two of its commits changed a selected definition file and none refreshed the record.

## Why the gate missed it

- **A direct push runs no gate.** Antiphon checks the land graph only on its own land path
  (`delegate.ps1 -Land` and `scripts/land-graph-land-refresh.ps1` before it). A `git push` straight to
  `master` runs no audit: the repository tracks no git pre-push hook, and the GitHub `ci.yml` workflow that
  runs on master pushes builds the client and runs three small test projects, not the land-graph audit.
  It also reports after the push, so it could not have refused it.
- **Later batches classified it as inherited.** Standing rule 11 (`docs/orchestration-loop.md#land-graph-refresh`)
  makes a mismatch the base already carries a warning, not a blocker. `land-graph-land-refresh.ps1` acts
  on that: when the base's rows do not match the record and the batch changed no definition file it prints
  `LAND-GRAPH-NOTICE action=warn reason=inherited-mismatch` and exits 0. Every batch after 21:15 saw the
  mismatch as "inherited", including the pre-check refusal, which a refresh cannot approve.
- **Refreshes were computed before the land rebase.** `f3ac671e2` and `3aae33ea8` recorded digests
  computed on their branches, before landing rebased them onto a master that already carried the
  review-api csproj changes. Nothing re-ran `-Check` on the rebased tree, so each "refresh" moved the record
  to a digest no master commit has. This is why `land-graph-land-refresh.ps1 -Base <merge-base>` cannot
  repair master now: it requires the base's rows to match the record, and none does.

## Should a pre-push or Merge check run `-Check`?

Yes, but only for the hard refusals. A cheap rule closes both gaps:

1. **Pre-check refusals are never inherited.** When `-Check` prints `-Update cannot approve it` at the base,
   a Merge, a Review and the refresh script should file a Debug task at once rather than warn
   (the refresh script already refuses this case with `reason=property-or-construct`, but only for the
   batch's own tree; a base-only refusal is reported as an inherited warning).
2. **Re-check after the land rebase.** The land should run `-Check` on the rebased tree and refresh there,
   so the record matches the tree that reaches master.
3. **A client-side pre-push hook for direct pushes to `master`.** When the pushed range changes a file
   `Test-DefinitionPath` selects, run `audit-land-verification-graph.ps1 -Check` (about 20 s with the
   built evaluator) and refuse the push on a pre-check refusal. A hook only binds clones that install it,
   so it is a guard for operator pushes, not a proof.

These are proposals for a card; this Debug changes none of them.

## Graph classification of a seed change

- Land build: `LandVerificationPolicy` skips a land build only for `docs/**` and the root `README.md`. A
  seed file is under `server/`, so a seed change builds. Witness: the new
  `C1380_NonAllowlistedInputsBuild("server/Application/Review/Checks/Seed/concurrency/SEED-0004.md")` case.
- Test selection: no test names a seed by path, so `tests/derived-impact-index.json` had no edge for one,
  and a scratch commit touching `SEED-0004.md` selected nothing (`kinds: []`, `classes: []`). The new
  `review-check-seeds` row in `tests/impacted-classes.json` maps `server/Application/Review/Checks/Seed/**` to
  `ReviewCheckParserTests`, whose `The_built_in_seed_set_parses` loads every embedded seed. After it the same
  scratch commit selects `kinds: [review-check-seeds]`, filter `/*/*/(ReviewCheckParserTests*)/*` on
  `tests/Antiphon.Tests`.

## Resolution (2026-11-24)

The branch landed on master as `3ef75e8a0..53ab561f3`. On that landed master `land-graph-audit --check`
recomputed `39B06B3A948BF44BF1BAF758201BAC0D00D37A6DDB1A361A4075B9CF6EECD71A` / 80, and the pre-check refusal was
gone. `scripts/antiphon land-graph-audit --update` then re-recorded the manifest and the evaluation record in its own
commit, `8732a7c6b`: digest `5E0AD924...` to `39B06B3A...`, evaluation approved at pass 2, and the SDK line moved
from 10.0.204 to 10.0.404. Earlier records alternate between those two SDKs by host; global.json pins 10.0.204 with
`latestMinor`. The pins did not change. `land-graph-audit --check` exits 0 at that tip.

### A stale master record is re-recorded automatically, never asked about (operator, 2026-11-24)

The operator's ruling: "you should always automatically re-record master's line graph manifest and evaluation file.
That should never be a question and be automatically done." So when master's own land-graph record does not match
master's tree, whoever finds it re-records it (`land-graph-audit --update` on master, as its own commit, then
`--check` to exit 0) and reports the old and new digest, count and pins. It is not a warning to carry and not a
question.

These places still say or enforce the opposite, so the operator can relax them:

- `AGENTS.md` (Cards and tracker): "A bare `-Update` outside that land path stays forbidden."
- `docs/orchestration-loop.md` rule 11 (`#land-graph-refresh`): an inherited mismatch is "a loud warning and continue
  past it ... not a silent refresh", and "A bare `-Update` outside that land path stays forbidden." The same sentence
  is repeated near line 2781.
- `.claude/skills/antiphon-orchestrator/SKILL.md` and `.claude/skills/antiphon-rc-qualification/SKILL.md` repeat
  that sentence.
- `tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs`
  `instructions_say_the_land_path_refreshes_and_bare_update_stays_forbidden` pins the exact sentence in rule 11. A
  doc edit that drops it turns this test red, so change the test in the same commit.
- `scripts/lib/Antiphon.Nightly/LandGraphLandRefresh.cs` (`land-graph-land-refresh`) is the mechanism that leaves
  the record stale. When the base's rows do not match the record and the batch changed no record, it prints
  `LAND-GRAPH-NOTICE action=warn reason=inherited-mismatch` and exits 0 without refreshing. Its header comment also
  says "bare land-graph-audit --update stays forbidden".
- `scripts/lib/Antiphon.Nightly/LandGraphAuditRun.cs` and `tools/Antiphon.LandGraphAudit.Core/LandGraphAuditGate.cs`
  do not forbid it in code. `--update` refuses only while the pins differ from HEAD ("commit source and pins before
  auditing"); with committed pins, a bare `--update` runs, as it did here.
- The orchestrators' standing-authority text, which is not in this repository, carried "bare land-graph -Update is
  forbidden". That text lives in the orchestrator instructions file (`ANTIPHON_ORCHESTRATOR_INSTRUCTIONS`).
