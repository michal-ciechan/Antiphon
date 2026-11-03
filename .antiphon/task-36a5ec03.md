# CARD-1645 Review round 2 (diff-only), task 36a5ec03

**Outcome: one defect, D-1. The new `_` discard rule launders a store into a field named `_`.**

D-1, D-2 and D-3 from round 1 are closed. The ClearStartupTrustPromptAsync exemption is sound. All witnesses and all 8 mutants behave as claimed.

- **Subject:** `feat/card-task-455c5755` tip `55c4fb1b625d99d31575f7e947b00ec6165f52d0`.
- **Range reviewed:** `ad9bf0c14bb0248b6be070e6c9df509503e1b646..55c4fb1b625d99d31575f7e947b00ec6165f52d0`.
- **Original Code owner:** `54c47f10-e160-4ef8-b4c7-342695a5c431`.

## D-1: `_` treated as a discard even when it names a field

- **Where.** `tests/Shared/LiveClaude/LiveClaudeSinkAudit.cs:444`, in `StaysLocal`: `locals.Contains(name) || name.Identifier.ValueText == "_"`. It is reached from plain assignments (`:109`) and from `ref`/`out` arguments (`:116`).
- **Failure.**
  - C680 walk: declare `private static string _ = "";` in `ClaudeDialogWatchdogCanaryTests` and insert `_ = snapshot.RenderedScreen;` before `LiveClaudeEvidence.Success("c680-native"`. The tip gives Escape 0, every category 0. ad9bf0c14 gave esc=1.
  - Pty walk: append `_ = runner.SnapshotScreen();` to Control_auto…, Submitting_v… or C514_Idle_me…. The tip gives 0 in all three families. ad9bf0c14 gave esc=1 in each.
  - In a class that declares a field `_`, that statement is a field store, the same shape as the counted witness D3-Field (`s_last = …`). An `[After]` hook or another test can then emit it.
  - So this is a false green introduced by this diff.
- **Why.**
  - In C#, `_` is a discard only when no variable, field or member named `_` is in scope. The rule treats every `_` missing from `locals` as a discard.
  - The Pty walk's method roots never contain fields, so it can never tell the two apart.
  - It contradicts the audit header ("non-local store counts") and the docs ("a ref or out argument that is not a local or a discard"). It is not listed in "Live-lane sink audit residuals".
- **Fix.**
  - Treat `_` as a discard only when the containing syntax tree (`left.SyntaxTree.GetRoot()`, which is the whole file even for a Pty method root) declares no field, property or other member named `_`. Otherwise count the store.
  - Add a red-first witness for each walk:
    - a C680 cycle that inserts the field into the watchdog class plus `_ = snapshot.RenderedScreen;`;
    - a Pty raw-any case with a class-level field insert.
  - The honest `ClaudeScreen.TryReadComposer(snapshot.RenderedScreen, out _)` in the diagnostics file must stay 0. It does today, because the call is sanitizing.

## Verified sound

### (a) Round-1 probes at the tip

I used the no-build Roslyn probe on the tip's audit source.

- **Every category 0:**
  - the 7 real Pty callers, individually and combined (`PTY BASE rec=13`, all 0);
  - C680 (`rec=4`, all 0).
- **Round-1 C680 lines now count:**
  - `output.Write("{0}",…)` on the OutputWriter alias, esc=1;
  - `t.Write("{0}",…)` on File.CreateText, esc=1;
  - `s_log.Append(…)`, esc=1;
  - Arity and Return also count.
- **Round-1 Pty shapes now count, in all 3 families:**
  - `Array.ForEach(lines, l => OtherFile.Log(l))`, esc=2;
  - `.ToList().ForEach(l => throw new X(l))`, skip=1 and esc=1;
  - `lines.ForEach(l => OtherFile.Log(l))`, esc=2;
  - `.Select(l => { OtherFile.Log(l); … })`, esc=1;
  - the alias ObjectBag store, esc=1;
  - `ex.Data[…]` with rethrow, esc=1.
- **All tuples red.** The probe parsed 77 Pty insert tuples per family; every one is red in all 3 families. All C680 cycles plus R1D1-Return are red.

### (b) The ClearStartupTrustPromptAsync exemption

