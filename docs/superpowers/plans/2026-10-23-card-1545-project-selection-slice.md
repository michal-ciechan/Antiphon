# CARD-1545 first slice: advisory project selection

Direct Code brief, original landing owner 2dbadf60-f236-4086-ad24-58a6cd57ea1e.
Base c51023b99b3fcccd222ddbf8137de8c203e04760. No earlier CARD-1545 plan exists.

Implement changed paths -> owning projects -> reverse ProjectReference closure
at both pinned commits -> whole test-project batches. Explicit project/category
inventory distinguishes messaging, server, Claude, Codex, shared pty and git
operations. Mixed projects retain every category and their whole batch: splitting
Antiphon.Tests or the git handler is deferred. Exact linked source consumers join
the closure. Literal imported project files are traversed; unresolved graph inputs,
cycles, unknown ownership, shared build/scripts/policy changes select Full.
Record direct references and each project's transitive downstream closure, batch
categories, source SHAs, reasons and deterministic digest. This is advisory only:
release execution and coverage remain Full; no baseline reuse or scoped gate credit.
Standalone selector plus additive projectProposal on release selection intent.
No AppHost/restart, checkpoint-tool, fixture, or timeout-policy changes.

## Performance budget

Three isolated tiny Git fixtures, one per behavior; no DB/container/web host.
Each Git read has the existing 30-second bounded process runner. One test host,
existing ScriptHarness process ownership and 300-second deadline, no new timeout.
Provisional expected total <12 minutes including two builds, test bodies <25 seconds each;
no p95 sample yet. Record native TRX durations, checkpoint phases and slot wait;
fixture IO and Git times not instrumented individually remain unknown/remainder.
CARD-1545 tracks the provisional sample; no selection >30 minutes is authorized.
The red and green rows necessarily build different committed source; one build per
row, existing mode regression uses the green build. No full assembly or Unit lane:
the direct brief explicitly narrows ordinary testing to these behaviors.

## Verification design

S1 commits executable defect witnesses before implementation (safe Full-only skeleton).
S2 implements engine, inventory, manifest and release intent integration.
V-1: exact messaging closure, batch/category coverage, manifest closure/determinism.
V-2: unknown path, shared input, dynamic reference and missing category fail Full.
V-3: linked consumers, old references, deleted ownership and cycles.
R-1: existing release modes continue executing Full, unqualified credit refused.
Manual V-4: real committed repository graph/census and sample selection.
All ordinary IDs above required. PC-1 downstream edge omission, PC-2 unknown-input
fallback omission, PC-3 linked/old graph omission and PC-4 release Full override
remain pending for method-scoped SourceLanding Mutation, including discovery.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1545-red/` | red-witness | `/*/*/RcProjectSelectionTests/*` | V-1,V-2,V-3 | all 3 methods, expected assertion failures before S2 | 3 | 6 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1545-green/` | project-planner | `/*/*/RcProjectSelectionTests/*` | V-1,V-2,V-3 | all 3 methods, 0 failed/skipped | 3 | 6 |
| CP-3 | S2 | CP-2 | release-modes | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | R-1 | 1 method, 0 failed/skipped | 1 | 4 |
| CP-4 | S3 | n/a | consumer-red | `pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-consumer-red` | V-5 | 4 assertion rows, expected missing-consumer/batch failures before S4 | n/a | 1 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1545-final/` | final-planner | `/*/*/RcProjectSelectionTests/*` | V-1,V-2,V-3,V-5 | all 4 methods, 0 failed/skipped | 4 | 6 |
| CP-6 | S5 | n/a | path-red | `pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-path-red` | V-6 | 4 assertion rows, glob-consumer must fail on literal bracket name before S6 | n/a | 1 |
| CP-7 | S6 | n/a | path-green | `pwsh -NoProfile -File scripts/test-rc-project-selection.ps1 -Case C1545_ConsumerSafety -ResultsDirectory .antiphon/c1545-path-green` | V-5,V-6 | 4 passed assertion rows, 0 failed | n/a | 1 |

Measured first red checkpoint: build 266.90 s, host 21.16 s, three intended
assertion failures, no skips. This single sample is not p95. The build plus
source-boundary checks exceeded the initial 3-minute estimate; revised to 6 minutes
per building row without changing any test timeout.

S3 adds a direct script red witness for V-5 (literal and wildcard sibling content,
imported references, and missing TUnit batch census), then S4 repairs it. The same
case runs under TUnit in CP-5. This avoids an extra build merely to execute the
PowerShell defect witness. PC-5 literal/glob content omission and PC-6 missing
TUnit batch acceptance remain pending for Mutation, including imported-reference
and historical-graph variants of PC-3. Old-edge assertions now inspect both literal
graphs, not only the changed project's already-selected consumer.

CP-3 R-1 is red at 0f2f2ce7e803105d6a016d64fe194c0bfdf4c8f7:
missing dispositions for Antiphon.TestTiming.Probe and c1375-probe. A one-time
unlisted exact base harness probe at c51023b99b3fcccd222ddbf8137de8c203e04760
reproduced the same four mode failures (3 passed / 4 failed assertion rows). It ran
under a granted slot (wait 0), using the unchanged FilterHost from the verified
CP-2 build; no base build was necessary for the PowerShell policy failure. R-1
remains failed/inherited; no Unit lane, assembly, policy repair, or timeout change
is substituted. The caller must commission the policy owner's repair and rerun
that exact row before Review/land. The final row closes only the planner behaviors.

CP-4 at cdecd8bd3b6e55a4977b4489014061913bc744e3 executed the four
V-5 assertion rows: imported-reference passed; literal content, wildcard content
and batch census failed as intended. S4 adds those consumer edges and a TUnit
census with reasoned harness-only dispositions. Imported item paths resolve
relative to the consuming project, while explicit MSBuildThisFileDirectory and
Import paths resolve from their declaring file. No qualification policy changed.

S5 strengthens C1545_ConsumerSafety's glob-consumer guard with a literal bracketed
filename. V-6 requires that consumer as well as the wildcard consumer. S6 replaces
the PowerShell wildcard interpretation with escaped, bounded regex matching of
MSBuild's star/question-mark patterns; brackets remain literal. Only this existing
PowerShell method body/matcher changes after CP-5, so CP-6/CP-7 run that exact body
directly without recompiling its unchanged TUnit wrapper. PC-7 is the bracket-path
variant of C1545_ConsumerSafety, pending SourceLanding Mutation. CP-5 passed all
four methods at 257acdc2d453857e2c01b87f5598391508795ba1, clean/verified build.
