# CARD-1836 S6c prerequisite: C# port review, round 1

Review task: bb05708f. Code task (landing owner): 3a5b76f5, Grok author.
Subject: origin/feat/card-task-3a5b76f5 at ffa9e26a977d6b11d1d34e53e4ffb993e4566384 (six commits on c6565bc95776c93a01109385d25420d19372afe8).
Round: 1 of 3, Final profile, full ordinary scope for this change. Reviewer: Claude Opus (different company from the author).

## Verdict

CLEAN. No blocker. Five advisories below; they go to cards and do not hold the land.
The land needs a land-graph refresh replay: run `scripts/land-graph-land-refresh.ps1` before `delegate.ps1 -Land`.

## Evidence (server2, Linux; Windows UNOBSERVED)

| Run | Selection | Result |
|---|---|---|
| Build | `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, native output, one build-slot lease (372 s) | exit 0, 0 errors |
| CP-1 rerun | `(RcShadowSampleCSharpTests*)\|(RcDirtyOrderCSharpTests*)\|(RcShadowDirtyDifferentialTests*)` | 19/19 passed, 0 skipped (TRX: 12 + 6 + 1) |
| Guards + wrapper harnesses | `TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`, `RcIncrementalShadowTests*`, `RcDirtyLaunchOrderTests*` | 71/71 passed, 0 skipped (TRX: 42 + 1 + 5 + 2 + 3 + 12 + 6) |
| CP-5 to CP-7 rerun | `(DerivedImpactIndexTests*)\|(NightlyCoreTests*)\|(LiveLaneRunnerTests*)` | 32/32 passed, 0 skipped (TRX: 27 + 1 + 4), 3m38s |
| Golden authenticity | own pwsh replayer over the base ps1 (`git archive c6565bc95776 scripts/lib`), all 184 proposal/pilot/trust cases without a write step | 183 match; 1 (`edge-expando`) is a replayer limitation (PowerShell cannot index an ExpandoObject cast), not a golden defect |
| Wrapper probes | the same 10 caller-shaped probes through the base ps1 and the new wrappers | 7 identical; 3 differ, all fail closed (A-1, A-2) |
| check-evidence-diff | `-BaseRef c6565bc95776 -HeadRef ffa9e26a9` | commits=6 entries=0 violations=0 |
| merge-tree | `origin/master` 2c662e3bda11cd36dd4a98ba191f20079bea9cf9 vs ffa9e26a9 | clean, tree 3282d781c9f4bc51e4a659e3c06bd1367194fd44 |

All seven plan rows were rerun with the plan counts: CP-1 19, CP-2 12, CP-3 6, CP-4 42 (TestClassificationPolicyTests, in the guard run), CP-5 27, CP-6 1, CP-7 4. They were direct build-slot runs on one build, not checkpoint-tool receipts. PCs: none defined; every PC stays pending for SourceLanding Mutation (operator waiver 2026-11-12).

## Key questions

### 1. Differential validity

- The golden blob `2a6133d3` was added in the red commit 6bf65042 and is byte-identical at every later commit (d0d685fa, 3c4e3d34, f42aafe1, 39e0abd1, ffa9e26a). Its `source` field is c6565bc95776. It was not regenerated after the port.
- I replayed 183 of the 184 non-repo cases through the base ps1 and they match. I did not replay the 15 dirty-repo and `secondError` cases on their own.
- Coverage by witness: 199 cases. NonBooleanProvenance 43, DictionaryKeyShadow 37, CarriedChunkResult 19, EmptyCollection 12, EvidenceDerives 12, SampleSize 10, plus 20 `edge-*` cases. The other witnesses have 1 to 7 cases each.
- Fail-closed rules present in the golden:
  - unrolled and one-element flag arrays
  - non-boolean provenance and skip flags (string, number, null, object, JSON string or null)
  - carried rows (passed and failed)
  - planned and executed N, and an empty or malformed digest
  - empty string, list, ArrayList, enumerable and HashSet collections
  - shadowed `Keys` and `Count` keys
  - unreadable and null chunk ids
  - duplicate provenance, outcome and results keys, a BOM key and a trailing-space key
  - case-variant flags and results keys
  - an ISO-shaped id and outcome, Unicode, a newline in an id, a duplicate id, ExpandoObject, and null or empty carries
- Not covered: PSObject-wrapped elements (A-1). Also not covered: a contract dictionary with its own `Keys` entry, non-integral `sequence` values, and array-valued keys (A-4).

### 2. Port differences that matter

- The port parses no JSON. Callers still pass PowerShell objects, so `ConvertFrom-Json` keeps refusing case-variant duplicate keys before the C# code sees them. The golden `case-*`, `edge-dup-*` and `edge-bom-key` cases pass. The System.Text.Json duplicate-key concern applies only to `ProposalJson` output, which builds a fixed `JsonObject`.
- Types are strict:
  - `is bool`, so the string `'true'` refuses (probe P10: `shadow-chunk-carryable` in both versions).
  - `is string`.
  - `Whole` accepts only int or long. That matches `Test-RcShadowWholeNumber`.
- A missing property and null both read as null, as they did in the ps1. `HasKey` keeps the ps1's distinction between a present and an absent `sampled`/`results`.
- Every sort and comparison uses `StringComparer.Ordinal` or `CompareOrdinal`, and every regex is `CultureInvariant` with `\A..\z`. No culture-sensitive sort or comparison is left.
- `fullAnchor` and `utcNow` are ignored. This matches the ps1: the old trust kept only the `contract-*` reasons from `Get-RcForcedFullDecision`, and the anchor and clock produce no `contract-*` reason.
- No date is parsed in the library.
- No roster is loaded from disk. A null chunk list gives `not-applicable`.

### 3. Wrappers

- The parameter blocks of `New-RcShadowProposal`, `Test-RcShadowPilotEligible`, `Get-RcShadowTrustDecision` and `Get-RcDirtyLaunchOrder` are unchanged.
- Errors keep their text. `Invoke-*Core` rethrows the innermost message as a string, and the golden `error` cases compare those strings exactly.
- Loading goes through `Test-NightlyCoreAvailable`. It builds `Antiphon.Nightly` into the temp cache keyed by a SHA-256 of the `Antiphon.Nightly` sources, and it asserts the loaded identity.
- An absent build is built from source or throws. A failed build or an identity mismatch throws, and the wrapper then throws `nightly-core-required`. There is no fallback to the old logic.
- A stale cached build is rejected by the source hash.
- The return objects are now typed with PascalCase properties (A-2).

### 4. csproj and the land graph

- The only build-definition change in c6565bc9..ffa9e26a is `tests/Antiphon.Tests/Antiphon.Tests.csproj`. It adds three `Compile Include="../../scripts/lib/Antiphon.Nightly/<X>.cs" Link="Scripts/<X>.cs"` lines next to the existing `RcTagBacking.cs` line, in the same pattern and without a wildcard.
- `Antiphon.Nightly.csproj` and every `.props` and `.targets` file are unchanged.
- The refresh is `scripts/land-graph-land-refresh.ps1`, run before `delegate.ps1 -Land` (orchestration-loop rule 11, CARD-1753). Never run a bare `-Update`.
- I did not recompute DA2C126C -> 9147CEA0 myself; `-Check` needs the land-audit evaluator build.

### 5. Red-row fixes

- f42aafe1 is a test-harness fix. It changes the reflection lookup from `Add(PSMemberInfo)` to `Add(PSPropertyInfo)` and adds a null guard that throws `ps-property-add`. No assertion was weakened, and the guard makes a missing method fail loudly.
- 39e0abd1 is a wrapper fix. `GetNewClosure()` rebinds `$script:`, so the checkout root is copied into a local first. Without it the root was empty. The `proposal-output-inside-checkout` guard was not relaxed, and the C# check is unchanged.

### 6. Shadow guarantees

- No production caller was added. The only consumers are the harnesses `scripts/test-rc-shadow-sample.ps1` and `scripts/test-rc-dirty-order.ps1`.
- `rc-forced-full.ps1` is untouched.
- These fields are constants: `EffectiveMode = "Full"`, `GateCredit = false`, `TrustGranted = false`, `TrustRecordWritten = false`, `IncrementalValidation = "Shadow"`, and in dirty order `TrustTransition = false` and `Activation = "off"`.
- `trustRecordPath` is discarded. The only file write is the caller-named, write-once proposal outside the checkout, as before.

## Advisories (cards, not land blockers)

**A-1. PSObject-wrapped dictionary elements are read differently.**
- Where: `RcPort.IsPs`, `Field` and `DirtyProp` (`scripts/lib/Antiphon.Nightly/RcPort.cs`).
- Failure: an array element that PowerShell wrapped in PSObject reaches C# as a PSObject. Examples are `@($x)` where `$x` came from a one-item `ForEach-Object`, or `Write-Output` items. The ps1's `-is [IDictionary]` saw through the wrapper. C# reads the adapted Hashtable properties instead, so the keys come back null.
  - Probe P3: the old preserved failure kind was `sample-outcome`; the new one is `unreadable-chunk-id`.
  - Probe P5: the old error was `carry-with-open-failure`; the new one is `invalid-attempt`.
  - Every path I traced fails closed: the input is refused, ineligible, or invalidates carries. I found no false trust or false carry, but the port is not behaviour-identical for this input class.
- Fix: unwrap `PSObject.BaseObject` when the base is not a PSCustomObject, and add a wrapped-element case to the corpus. Do this before S6c/S6d wire the library.

**A-2. The return shape changed.**
- Results are now typed objects with PascalCase properties. PowerShell property access is case-insensitive, so harness callers are unaffected. `ConvertTo-Json` of a returned proposal now emits `"EffectiveMode"` and similar keys, where the old output was `"effectiveMode"` (probe P9).
- The proposal file itself stays camelCase.
- Nothing serializes these results today. Pin the shape before wiring.

**A-3. The golden generator is not committed.**
- The fixture cannot be extended from the base the way it was made.
- My replayer (`M` materializer, about 40 lines) reproduced 183 of 184 cases. A committed generator, or the recorded command, would make the corpus auditable.

**A-4. Uncovered divergences, all fail closed.**
- A contract dictionary with a `Keys` entry: the old `Get-RcFullField` used shadowable `.Keys`. Both versions invalidate, but the reason lists differ.
- A non-integral `sequence`: the ps1 coerced it with `[int64]`; C# refuses it with `invalid-attempt`.
- An array-valued `key`: the ps1 gave `"a b"`; C# gives `"System.Object[]"`.

**A-5. The wrapper no longer loads `rc-forced-full.ps1`.**
- The old library dot-sourced `rc-forced-full.ps1`, so a caller of `rc-shadow-sample.ps1` got `Get-RcForcedFullDecision` transitively. The current consumers do not rely on it, and the harness is 12/12. A future caller must dot-source `rc-forced-full.ps1` itself.

## Review evidence

subjectTaskId: 3a5b76f5-0bf5-4b88-a5a8-c1ecc27b6334
reviewedSourceSha: ffa9e26a977d6b11d1d34e53e4ffb993e4566384
reviewedSourceClean: false (no SHA-validated checkpoint receipt; direct build-slot runs only)
ordinaryScopeCompleted: Full (CP-1 to CP-7, the guard batch, evidence-diff and merge-tree all executed at the reviewed SHA)
reviewRound: 1
reviewedRange: c6565bc95776c93a01109385d25420d19372afe8..ffa9e26a977d6b11d1d34e53e4ffb993e4566384
