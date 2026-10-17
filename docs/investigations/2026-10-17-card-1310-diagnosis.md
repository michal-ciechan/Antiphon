# CARD-1310: Windows checkpoint receipts are invalid, and the Windows build drops the apphost

Read-only diagnosis. No product code was changed and no checkpoint lane was run.
Branch `feat/card-task-be14f4d6` is `origin/rc/2026-10-15` at `ece7cb2e105cf60b5114034aec226272759ff7cd`.
`origin/master` is `55c2a4adc` (merge of that rc). Both defective commits are ancestors of the rc and of master.

Two cards already exist. Do not file a third.

- CARD-1310 (InProgress) is the ledger refusal that makes a green Windows E2E receipt invalid.
- CARD-1236 (Backlog, filed 2026-10-14) already names both symptoms. Its prescribed fix is the wrong shape: a per-row `needs-apphost` flag, and a special case for one coverage filename. Replace that prescription with the two fixes below. Fold the work onto CARD-1310 or keep 1236 as the duplicate and close it onto 1310.

Fix both on `rc/2026-10-15`. A master-only patch leaves the release candidate broken. Master already contains the same lines, so the rc patch is the one that has to land.

## 1. Green Windows output is `cleanup_refused` because seal adopts nothing off Linux

### What the refusal actually is

`CheckpointOutputLedger.Check` does not compare the JSON ledger. The JSON stores `file-size:` only. Check walks the directory and returns on the first path whose in-memory creation record is missing or different:

```50:52:tools/Antiphon.Checkpoints/Cleanup/OwnedOutputLedger.cs
            foreach (var path in actual.Keys.Append(Ledger))
                if (!SafeWrite.MatchesCreation(Path.Combine(root, path)))
                    return "output-ledger-mismatch; left in place: " + Path.Combine(root, path);
```

`MatchesCreation` is false when no record exists (`SafeWrite.cs:114`). Records are append-only (`CheckpointCreationRegistry.RecordContent` is `TryAdd`, `src/Antiphon.HostCleanup/Checkpoints/CheckpointCreationRegistry.cs:23`).

Seal is the only place a build child can be adopted, and only after the driver observed the build exit (`SourceRunGuard.Write` at `tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs:121-127` calls `OwnedOutputLedger.Seal` at `OwnedOutputLedger.cs:31-34`). Adoption calls `OutputCustody.Stat`. On anything except Linux that returns null, so `AdoptCreated` records nothing:

```52:54:tools/Antiphon.Checkpoints/Cleanup/OutputCustody.cs
    public static Entry? Stat(CheckpointCleanupContext context, string path)
    {
        if (context.Io.Platform != CheckpointPlatform.Linux) return null;
```

```103:106:tools/Antiphon.Checkpoints/Cleanup/SafeWrite.cs
            var before = OutputCustody.Stat(context, full);
            if (before is null || OutputCustody.Refusal(before.Value, window) is not null) return;
            var record = Describe(full);
            if (OutputCustody.Stat(context, full) == before) context.Registry.RecordContent(full, record);
```

That null return is `367c8ee4cdcc877aa71f1a46d30c410d628edc3b` (2026-10-15, "bind S1a0 custody primitives"), already on the rc. The design comment at `OutputCustody.cs:13-14` and the skip at `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1216-1217` say the same thing: off Linux, nothing is adopted. CARD-1242 fixed the Linux case where every green build ended `cleanup_refused` because the child had no creation record (`OutputCleanupTests.cs:144-145`). Windows was left fail-closed.

`OutputCleanup.CleanOwnedOutputs` puts that string on the report (`OutputCleanup.cs:107-108`). Both validators then reject the receipt for the same field:

- `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:26-27` returns `cleanup_refused`
- `scripts/validate-checkpoint-receipt.ps1:70` throws `cleanup_refused`

Review may set `reviewedSourceClean: true` only after one of those validators passes (`docs/orchestration-loop.md:2268-2273`). Evidence-backed land requires true and otherwise returns `review_evidence_source_not_clean`. One missing creation record blocks the land. There is not a second, independent ledger bug.

### Why the named file is `.msCoverageExtensionSourceRootsMapping_Antiphon.E2E`

The file is not a checkpoint ledger entry that was hashed too early. Nothing on Windows writes a creation record for it at all. Check stops at the first unrecorded path, and this file is written into the fresh output directory before the entry assembly.

