# CARD-1694 Review round 1 (task 4af5405a)

Outcome: Clean. Subject feat/card-task-08cca1b8, tip aa33d8f639c9c6ede9a54fc454472f756abba039 (report only on top of the
tested code commit ace714ae2fa7897f4583239d05cebb41e5428676); base master 5449f8f50 (= origin/master at review).
Code landing owner: 08cca1b8-c685-424f-adf3-9ca19fdea77a.

## Verification

1. The parser is never weaker than at base. A line is now skipped only when the base also skipped it, and that set is
   smaller: the comment branch adds a `*/`-before-token condition, and exempt-file lines are scanned after the reference
   forms are stripped instead of being skipped. The canonical-marker branch is unchanged.
   I ran both versions, extracted by AST, on the real tree: base and tip both give Ok=True with 74 markers, 74 rows and 75 marker
   files, and the MarkerKeys, MarkerFiles and RowKeys sets are byte-equal. There are no new false positives.
2. A commented-out stacked marker on a middle block-comment line without a leading `*` fails marker-noncanonical, at base and at tip.
   A bare canonical `[NativeLinuxOnly("x")]` middle line is counted as a marker. That behaviour is unchanged from base:
   it is the `block-comment-interior` fixture, which gives marker-without-row.
3. Scratch adversarial probe, base vs tip (not committed). Every shape below passed at base and now fails closed:
   - `/* a */ /* b */ [NativeLinuxOnly(..)]`
   - `/* NativeLinuxOnly */ [NativeLinuxOnly(..)]`
   - `* x */ [..]`
   - `/*/ [..] */` (conservative)
   - in an exempt file: `[method: NativeLinuxOnly(..)]`, a token-bearing Description string before a stacked marker,
     `[Test] [NativeLinuxOnlyAttribute(..)]`, `Foo(typeof(..))` before a marker, the name split across lines, a verbatim string tail before a marker,
     `global::`-qualified, `new ...(..)` before a marker.

   These stay Ok, correctly: `/* c */ typeof(..)` and the real WindowsUnitPlatformAdmissionTests shapes in an exempt file.
   I found no way for the string reference form to launder an application. Applying the attribute needs `[`, `,`, `(` or `]` next to
   the token, and the regex's character class excludes all four.
4. Mutants: each was an exact edit of scripts/test-rc-platform.ps1, run through the harness with one lease each, then restored with git checkout.
   - PC-1 (drop the `*/`-before-token clause): 29/34. Red on block-comment-prefix, empty-block-comment-prefix,
     block-comment-close-prefix, indented-block-comment-prefix and exempt-file-block-comment-prefix.
   - PC-2 (restore `$exempt -or` whole-line skip): 30/34. Red on exempt-file-stacked-marker, exempt-file-attribute-list-line,
     exempt-file-string-before-marker and exempt-file-block-comment-prefix.
   - PC-3 (widen the string form to `"[^"]*T[^"]*"`): 33/34. Red on exempt-file-string-before-marker.

## Runs (Linux server2, one build-slot lease each, at ace714ae2)

| Run | Result |
|---|---|
| test-rc-platform.ps1 -Case C1686_LinuxOnlyCensus | 34/34 (twice: the second was an unintended rerun when a PC-3 edit failed to apply) |
| test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy (real tree) | 6/6, including markers-and-rows-agree |
| dotnet build tests/Antiphon.Tests -p:UseAppHost=false | 0 errors (209 s) |
| Antiphon.Tests `/*/*/(ReleaseGatePlatformTests*)\|(NativeLinuxPolicyWitnessTests*)\|(LiveLaneRunnerTests*)/*` | 13 total, 0 failed, 0 skipped (TRX); includes C1686_LinuxOnlyCensus, LinuxOnlyWindowsExclusionPolicy, WrapperOnlyPaidManifest |
| test-live-lane-run.ps1 -PrintInputDigest | 836bbee0...74b7 = recorded $script:PinnedInputDigest |
| Get-NightlyPolicyHash(tests/test-execution-policy.json) | 5c7f5f06...aad4 = recorded policyHash (the policy json is unchanged) |
| check-evidence-diff 5449f8f50..aa33d8f63 | 5 commits, 1 entry, 0 violations |
| git merge-tree --write-tree origin/master aa33d8f63 | clean |

Not run: the whole Unit lane. The brief commissioned an Interim scope (named rows only, accepted by the operator for master-only work).
There is no `### Checkpoints` plan table, so there are no checkpoint receipts. PCs PC-1..PC-4 remain pending for a SourceLanding Mutation.

## Advisories

- A-1 (pre-existing, unchanged from base): a code line that starts with `*`, such as a multiplication continuation
  (`int x = 2` / `* 3; [NativeLinuxOnly("y")] public void M() {}`), is still taken as a comment line. A marker on that line
  passes the census silently at both base and tip. It needs deliberately contrived formatting, and the compiled
  NativeLinuxPolicyWitnessTests reverse-coverage check still catches a marked test without a row. Closing it would need
  block-comment state tracking. File a card only if the census must stand without that backstop.
