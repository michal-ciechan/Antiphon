# Review 4c271f2f: CARD-1820 S5 follow-up, round 1 (Final, blockers only)

Subject: `origin/feat/card-task-4940cf9f`, tip `09e2499859112e55f1e93eaede683af498b7e323`. Base `cec1ed34f6c0b547ead0eb945c18d19f05f9a039`. The three commits are red `e29c5881c`, predicate `62e8f4945` and index `09e249985`. Code owner task `4940cf9f-3f5d-437d-b11c-9d4f2d04af9f`. The author is Grok, so the reviewer (Claude) is from a different company.

**Verdict: CLEAN.** No blockers. Five advisories (A-1 to A-5) are below and go to cards.

## 1. False FULL attack (dangerous first)

I loaded the subject `RcTagBacking.cs` byte-identical, compiled into a scratch assembly, and called the public `RcTagBacking.Test` through reflection. I ran **38 extra scenarios**. Every one matched the claimed rule. For each scenario, `Answer == "full"` held exactly when `Kind == Full`. `ChainToFull` never appeared, and `GateCredit` was false on every result.

| Scenario | Answer |
|---|---|
| completed == utcNow+5m, at second and at 7-digit tick precision | full |
| utcNow+5m+1 tick (`.0000001Z`), including a utcNow with a 7-digit fraction | not-backed(full-time-in-future) |
| utcNow+5m-1 tick (`04:59.9999999Z`) | full |
| utcNow+5m+1s | not-backed(full-time-in-future) |
| completed null | not-backed(full-time-unknown) |
| completed `""`, 8 fraction digits, offset, lowercase `z`, leap second `:60`, date-only, or a number | not-backed(chain-invalid) |
| utcNow missing, null, empty, space form, offset form, or no `Z` | not-backed(chain-invalid) |
| duplicate `utcNow` key | not-backed(chain-invalid) |
| utcNow `9999-12-31T23:59:59Z` (utcNow+skew overflows) | not-backed(chain-invalid), fail-closed |
| utcNow `9999-12-31T23:49:59Z`, completed `23:54:59Z` | full (the documented A-5 behaviour) |
| utcNow year 1, completed +5m / +5m+1s | full / full-time-in-future |
| completed year 1 (exact Full has no age bound, as documented) | full |
| future exact Full that is not all-executed, gateCredit, or seed | the earlier refusal wins (anchor-not-all-executed, gate-credit-claimed, seed-is-not-credit) |
| chain anchor == utcNow, at 7d-1 tick | chain-form-disabled-until-trusted-cuts |
| chain anchor utcNow+1 tick and +4m (no skew) | not-backed(anchor-in-future) |
| chain anchor null time | not-backed(anchor-time-unknown) |
| chain anchor at exactly 7d | not-backed(chain-older-than-seven-days) |
| chain utcNow at the 9999 maximum (no skew, so no overflow) | chain-form-disabled-until-trusted-cuts |

The clock is optional nowhere. `utcNow` is a required key, read through `RequiredString` and `ReadUtc`, so an absent or malformed clock is `chain-invalid` and never `full`. The skew is a compile-time constant, so a negative skew cannot be supplied.

**Document clock (A-5, still advisory).** An attacker-controlled document defeats the future check by setting `utcNow` late. That is the 9999-minus-10-minutes row above, and the same holds for any late `utcNow`. Both the docs ("The clock is the document field `utcNow`, not the host clock") and the code say so. It is still acceptable now: no caller exists, the chain form is compiled off, and the exact-Full verdict needs an admitted, all-executed Windows envelope whose admission happens upstream. Before any tag, publish or promote caller is wired, that caller must supply `utcNow` from its own clock and must not take it from the envelope author.

## 2. Typed Kind and construction surface

- `RcTagBackingResult` is sealed. Its constructor is internal, and so are `Full`, `Chain` and `NotBacked`. Reflection found no public constructor. All six properties are get-only, with readonly compiler backing fields and no `init` setters. There are no mutable collections and no public fields. The only public methods are the getters and `FormatAge`, which is pure.
- No `InternalsVisibleTo` exists in `scripts/lib/Antiphon.Nightly/`, the Nightly csproj or the root `Directory.Build.*`. The file is compiled twice: into the `Antiphon.Nightly` assembly, which `nightly-core.ps1` loads into PowerShell, and by `<Compile Include>` into `Antiphon.Tests`. Internal access therefore exists only inside each of those assemblies. Neither has a production caller.
- `System.Text.Json` cannot deserialise a result: `Deserialize('{"Kind":0,"Answer":"full"}')` throws `NotSupportedException`.
- `Answer` remains as the display string. The docs say callers compare `Kind` and that `full-time-in-future` and `full-time-unknown` contain the letters `full`. A caller doing `Answer.Contains("full")` is therefore explicitly documented as wrong.

## 3. Docs and plan against code

`docs/release-gates.md` D-5 and the plan's V-1 match the code: the reason names `full-time-unknown`, `full-time-in-future`, `anchor-time-unknown` and `anchor-in-future`; `ExactFullClockSkewMinutes` 5; a time at the bound is full; the chain comparison is strict with no skew; `ChainFormEnabled` is a const false; utcNow comes from the document; depth 10000 and 100000 answer `chain-invalid`; and the constructor and factories are internal. Nothing contradicts. Minor omissions are A-3.

## 4. Callers and pins

