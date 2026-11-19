# Review: CARD-1820 S5 tag and promotion backing rule (D-5), round 1, Final

Subject: `origin/feat/card-task-e922a94b` at `ff4b4a7e6dbfc352a38c2221f57bb62f60a18d22`. Base `9bcd61113f631ec58f158ff3fb637736fb786fd4`, 8 commits. Author: Grok. Reviewer: Claude Opus 5.5 (task ec3cbbac).
Verdict: **3 blockers** (D-1 to D-3), 6 advisories. The plan rows are green, the receipt is valid, and the three requested mutants are each caught by a named witness.

## Adversarial scenario table

I wrote a scratch harness (outside the repo) that compiles `scripts/lib/Antiphon.Nightly/RcTagBacking.cs` at the subject SHA and calls `RcTagBacking.Test`.

**Result: 82 scenarios tried. 5 gave a dangerous wrong answer (1 false `chain-to-full` and 4 escaped exceptions). One more refused for the wrong reason.** No input produced a wrong `full`. `gateCredit` was false on every answer.

| # | Scenario | Answer | OK |
|---|---|---|---|
| 01 | exact all-executed Windows Full of the tagged SHA | full | yes |
| 02 | Full of a different SHA that shares a 39-char prefix | not-backed(missing-link) | yes |
| 03/04 | exact Full with platform `linux` / `Windows` | not-backed(not-windows) | yes |
| 05 | exact Full not all-executed | not-backed(anchor-not-all-executed) | yes |
| 06 | exact Full with an open failure (failed chunk) | not-backed(open-failure) | yes |
| 07 | exact Full not admitted | not-backed(link-not-admitted) | yes |
| 08 | exact Full with carriedCount 3 | not-backed(anchor-has-carries) | yes |
| 09/10 | Full flagged seed; disposition `seed` | not-backed(seed-is-not-credit) | yes |
| 11 | two links both claim the tagged SHA | not-backed(chain-invalid) | yes |
| 12 | chain, anchor 1 day old, trusted 2 | chain-to-full(3…3,1.00:00:00.0000000) | yes |
| 13 | anchor exactly 7 days old | not-backed(chain-older-than-seven-days) | yes |
| 14 | anchor 6d23h59m old | chain-to-full(…,6.23:59:00.0000000) | yes |
| 15/16 | anchor 7d + 1 tick / 7d - 1 tick | expired / chain-to-full(…,6.23:59:59.9999999) | yes |
| 17/18 | anchor 1 s / 30 d in the future | not-backed(full-anchor-future) | yes |
| 19/20/21 | `+05:00` offset; no zone; utcNow with `+00:00` | not-backed(chain-invalid) | yes |
| **22** | **anchorCompletedAtUtc `"00:00Z"` (no date)** | **chain-to-full(…,0.00:00:00.0000000)** | **NO (D-2)** |
| 23 | anchor time null | not-backed(full-anchor-time-unknown) | yes |
| 24/25 | anchor on Linux; anchor not all-executed | not-windows / anchor-not-all-executed | yes |
| 26/27 | anchor seed flag; anchor disposition seed | seed-is-not-credit | yes |
| 28/29 | head not admitted; head on Linux | link-not-admitted / not-windows | yes |
| 30/31/32 | middle link: shadow discrepancy / not admitted / open failure | each refused with its reason | yes |
| 33 | middle link missing | missing-link | yes |
| 34 | envelopes reversed (3-link chain) | chain-to-full | yes |
| 35/36 | 2-cycle; self-referencing link | chain-cycle-or-depth | yes |
| 37 | duplicate runId | chain-invalid | yes |
| 38/39 | chain length 0; length 1 with no origin | chain-invalid / carry-without-anchor | yes |
| 40/41/42 | anchor off the parent path; anchor with the same prefix; anchor a descendant | anchor-not-ancestor | yes |
| 43/44 | trusted 1 / 0 | shadow-trust-absent | yes |
| 45/46/47 | trusted -5 / 2.0 / "2" | chain-invalid | yes |
| 48 | anchor claims gateCredit | gate-credit-claimed | yes |
| 49/50 | uppercase SHA; 12-char tagged SHA | chain-invalid | yes |
| 51–54 | unknown disposition; extra field; missing field; wrong envelope format | chain-invalid | yes |
| 55/56 | duplicate name (trusted 0 then 2); duplicate through the escape `admitted` | chain-invalid | yes |
| 57–64 | comment; trailing comma; depth 5000; BOM; number for a string; null; trailing garbage; two documents | chain-invalid | yes |
| **65/66** | **escaped lone surrogate `\uD800` in a property name / runId value** | **InvalidOperationException escapes** | **NO (D-1)** |
| **67/68** | **raw lone UTF-16 surrogate in a string / anchor time** | **ArgumentException escapes** | **NO (D-1)** |
| 69/70 | 1 MiB + 1 input; root array | chain-invalid | yes |
| 71–73 | FormatAge and time parse under the ar-SA culture | invariant `d.hh:mm:ss.fffffff` | yes |
| 74 | shadow head, trusted 2 | chain-to-full | yes |
| 75 | anchor SHA equals the tagged SHA through a carry | chain-invalid | yes |
| 76 | caller-supplied utcNow 29 d in the past makes a 30-day anchor 1 day old | chain-to-full | as coded (A-1) |
| 77/78 | exact Full dated in the future / with no time | full | as documented (A-2) |
| 79 | `"11/18 00:00Z"` | chain-invalid | yes |
| 80/81 | `"18 Nov 2026 00:00:00Z"`, `"2026-11-18Z"` accepted | chain-to-full | benign (D-2 fix covers it) |
| 82 | utcNow `"00:00Z"` uses the host's date | not-backed for the wrong reason | refused (D-2) |

