# CARD-2005 Astra A advisories: Review round 1 (Final)

Review task `4b1be4f6`. Subject: Code task `fb6a2e49`, `origin/feat/card-task-fb6a2e49` tip `71bd0d5dc2709016df2455a3b9a9d3a5785e4618`, base `5be178005f06aa868bbb038e561c2ef22a654cb5` (8 commits). Master `5f0804e425384f8b03f88b833fbdad0e93f966d3`.

Verdict: **CLEAN**. No blockers. Advisories below are not land holds.

## Checkpoints (run `20261119-202633-0098`, one run, `validate` SOURCE VALID rows=10)

| CP | Result |
|---|---|
| CP-1 DiscoveryCacheAgrees | exit 0, 2 passed, 0 failed |
| CP-2 DiscoveryParserTolerance | exit 0, 10 passed, 0 failed |
| CP-3 DiscoveryCacheFileId | exit 0, 11 passed, 0 failed |
| CP-4 PrintInputDigest | exit 0, `89fec158ad8dca3bd17b4baf1284822552938069e2e772ad6b88f5821cc2f4a6` |
| CP-5 CoreMatchesFallback | exit 0, 38 passed, 0 failed |
| CP-6 TestClassificationPolicyTests | 42/42 passed (floor 42), build 251 s |
| CP-7 guard batch | 39/39 passed (floor 39), C1729 VIOLATIONS 0 LISTED 162 |
| CP-8 land graph -Check | exit 0 |
| CP-9 check-evidence-diff base..HEAD | exit 0 |
| CP-10 check-os-witnesses | exit 0 against a Review-authored declaration (see A-4): W-1 native pending, restartImpact native pending |

Unlisted runs: `check-os-witnesses.ps1` three times while authoring the CP-10 declaration (lint only, no build); one decoder attack probe that loads the already-cached nightly core dll (no build).

## Judgement

1. Decoder (`NightlyJson.TryUtf16Bom`). Probe against the cached core dll: BOM only -> `empty`; BOM + odd byte -> `invalid-json`; FF FE + UTF-16 LE JSON -> ok; FE FF + BE -> ok; EF BB BF -> ok (unchanged); BOM + lone surrogate -> `invalid-json` (throwing `UnicodeEncoding`, no partial parse); BOM + truncated JSON -> `invalid-json`; BOM + trailing garbage -> `invalid-json`; FF FE 00 00 + UTF-32 -> `utf-16`; double BOM -> `invalid-json`; BOM + NUL -> `invalid-json`; bare UTF-16 LE -> `utf-16`. The decoded document goes through the same `ParseDiscoveryUtf8` (comment and trailing-comma rules unchanged). Moving the `empty` check after the NUL check is behaviour-equivalent (whitespace never contains 0). Nothing master refused is now accepted except UTF-16 with a BOM, which is the intent.
2. FileIdInfo. `FILE_ID_INFO` = ULONGLONG + 16-byte `FILE_ID_128`, 24 bytes, marshalled as three ulongs; LE re-serialisation keeps the byte order. Class 18 is `FileIdInfo`. The handle is the existing `CreateFileW(FILE_READ_ATTRIBUTES, share all, BACKUP_SEMANTICS | OPEN_REPARSE_POINT)` inside `using`, disposed on every path. A false return (FAT, SMB, old OS) falls back to the 64-bit index; `EntryPointNotFound`/`DllNotFound` are caught; no other throw path. `CacheIdentity.Same`: unknown is never equal (including to itself), kinds must match and volumes must match, so 128 vs 64 is never equal; a mid-run kind change is a miss (safe).
3. Windows-only lines: `HistoryLinks.cs` `ReadWindows` (new `id.CacheId = WindowsCacheId(...)`), `WindowsCacheId`, the `GetFileInformationByHandleEx`/`FileIdInfoData` marshalling and `WriteUInt64Le` on that path. The C# is compiled by the net9.0 SDK project `Antiphon.Nightly.csproj` (nightly-core.ps1 builds and loads the dll; Windows PowerShell 5.1 never loads it), not by `Add-Type` on PS 5.1, and master already uses `out var` and expression bodies in this file, so the C# 5 concern does not apply. The two `Add-Type -Path` sources (`TestPhaseStreamReaders.cs`, `TestPhaseLinuxIdentity.cs`) are untouched. Desktop observation: `pwsh -NoProfile -File scripts/test-nightly-core.ps1 -Case DiscoveryCacheAgrees` (pending; see A-1 for what it can and cannot prove).
4. Preamble as a byte array: `UnicodeEncoding.GetBytes` never writes the BOM, so the pre-836e5a38 witness fed a BOM-less document; the fix prepends `GetPreamble()` and asserts the first two bytes, so the LE/BE rows really test the BOM path. Witness counts 10/11/38 and guard floor 39 confirmed green above. Mutants (Same compares only the 64-bit index; unknown compares equal): analytic only, mutation execution waived (operator 2026-11-12). An index-only compare makes `$full`/`$different` (both Index 0 for FileId128) equal, so `fileId-differs` and `different-id-miss` go red. Unknown-equal makes the `unknown` row red; `unknown-miss` is additionally held by `identified=false` in `CachedFileSha256`. The red commit f6a03cb7 was not re-executed by Review.
5. Pinned inputs. `git merge-tree --write-tree origin/master 71bd0d5dc` gives one conflict, `scripts/test-live-lane-run.ps1` (the pin line only). A scratch merge of the branch into master `5f0804e42` prints `-PrintInputDigest` = `01910124cff99ecfa0aabc4c3f1b970fc55da57ff14b4b4adf2c0e260273e522`. The land must reseal `PinnedInputDigest` to that value (neither `89fec158...` nor master's `063da62b...` is correct on the merged tree).

## Advisories (cards, not land holds)

- A-1. The named desktop command cannot prove the FileIdInfo branch ran: `DiscoveryCacheAgrees` asserts the cache did not serve stale data and prints the 64-bit `Ino`, never `CacheId.Kind`; `DiscoveryCacheFileId.read-cache-id` asserts `Known` only. A wrong class or struct would silently fall back to the 64-bit index (safe, master behaviour) and still pass. Fix: on Windows NTFS assert `CacheId.Kind -eq 'FileId128'` and print it.
- A-2. `DiscoveryCacheFileId.same-id-hit` cannot go red on a miss (content unchanged, so the hash is equal either way). No safety impact; a hit witness needs a stale-content probe or a hit counter.
- A-3. Some SMB/FAT stacks return a zero or non-unique FileIdInfo id; same weakness as master's zero file index, not a regression. Consider treating an all-zero id as unknown.
- A-4. CP-10's `.antiphon/card-2005-os-witnesses.json` is Code-local and ignored, so Review authored its own declaration (W-1: `HistoryNodeIdentity.Read`, `scripts/test-nightly-fileid.ps1` `Test-NCDiscoveryCacheFileId`, CP-3, Windows native pending; restartImpact required, Windows native pending). `scripts/test-nightly-core.ps1` is unlexable by the OS-witness scanner.
- A-5. Land reseal value: `01910124cff99ecfa0aabc4c3f1b970fc55da57ff14b4b4adf2c0e260273e522` (above).

Pending: native Windows observation (W-1, restartImpact). No PC declared; mutation waived and not discharged.
