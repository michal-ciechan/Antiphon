# CARD-2005 Astra fixes A: Review round 1 (Final, clean)

Review task `5c4b0d4e`. Subject `feat/card-task-a994b73f` at `fa3804684a0d9ed321203298951d421e1056b5a6`
(Code owner `a994b73f`, Grok author). The merge base with master is `a12ff2eefdbcec889353802f50fb11a76a06390c`.
The brief's range starts at `d86d0f39422b152faf8c39e362a58b745eb95267`. The five commits between that base and
`a12ff2eef` are already on master `eb31e339203dd7432d1d8b7be129b6e3728591c8`. The reviewed diff is the four task
commits: `640cdfb29`, `ea9e076e5`, `93ec1faa5`, `fa3804684`.

Verdict: **CLEAN**. There are no blockers and no regressions. The advisories below should go to cards and do not
hold the land.

## Checkpoint rerun (one checkpoint-tool run)

Run `20261119-164756-de97` used plan `docs/superpowers/plans/2026-11-19-astra-fixes-a-checkpoints.md` with
`--expected-source-sha fa3804684a0d…`. The result was `verdict: GREEN exit=0`, and
`validate` printed `CHECKPOINT SOURCE VALID source=fa3804684a0d… rows=12`. Every row shows `slot=granted`,
`sourceState=clean`, and `buildSource=verified` or `notApplicable`.

| CP | Result |
|---|---|
| CP-1 DiscoveryCacheAgrees | exit 0; `C487: 2 passed, 0 failed, 2 rows` |
| CP-2 DiscoveryParserTolerance | exit 0; `C487: 6 passed, 0 failed, 6 rows` |
| CP-3 CoreMatchesFallback | exit 0; `C487: 38 passed, 0 failed, 38 rows` |
| CP-4 pin digest | exit 0; `pin-ok` (stated `ab041a1c…` = computed by `-PrintInputDigest`) |
| CP-5 NightlyCoreTests | 1/1 passed |
| CP-6 LiveLaneRunnerTests | 4/4 passed |
| CP-7 ReleaseImpactScriptTests | 10/10 passed |
| CP-8 TestClassificationPolicyTests | 42/42 passed |
| CP-9 DerivedImpactIndexTests | 27/27 passed (driver pre-built under lease `942158b0`, as R-2 states) |
| CP-10 Linux guard batch | 39/39 passed; `VIOLATIONS 0 LISTED 162` |
| CP-11 land graph `-Check` | exit 0, `approved` |
| CP-12 check-evidence-diff d86d0f39..HEAD | `commits=9 entries=0 violations=0` |

Merge-tree against master `eb31e339203d` is clean (tree `8966e70cbddad8fb4cb4522040238edb398100c6`).

## Red witnesses and mutant

For each witness, I took a `git archive` scratch copy and set `TMPDIR`/`TEMP` to a scratch directory, so the
mutant core compiled into its own cache key. One lease covered each command.

- At `640cdfb29` (red), lease `80e54a9e`: `FAIL NC DiscoveryCacheAgrees.cacheAgrees - cachedUid=u1 diskUid=u2
  cachedOk=True diskOk=False missing=u2 len=207/207 ino=25050816->25050818`; `1 passed, 1 failed`.
- At `640cdfb29` (red), lease `be0e5684`: trailing-comma, utf16 and comment each failed with a bare
  `discovery-unreadable: <path>` and no reason. producer, utf8 and utf8-bom passed. Result: `3 passed, 3 failed`.
- Mutant at the tip (lease `a53350a8`): I reduced the hit condition to `Hashes.TryGetValue && Length && WriteTicks`,
  removing the `identified`, `hit.Identified`, `Dev` and `Ino` terms. The mutant compiled its own core
  (`Antiphon.Nightly-0A443351…`). Result: `FAIL … cachedUid=u1 diskUid=u2 cachedOk=True`, `1 passed, 1 failed`.
  The mutant is killed: the cache witness can go red.

## Judgments the brief asked for

