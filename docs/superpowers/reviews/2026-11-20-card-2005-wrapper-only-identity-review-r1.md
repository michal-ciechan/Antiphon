# Review r1: wrapper-only census identity (Debug 0cb46743)

Review task `c4939773`, ordinary round 1, Final/Full. Subject `origin/feat/card-task-0cb46743`
tip `d4aeda4eed6c3e7b1b154d1e87ff5c9c32cbdb72` on base `c83d14d4858a66e41e2c51c4631869db99d0a2c7`
(code `0ecf8ba031b0652f471002b975cecf159bcacedd`, docs `d4aeda4e`). Author Grok, reviewer Claude.

Verdict: CLEAN. No blockers. Two advisories.

## Diagnosis and production path

The failure is in the test harness only. `Read-Census` exists only in
`scripts/test-live-lane-run.ps1`, and nothing in production calls it. The real wrapper entry,
`scripts/run-live-lane.ps1`, dots `scripts/lib/live-lane-run.ps1`, which dots `nightly-common.ps1`
from the full checkout.

Probe on the full checkout at the reviewed tip, in a fresh pwsh under build-slot lease
`527fd5b8-2361-44ba-8474-0a2d06e7732b`:

- `Get-NightlyCoreBuildIdentity` returned `0853D61AD7905F06D149B598`, so it was not null.
- `Test-NightlyCoreAvailable` returned true, with the core loaded from the matching cache folder.
- A second fresh runspace in the same process dotted the full checkout and was accepted with the
  same identity and no errors.

The real path admits, so there is no product defect. The fixture's `valid-context` row runs the
real `run-live-lane.ps1` and passed at both base and tip.

Red and green were run directly, each under its own lease:

- The base harness, which is the tip without the re-dot, gave 15 passed and 1 failed:
  `C1545 WrapperOnlyPaidManifest/harness`, `nightly-core-identity-mismatch: required-identity-unavailable; start a fresh pwsh at 268`.
- The tip harness gave 21 passed and 0 failed.

## Pin integrity

- `nightly-core.ps1` defines only loader, JSON and evidence helpers. None of the census functions
  is defined there: `Read-NightlyExecutionPolicy`, `Get-NightlyProfileExclusions` and
  `Get-NightlyDiscoveryCensus` live in `nightly-policy.ps1` and `nightly-coverage.ps1`. The re-dot
  therefore cannot replace a pinned census function. `nightly-coverage.ps1` also has an import
  guard (`$script:AntiphonNightlyCoverageLoaded`), so dotting the checkout core does not reload it.
- The witness can go red. A mutant that also reloaded the checkout `nightly-coverage.ps1` (guard
  reset, then dot) failed `reader-edit`: 20 passed, 1 failed. A first mutant that skipped the
  guard reset was a no-op because of the import guard, at 21 passed. The narrow fix stays at 21.
- The compiled census was already the checkout's before this fix. The copy has no
  `src/Antiphon.IO.WriteBehind`, so it never builds a core, and before `2a9ffa344` the loaded type
  was accepted unchecked. The comment at `scripts/test-live-lane-run.ps1:17-23` documents this.
- `PinnedInputDigest`: `-PrintInputDigest` printed
  `3162a7daa58003f844e4d2b39237095e7cb54df897efc5dd8fc002dfb25a78c6`, the committed pin. The diff
  touches no protected input, and `test-live-lane-run.ps1` is not one, so no reseal is needed.
- Production `nightly-core.ps1` is unchanged. The Astra B refusal paths therefore remain, and
  `AstraFixesBTests` passed 5/5 and `NightlyCoreTests` passed 1/1 at the tip. The base-harness
  mutant above shows the null-identity refusal still fires when the re-dot is absent.

## Why-missed note

The ancestry claims check out:

- `2a9ffa344` is an ancestor of `3f24857e`, `5f0804e4` and master.
- It is not an ancestor of `ea9e076e`.
- The Astra B plans do not name `LiveLaneRunnerTests`.
- Astra A CP-6 does name it.

## Checkpoints (one tool run, `20261120-002724-ac16`, GREEN exit 0, validate `SOURCE VALID rows=11`)

| CP | Result |
|---|---|
| CP-1 LiveLaneRunnerTests | 4/4 |
| CP-2 NightlyCoreTests | 1/1 |
| CP-3 NightlyScriptsTests | 7/7 |
| CP-4 AstraFixesBTests | 5/5 |
| CP-5 guard union (nine classes) | 81/81, C1729 `VIOLATIONS 0 LISTED 162` |
| CP-6 derived driver build | exit 0 |
| CP-7 DerivedImpactIndexTests | 27/27 |
| CP-8 land graph `-Check` | approved |
| CP-9 `-PrintInputDigest` | equals pin |
| CP-10 check-evidence-diff | commits=2 violations=0 |
| CP-11 merge-tree vs `56d7340c` | tree `e49aa9152d7d7e1e71ffebc9caa6887e24a706b8`, clean |

## Advisories

- A-1. The re-dot rebinds every `nightly-core.ps1` helper in the pinned reader, not only the
  loader, so an edit to a copy's `nightly-core.ps1` is invisible to `Read-Census`. This is harmless
  today: the file is a protected input, every copied root is a byte copy (LF or CRLF) of the
  checkout's, and the pin digest catches the edit. No row edits a copy's core script.
- A-2. The note correctly keeps `LiveLaneRunnerTests` off the guard union. The selector gap is real
  as well: `tests/derived-impact-index.json` maps `scripts/lib/nightly-core.ps1` only to
  `ImpactedClassesSelectorTests`, `ReleaseImpactScriptTests` and `SelectorLineEndingAttributeTests`.
  Script consumers of the loader, including `LiveLaneRunnerTests`, `NightlyCoreTests` and
  `AstraFixesBTests`, are not edges, so an impact-selected plan for a loader change would miss them.
  Card candidate.
