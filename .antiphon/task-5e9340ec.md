# Review 5e9340ec: CARD-1687 live-lane fingerprint speed-up and C487 cleanup (round 1 of 3, full)

Subject: origin/feat/card-task-4f64e784 tip 4ca16a502dfaf16056d8ade00891f62ceac2ddfa (base f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0).
Code owner: 4f64e784-ba19-499d-8044-57b6ed2c9809.

Outcome: the fingerprint value and cache are sound (fingerprints byte-identical to base on a new
34-entry set, including unusual-path fixtures; the mutant goes red). The C487 cleanup behaves the
same as the old code on Linux. **One defect: the documented forced-Windows cleanup witness
`scripts/fixtures/c487-readonly-windows-witness.ps1` now fails at the tip.** It passes at the base.

## D-1 The forced-Windows read-only cleanup witness is red at the tip

- **Where:** `scripts/lib/c487-harness.ps1:331` (6d7cd7814) and `scripts/fixtures/c487-readonly-windows-witness.ps1:42-48`.
- **Failure:** the witness fakes the Windows tree by shadowing `Get-Item`/`Get-ChildItem` over
  PSCustomObject nodes. It asserts three things: no link is ever enumerated, there are exactly 12
  enumerations, and the read-only Git objects are normalised. At the tip the completion body calls
  `$directory.GetFileSystemInfos()` on those nodes and dies with
  `Method invocation failed because [System.Management.Automation.PSCustomObject] does not contain a method named 'GetFileSystemInfos'`.
  Exit codes: tip `tip-exit=1`, `-SourceRef f32aed42e` `base-exit=0` (the base run reports
  "C487 Windows cleanup witness passed"). Both ran in one build-slot lease
  (`review-5e9340ec-c487-witness`).
- **Why it matters:** `docs/testing-and-build.md:19` lists this file as the forced-Windows witness
  for exactly this function. Without it, nothing shows on this host that the Windows path still
  skips junctions, directory links, file links and a linked root, or that the inventory is
  unchanged. The commit says "reparse points still never followed", but the only Windows
  evidence for that claim no longer runs. Product behaviour looks right: .NET reports
  `ReparsePoint` for junctions and symlinks, the same objects `Get-ChildItem` returned. This is
  a broken witness, not a product bug.
- **Fix (Code 4f64e784):** make the witness model the new listing. Give each fake directory node
  a `GetFileSystemInfos` ScriptMethod. It records the enumeration and throws
  `'external link was followed'` for the outside, link and root-link paths, as the
  `Get-ChildItem` shadow does now. Keep the 12-enumeration, outside-file and node-count
  assertions. Re-run the witness at the tip (green) and with `-SourceRef 4ca16a502` before the
  fix (red), and record both.

## Verified sound

