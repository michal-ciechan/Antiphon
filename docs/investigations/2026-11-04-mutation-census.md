# Mutation sweep: C1323 Linux-only census chain (CARD-1694, CARD-1707)

Task a41890aa (Mutation, Worktree, unsourced: no SourceLanding operation on the task). Subject
`Get-C1323LineStates` and `Get-C1323LinuxOnlyCensus` in `scripts/test-rc-platform.ps1`, the
`C1686_LinuxOnlyCensus` harness case (44 named assertions) and the `ReleaseGatePlatformTests` wrapper.

- Mutated source L = master `2e07f8d38bf520817bc25f317835831141825c69` (HEAD for every run; tracked
  tree clean before and after every mutant; `scripts/test-rc-platform.ps1` sha256
  `3a2b748ccfa39849fbef9153df5c34cd69332f79114ed77cf3ef177eb694ca83` = `HEAD:` blob after restore).
- Host: server2 (Linux). Every harness run and every wrapper run took its own build-slot lease.
- Method: an exact-string edit of `scripts/test-rc-platform.ps1` (each find string asserted to occur the
  stated number of times), then `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case
  C1686_LinuxOnlyCensus` and, for most mutants, `-Case LinuxOnlyWindowsExclusionPolicy` (the real tree),
  then `git checkout -- scripts/test-rc-platform.ps1` and a clean-diff check. The wrapper ran from one
  `dotnet build tests/Antiphon.Tests -c Debug -p:UseAppHost=false` (0 errors) as `--no-build` runs.

## Baseline and restored green

| Run | Baseline (L) | Restored (L) |
|---|---|---|
| C1686_LinuxOnlyCensus harness | 44 passed, 0 failed, 44 rows | 44 passed, 0 failed, 44 rows |
| LinuxOnlyWindowsExclusionPolicy harness | 6 passed, 0 failed, 6 rows | 6 passed, 0 failed, 6 rows |
| Wrapper `ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)\|(LinuxOnlyWindowsExclusionPolicy*)` | total 2, succeeded 2 | total 2, succeeded 2 |
| Real-tree census | rows=74 markers=74 files=75 problems=0 | rows=74 markers=74 files=75 problems=0 |

## Results

C1686 counts are passed/44. "Policy" is the real-tree `LinuxOnlyWindowsExclusionPolicy` case (6 rows).

