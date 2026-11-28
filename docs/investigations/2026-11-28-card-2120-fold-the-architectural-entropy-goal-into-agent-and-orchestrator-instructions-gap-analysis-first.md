# CARD-2120 | Architectural entropy evidence

The standing instructions already prescribe layer direction and staged verification. The missing enforcement is at the handoff between architecture decisions and migration evidence. Plan records alternatives and touched files (`server/Bundles/stage-plan.md:5–6`), Code executes the plan's rows (`server/Bundles/stage-code.md:1–5`), and Review audits those rows and evidence (`server/Bundles/stage-review.md:1–6`). None of those entry points asks for an authoritative implementation, a census of the old callers, or evidence that the replaced path was removed. `docs/project-context.md:118–126` rules out several patterns but leaves the trade-off for new abstractions implicit. The [gap analysis and proposal](2026-11-27-architecture-entropy-prompt-gap.md) traces the original prompt item by item.

## Provenance

- Inspected master `60e6b9c3b260762c7e6a7ce4448449d77dd945c7` and the assigned worktree base `263a2880dde33eac23b542b133baba0c87008ac7`. Master line numbers govern where these differ. All evidence is from stored source or read-only card responses. No product build, TUnit run, production endpoint calls for Review engine data, or database query was needed for these measurements.
- `scripts/antiphon card search 'architectural entropy' --board Antiphon --all` returned one match, `CARD-2120`. `scripts/antiphon card get CARD-2120 --board Antiphon` returned the operator's 2026-11-27 request and the original prompt, revision count 2. The board ID is `8988ca03-7414-47ad-b0b6-51556c701703` and card ID is `2ce0c464-fa17-4de6-95f1-d249ac4de7e7`. The board read supplied the proposal requirements. Repository evidence supplied current coverage.

## Prompt composition and competing limits

`InstructionBundles.cs:327` reads the resource, normalizes line endings to LF, and trims. The local values can be reproduced in PowerShell with `$sha` set to the surveyed master SHA and `$bundle` set to the bundle key.

```powershell
((git show "${sha}:server/Bundles/$bundle.md") -join "`n").Trim().Length
```

| Bundle | Normalized master units | Relevant cap | Modeled argv units with board API and explanatory style |
| --- | ---: | ---: | ---: |
| Orchestrator | 11,498 | 11,500 | 27,960 |
| Plan | 1,975 | 2,500 | 18,435 |
| TestDesign | 3,868 | 10,000 and 2,500 | 20,335 |
| Code | 2,476 | 2,500 | 18,936 |
| Review | 2,580 | 2,600 | 19,042 |
| Mutation | 2,439 | 2,500 | 18,903 |
| Investigate | 701 | 2,500 | 17,168 |

The C1332 source reconstruction used `InstructionBundles.cs:30–33`, `:233–289`, `InstructionFragments.cs:26–29`, `InstructionBundleComposer.cs:63`, `:122–140`, and `ChannelPreamble.cs:41–43`, `:60–81`. The worst case joins the rendered orchestrator, delegate basics with one expanded git-common fragment, board API, explanatory style and Telegram preset with four two-unit separators. Each bundle header and fragment header uses an eight-digit version. The CRLF Telegram preset is 2,386 UTF-16 units. The composition is 27,836 units. The six fixed example arguments plus `--append-system-prompt` overhead add 124 units using `tests/Antiphon.Tests/Application/InstructionBundleTests.cs:679–702`, `:765–766`. This statically reconstructs 27,960 against 28,000. `OrchestratorBundleOwnershipTests.cs:284–309` separately gives the 11,500 body limit, leaving two units. The brief's previous values are useful context for the constraint. The table records the measured source revision.

**Confirmed mechanism for the TestDesign limit conflict.** The stage-cap test now selects 10,000 for TestDesign (`tests/Antiphon.Tests/Application/InstructionBundleTests.cs:924–941`). An independent loop still includes TestDesign and applies 2,500 to every stage except Review (`tests/Antiphon.Tests/Application/ScopedVerificationInstructionTests.cs:124–136`, `C487_G141`). Both call `InstructionBundles.TextOf`. The same normalized 3,868-unit blob fits the first bound and exceeds the second by 1,368. This is a deterministic source-level witness, with no claim about the current shipping binary or the most recent rc results. `card search 'C487_G141' --board Antiphon --all` returned zero at the time of the read.

The proposal increases only Plan by 69 units, including the added separator, so its modeled row is 18,504 and its ASCII body is 2,044. The orchestrator row remains the maximum and its raw SHA pin (`server/Bundles/README.md:105–109`) still refers to the same content. The AGENTS blob is 22,615 UTF-8 bytes after the new row against a 24,576-byte delivery target (`tests/Antiphon.Tests/Scripts/AgentContextCheckTests.cs:45`). `git apply --cached --check` passed on a temporary index seeded with the surveyed master. Applying the five-file patch in that index produced the same draft tree as generation, `69c88f4daeec2686a50618436efb60c1daddc0d1`.

## Other repository witnesses

| Witness | Reconstructed evidence | What remains uncertain |
| --- | --- | --- |
| Review engine route and docs drift | `server/Program.cs:1476–1477` registers the two feature endpoint maps. Counting `MapGet`/`MapPost` in `ReviewRunEndpoints.cs` and `ReviewAuthEndpoints.cs` gives 25 mappings. Counting method rows in `docs/features/012-review-engine/api.md:167–194` gives 24. `ReviewRunEndpoints.cs:47` registers `GET /api/review-runs/{id}/packs/{iteration}/{group}` and `tests/Antiphon.Tests/Review/ReviewDelegationRunnerApiTests.cs:123` calls it. The route appears in design prose at `docs/features/012-review-engine/design.md:953` and has zero rows in the API contract. `git ls-tree -r --name-only` on the surveyed SHA returned zero paths named `ReviewEngineRouteDocumentationTests`. | The in-progress `CARD-2107` (GH-60) names this exact omission. The current working tip and guard scope of that card were outside this survey. |
| Retired scripts and active CLI | The same `git ls-tree` returned zero paths for `scripts/review-standalone.ps1` and `scripts/review-github-setup.ps1`. `scripts/lib/Antiphon.Scripts/ReviewEngineCommands.cs:2`, `:20–21` names the replacements, while `docs/features/012-review-engine/getting-started.md:346`, `:356–358`, `:379–383` still tells readers to invoke the removed files. `card search 'review engine route' --board Antiphon --all` returned `CARD-2101` and `CARD-2107`. | The recorded mismatch belongs to the surveyed tip. The existing issue owns the repair. |
| Timing journal writers | The nightly/CLI writer implements the v1 path, path-component checks, event identity, duplicate producer completion suppression, and append under a named mutex (`scripts/lib/Antiphon.Nightly/TestTimings.cs:141–199`). The checkpoint journal shares those invariants at `tools/Antiphon.Checkpoints/Timing/TimingJournal.cs:25`, `:75–123`, with a spool, per-shard semaphore, sequence and durable flush. `TimingRowRecorder.cs:39–51`, `:88–105` uses the checkpoint journal. `NightlyTests.cs:767–791`, `:891–895` uses the nightly writer through a best-effort wrapper. `tests/Shared/TestTiming.cs:356–369` records a separate per-case phase journal. | No live journal was opened. Shared format and overlapping rules establish duplication candidates. The custody, root, identity and concurrency differences require caller evidence before any change. The exception suppression in the small wrapper is real behavior. |

## Not done, noted

Use a single authoritative stage-size policy for the conflicting TestDesign checks in a separately commissioned card.
