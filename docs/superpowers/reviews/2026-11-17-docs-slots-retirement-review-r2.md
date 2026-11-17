# CARD-1514 docs review, round 2: D-1 and applied advisories

Review task `8d32fab2-7cd0-4180-ae6d-6ba66c7300fd`. Diff-only, blockers only. Documents were not edited. The only commit on this review branch is this file.

Subject task `83d30eec-6df9-40dd-ae62-050bb29e116b` (Docs), branch `feat/card-task-83d30eec`, tip `3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`. One commit on top of the round-1 reviewed tip `12105bfbc2e0f472614e51a96adc4240b2384a32` (`origin/feat/card-task-d336fcc3`, Docs task `d336fcc3-e8ba-4433-84b0-8f5e1dbffdcd`). The two-dot diff changes only `docs/docker-stack.md` (+12/−10). Round-1 range `61259342c25c9960c12ac7171488e52b61dc03be..12105bfbc2e0f472614e51a96adc4240b2384a32` plus this commit is `61259342c25c9960c12ac7171488e52b61dc03be..3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`.

`origin/master` is `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`. Code claims were read from that tip. The worktree copy of `scripts/c1008-retirement.sh` is behind master and was not used. `scripts/c590-remote.sh`, `RunnerWorkspaceSlotLedger.cs`, and `RunnerWorkspaceSlotPool.cs` match `origin/master`.

## Verdict

**Clean.** D-1 is fixed and the new sentences match the validator on `origin/master`. Applied advisories A1, A4, and A6 are factually right. No other hunk. No new blocker. Round-1 advisories A2, A3, A5, A7, and A8 were not part of this diff; they still do not hold the land.

## D-1. Fallback receipt post-check

The new post-removal text says the fallback leaves `retirement/<operation-id>/receipt.json` in the state the failed attempt wrote; confirm `Refused` with zero effects and zero intents, or `Completed` with one recorded effect per target; `Applying`, `Partial`, `PartialRefused`, or `Refused` with any effect or intent needs audited `-Close` after absence is proved; the fallback does not mark a sanctioned Apply Completed.

That matches the code:

- The fallback commands are unchanged: four `docker volume rm --` lines, one exact name each. They do not write `receipt.json`.
- `c1609_close_receipt_valid` (`scripts/c1008-retirement.sh` 1331–1366) accepts `Completed` only when `consumed==true` and every target has an effect (1363–1364). The four targets are the fixed temp set.
- `c1609_mark_completed` (965–976) is the only writer of `.state="Completed"` (975). It requires `Applying`, `consumed==true`, no pending intent, and one effect per target, then sets `consumed=true`. Its only caller is the sanctioned temp Apply loop in `scripts/c590-remote.sh` (9678). The manual fallback cannot produce `Completed`. A `Completed` receipt the fallback leaves behind already had one effect per target.
- `c1609_temp_retirement_clearable` (`scripts/c590-remote.sh` 9893–9951) calls `c1609_close_receipt_valid` at 9929 before the state case. `Ready|Refused|Pending` pass only with zero effects and zero intents (9941–9942). `Completed` passes when the validator passed (9944). `Applying|Partial|PartialRefused` return `TempRetirementUnresolved` (9945).
- `c1609_close_host` (1239) requires that validator (1250), refuses a live owner, and requires all four volumes already absent (1286–1288) before it writes `closure.json`. It does not remove a volume.
- A refused Preview writes no `receipt.json`. `c1609_write_refused_preview` (714–723) calls `c1609_copy_preview_rows` (125–144), which writes `proof.md`, `findings.jsonl`, and optionally `audit.txt`. With no receipt, clearable requires `readyPublished==false` and no `closure.json` or `close-pending.json` (9923–9927), plus a valid finished lifecycle and a dead owner.

## Applied advisories