- **Reachable values.** `ClaudeBlockingPrompt.cs:360-455` sends `write` only `ClaudeTrustDialogKeys.LegacyDigit`, `HighlightNextCandidates` or `Enter`. The `AffirmativeKey` branch of `TryAnswerDetailedAsync` cannot be reached from it, because only TrustFolder prompts get there.
- **Keying.** The exemption is keyed by exact call text plus positional index 1, and drops only the lambda's parameter taint. The lambda body is still walked.
- **Probes that still count:**
  - a callback body that reads `runner.SnapshotScreen()`, esc=1;
  - a named-argument reorder `(write: …, snapshotScreen: …)` logging `data`, esc=1;
  - `OtherDetector.ClearStartupTrustPromptAsync(…, (data,_) => OtherFile.Log(data))`, esc=1.
- **Residual.** A same-spelled type elsewhere is the documented exact-call-text residual.
- **The `_` handling itself:**
  - A local or lambda parameter named `_` is handled correctly: `ForEach(_ => OtherFile.Log(_))` gives esc=2, and `string _ = ""; _ = screen; OtherFile.Log(_)` gives esc=1.
  - Only the field case fails; see D-1.

### (c) Residuals

The docs list is honest, and each listed residual matches the code. The only contradiction is D-1.

### (d) Runs

Every build and run went through `scripts/build-slot.ps1`, one lease each, with native output, `-p:UseAppHost=false` and `TEMP=/tmp`.

| Run | At | Result |
|---|---|---|
| Pty build | 55c4fb1b6 | 0 errors, held 12 s |
| Pty `(LiveClaudeEvidenceWiringTests*)/*` | 55c4fb1b6 | total 3, succeeded 3 (TRX: CanarySinksCannotBypassSanitizer, CrlfCheckoutCountsSanitizerAnchorsOnce, EvidenceIsSanitized) |
| Antiphon.Tests build | 55c4fb1b6 | 0 errors, held 262 s |
| Antiphon.Tests `(LiveLaneEvidenceTests*)\|(LiveLaneInventoryTests*)/*` | 55c4fb1b6 | total 6, succeeded 6 (TRX: C680EvidenceIsSanitized, CensusAdditiveIdentity, CensusTargetScope, ExactCoreRoster, ArtifactGateRequiresPresence, EveryLiveGateHasDisposition) |
| Gated Pty build | 55c4fb1b6 + C1645_MUTANT copy | 0 errors |
| Gated Antiphon.Tests build | 55c4fb1b6 + C1645_MUTANT copy | 0 errors, held 212 s |

I re-derived the 8 mutants from the Code stage's mut-*.cs as env gates on the tip's audit, in a detached scratch worktree, which I have since removed. There is one run per mutant; each failed 1 of 1 at its own witness:

| Mutant | Red at |
|---|---|
| D1a | R1D1-OutputWriter |
| D1b | R1D1-Arity |
| D1c | R1D1-Return |
| D2 | R1D2-ForEachThrow |
| D3 | R1D3-AliasBag |
| Grp | R1D2-Group |
| Qry | R1D2-Query |
| Ref | R1D3-RefField |

The no-build probe agrees: with no mutant, every category is 0, and each mutant MISSes exactly its own witnesses.

### Other checks

- **Census unchanged.** `git diff ad9bf0c14..HEAD` over `scripts/`, `tools/` and `LiveLaneInventoryTests.cs` is empty.
- **Evidence diff.** `check-evidence-diff.ps1` gives commits=5 entries=1 violations=0 over `ad9bf0c14..HEAD`, and commits=10 entries=2 violations=0 over `907edaebb..HEAD`.
- **Master.** `git merge-tree --write-tree origin/master HEAD` is clean (tree 10a826b9d). Master is now `c4e7aa06e`, not `91aa16368`.
- **rc/2026-11-01 (8cf6b2d17).** A plain merge-tree conflicts, because the branch carries non-rc master history. A chained per-commit pick of the whole card range `907edaebb..HEAD` is clean (tree 1f773f69a). The round fix `ad9bf0c14..HEAD` alone does not pick without the earlier CARD-1645 commits, which is expected.

## Not run

The whole Unit lane did not run; this diff-only round 2 is scoped by the brief. Scope is Interim.

PCs: the 8 mutants are candidate method-scoped controls. All stay pending for SourceLanding Mutation.