- **Production callers.** A grep of `*.cs`, `*.ps1`, `*.csproj` and `*.json` finds no reference to `RcTagBacking` outside the class, its tests, the docs, plans and investigations, and the `Antiphon.Tests` Compile Include. The old names `full-anchor-future` and `full-anchor-time-unknown` survive only in `scripts/lib/rc-forced-full.ps1` and `scripts/test-rc-forced-full.ps1`, a separate forced-Full predicate with its own reasons. That is not a contract mismatch.
- **Pins.** All seven protected inputs of `test-live-lane-run.ps1` (plus the script itself) have identical blob ids at the subject tip, on master `b93f4b8b6` and in the merge tree. The `3162a7da…` digest therefore still matches on current master. CP-7 recomputed it at the tip: exit 0, `pin-ok`.

## 5. Mutation (operator waiver 2026-11-12 stands; run anyway as a scratch-copy probe)

Each mutant was a scratch TUnit project. It compiled one mutated copy of `RcTagBacking.cs` with the unmodified `RcTagBackingTests.cs` (TUnit 1.44.0, Shouldly 4.3.0) and ran all 51 tests. Each build and each run took its own build-slot lease.

| Mutant | Result | Killed by |
|---|---|---|
| M0 control (unmutated) | 51/51 green | (none) |
| M1 skew 5 minutes to 300 minutes | 2 red / 51 | ExactFullOneSecondPastFiveMinuteSkewIsNotBacked, DocumentUtcNowRefusesExactFullAfterIt |
| M2 drop the full-time-unknown refusal (null time falls through to Full) | 1 red / 51 | ExactFullMissingTimeIsNotBacked |
| M3 public result constructor | 1 red / 51 | ResultConstructorAndChainFactoryAreInternal |
| M4 boundary `>` to `>=` | 1 red / 51 | ExactFullAtFiveMinuteSkewStaysFull |

All mutants were killed. Every new witness can go red.

## 6. Checkpoint rerun at the reviewed SHA

Run `20261120-033423-97dc` on the plan `docs/superpowers/plans/2026-11-20-incremental-s5-followup-checkpoints.md`: verdict GREEN, exit 0, `source=09e2499859112e55f1e93eaede683af498b7e323 state=clean buildSource=verified`. `validate --expected-source-sha` printed `CHECKPOINT SOURCE VALID rows=7`.

| CP | Result | Floor |
|---|---|---|
| CP-1 RcTagBackingTests | executed 51, passed 51 | 51 |
| CP-2 TestClassificationPolicyTests | executed 42, passed 42 | 42 |
| CP-3 DerivedImpactIndexTests | executed 27, passed 27 | 27 |
| CP-4 guard batch (classification guard, slow-test registry merge, slow-test tripwire, native Linux policy, mechanical PC contract, TaskPlatformGuidance, C1729 layering, lane category) | executed 39, passed 39; `VIOLATIONS 0 LISTED 162` | 39 |
| CP-5 land-graph -Check | exit 0 | n/a |
| CP-6 check-evidence-diff cec1ed34f..HEAD | exit 0 | n/a |
| CP-7 pin digest + policyHash | exit 0, pin-ok | n/a |

The brief's guard batch is fully inside CP-2 and CP-4.

Unlisted runs, each with its reason: the scratch mutation and probe builds and runs listed in sections 1 and 5, all under build-slot leases. One process note: the M0 control run started while the 1-minute load was 42.5, because the first command printed the load without gating on it. Every later build and run waited for a load below 16. A first `start` attempt (`20261120-033402-c92b`) died before any row ran: it was launched inside a build-slot lease, the detached executor inherited the slot's TEMP directory, and that directory was removed when the lease released. The run was relaunched outside the lease and the tool leased each row itself. This is A-4.

**Merge-tree.** `git merge-tree --write-tree origin/master HEAD` against `b93f4b8b606c172f0315e3db1d0ca1946a5a7a24` is clean (tree `472f9bbe`). Master also edits `tests/derived-impact-index.json`, but in hunks that do not overlap. The Merge step's index regeneration owns any count drift after the land.

## Advisories (to cards; they do not hold the land)

- **A-1. Enum default is Full.** In `RcTagBackingKind`, `Full = 0`, so `default(RcTagBackingKind)`, an uninitialised field or `(RcTagBackingKind)0` reads as Full. Put `NotBacked` (or `Unknown`) first so that the zero value fails closed before any caller exists.
- **A-2. Document clock (carried A-5).** A late `utcNow` in the document defeats both future checks. The caller that gets wired later must own the clock.
- **A-3. Undocumented overflow.** A `utcNow` within 5 minutes of `DateTimeOffset.MaxValue` makes `utcNow + skew` throw. The exception is caught and answers `chain-invalid`, which fails closed, but D-5 attributes `chain-invalid` only to malformed or reader-error input. A one-line doc note or an explicit guard would make that exact.
- **A-4. Checkpoint start inside a build-slot lease.** Wrapping `dotnet run … -- start` in `scripts/build-slot.ps1` gives the detached executor a TEMP that is deleted when the lease releases (`DirectoryNotFoundException` in `PwshPrivateCache.WriteOwner`). The docs do not say to start the tool outside the lease, or the start should detach with its own TEMP. This is tooling, not the subject.
- **A-5. Windows unobserved.** As the plan states, `C1210_Windows_linux_only_outcomes_are_exact` skips on Linux, and the Windows parse behaviour of `:60` is still unobserved.
