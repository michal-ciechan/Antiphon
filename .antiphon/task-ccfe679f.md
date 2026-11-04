# Review ccfe679f: CARD-1707 census block-comment line states (round 1 of 3)

Subject: feat/card-task-3a179edb, red 167f6930637bfcf27562bd4117f15d6d701929e5, tip efd610c937db23899a4bac85e731419f538bc1ca, base da0106cbe7c500bade6659d4700b4a6cabe2b148.
Verdict: CLEAN, no defects. The tip is never weaker than base: on every probed shape, wherever base failed closed the tip fails closed too.

## 1. Real tree (census run through an AST-extracted harness for each script)
| tree | script | result |
|---|---|---|
| base da0106cbe | base | ok rows=74 markers=74 files=75, no problems |
| tip efd610c93 | tip | ok rows=74 markers=74 files=75, no problems |
| master f93fdaf5c | master | ok rows=74 markers=74 files=75, no problems |
| tip merged onto master (merge-tree 82249f688, clean) | tip | ok rows=74 markers=74 files=75, no problems |

The row, marker and file lists diff empty for base vs tip and for master vs merged.

## 2. Adversarial shapes (44 shapes, base and tip scripts, with no row and with a class row)
- Tip fails closed where base was **silent**: 01b star after a closed comment, 02b `//` with `/*` then a `*` code line, 03b and 04c a `*` marker inside a verbatim or raw string, 05 and 05c char `'"'` / `'\''` then a `*` code line, 08b `///` with `/*`, 09b and 09c CRLF, 11c `*` line at EOF with no newline, 13 close then reopen on the same line, 13d tab-led `*` code line.
- Tip fails closed where base **counted** the marker: 03 (a marker inside a verbatim string; base said unparsed), 06b interpolated verbatim string whose hole holds a quote, 07 `#region ... /*`, 12, 12c and 12d (unterminated `/*`, raw or verbatim string). These are false positives against C# semantics, but none occurs in the real tree.
- Base and tip give the same result on everything else: after-closed-block, linecomment-opener-canon, raw-opener, raw interpolated hole, char `'/'`, interpolation hole with a quote, `#if false`, `#region it's`, `///` marker, CRLF star-interior, BOM (2), EOF without newline (2), unterminated regular string and char literal, `/** */` doc, a block closed then `//` token, pointer deref, `"*/"` inside a comment.
- No shape where base failed closed and the tip passes or counts a commented-out marker.

## 3. Malformed input and performance (Get-C1323LineStates at the tip)
- No throw on any malformed shape. An unterminated `/*`, raw string or verbatim string at EOF fails closed for any non-star line that carries the token.
- Largest test file (RemoteScriptContractTests.cs, 559 KB): 0.93 s. A realistic 5.6 MB synthetic: 2.0 s. A 5 MB `/*****/` banner file: 2.5 s.
- Pathological 5 MB inputs: unterminated `/*` 34 s, raw 21 s, verbatim 12 s (900k and 600k lines). These are linear: the time is the PowerShell per-line state loop, not regex backtracking.
- Whole census on a 5 MB file: base 13.0 s, tip 9.4 s.

## 4. Mutants (scratch copy of the tip, one run each under its own lease)
- Raw handling dropped: red, raw-string-opener (43/44).
- Verbatim handling dropped: red, verbatim-interpolated-glob, verbatim-multi-line-opener and verbatim-string-marker-line (41/44).
- `$first = $true`: red, block-comment-mid-line-open (43/44).
- Red commit 167f69306: C1686 35/44, the 9 failures the Code report claims.

## 5. Runs at the tip (one build-slot lease each, native output, UseAppHost=false)
- `-Case C1686_LinuxOnlyCensus`: 44 passed, 0 failed.
- `-Case LinuxOnlyWindowsExclusionPolicy`: 6 passed, 0 failed.
- `dotnet run --project tests/Antiphon.Tests -- --treenode-filter '/*/*/(ReleaseGatePlatformTests*)|(NativeLinuxPolicyWitnessTests*)|(LiveLaneRunnerTests*)/*'`: 13 succeeded, 0 failed, 0 skipped (fresh TRX, 2m32s, cold build in the lease, held 320 s).
- `-PrintInputDigest` = 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7 (matches).
- `Get-NightlyPolicyHash` = 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4 (matches the stored policyHash).
- `check-evidence-diff` base..tip: commits=2, entries=0, violations=0.
- `git merge-tree --write-tree origin/master(f93fdaf5c) efd610c93`: clean, tree 82249f688.
- PowerShell 5.1: not applicable. The script carries `#requires -Version 7.0`, and the new regex constructs (named groups, `\k<q>`, `\z`) are plain .NET.
- Not run: the whole Unit lane. The brief accepted Interim scope for this master-only work.

## Advisories (not defects; base behaves the same)
- A-1: The `Get-C1323LineStates` comment says an interpolation-hole misread "only ever fails closed". That is not always true. Shape 06c is `$@"{d["k"]}` with the next line starting `/*` inside the string. The lexer opens a block comment at the start of that line (AtStart=true) and it runs to the next `*/` or EOF, so a real `*`-led code line carrying the token is skipped silently. Base skipped every `*` line, so this is no regression; consider rewording the comment.
- A-2: C# ends a `//` comment at U+2028, U+2029 and U+0085. The census splits on `\n` only, so `// c<U+2028>[NativeLinuxOnly("x")]` is missed silently, in base and tip alike.
- A-3: The `\$*` prefixes go quadratic on long `$` runs: 8k takes 0.26 s and 32k takes 1.9 s. That is unrealistic in C#; an atomic `(?>\$*)` would remove it.

No PCs exist for this card; none were discharged.
