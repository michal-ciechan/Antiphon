# CARD-1694 Code report (task 08cca1b8)

Outcome: both C1323 census holes (D-1 block-comment prefix, D-2 exempt-file hiding) were open on master
5449f8f50 and are now closed in `Get-C1323LinuxOnlyCensus` (scripts/test-rc-platform.ps1). Red-first, green after.

- Branch: feat/card-task-08cca1b8, worktree /work/worktrees/task-08cca1b8 (server2 runner mirror), Code task 08cca1b8 (landing owner).
- Base: 5449f8f50b9272d6ea468920725724ec88c56d68 (= origin/master at fetch; `git log 5449f8f50..origin/master -- scripts/test-rc-platform.ps1` empty; the last census commits are CARD-1686 106b8c37a/ed67056c5, which carry both holes).
- Commits:
  - 1a50b5b00569d09c4a28b520824f50eb01f5ce51 test (red): 7 new fail-closed fixtures + 1 positive exempt-references fixture; compiled test expects 33.
  - fa0d202adf395547c6a472f210a9c945771c769d fix.
  - a0f1cd69bbc67d0603d140c3a9e674fc81bfd747 + ace714ae2fa7897f4583239d05cebb41e5428676 test: witness for the exempt-file string reference form (`exempt-file-string-before-marker`); green-on-arrival against the fix, red against the base whole-file skip and against a widened string form (scratch mutant below). Final tested source SHA: ace714ae2.
  - this report commit (report only).

## Per-hole evidence

D-1 (a `*/` before the token on a comment-start line): fixtures `block-comment-prefix` (`/* x */ [NativeLinuxOnly("y")]`),
`empty-block-comment-prefix` (`/**/[...]`), `block-comment-close-prefix` (`*/ [...]` closing a multi-line comment),
`indented-block-comment-prefix` (`    /* c */ [...]`). Each must give exactly `marker-noncanonical:<file>:<line>`.
Red at 1a50b5b00: all 4 FAIL (census Ok, no problems). Green at fa0d202ad.
Fix: a line is a comment line when it starts with `//`, or starts with `/*`/`*` and has no `*/` followed by the token.

D-2 (exempt file hides an applied attribute): fixtures `exempt-file-stacked-marker` ([Test, NativeLinuxOnly("y")] in
WindowsUnitPlatformAdmissionTests.cs, the card's repro), `exempt-file-attribute-list-line` (bare `NativeLinuxOnly("y")`
line inside a multi-line `[ ... ]` in NativeLinuxPolicyWitness.cs), `exempt-file-block-comment-prefix`
(`/* c */ [Test, NativeLinuxOnly("y")]` in NativeLinuxPolicyWitnessTests.cs). Red at 1a50b5b00: all 3 FAIL. Green at fa0d202ad.
Plus `exempt-file-string-before-marker` (`[Test(Description = "a"), NativeLinuxOnly("y")]` in NativeLinuxOnlyAttribute.cs):
scratch mutant widening the string form to `"[^"]*T[^"]*"` gives Ok=True (red); the fix gives marker-noncanonical:5.
Fix: exempt files no longer skip lines; they strip only the reference forms (`typeof(..T)`, `new ..T(`, `class T`,
`public T(` constructor, a plain `"..."` string with no `"\()[],/*` around the token) and the rest is scanned as
anywhere else. Positive fixture `c1686-exempt-file-references-pass` keeps the real exempt-file line shapes green.

Extra scratch probe (not committed, census function extracted by AST): in an exempt file `[Test(Description = "a"), NativeLinuxOnly("y")]`,
`[ /*"*/ NativeLinuxOnly /*"*/ ("y")]`, global-qualified, `[Test][NativeLinuxOnly(..)]`, typeof/ctor/same-word string beside a
marker, `* old */ [...]`; and outside, `/* a */ /* b */ [...]` all fail closed; `/* [Test, NativeLinuxOnly("x")] */`,
`/* c */ typeof(...)` and the resource-name string in an exempt file stay Ok.

## Pins

Census output unchanged: the policy json is untouched and the real tree still gives markers-and-rows-agree.
scripts/test-rc-platform.ps1 is not a C1545 protected input, so neither policyHash nor PinnedInputDigest is re-recorded
(no re-record commit). WrapperOnlyPaidManifest (the pin) green.

## Runs (one build-slot lease each, Linux server2)

| Run | Result | waited |
|---|---|---|
| harness red `-Case C1686_LinuxOnlyCensus` at 1a50b5b00 | 26 pass / 7 FAIL (the 7 new fail-closed rows) | 105s |
| harness `-Case C1686_LinuxOnlyCensus` at fa0d202ad | 33/33 pass | 300s |
| harness `-Case C1686_LinuxOnlyCensus` at a0f1cd69b / ace714ae2 | 34/34 pass (25 existing + 9 new) | 90s / 0s |
| harness `-Case LinuxOnlyWindowsExclusionPolicy` (real tree) at fa0d202ad (census function unchanged since) | 6/6 pass | 226s |
| build Antiphon.Tests (UseAppHost=false) at fa0d202ad / ace714ae2 | 0 errors | 195s / 30s |
| Antiphon.Tests `/*/*/(ReleaseGatePlatformTests*)\|(NativeLinuxPolicyWitnessTests*)\|(LiveLaneRunnerTests*)/*` at fa0d202ad and again at ace714ae2 | 13/13 each, 0 skipped (incl. C1686_LinuxOnlyCensus with 34 rows, LinuxOnlyWindowsExclusionPolicy, WrapperOnlyPaidManifest, 3 witness) | 15s / 60s |
| SessionRunner.Tests build + NativeLinuxPolicyWitnessTests at fa0d202ad (no input changed after) | build ok; 3/3 | 135s / 165s |
| PtyHost.Tests build + NativeLinuxPolicyWitnessTests | build ok; 3/3 | 60s / 90s |
| Agents.Pty.Tests build + NativeLinuxPolicyWitnessTests | build ok; 3/3 | 0s / 15s |
| adversarial probes (pwsh, scratch) | 14/14 as intended; string-widening mutant red | n/a |

No plan/`### Checkpoints` table exists for this card, so runs were direct build-slot runs, not checkpoint-tool rows (no
CHECKPOINT lines/receipts). check-evidence-diff over 5449f8f50..HEAD: see final report line.

Not run: the whole Unit lane (Final profile). The brief names the run set above and a 90-minute budget; the Unit lane is
~3 h on server2. The change is one PowerShell function reached only by the C1323 harness cases plus a name list in
ReleaseGatePlatformTests. Deferred-to-final: Unit lane (not passed).

PCs pending for SourceLanding Mutation: PC-1 drop the `*/`-then-token clause (D-1 rows red); PC-2 restore whole-file
exempt skip (D-2 rows red); PC-3 widen the string reference form to `"[^"]*T[^"]*"` (exempt-file-string-before-marker red); PC-4 drop a reference
form such as `typeof` (exempt-file-references-pass and the real-tree LinuxOnlyWindowsExclusionPolicy red).