## Blockers

### D-1: some parse errors throw instead of answering not-backed
- **Where:** `scripts/lib/Antiphon.Nightly/RcTagBacking.cs:75-89`. `Test` catches only `ChainInvalid` and `JsonException`.
- **Failure:** an escaped lone surrogate (`"\uD800"`) in any property name or string value throws `InvalidOperationException` ("Cannot read incomplete UTF-16 JSON text…") from `Utf8JsonReader.GetString` (line 225) or `JsonElement.GetString` (line 368). A raw lone surrogate char in the input throws `ArgumentException` ("Cannot transcode invalid UTF-16…") from `JsonDocument.Parse` (line 242). Cases 65–68.
- **Why:** the D-5 contract and the brief require every parse error to answer `not-backed(reason)`. An escaped exception is a non-answer, and a future tag caller could treat it as "no answer, proceed".
- **Fix:** in `Test`, map every exception (at least `InvalidOperationException` and `ArgumentException`) to `not-backed(chain-invalid)`. Add a witness with an escaped `\uD800` and one with a raw `\uD800`.

### D-2: a timestamp with no date can give chain-to-full
- **Where:** `ReadUtc`, `RcTagBacking.cs:400-408`. It uses `DateTimeOffset.TryParse(..., RoundtripKind)` and only checks for a trailing `Z`.
- **Failure:** `anchorCompletedAtUtc: "00:00Z"` parses with the evaluating host's current date. Case 22 answers `chain-to-full(<anchor>,0.00:00:00.0000000)` even though the anchor's real date is absent. The anchor could be any age. `utcNow` has the same leniency (case 82).
- **Why:** this is an input that wrongly yields chain-to-full, through a fail-open parse. The anchor age decides the 7-day bound.
- **Fix:** `DateTimeOffset.TryParseExact` with a closed ISO-8601 pattern set (`yyyy-MM-dd'T'HH:mm:ss'Z'` and a `.fffffff` variant) and `DateTimeStyles.AssumeUniversal | AdjustToUniversal`. Refuse anything else as `chain-invalid`. Add a witness: `"00:00Z"` gives not-backed.

### D-3: a caller-supplied trust count unlocks the chain form
- **Where:** `RcTagBacking.cs:253` reads `trustedShadowCuts` as a bare non-negative integer from the chain document. `:182` compares it to 2.
- **Failure:** any producer that writes `"trustedShadowCuts": 2` unlocks `chain-to-full` (cases 12, 14, 74). Nothing derives the count from cut records, removes a cut counted twice, or proves the count. The same cut cannot even be detected as double-counted, because only the integer is supplied.
- **Why:** the brief lists "a seed or caller-supplied trust count that can grant" as blocker-grade. D-5 and OD-2 (default "Full only") allow the chain form only once two trusted shadow cuts actually exist under CARD-1836 D-8. Today that condition is an unverified input.
- **Fix (either):**
  - (a) Hard-disable the chain form with a constant until a CARD-1836 D-8 producer exists. This matches the OD-2 default.
  - (b) Replace the integer with an array of trusted-cut records (`runId`, `sourceSha`, admission digest). Remove duplicates by runId and require each to be an admitted Windows shadow cut with no discrepancy, then count the distinct ones. Add a witness: one cut listed twice counts once and answers `shadow-trust-absent`.

## Advisories (go to cards, do not hold the land)

- **A-1 Trust boundary.** Ancestry is never checked against real Git. It comes from the document's `commits` array. `admitted`, `allExecuted`, `platform` and `utcNow` are also taken from the document (case 76: an old utcNow rejuvenates a 30-day anchor). Nothing authenticates the envelope or binds it to the CARD-1836 admission ledger. Today the only thing stopping a forged envelope is that nothing calls this rule. Before any caller exists:
  - build `commits` from `git rev-list --parents`;
  - read envelopes from the ledger and check their digests;
  - take the clock from the host.

  `docs/release-gates.md` should state this boundary. No producer of `antiphon-rc-tag-chain-v1` exists, so there is no producer-to-recipient test. That is acceptable only while no recipient exists.
