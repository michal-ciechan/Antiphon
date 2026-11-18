# CARD-1986 Code review, round 1 (Final/Full)

Subject: origin/feat/card-task-ee23f2a2 at 8e163069bcfcdd0cf5c5610e7bfa9f6994a2f309 (Codex author), base/master e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4.
Reviewer task: aaf353cd (Claude Opus). Verdict: **CLEAN**. No blockers or regressions; advisories below go to cards.

## Safety analysis (retire-temp)

- The server-side temp audit (`scripts/c590-remote.sh` `fetch_origin` -> `c1609_unbundle_host_proof`) uses only the proof bundle's heads/salvage tips as comparison roots and refuses any volume tip not reachable from them. Narrowing the proof to covering refs can only remove comparisons, so the audit can refuse more, never accept more.
- A volume branch whose tip exists only on one origin branch: that branch is the selected cover. If it is deleted, rewound or force-pushed, the renewal (`Assert-C1986PublicationRefs` over dependencies selected in the OLD graph) and the live control check refuse `RecycleOriginMoved reason=withdrawn-ref ... new=deleted|<sha>`. If it was never published, `Select-C1986DependencyAdvertisement` throws `unpublished-commit`. Witnessed by CP-3 (delete/rewrite/tag).
- A fast-forward is accepted only after `rev-list --count old ^new` = 0 in a scratch that holds the previous bundle's objects and the newly fetched tip. The live path also pins the fetched tips to the ls-remote values and rechecks them with a second ls-remote (`-Exact` both sides). If the old object is missing or Git fails, the check fails closed.
- Tags and salvage refs keep exact-object semantics. Every tag is a dependency, and a salvage ref can only be deleted, which refuses.
- The cover traversal is correct. `rev-list --topo-order --parents` lists children before parents, so each commit's minimum rank is final before it is used. Master and main rank first.
- The diagnosis override in `Write-C590Result` applies only when the result is not accepted, the exit is 2, the diagnosis is generic and the variable matches the closed grammar. It changes text only and never the verdict. Both proof entry points reset the variable.
- `scripts/c590-real.ps1` is untouched (CARD-1989 owns it). The live check still ends with `Get-C1986Remaining` against the caller's 5 s control budget.

## Evidence (reviewed SHA 8e163069bc, server2, native default output, UseAppHost=false, every driver under build-slot)

| Row(s) | Selection | Result |
|---|---|---|
| build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` | exit 0, 0 errors, 222 s lease |
| CP-1..6, 9, 11 | `/*/*/VolumeRetirementHostProofTests/*` (whole touched class) | 29 total, 29 passed, 0 failed, 0 skipped, 2m54s |
| CP-7, 8, 12 | `VolumeRetirementPublicationTests/(Salvage_withdrawn_after_staging_refuses_control*)\|(Head_rolled_back_after_staging_refuses_control*)\|(Host_origin_salvage_proves_exact_required_objects*)` | 3 total, 3 passed |
| CP-10 | guard union (11 classes as in the plan) | 115 total, 114 passed, 0 failed, 1 skipped (`C1210_Windows_linux_only_outcomes_are_exact`, Windows-only by design) |
| red replay | `C1986_*` with test file identical at 024b404585 and the three scripts restored to 024b404585 bytes (same build; scripts are runtime-read), then restored | 10 total, 9 failed, 1 passed (`(new)` control): matches the plan's claim |
| check-evidence-diff | e229fb4d3..8e163069bc | commits=8 entries=0 violations=0, exit 0 |
| merge-tree | origin/master e229fb4d3 vs tip | clean, tree 67d4682a26ce5a5912e009060939d6bac240f4dc |
| cost probe | cover traversal over this mirror's 5,409 unique origin tips / 30,141 commits | 2.7 s total (git 1.0 s). This runs in the 600 s proof writer, not in the 5 s live path. |

Process note: the whole-class run started at load1 27.3 because the load gate was not read before it. It finished green. Every later run was gated (11.3, 11.2, 6.6).

PCs remain pending (mutation waived by the operator on 2026-11-12).

## Advisories (card them, do not hold the land)

- **A-1, the live fast-forward path at fleet scale.** When a depended head moves, `Assert-C1609LiveOriginProof` unbundles the entire proof bundle inside the 5 s control budget. Master is the preferred cover and moves on every land, and this origin's pack is about 275 MiB. A master fast-forward between the last renewal and a control request will therefore probably hit `deadline` and report `RecycleGitAuditUnknown` instead of passing or reporting `withdrawn-ref`. This fails closed, and the base refused any change anyway, so it is not a regression. However, the card's live-window fast-forward acceptance and its clear diagnosis are only witnessed on tiny fixtures. Measure an unbundle at fleet size, or prefer a stable cover that is not moving.
- **A-2, producer-to-recipient diagnosis.** CP-4 sets `$script:C1986OriginMovedDiagnosis` and calls `Write-C590Result` directly. No test drives a real `withdrawn-ref` from the proof writer or live check through the `c590-real.ps1` catch sites (including the `RemoteResultMissing` transport path) into `c590-result.json`.
- **A-3, renewal reuse is now dead code.** After narrowing, the old pruned advertisement never equals the full `before` on a multi-ref origin, so every renewal fetches again, incrementally over the prior bundle. The CARD-1693 reuse comment no longer describes real behaviour.
- **A-4, worst case of the tree-cover loop.** Each unadvertised required tree runs one full-history `rev-list --objects` per head, in name order, until a cover is found. On 6,800 refs this can be thousands of walks. It is bounded by the 600 s budget and fails closed, and tree tips are rare.
- **A-5, liveness gaps.** Every origin tag is a dependency, so deleting or moving an unrelated tag refuses. The "v2 requires a head" fallback picks the first head by name, which may be an ephemeral task branch, so deleting that branch refuses.