`Microsoft.Testing.Extensions.CodeCoverage` 18.6.2 (pulled in by TUnit 1.44) runs `MsCoverageExtensionReferencedPathMaps` before `CoreCompile` for the test project and, via `buildTransitive`, for referenced projects. The target is `buildMultiTargeting/Microsoft.Testing.Extensions.CodeCoverage.targets:17-27` in that package:

- `DependsOnTargets="ResolveProjectReferences;InitializeSourceRootMappedPaths"`
- one `WriteLinesToFile` with `Overwrite="true"` (not append)
- path `$(OutputPath).msCoverageExtensionSourceRootsMapping_$(AssemblyName)`
- lines are `FullPath|OriginalPath=MappedPath` for each annotated project reference

Checkpoint confinement passes one global `OutputPath` for the whole graph (`BuildConfinement.Rewrite`, and `run-checkpoint.ps1` `Invoke-ConfinedCheckpointBuild`). Every project therefore drops its own mapping file in the E2E output directory. The E2E file lists the referenced projects' source roots, which is the "42 lines, 14 projects" observation. It is one overwrite per compile of that assembly, finished before `Antiphon.E2E.dll`, which matches "last written before the dll and before the first test row."

The same missing record applies to every other file the build child wrote (the dlls, the other assemblies' mapping files). Allow-listing this filename, or teaching the ledger an "append pattern," makes the next unrecorded path the new refusal. The receipt stays invalid.

### Smallest fix

Adopt build-child files on Windows at seal, after the observed build exit, using the custody window CARD-1242 already defined. Do not special-case the coverage filename. Do not record the file on first create: `TryAdd` would freeze a mid-build overwrite, and this target does overwrite before `CoreCompile`.

`OutputCustody.Stat` (`OutputCustody.cs:52`) has to return an `Entry` on Windows instead of null. `CheckpointNativeIo.Inspect` already reads the handle (`src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:97-105`: link count, size, reparse via attributes, final path). Fill the entry from that:

- regular file `Mode` `0x8000`, directory `0x4000`, and no low permission bits, so `For`'s existing `(dir.Mode & 0x3F) == 0` owner-only check (`OutputCustody.cs:45`) stays in force without inventing a Unix mode
- `Links` from `GetFileInformationByHandle`; `Refusal` already rejects `Links != 1` (`OutputCustody.cs:30`), which keeps Playwright `node.exe` hardlinks refused (the CARD-0987 leftover named on CARD-1310)
- reject reparse points
- `Owner` = `context.Io.Owner` (`uint.MaxValue` on Windows, `CheckpointNativeIo.cs:11`), so the existing owner equality holds
- `Changed` = file creation time, `Modified` = last write time, both inside `[root creation, observed build exit]`
- inode = NTFS file index, only so the current `Inode == 0` fail-closed at `OutputCustody.cs:55` does not throw the entry away; it is not a new trust signal

`AdoptCreated` then hashes the bytes once, and only if a second stat matches (`SafeWrite.cs:105-106`). That hash is the post-exit content, including the coverage map's final overwrite. A write after the observed exit still fails `modified-after-build` / `changed-after-build` and stays unrecorded.

Red-first: `OutputCleanupTests.ExternalBuild` (`OutputCleanupTests.cs:1214`) currently skips off Linux. On Windows, a file overwritten before `Seal(output, exited)` must be removed with the rest of the output, and a write after seal must still be `output-ledger-mismatch` with the tree kept. A second link and a reparse point must still refuse. Do not weaken `OutputCustody.Refusal`.

## 2. Windows build forces `UseAppHost=false`, then the run looks for the `.exe`

CARD-0671's contract is already implemented for the property list, and only off Windows:

- `scripts/run-checkpoint.ps1:538-541` adds `UseAppHost=false` only when `-not $onWindows`
- `tools/Antiphon.Checkpoints/Execution/BuildStep.cs:28-29` does the same
- `RowRunnerTests.windows_leaves_use_app_host_unset` (`tests/Antiphon.Tests/Checkpoints/RowRunnerTests.cs:131-135`) locks the Windows run arguments as having no `UseAppHost` property

The real `dotnet build` then injects it on every platform. Two copies:

```469:470:tools/Antiphon.Checkpoints/BuildConfinement.cs
        if (!hostSeen)
            result.Add("--property:UseAppHost=false");
```

`BuildConfinement.Evaluate` repeats it for the project-reference walk (`BuildConfinement.cs:553-554`). The script copy is `Invoke-ConfinedCheckpointBuild`:

```983:983:scripts/run-checkpoint.ps1
    if (-not $sawHost) { [void]$rewritten.Add('--property:UseAppHost=false') }
```

and the walk at `run-checkpoint.ps1:725`. Those four lines are `fae9040375f79b09ec5f2dad1fe1ab81bbaa9576` (CARD-1199, 2026-10-12), already on the rc.

The run does not go through that rewrite.

- The tool confines only a `dotnet build` (`ProcessDriver.Confines`, `tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs:67-71`). `dotnet run` keeps `BuildStep`'s property list, which on Windows has no `UseAppHost`.
- The script builds through `Invoke-ConfinedCheckpointBuild` (`run-checkpoint.ps1:1076`) and runs through `Invoke-Dotnet` with `$propertyArguments` (`run-checkpoint.ps1:1128`).

`dotnet run --no-build` without `UseAppHost=false` resolves the apphost. On Windows that is `<AssemblyName>.exe`. The build just refused to produce it, so the run fails before any test with the application path missing. That is the CARD-1236 failure ("no `Antiphon.E2E.exe`", row red before tests). The same split is why a Windows row that needs `Antiphon.Tests.exe` (`TestAppHostPath`, and the CARD-1210 child audit) cannot see an exe.

The CARD-0671 offline tests never enter the confined function. A `DotnetShim` takes the other branch (`run-checkpoint.ps1:1075-1078`), so `Test-C585_MsBuildWindowsUnchanged` still sees a Windows build with no `UseAppHost` property. The bug is only the real, unshimmed build.

Explicit `UseAppHost=true` already wins (`hostSeen`). CARD-1236's imported manifest workaround is that override. A new per-row `needs-apphost` flag is not needed.

### Smallest fix

Gate the four injections on "not Windows", using the same predicate as the property default (`$onWindows` in the script, which honors `C671_PLATFORM`; `OperatingSystem.IsWindows()` in `BuildConfinement`):

- `scripts/run-checkpoint.ps1:725`
- `scripts/run-checkpoint.ps1:983`
- `tools/Antiphon.Checkpoints/BuildConfinement.cs:470`
- `tools/Antiphon.Checkpoints/BuildConfinement.cs:554`

Leave the Windows run without `UseAppHost=false`. Passing `false` on the run as well would hide the missing exe and break rows that launch the apphost. Linux must keep the default: the extensionless `fakeclaude` apphost collides with the `fakeclaude/` directory (CARD-0671).

Red-first: a Windows confined build with no caller `UseAppHost` must not pass `--property:UseAppHost=false`, and the matching `dotnet run --no-build` must be given no `UseAppHost` property. The existing shim test does not cover `Invoke-ConfinedCheckpointBuild`.

## Corrected card text

Use this on CARD-1310 (or as the CARD-1236 amendment). Do not open another card.

Checkpoint confinement on Windows does two things that together block an evidence-backed land of any green Windows row, not only Antiphon.E2E.

Seal adopts build-child files only when `OutputCustody.Stat` returns a Linux statx entry (`OutputCustody.cs:53`). On Windows it returns null, `AdoptCreated` records nothing, and `CheckpointOutputLedger.Check` returns `output-ledger-mismatch` for the first unrecorded path. For an E2E output that path is `.msCoverageExtensionSourceRootsMapping_Antiphon.E2E`, written before CoreCompile by Microsoft.Testing.Extensions.CodeCoverage's `MsCoverageExtensionReferencedPathMaps` (`Overwrite=true`, one file per AssemblyName under the global OutputPath). The same missing record applies to the dlls. Both receipt validators turn that into `cleanup_refused`, so Review cannot set `reviewedSourceClean: true` and land returns `review_evidence_source_not_clean`.

Separately, `BuildConfinement.Rewrite` and `run-checkpoint.ps1` `Invoke-ConfinedCheckpointBuild` add `UseAppHost=false` on every platform. The following `dotnet run --no-build` does not. On Windows the SDK looks for `<AssemblyName>.exe`, which the build did not produce, so the row dies before tests. CARD-0671 already leaves `UseAppHost` unset on Windows for the property list; the confined rewrite contradicts it. Explicit `UseAppHost=true` already overrides.

Fix on rc/2026-10-15: Windows seal adopts regular, single-link, non-reparse files created inside the observed build window and hashes them after the build exits; the four `UseAppHost=false` injections run only off Windows. Do not allow-list the coverage filename and do not add a per-row apphost flag. Keep link-count and reparse refusals. Red-first: a file overwritten before seal is cleaned, a file written after seal is kept, a Windows confined build emits an exe and the run is not passed `UseAppHost=false`.