1. **Identity check on Windows.** `HistoryNodeIdentity.ReadWindows` (`HistoryLinks.cs`) opens the node with
   `CreateFileW(FILE_READ_ATTRIBUTES, share all, OPEN_EXISTING, BACKUP_SEMANTICS|OPEN_REPARSE_POINT)` and then
   calls `GetFileInformationByHandle`. The key is `Dev = VolumeSerialNumber` and
   `Ino = FileIndexHigh<<32 | FileIndexLow`.
   - **Failure branches.** An invalid handle or a failed query returns `MissingNode` (error 2 or 3) or
     `UnreadableNode`. `TryFileIdentity` returns false for Missing, Unreadable, Linked or Directory. A hit then
     requires `identified && hit.Identified`, and admission requires `identified && still` with equal dev and ino.
     So an unknown identity never hits and is never stored. It is never treated as 0/0-equal. The change only
     adds conjuncts to the hit, so it cannot accept anything the base accepted less safely.
   - **NTFS.** The file index is the MFT reference with its sequence number, so a reused record gets a new index.
   - **ReFS.** The 64-bit index may not be unique there. This is advisory A-2 below. Length and mtime still have
     to match, and the production writer always produces a fresh mtime.
   - **Hard links.** A regular file with `NumberOfLinks > 1` is treated as Linked and is never cached. That is
     slower but correct.
   - **Production writer.** `Write-NightlyAtomicJson -DiscoveryDocument` (`nightly-common.ps1:395-430`) is the
     only production writer. It writes `<path>.<guid>.tmp` through `DiscoveryWriter.WriteFile`, then calls
     `File.Replace(tmp, path, bak)`, or `File.Move` when the path is new. This is a replacement with a new file
     identity and a write time of now. Neither the length+mtime check nor the identity check can be fooled by
     it. The only producers are `Invoke-NightlyProduceDiscovery` (`nightly-coverage.ps1:537`) and the selection
     leaf (`nightly-tests-impl.ps1:1265`).
   - **Remaining gap.** A same-inode, in-place rewrite that also restores mtime would still be missed. No
     production path does that. Production cannot go stale through this cache.
2. **Cost.** A cache hit now makes one extra Windows handle open and query. A miss makes two. The per-chunk
   callers are the chunk verdict's discovery index key and `Get-NightlyAssemblyHash` (when the profile is not
   used); the phase assembly digest is per suite, and the heartbeat is per tick. That is a few calls per chunk,
   each tens of microseconds with no data read. Over 1,289 chunks this adds well under a second to a 16-hour run.
   I measured this by reading the code; it was not timed on Windows. This is not a per-chunk regression.
3. **Reader tolerance.**
   - **Encoding.** The production document is written by .NET (`File.WriteAllText(…, new UTF8Encoding(false))`),
     so it is UTF-8 whatever the PowerShell edition.
   - **5.1 hop.** The `db8e75e1b` summarizer hop to pwsh neither writes nor reads discovery documents.
   - **Other writers.** The C487 fake host writes `Set-Content -Encoding UTF8`, which is UTF-8 with a BOM under
     5.1 and is accepted. Live-lane captures use their own `ConvertFrom-Json` reader.
   - **UTF-16.** Master already refuses UTF-16 and comments, since `20bf32214` (an F-5 regression). This branch
     only adds trailing-comma acceptance and named reasons. Refusing UTF-16 is safe: no real producer path can
     reach it mid-run. Decoding UTF-16 is not needed for this land (see advisory A-1).
4. Red and green rows reproduce, and the mutant is killed (see above).
5. **Pins.** CP-4 confirms the reseal. `NightlyJson.cs` is not a protected input, and `NightlyCore.cs` is.
   No other test pins these files. The sibling task `7e8fa6a4` currently sits at `d86d0f39` (an ancestor of
   master) with no task commits. Whichever of the two lands second must rerun `-PrintInputDigest` and reseal if it
   touches a protected input.

## Advisories (cards, not land holds)

- A-1: `ReadDiscovery` could decode a UTF-16 document that starts with a BOM, as the old `ConvertFrom-Json` path
  did. That would remove the F-5 compatibility case completely. No current producer emits UTF-16.
- A-2: On ReFS, `GetFileInformationByHandle`'s 64-bit `FileIndex` may not be unique. `GetFileInformationByHandleEx(FileIdInfo)` gives the
  128-bit id. This is defence in depth only, because mtime and length already change on every production write.
- A-3: Windows behaviour of `TryFileIdentity` in the cache path was not executed. The plan states this, and
  `WindowsUnitPlatformAdmissionTests` skips on Linux. A Windows-lane run of `-Case DiscoveryCacheAgrees` would
  observe it.