1. **Fingerprint value (base f32aed42e versus tip).** I wrote a separate probe that dot-sources each library and records `ConvertTo-TestSelectionCanonicalJson` of the full fingerprint result, or the error text. It ran in three passes: base with a fresh cache per fingerprint, tip with a fresh cache, and tip with one cache shared across every repository and commit, to stress staleness.
   - **Real commits, lane `live-claude-core-windows` (18 entries):** 4ca16a502, f32aed42e, 919d8d42f, 10a826b9d (no declaration, error path), de27ce2fd, 359bd5d48, d77ac25be (csproj changes; d77ac25be has no declaration), 3494aabcd, 30ddbcda7, 1154d4201 (scripts/lib changes), c35aeda5c, 006347f48 (docs-only), 0ad30955 (declaration introduced), e5d111209, 52b833f74, 6d7cd7814, origin/master e56e4d1b6, plus a missing-lane error.
   - **Fixture commits (16):** a new fixture repository with paths under the live, runtime and test roots. The paths contain a space, `"`, `\`, a non-ASCII character, a tab, DEL, ESC, CR, U+2028, U+0085, `<>&'+`` `=`, `{[]}`, `%2F`, and case-only twins (caps.ps1 and Caps.ps1). Further commits edit the quote, backslash, DEL, non-ASCII and case-only files and set the executable bit on a backslash path and on an ASCII path. Others add symlinks (120000, including one with a non-ASCII name), a gitlink (160000) and a 6x50-character long path, a mid-path newline (malformed-tree error in both) and a trailing-newline path.
   - **Result:** all 34 canonical JSONs, digests and error texts are byte-identical across the three passes. Every fixture edit gives a distinct digest.
   - **Direct path:** `^[ !#-\[\]-~]*$` copies only printable ASCII without `"` or `\`, which Newtonsoft escapes neither of. Everything else falls back to `ConvertTo-TestSelectionCanonicalJson`. A path missing from the tree falls back to the original form, which is unreachable because every member comes from the tree. Keys sort as blob, mode, path, which matches the ordinal sort.
   - **`text:` cache entries:** these hold raw bytes keyed by blob id. Declaration, manifest, catalog and instruction bytes are parsed fresh on every call, so nothing parsed depends on path or revision. The prefix cannot collide with the bare blob-id XML keys. `cat-file blob <id>` returns the same bytes as `<sha>:<path>`, symlink blobs included. A gitlink fails in both.
   - **Listing reuse:** the listing is the same `ls-tree` output for the same `$Sha`, taken in the same call. Status forwards the same CandidateSha. Receipt callers pass no cache and keep the original path.
2. **Tests (one build-slot lease each, `-p:UseAppHost=false`, native output).**
   - LiveLaneFingerprintTests 7, LiveLaneReceiptTests 6, LiveLaneAttestationTests 6 and LiveLaneRunnerTests 4: **23/23 passed** (TRX total=23 executed=23 failed=0, 3m37s).
   - Mutant: drop `$entry.blob` from the direct digest. ReferenceClosureInvalidates and TestAndFixtureInvalidates **both went red, 2/2 failed**. Failing sub-cases: direct-edge, transitive-edge, edge-added, shared-build-inputs, test-source, linked-shared and content-glob. I restored the file with `git checkout --`.
3. **C487 cleanup on Linux.** The same probe tree ran through the base and tip functions with identical results.
   - Read-only files in `.git/objects`, dotfiles, nested hidden directories and a path of more than 300 characters are cleared (`clearedReadOnlyFiles=6` in both).
   - A directory symlink and a file symlink pointing outside the root are not followed: the outside read-only files are untouched. A dangling link is tolerated.
   - A missing root throws `ItemNotFoundException` in both, so that behaviour is unchanged, not a regression. Read-only directories are not cleared in either version (old behaviour).
   - `GetFileSystemInfos()` with no options uses `EnumerationOptions.Compatible`, so hidden and system entries are included.
   - The file keeps `#requires -Version 5.1`. `DirectoryInfo.GetFileSystemInfos()` exists on .NET Framework, and `\\?\` FullNames carry through to child entries as before.
   - Four cheap dot-sourcing harness classes passed **10/10** (TRX): ReleaseSelectionHopSeedTests 3, CheckpointOwnedOutputScriptTests 2, RcProjectSelectionTests 4 (this class also covers the changed `rc-project-selection.ps1`) and NightlyProbeSupervisorTests 1.
4. **Budgets.** `ceil(2*6*p95/30)*30` reproduces all ten values: 300, 210, 90, 300, 90, 150, 150, 150, 120, 240.
   - TestAndFixtureInvalidates and RuntimeScriptsIncluded at 90 s are at least 12x their Linux p95 (6.7 s and 6.0 s). They also exceed the flat 60 s these methods passed on Windows in rc f40ad905, with slower code.
   - None masks a hang: the maximum is 300 s, down from 510 s.
   - This task does not close CARD-1687: the Windows factor is still one sample.
5. **Other checks.** `check-evidence-diff` over f32aed42e..4ca16a502: 3 commits, 0 violations. `git merge-tree --write-tree origin/master(e56e4d1b6) 4ca16a502` is clean (tree c673a45fb).

## Advisories (not defects)

- A-1: In this run (load about 20-26), ReuseIsNotExecution took **30.0 s** against a recorded Linux p95 of 17.8. StableCanonicalInputs took 24.9 s (p95 23.9) and UnknownDependencyRefuses 25.4 s (p95 25.0). The "p95" is the maximum of 5 runs, so the Windows margin on ReuseIsNotExecution is about 1.3x (180 s estimated against 240 s), not 2x. Fold this into the CARD-1687 Windows re-derivation.
- A-2: `Get-LiveLaneEntryDigest` anchors with `$`, which also matches before a final `\n`. It is safe only because `Read-LiveLaneTree`'s `(.+)$` can never yield a key ending in `\n`. `\z` would make that independent.
- A-3 (pre-existing): `Get-LiveLaneGrep` returning exactly one row unrolls the row into its two strings at `foreach ($row in (...))`. Neither the old nor the new code wraps it.

## Scope

The brief's commissioned selection ran completely. The verification profile's whole Unit lane did
not run: it takes about 3 h on server2, against this round's 75-minute budget with the build-slot
queue saturated. `ordinaryScopeCompleted: Interim`.