- **A1.** Attention rows are `WorkspaceSlotAttention`: `Key`, `SlotId`, `Path`, `Detail`, `At` (`RunnerWorkspaceSlotLedger.cs` 81–88). `JsonSerializerDefaults.Web` (109) writes `key`, `slotId`, `path`, `detail`, `at`. `RunnerWorkspaceSlotPool.Attention` (1342–1343) fills those five fields. The new prose names those fields. The existing `jq` line is unchanged.
- **A4.** `c1609_phase_refusal` (1008–1016) prints `recovery=-Close` when the canonical receipt is the same operation, `consumed==true`, and state is `Applying` or `PartialRefused`. Otherwise it prints `recovery=new-operation`. The new resume sentence says that, and it still requires Close's absence, census, and custody checks.
- **A6.** The sentence now says `batch 253`, not `Merge 253`. `870a93d21f2b18c85a8b0905ab689b28e3099813` is `test: regenerate impact index on the batch 253 tree`. `6905ae9c5dbb46d7412d57323e37e19eddcd9c10` is its ancestor (three first-parent commits) and is on `origin/master`. The repair SHA in the sentence is unchanged.

## Diff safety

Four hunks only: A6, A4, D-1, A1. `git diff --check` on `12105bfbc..3d1fb98c` exited 0. The added lines add no command. No wildcard, `rm -rf`, `git clean`, secret printing, or delete beyond the four existing exact volume names.

## Verification

No product file was modified.

`scripts/check-evidence-diff.ps1 -BaseRef 12105bfbc2e0f472614e51a96adc4240b2384a32 -HeadRef 3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`: `EVIDENCE result commits=1 entries=0 violations=0`.

Full reviewed span `61259342c25c9960c12ac7171488e52b61dc03be..3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`: `commits=4 entries=0 violations=0`.

`scripts/impacted-classes.ps1 -MergeSubset -Range 12105bfbc2e0f472614e51a96adc4240b2384a32..3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`: `mode=merge-subset`, `migrations=False`. The downgrade selector was not selected (0 migration rows). The printed fixed classes are the always-on merge-subset set. This docs diff does not re-run them. No `server/Migrations`, `AppDbContext`, or `*Model.cs` row is in the subject diff.

`git merge-tree --write-tree origin/master 3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b` exited 0. Tree `c5c482f9c78dbc4209dd7dd1c35879a339251274`.

OS-branch witnesses: the diff is prose in `docs/docker-stack.md`. No OS-selected production behavior changed. `scripts/check-os-witnesses.ps1` was not owed. No new input wait. Parking stays dormant (CARD-1083). Positive controls stay pending under the operator waiver of 2026-11-12.

Platform reads, both HTTP 200, with no fleet location recorded: `GET /api/runner-defaults` keys `revision`, `globalRunnerId`, `kindDefaults`, `updatedAt`, `lastReason`, `lastProvenance`, `lastCallerTaskId`, `supportedKinds`, `unresolvedReferences`; `GET /api/session-runners` returned an array of three runner objects. `-Runner` and `-Platform` are omitted.

`DockerStackDocumentationTests` was not re-run. The changed sentences are not pinned by that class (`PartialRefused` remains, and round 1 already recorded that the old zero-effects sentence was not pinned). The brief runs that class only when a claim is in doubt. These claims were read from `origin/master`.

One-minute load before the checkpoint was 9.65. The named guard batch ran once, leased inside `scripts/run-checkpoint.ps1`, at `3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b`, `UseAppHost=false`, `TEMP=/tmp`. Cold output `bin-r2/` is the checkpoint driver's required path.

CHECKPOINT R2-guards commit=3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b build=ok filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/task-8d32fab2/.antiphon/checkpoints/R2-guards-20261117-020151-c2a8/run.trx slot=granted waited=0s dirty=0 source=3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b sourceState=clean buildSource=verified

Build inside that row: 0 errors, 2145 warnings, elapsed 00:13:09.62. Tests: total 53, failed 0, succeeded 53, skipped 0, duration 51s 027ms. Lease `c6ab0a15-a0d3-4cb9-86c2-f0aae8218a5a`, waited 0s, held 940s. `scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/R2-guards-20261117-020151-c2a8/source.json -ExpectedSourceSha 3d1fb98c8b90abdcd2476f34e0bf07f1646cc43b -Rows R2-guards` printed `CHECKPOINT SOURCE VALID` and exited 0. Start and end fingerprints match; `dirtyFiles=0`; `state=clean`; `buildSource=verified`.

`reviewedSourceClean` is true.
