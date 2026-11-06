# C1714 Windows native boundary verification

Task `211808d0`: native RED reproduced, fixture corrected, exact method GREEN (2/2), and the whole `SettledWorktreeRemovalTests*` class GREEN (82/82, no skips). Only the test and this findings document changed. Production code and assertions were preserved.

## Outcome and limits

Unchanged master `d164e93ae47a79ed175beeb9367212f7a7d5d1d5` failed `under` and passed `over-once-set-aside`. The observed refusal was **`untracked_after_proof`**, different from the supplied live qualification's `worktree_removal_incomplete`. A diagnostic-only rerun at `b206b9feed6b818c4589202fa662432f5e0aaee1` printed:

```text
shape=under; path-length=399; residue=untracked_after_proof; detail=
```

`Detail` was empty and **no native error code was emitted**. Source inspection places this refusal in the late tracked/ignored-name audit before deletion. It is not evidence of error 3, 32, or 206, and this task cannot supply the historical live run's missing delete errno. The baseline over shape passed its pre-move refusal and byte/registration/branch preservation assertions; its Detail was not logged in that baseline run. No Defender/indexer or load-only diagnosis is established.

The fixture injected 400 although `WorktreePathLimit.WindowsMaxPath` is 260. Changing that one boundary produces 259 after move-aside for `under`, and 260 for `over-once-set-aside`. At tested fix `7a957782c267bc888d0c1ea53a51ad3bbf0e63c6`, native output was:

```text
shape=under; path-length=259; residue=; detail=
shape=over-once-set-aside; path-length=260; residue=worktree_path_too_long; detail=path: <260-character future set-aside file path>
```

The latter line abbreviates the fixture-owned absolute path; raw output is retained in the ignored evidence logs. Both cases passed. This confirms the test fixture's invalid native boundary through a boundary-only red/green correction; it does not reconstruct the original live-run syscall failure.

## Native evidence

Host `DESKTOP-KTLKPIF`, Intel i7-6700K, 8 logical processors; Windows 10.0.19045 win-x64, SDK 10.0.300, .NET 9.0.22, TUnit 1.44 / MTP 2.2.2. Observed CPU load: 77% initially, 94% at the first two builds, 99-100% during compilation/exact runs, 79% at class start. Initial build admission waited 758 seconds at the 6144 MB memory floor (reported available memory 4205-6024 MB during the wait). All later admissions waited 0 seconds. Every driver used its own granted build slot, capped at four workers for builds.

| Build source | Build duration | Outer wall including admission | Errors / warnings |
| --- | --- | --- | --- |
| master above, unchanged | 14m06.08s | 27m07.86s | 0 / 1803 |
| diagnostic-only `b206b9fee...` | 11m49.33s | 11m56.17s | 0 / 1803 |
| fixed `7a957782c...` | 5m19.34s | 5m27.51s | 0 / 1751 |

One incremental build was made per source state; the diagnostic state was necessary to expose the missing Detail. The final build used `--no-dependencies` to reuse unchanged dependency outputs built at the diagnostic commit. All production sources are byte-identical across these commits. Native default `bin/` and `obj/` outputs and `UseAppHost=false` were used throughout.

| Run | Passed / failed / skipped | Runner duration | Outer wall | Under / over case seconds |
| --- | --- | --- | --- | --- |
| unchanged master RED | 1 / 1 / 0 | 37.749s | 51.370s | 10.550244 / 1.999784 |
| diagnostic RED | 1 / 1 / 0 | 39.989s | 50.351s | 14.675321 / 4.536226 |
| fixed exact method GREEN | 2 / 0 / 0 | 34.703s | 44.424s | 11.534709 / 3.739461 |
| fixed whole class GREEN | 82 / 0 / 0 | 4m26.634s | 4m33.591s | 6.207888 / 6.190865 |

Class p95 case duration was 8.879140s; maximum 12.912088s (one loaded-host sample, provisional). No existing per-test deadline, retry, or assertion was relaxed. Operational bounds were 15 minutes for slot admission, 15 minutes for test execution, 32 minutes for the outer test wrapper including admission/teardown, and 35 minutes for each outer build wrapper. These brief-requested generous bounds are not measured-p95 claims. Each run shared one PostgreSQL container and the existing snapshot fixtures.

Selections were exactly:

```text
/*/*/SettledWorktreeRemovalTests/C1714_Path_past_the_delete_limit_refuses_before_the_move
/*/*/(SettledWorktreeRemovalTests*)/*
```

No full-suite or Unit lane was run: this Custom brief expressly requested the method and one native class run. Linux was not rerun. Debug `14afd1ae` already reported the original class at this master: 81 passed, 1 Windows-junction skip, 46.496s; that is historical Linux evidence, not execution of this corrected fixture there.

TRX SHA-256 before required result cleanup:

```text
red        E95152215E0EEA323B1BB0165708CEDF231D6F80936610751FD8239DCD3CCA3C
diagnostic 7AF9EF9C81498631FD27ADE053706D40A6ABF64134F57795A11538549C9210F3
green      872D5AC284C6E5CDC79BA44F4AC6648060425E7FB6E8C46B17F189802316CDE0
class      87A73C16FFC8603427885240A739E1E2324D48FB84D076E4F16F5C82FFCC0E6F
```

The exact and class GREEN assembly MVID is `f115ebf7152b488588f1e4bfa25bf235`. Ignored `.antiphon/native-211808d0/case-results.json` retains all 88 per-case outcomes/durations, source/host/category and original TRX hashes. The adjacent logs and `phases/` JSONL retain run/group/MVID phase provenance. The recorder's external-span cap was reached; uninstrumented/dropped phase work remains unknown/remainder, not zero. These receipts do not claim full rc qualification.

## Exact test diff

```diff
-        const int limit = 400;
+        const int limit = WorktreePathLimit.WindowsMaxPath;
@@
         var result = await h.RemoveAsync();
 
+        Console.WriteLine($"shape={shape}; path-length={file.Length + growth}; residue={result.Residue}; detail={result.Detail}");
         if (shape == "under")
         {
-            result.IsClean.ShouldBeTrue(result.Residue);
+            result.IsClean.ShouldBeTrue($"{result.Residue}; {result.Detail}");
```

Rerun from the assigned worktree, with TEMP/TMP in a task-owned directory as in the retained run metadata:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1714-build -SlotWaitMinutes 15 -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1714-method -SlotWaitMinutes 15 -- dotnet run --project tests/Antiphon.Tests --no-build --property:UseAppHost=false -- --treenode-filter '/*/*/SettledWorktreeRemovalTests/C1714_Path_past_the_delete_limit_refuses_before_the_move' --timeout 15m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1714-class -SlotWaitMinutes 15 -- dotnet run --project tests/Antiphon.Tests --no-build --property:UseAppHost=false -- --treenode-filter '/*/*/(SettledWorktreeRemovalTests*)/*' --timeout 15m
```

The caller's existing Debug authority covered proceeding without another approval. No live qualification path/process, rc/master push, land, deployment, or runner restart was touched. Cleanup and final Git checks are recorded in the handoff.