- **A-2 Exact Full ignores its time.** An exact Full with a future or null completion time still answers `full` (cases 77/78). This matches the doc ("no seven-day age limit"), but a future-dated record means clock or ledger corruption. Consider refusing it.
- **A-3 7-day wording.** The plan's D-5 wording is "no older than 7 days" / "≤7-day Full". The code and release-gates use strictly under 7 days. The code is the conservative side, so align the plan text.
- **A-4 Only walked links are checked.** An open failure or shadow discrepancy on an envelope that is not on the walked chain is ignored, for example a later run of an intermediate commit. The plan's risk section says a discrepancy invalidates the whole chain since the anchor. Decide whether "no open failure" means chain links only.
- **A-5 Witness gaps.** The author's 13 witnesses miss `not-windows`, `anchor-not-all-executed`, `anchor-has-carries`, `full-anchor-future`, `chain-cycle-or-depth`, duplicate-name and other `chain-invalid` cases. My table covers them; port several into `RcTagBackingTests`.
- **A-6 Compiled but unreferenced.** `RcTagBacking.cs` sits in `scripts/lib/Antiphon.Nightly/`, so the project's default Compile glob builds it into the Nightly assembly that `nightly-core.ps1` loads. It is compiled there but unreferenced, and the pin digest is unchanged (CP-7 pin-ok). Shadow safety (`git grep`): only the docs, the plans, the test csproj Compile include and `RcTagBackingTests` mention it. No reference from `publish-release.ps1`, release scripts, `server/` or tag paths.

## Evidence

- **Checkpoint tool run `20261119-233058-3ee6`** at ff4b4a7e6, plan `docs/superpowers/plans/2026-11-19-incremental-s5-checkpoints.md`, all 7 rows. Verdict GREEN. `unlisted: none`, 1 build, wall 16m03s. `validate --expected-source-sha ff4b4a7e6…` gives `CHECKPOINT SOURCE VALID rows=7`.
  - CP-1 RcTagBackingTests executed=13 passed=13
  - CP-2 TestClassificationPolicyTests 42/42
  - CP-3 DerivedImpactIndexTests 27/27
  - CP-4 guard batch 39/39 (C1729 `VIOLATIONS 0 LISTED 162`)
  - CP-5 land-graph `-Check` exit 0
  - CP-6 check-evidence-diff exit 0
  - CP-7 pin-ok: stated = computed `063da62b…c8d1`
- **Unlisted builds, with reasons:**
  - `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`, which CP-3 needs (as the plan states);
  - `tools/Antiphon.Checkpoints` (the tool itself);
  - the scratch adversarial harness;
  - 4 scratch mutant TUnit projects.

  Each ran under its own build-slot lease.
- **Mutants** (scratch copies of `RcTagBacking.cs` + `RcTagBackingTests.cs`, TUnit 1.44.0, each built and run under its own lease):
  - control: 13/13 green
  - M1 `age > MaxAnchorAge`: 1 failed, `ExactlySevenDaysIsNotBacked`
  - M2 seed check removed: 1 failed, `SeedAnchorIsNotBacked`
  - M3 admitted check removed: 1 failed, `UnadmittedLinkIsNotBacked`

  Nothing was reverted in the repo, because the mutants never touched it. Mutation as a stage stays waived (operator 2026-11-12). These mutants answer the brief's witness question only.
- **Collision history.** 7d15328b changed `tests/derived-impact-index.json`, then dab5c171 restored it. The final tree's index is byte-identical to base, and CP-3 is green at the tip. The land-graph records (manifest/evaluation `BE21100D…`) are consistent: CP-5 `-Check` exit 0.
- **merge-tree against master `c83d14d4858a66e41e2c51c4631869db99d0a2c7`:** clean, tree `6a3c2e535025206cb301dd0346c165730f2b44f4`. No replay needed. I regenerated the derived index on an export of that merged tree. The only change was the `.antiphon/fixtures/card-1521/…` edge, an ignored path that `git archive` omits. Nothing related to RcTagBacking.
- **Docs.** release-gates D-5 text matches the code on these points: strictly under 7 days, Windows all-executed Full, every link admitted, no discrepancy or open failure, ancestor anchor, no seeds, `gateCredit` false, no tag/publish caller. Beyond that: A-3 (plan wording) and A-1 (the trust boundary is not stated).
- **Process deviations.** Three slot-gated activities started while the 1-minute load was above 16: the harness build at 39.6, the checkpoint `start` at 22.0, and the merged-tree index regeneration at 30.7. The later mutant builds and runs were gated below 16. The plan's TUnit rows carry no `--timeout 570s`; the scratch mutant runs used it.
