# CARD-1419 discovery census once per suite

`ConvertTo-NightlyNativeSuiteVerdict` (`scripts/lib/nightly-coverage.ps1`)
ran after every native launch. Each run re-read the whole discovery file,
rebuilt every node with `+=` array growth, and built the full census twice.
On the desktop rc evidence this took 30.0 s per launch for 18,993 nodes. The
cost followed the suite size, not the launch size
([time profile, Q1](../../investigations/2026-10-21-antiphon-tests-time-profile.md)).

Inspected at `47e6916c7590b101c9336fab84398fa93b68878f`. It is origin/master
less two docs-only commits. `scripts/lib/nightly-coverage.ps1`,
`scripts/lib/nightly-tests-impl.ps1` and `scripts/test-nightly-tests.ps1` are
byte-identical at rc/2026-10-20 `806b23084fa91daaa61ff0ff1957f39c5fe76baa`.
Checkpoints name no host and no OS.

## Decisions

D-1. Each suite builds one discovery index (`New-NightlyDiscoveryIndex`)
before its launch loop and passes it to every verdict (`-DiscoveryIndex`).
The profile discovery read and its suite census use the same index.

D-2. Entries are keyed by the SHA-256 of the discovery bytes, not by path.
The profile lane reuses one file. The default lane writes the same bytes
beside each launch. A rewritten file is new bytes and is parsed again. A
failed read is not cached, so its error repeats unchanged.

D-3. An entry keeps one view per exclusion policy: the census, the
all-classes required set, the all-classes class list and a class lookup. The
lookup mirrors `Test-NightlyClassAssigned`: name or bare name, of ClassName
or Type, ordinal ignore-case. A launch takes required UIDs from its own
classes in discovery order. Nothing else changes: missing-trx,
missing-discovery, extra-class-in-chunk, missing-expanded-row,
missing-required-class, zero-required-uid, duplicate-discovery-uid, the
coverageComplete/testsPassed rules and the end-of-suite union check.

D-4. A verdict called without an index builds a private one. Every caller
therefore runs the same code path.

D-5. `ConvertFrom-NightlyDiscoveryDocument`, `Get-NightlyDiscoveryCensus`,
`Get-NightlyRequiredDiscoveryUids` and `Test-NightlyExpandedRowsPresent`
now grow generic lists, not arrays. They still return arrays. The census
digest input and its `Sort-Object` order are unchanged.

D-6. `nightly-tests-impl.ps1` changes only at the suite setup and the verdict
call. The CARD-1410 packer work is in progress in the same file.

## Verification design

- V-1: seven launches on one discovery path read it once. Three paths with
  identical bytes read it once. A profile lane with an exclusion reads it once.
  A rewritten file is read again (`C1419_DiscoveryParsedOncePerSuite`).
- V-2: each indexed verdict equals the unindexed verdict: coverage, pass,
  census counts, digest and reasons. Clean launches are complete. A launch
  missing one row names exactly `missing-expanded-row ClassB.Two`. A launch
  with a foreign class names `extra-class-in-chunk`. A constructor-suffixed
  type `ClassF(Factory)` is found from chunk class `Ns.ClassF`. Its launch
  missing one row names exactly `missing-expanded-row ClassF(Factory).Two`.
- V-3: `Invoke-AntiphonNightlyTests` has one verdict call. It passes
  `-DiscoveryIndex` from a single `New-NightlyDiscoveryIndex` assignment
  made before the `foreach ($chunk ...)` loop (AST check).
- V-4: the existing verdict and census cases stay green (CP-2 to CP-7).
- R-1: one checkpoint-tool run of CP-1 to CP-7, exit 0.

Red: `C1419_DiscoveryParsedOncePerSuite` failed at the test commit, before the
fix. That run is unlisted. Unlisted (inherited Linux reds): `C487_G039`,
`C487_G040`, `C487_G051` and release-gate `C599_MetadataParsers` fail on this
Linux runner. Each failure is identical at the base commit and at the fix. The
G039/G040 failures are their `Invoke-E` end-to-end rows: a clone universe
mismatch and production discovery. G051's failure is its probe-fixture TRX
class row. C599_MetadataParsers throws `truncated diagnostic record uid=u1`.
None of these is a checkpoint row.

PC-1 (pending, SourceLanding Mutation): make `Get-NightlyDiscoveryIndexEntry`
miss on every call; V-1's read counts must go red.
PC-2 (pending): drop the bare-name lookup in
`Get-NightlyDiscoveryIndexRequiredUids`. V-2's suffixed-launch reason must go red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1419_DiscoveryParsedOncePerSuite` | V-1, V-2, V-3 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case D13_RealTimeoutCancelled` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case D13_ErrorTerminal` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case D13_StatePropertyBoundary` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1044_DiscoveryReconciliation` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-6 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-release-gate.ps1 -Case C599_ExpandedCoverage` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-7 | S2 | n/a | census-once | `pwsh -NoProfile -File scripts/test-release-gate.ps1 -Case C599_EligibilityCensus` | V-4 | exit 0 | n/a | 1 | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

### Cost

Ordinary floor is 5 minutes.
