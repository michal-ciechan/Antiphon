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
Provisional expected total <5 minutes including build, test bodies <10 seconds each;
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
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1545-red/` | red-witness | `/*/*/RcProjectSelectionTests/*` | V-1,V-2,V-3 | all 3 methods, expected assertion failures before S2 | 3 | 3 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1545-green/` | project-planner | `/*/*/RcProjectSelectionTests/*` | V-1,V-2,V-3 | all 3 methods, 0 failed/skipped | 3 | 3 |
| CP-3 | S2 | CP-2 | release-modes | `/*/*/ChangeAwareReleaseTests/C1313_ModeCredit` | R-1 | 1 method, 0 failed/skipped | 1 | 1 |
