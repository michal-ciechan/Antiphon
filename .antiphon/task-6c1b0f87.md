# Review 6c1b0f87: CARD-1711 census (round 1 of 3)

Subject: feat/card-task-8ac2299c, tip and tested commit 83a43ddec29fa4031394806df14e25ab05d27239 (base 1ef88240b3c5ee2c759db958e5fb0b635b1248cc).
Verdict: **one defect (D-1): a silent miss that base failed closed on.**

## D-1: a `\"` in a regular interpolated string's format clause brings back the A-1 silent miss

- **Where:** `scripts/test-rc-platform.ps1`, `Get-C1323LineStates`, the `$hole` pattern (`[^{}"''/@$]` code class), line 552.
- **Failure:** Roslyn 4.11 parses this file with no errors and puts `[NativeLinuxOnly("y")]` on `M`, a live marker:
  ```
  public sealed class ZetaTests
  {
      static string S(int x) { return $"{x:\"}"; } static int T = @"
  /* c".Length
          * 2; [NativeLinuxOnly("y")] public void M() { }
  }
  ```
  - Base gave `marker-noncanonical:…:7` (failed closed).
  - Tip gives **no problems at all** (silent miss).
  - The variants `… = $@"` and a format of `\"\"` also go silent.
- **Why:** the hole lexer does not know about format clauses. In a regular interpolated string, the text after a top-level `:` is string text, and `\"` there is an escape (Roslyn token `\"`, format `:\"`).
  - The hole lexer takes `\` as code and the `"` as the start of a string literal. That literal ends at the C# string's real closing quote.
  - The hole stays open and later closes on the method's `}` without hitting a stop character.
  - The outer body then ends at the `"` of `@"`, so the parity flips. The census sees code where C# has a verbatim string.
  - The `/* c` at the start of the next line opens a phantom block comment with AtStart=true. The `*`-led live marker line is then skipped silently.
  - `LostFrom` is never set, so no fail-closed line catches it.
- **Fix:** make `\` outside a literal unlexable in a hole: add it to the excluded class, `[^{}"''/@$\\]`.
  - C# hole code has no bare `\`, so the only cost is that a regular hole with a `\` escape in its format fails closed from that line on.
  - In a scratch copy, that one-character change turns all three shapes into fail-closed, and the real tree is unchanged (74/74/75, same digest).
  - Add the three shapes as fixtures that go red first. Optionally, lex format clauses properly instead.

## Verification at 83a43ddec

- **Build:** `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, one lease, exit 0.
- **TUnit run:** `/*/*/(ReleaseGatePlatformTests*)|(NativeLinuxPolicyWitnessTests*)|(LiveLaneRunnerTests*)/*`, one lease: total 13, failed 0, skipped 0.
- **`-Case C1686_LinuxOnlyCensus`:** 59/59 PASS. **`-Case LinuxOnlyWindowsExclusionPolicy`:** 6/6 PASS. One lease each.
- **Digests:**
  - `-PrintInputDigest` = 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7 (matches).
  - `Get-NightlyPolicyHash` = 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4 (matches).
- **check-evidence-diff** 1ef88240b..83a43ddec: commits=2, violations=0. **merge-tree** with origin/master 5a95ae7a5: clean.
- **Real tree:** base and tip census results are identical: ok, rows=74 markers=74 files=75, problems=0, same marker+problem digest D053E382…C0B9.

### Adversarial shapes (45, with Roslyn 4.11 Preview as the oracle)

Each shape was run through the census functions extracted from base and from tip.

- **Regressions (base failed closed, tip silent):** only the D-1 family, 3 shapes.
- **Base silent, tip fixed (fails closed):** 3 shapes, A-1 as claimed.
  - raw hole closing inside a nested raw string
  - `$@"{D["k"]}` quoted key
  - `$@"{'"'}` char literal
- **Base counted or failed closed, tip now counts correctly:** hole spanning lines, ternary with a `"/*"` string, CR-only file.
- **Base counted, tip now fails closed (acceptable):**
  - nested interpolated string in a hole
  - `//` comment inside a multi-line hole
  - `@class` verbatim identifier in a hole
  - marker after U+0085 inside a block comment that opens at the start of its line
- **Same result on both:**
  - nested braces, `{{ }}`, format specifiers and alignment
  - `'}'` and `"}"` in holes
  - raw strings with 1, 2 and 3 dollars
  - `$@` and `@$` dead markers
  - U+2028 in a verbatim string (stays in string state)
  - CRLF, BOM, no trailing newline
  - unterminated hole or raw string at EOF
  - lambda and switch-expression holes
  - `HH':'mm` format
  - a lambda attribute inside a hole (marker-unparsed)
- **No dead marker was counted by either version.**

### Performance (tip; nothing hung or threw)

| Input | Tip | Base |
|---|---|---|
| Largest real file, RemoteScriptContractTests.cs (559 KB) | ~2.6 s | 2.1 s |
| 5 MB synthetic | 18.9 s wall | 14.6 s |
| 64k dollar run | 0.45 s | 13.9 s |
| 128k dollar run | 0.9 s | not run |
| 1M dollar run | 1.8 s | killed at 60 s |
| 1M `{` in code / in an interpolated string | 1.1 s / 0.6 s | not run |
| 100k nested holes, plain / raw / verbatim | 1.5 s / 0.3 s / 2.9 s | not run / not run / 0.27 s |
| 100k multi-line holes | 3.4 s | 1.9 s |
| 1M `"` / 1M `@` | 0.24 s / 2.2 s | not run |
| Unterminated hole / raw string at EOF | 0.9 s / 1.6 s | not run |

### Mutants (C1686 case, one lease each; file restored with `git checkout --`)

| Mutant | Failed rows | Count |
|---|---|---|
| M1: raw-hole check dropped | raw-interpolation-hole-close | 58/59 |
| M2: hole never matches | interpolation-hole-opener, quotes-in-sync, verbatim-interpolated-glob | 56/59 |
| M3: split on `\r?\n` only | u2028, u2029, u0085, lone-cr, line-separator-opens-string | 54/59 |
| M4: lookbehind removed | dollar-run-is-linear (59.6 s) | 58/59 |

## Advisories

- **A-1:** M2 leaves `verbatim-interpolation-hole-quote` green, because a dropped hole makes the line lost and so fails it closed. No fixture separates a lexed hole from a lost hole on that shape. That is fail-closed, so no action is needed.

PCs stay pending for SourceLanding Mutation.
