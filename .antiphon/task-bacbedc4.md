# CARD-1645 Review round 3 (blockers only): subject feat/card-task-c7ae562e @ 204745bc854e99559d47398189a85cbbf4d91193

Verdict: BLOCKER. Round-2 D-1 is closed for the witnessed shapes but reopens whenever any parameter/local named `_` exists elsewhere in the walked root.

Reviewed range: 55c4fb1b625d99d31575f7e947b00ec6165f52d0..204745bc854e99559d47398189a85cbbf4d91193 (2 commits).

## D-1 (blocker): a `_` parameter or local anywhere in the walked root launders a field-`_` store
- Where: tests/Shared/LiveClaude/LiveClaudeSinkAudit.cs:445 (`StaysLocal`): `locals.Contains(name.Identifier.ValueText) || (... && !UnderscoreInScope(name))`; `Locals(root)` (:421) collects every ParameterSyntax / foreach / declarator name across the whole root by name.
- Failure: with a field or property `_` in the class, `_ = <screen>` and `out _` count 0 when the same walked root also declares any `_` that does not shadow the store: a lambda `_ => 0` elsewhere in the method, `foreach (var _ in ...)`, or (file-root C680 walk) another method `Other(string _)` or a field initializer `_ => 0`. The new `UnderscoreInScope` correctly says "member in scope", but `locals.Contains("_")` is evaluated first and returns true.
- Evidence (temporary uncommitted probe in Antiphon.Agents.Pty.Tests calling `LiveClaudeSinkAudit.Walk` exactly as the R2D1 witnesses do; probe deleted afterwards):
  - Pty method root (Control_auto_connects...): M0 field-only store = 1 (control); M1 field + `System.Func<string,int> f = _ => 0; _ = runner.SnapshotScreen();` = 0; M2 field + `foreach (var _ in new[]{1}) { }` + store = 0; M6 field + lambda `_` + `Keep(runner.SnapshotScreen(), out _)` = 0.
  - C680 file root (diagnostics + watchdog): F0 field-only = 1 (control); F1 field + `private static void Other(string _) { }` + `_ = snapshot.RenderedScreen;` = 0 (this is the R2D1-OtherParameter shape, witnessed only on the Pty method root, never with a field present); F2 field + `static readonly Func<string,int> F = _ => 0;` + store = 0.
  - `_ => ...` lambdas are idiomatic (the honest Startup file has them), so this is the round-2 D-1 failure reached through the brief's own area (b).
- Why: a real screen-derived store into a class field counts 0, i.e. the same false green D-1 was raised for.
- Fix: for a `_` target, do not consult the by-name `locals` set; decide from `UnderscoreInScope` alone (member `_` in file -> counted store; scoped `_` whose owner contains the store -> local; else discard). Add witnesses: field `_` + lambda `_ =>` in the same method (Pty), field `_` + `Other(string _)` and field `_` + lambda field initializer (C680 file root), each must count >= 1; keep R2D1-OwnParameter / OtherParameter at 0.

## Brief checks
- (a) Round-2 probes: FieldStore, FieldOut, PropertyStore count >= 1 on all three Pty families and on the C680 file walk; Discard/DiscardOut/OtherParameter/OwnParameter stay 0; honest canary and C680 files count 0 in every category (green runs below). Closed for those shapes only, see D-1.
- (b) Narrower scoping: a lambda/method parameter `_` in the same method with no member `_` is a true discard -> 0 (probe M4 = 0, correct). A local function `_` inside a lambda: with no field, store outside the lambda = 0 (true discard, M5); with a field `_` = 1 (M3, correct). The owner-scoping itself is sound in-file (unknown declaration kinds and owner-less declarations fail closed); the hole is the `locals` short-circuit (D-1).
- (c) Runs at 204745bc8 (build-slot leased, native output, UseAppHost=false):
  - Antiphon.Agents.Pty.Tests `/*/*/(LiveClaudeEvidenceWiringTests*)/*`: total 3, failed 0, succeeded 3.
  - Antiphon.Tests `/*/*/(LiveLaneEvidenceTests*)|(LiveLaneInventoryTests*)/*`: total 6, failed 0, succeeded 6, skipped 0.
- check-evidence-diff: 55c4fb1b6..204745bc8 commits=2 violations=0; merge-base 907edaebb..204745bc8 commits=12 violations=0 (origin/master c4e7aa06e is not an ancestor, so base origin/master returns base_not_ancestor exit 2 by design).
- merge-tree onto origin/master c4e7aa06e9f8c119f7dd59f52e38ed413293c9f2: clean (tree cdc7820d6b9b5058e077d7cf902ac2d1d6422ddf).

## Advisories (one line each, not blockers)
- A-1: the same by-name `locals` shortcut lets any field `X` store be read as local when a parameter/local `X` exists elsewhere in the root (pre-existing, all names); worth a card or a residual line.
- A-2: documented residual (field `_` in another partial/base file) stands as written.

PCs: all pending for method-scoped SourceLanding Mutation; none discharged here.
Cleanup: all bin/obj produced by this review deleted by literal path; probe file deleted; worktree clean.