| ID | Mutation (current code) | C1686 | Red fixtures | Policy | Verdict |
|---|---|---|---|---|---|
| PC-1 (1694) | code-line comment clause loses `-and -not '\*/.*token'` | 40/44 | block-comment-prefix, empty-block-comment-prefix, indented-block-comment-prefix, exempt-file-block-comment-prefix | 6/6 | killed |
| PC-2 (1694) | `if ($exempt) { continue }` (whole-file exempt skip) | 40/44 | exempt-file-stacked-marker, exempt-file-attribute-list-line, exempt-file-string-before-marker, exempt-file-block-comment-prefix | 6/6 | killed |
| PC-3 (1694) | string reference form widened to `"[^"]*\btoken\b[^"]*"` | 43/44 | exempt-file-string-before-marker | 6/6 | killed |
| PC-4 = M-l1 | drop the `typeof(...)` reference form | 43/44 | exempt-file-references-pass | 5/6 (markers-and-rows-agree: 7 real lines) | killed |
| M-l2 | drop the `new X(` form | 1/44 | 43 (every tree carries `new ...NativeLinuxOnlyAttribute(` in the exempt attribute file) | 5/6 | killed |
| M-l3 | drop the `class X` form | 1/44 | 43 (same base file) | 5/6 | killed |
| M-l4 | drop the `public X(` constructor form | 1/44 | 43 (same base file) | 5/6 | killed |
| M-l5 | drop the plain-string form | 43/44 | exempt-file-references-pass | 5/6 | killed |
| M-a | `*`-led line is a comment line in code mode | 39/44 | split-multiplication, nested-block-comment, string-literal-opener, char-literal-quote, verbatim-multi-line-opener | 6/6 | killed |
| M-b | block comment never closes (`(?:\*/\|\z)` -> `\z`) | 41/44 | block-comment-star-interior, nested-block-comment, comments-are-not-markers | 6/6 | killed |
| M-c | regular-string token removed from the lexer | 43/44 | verbatim-interpolated-glob | 5/6 | killed (see note 1) |
| M-d | verbatim-string token removed | 41/44 | verbatim-interpolated-glob, verbatim-multi-line-opener, verbatim-string-marker-line | 6/6 | killed |
| M-e | raw-string token removed | 43/44 | raw-string-opener | 6/6 | killed |
| M-f | char-literal token removed | 44/44 | none | 6/6 | **survivor: missing fixture** |
| M-g | `$first = $true` (every comment opened at line start) | 43/44 | block-comment-mid-line-open | 6/6 | killed |
| M-h1 | unstarred interior canonical line counted as a marker | 42/44 | block-comment-interior, interpolation-hole-opener | 6/6 | killed |
| M-h2 | unstarred interior line silently skipped | 41/44 | block-comment-interior, block-comment-mid-line-open, interpolation-hole-opener | 6/6 | killed |
| M-i | CRLF normalisation dropped | 44/44 | none | 6/6 | **survivor: equivalent** |
| M-j1 | census reads state `[$i + 1]` (all three reads) | 44/44 | none | 6/6 | **survivor: missing fixture** |
| M-j2 | census reads state `[$i - 1]` | 38/44 | block-comment-interior, block-comment-star-interior, verbatim-string-marker-line, raw-string-opener, interpolation-hole-opener, comments-are-not-markers | 6/6 | killed |
| M-j3 | lexer line advance `-le $m.Index` -> `-lt` | 44/44 | none | 6/6 | **survivor: equivalent** |
| M-j4 | lexer mark loop `-lt $end` -> `-le` | 44/44 | none | 6/6 | **survivor: equivalent** |
| M-k | block-mode "closed before the token" check dropped (`if ($true)`) | 43/44 | block-comment-close-prefix | 6/6 | killed |
| M-m | single-line token skip removed | 44/44 | none | 6/6 | **survivor: equivalent** |
| M-n | `$first` only at column 0 (indent not allowed) | 42/44 | block-comment-star-interior, comments-are-not-markers | 6/6 | killed |
| M-p | `/*`-led interior line no longer accepted as comment | 43/44 | comments-are-not-markers | 6/6 | killed |
| M-q | string-mode lines silently skipped | 43/44 | verbatim-string-marker-line | 6/6 | killed |
| M-r | `//` comment-line clause dropped | 0/44 | all 44 | 5/6 | killed |
| M-s | multi-line strings marked `code` | 43/44 | verbatim-string-marker-line | 6/6 | killed |
| M-t | exempt strip dropped on a block-mode line that closes before the token | 44/44 | none | 6/6 | **survivor: missing fixture** |
| M-u | exempt strip dropped on a string-mode line | 44/44 | none | not run | **survivor: missing fixture** |

All fixture names above carry the `c1686-fail-closed-` or `c1686-` prefix. Review 4af5405a saw
PC-1..PC-3 at 29/34, 30/34 and 33/34 before the line-state rewrite; on the current code they are
40/44, 40/44 and 43/44.

Wrapper red (each one `--no-build` run under its own lease, assertion failures with
`C487 HARNESS EXIT CODE: 1` in the Shouldly message, never 0 tests):

| Mutant | total | failed | Failed tests |
|---|---|---|---|
| PC-1 | 2 | 1 | C1686_LinuxOnlyCensus |
| M-l1 (PC-4) | 2 | 2 | C1686_LinuxOnlyCensus, LinuxOnlyWindowsExclusionPolicy |
| M-b | 2 | 1 | C1686_LinuxOnlyCensus |
| M-j2 | 2 | 1 | C1686_LinuxOnlyCensus |

## Survivors

Every proposed shape below was checked with a scratch probe (outside the repository) that loads the two
census functions from the original and from each mutated copy and runs them on a one-file tree under the
shipped policy. `row-without-marker` noise from the empty tree is filtered out.

### M-f (char literal state): missing fixture

`c1686-fail-closed-char-literal-quote` does not distinguish the mutant. Without the char token, `'"'`
starts a regular string, so the `/*` on that line opens a block comment mid-line. Line 6 then fails as
`marker-noncanonical:6`. That is also the original result, because line 6 is a code line that starts with
`*`. The fixture expects the same outcome either way.

Missing shape (marker on the next line, so a misread opener hides a real marker):

```
    static readonly char Q = '"'; static readonly string S = "/*";
    [NativeLinuxOnly("x")]
    public void M() { }
```

Expected `marker-without-row:antiphon|ZetaTests|M` (original). Probe: original gives exactly that; M-f
gives `marker-noncanonical:...:6` with no marker.

### Note 1: M-c is killed, but `string-literal-opener` is vacuous

`c1686-fail-closed-string-literal-opener` has the same flaw as the char-literal fixture. Under M-c it
still passes (expected `marker-noncanonical:6` either way). M-c dies only through
`verbatim-interpolated-glob`'s `"**/*.cs"` and the real tree. The equivalent discriminating shape is
`static readonly string S = "/*";` followed by a canonical marker line. The probe gives
`marker-without-row` for the original and `marker-noncanonical` for M-c.

### M-j1 (state index off by +1): missing fixture

The interior, prefix and close fixtures put the token on lines where the state at line i+1 gives the same
verdict as the state at line i. Two shapes tell them apart, and both are killed under the probe:

1. A starred closing line (a false positive under the mutant). Add it to `comments-are-not-markers`:
   ```
       /*
        * [NativeLinuxOnly("old")] */
       public void M() { }
   ```
   Original: no problem. M-j1: `marker-noncanonical:6`.
2. A canonical marker that is the comment's closing line. Same shape as `block-comment-interior` line 8,
   but without `Test,`:
   ```
       /*
       [NativeLinuxOnly("old")] */
       public void M() { }
   ```
   Original: `marker-noncanonical:6`. M-j1 reads the line as code: it becomes a canonical marker hit and
   then `marker-unparsed:6`. It still fails closed but under a different name, so an exact-set fixture
   kills it.

### M-t (block-mode exempt reference strip): missing fixture

No fixture puts an exempt reference form after a block comment that closes on the same line. Missing
shape (in an exempt file, expected Ok):

```
    /*
     * doc
     */ static Type A => typeof(NativeLinuxOnlyAttribute);
```

Probe: original gives no problem. M-t gives `marker-noncanonical:tests/Shared/NativeLinuxPolicyWitness.cs:5`.
The mutant only over-refuses (it fails closed). It cannot hide a marker.

### M-u (string-mode exempt reference strip): missing fixture

No fixture puts an exempt reference form on an interior line of a multi-line string. Missing shape (in
an exempt file, expected Ok):

```
    const string V = @"first
    typeof(NativeLinuxOnlyAttribute)
    last";
```

Probe: original gives no problem. M-u gives `marker-noncanonical:...:4`. This also only over-refuses.

### M-i (CRLF normalisation): equivalent

Proof by reading the code: without the replace, each line keeps a trailing `\r`.
- Every line test uses `Trim()`, an anchored `^\s*` or `\b`-bounded regex, a `StartsWith`, or a
  substring after `*/`. A trailing `\r` changes none of them. In the declaration parse, `^\s*` eats a
  lone `\r` rest just as it does the empty LF rest.
- Line states are computed over `$lines -join "`n"`, which is the original text. Line starts (indexed
  by `\n`) are unchanged. Every lexer alternative that excludes `\n` stays single-line; a `\r` can only
  join a token that already ends before the `\n`.

Empirical check: the same 44 fixtures were rewritten as CRLF (a temporary `Write-C1323File` edit). They
give 44/44 both with the normalisation and with M-i (`CRLF-writer`, `CRLF-writer+M-i`).

### M-j3 (lexer `-le $m.Index` -> `-lt`): equivalent

The two differ only when a multi-line token starts exactly at column 0. The mutant then also marks the
opener's own line ('block' with AtStart true, or 'string'). Here is why that cannot change a verdict:
- For a block opener at column 0, the line starts with `/*`. Its first `*/` is the token's closing,
  which is on a later line (the only same-line `*/` is the `/*/` overlap). The code-mode branch
  (comment line unless `\*/.*token`) and the block branch then reach the same verdict: no problem
  (AtStart and `/*`), or the same strip and scan when a `*/` precedes the token.
- For a string opener at column 0, the line never starts with `//`, `/*` or `[NativeLinuxOnly(`. The
  code branch and the string branch therefore both do "exempt strip, then scan".

The probe `col0-opener-token` agrees.

### M-j4 (lexer `-lt $end` -> `-le`): equivalent

`$starts[$line] -eq $end` needs the token's last character to be `\n`. Block and string tokens end in
`*/`, a quote, or `\z`. Only an unterminated token at `\z` reaches a line start equal to `$end`. That is
the empty line after the final newline, and it carries no token.

### M-m (single-line token skip removed): equivalent

A token with no `\n` covers no line start after its own, so the mark loop never runs for it. The `//`,
regular-string and char tokens still `continue` at the mode test. The skip is only an optimisation.

## Classification

- Coverage survivors (fixture missing): M-f, M-j1, M-t, M-u. M-f and M-j1 can change which marker is
  counted. M-t and M-u only over-refuse.
- Weak but killed: M-c (its named fixture `string-literal-opener` is vacuous; it dies only through
  `verbatim-interpolated-glob`).
- Equivalent: M-i, M-j3, M-j4, M-m.
- Unmutated-L defects: none (baseline and restored runs green, real tree 74/74/75).
- Infrastructure failures: none (no slot timeout, no build or fixture error, no zero-test run).
