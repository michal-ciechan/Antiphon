# Why missed: test selection CRLF fixture omitted nightly-core.ps1

One committed note for rc qualification run 4e0d1159, chunk
`antiphon-all-group-123.trx`, one failure in
`Antiphon.Tests.Scripts.TestSelectionTests`. CARD-1781.
The Windows TRX at
`C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\antiphon-all-group-123.trx`
is not mounted on this runner. Linux at master
`b35fb137acde6e0c52e296dd6d7b4e345fe3d238` reproduced the same method.

## Failure

`/*/*/TestSelectionTests/*` at that SHA: 9 executed, 8 passed, 1 failed, 0 skipped (10m 20s).
The only failure is `C1313_SelectorLineEndings`.

```
ShouldAssertException: result.ExitCode should be 0 but was 1
FAIL C1313 crlf-disk-selector-accepted - exit=2 selection refused:
  The term '<crlf-root>/scripts/lib/nightly-core.ps1' is not recognized
  as a name of a cmdlet, function, script file, or executable program.
FAIL C1313 crlf-disk-selector-hash-is-lf
FAIL C1313 crlf-disk-selector-refused - selection refused: (same missing loader)
C487: 9 passed, 3 failed, 12 rows
  at ScriptHarness.Validate
     tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25
```

`New-C1313CrlfScriptRoot` copies `nightly-common.ps1` and `nightly-policy.ps1`
into an isolated script root and runs `select-test-rosters.ps1` from there.
Both files dot-source `nightly-core.ps1` from their own directory. The copy
list did not include that loader, so the on-disk selector exits 2 before it
can accept or refuse a CRLF edit. The in-git CRLF blob checks earlier in the
same method passed. This is a missing fixture file, not a hash or assertion
change.

## Introducing commit/card

introducing commit/card: `34b750e0f42c7175a02199f6c232eceab48cd8b8` (no card)

That commit adds `scripts/lib/nightly-core.ps1` and the dot-source from
`scripts/lib/nightly-policy.ps1`, which the CRLF fixture already copied.
`c2965dc7b1a377ce57aabfede628d5179bcdfaad` (same author, same minute, child
of `34b750e0f4`) adds the same dot-source at the top of
`scripts/lib/nightly-common.ps1`, which is why the refusal names
`nightly-common.ps1`'s load. Neither commit edits
`scripts/test-test-selection.ps1`. No Antiphon board card matches
`NightlyCore` or `nightly-core.ps1`.

The same copy list is on rc/2026-11-10 `74a8004f405bc54666fea33889839f0ff4d62ec7`.
Run e737e9fe's TRX is not on this runner, so this note does not claim that
run executed the method. The dispatcher reported the class as not failing
there. The code at that tip has the same omission.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- No Review row. Board search for `NightlyCore` and `nightly-core.ps1` returned
  no card, so there is no Full or Interim scope and no reviewed selection.
- The introducing commits name their witness as `NightlyCoreTests`
  (`scripts/test-nightly-core.ps1`, one Integration method
  `CoreMatchesFallback`). That harness compares compiled-core output with the
  PowerShell fallback. It does not build the CRLF script root.
- Follow-up `416ea1428d48075229ef22e61f48f25e81f6fb7f` (cherry-pick of
  `a12e74b83603b3a17a0f76a65db7d8de16c41b00`, 2026-11-07 22:39) fixed the same
  omission for the live-lane census pin in `scripts/test-live-lane-run.ps1`
  by adding `scripts/lib/nightly-core.ps1` to `$script:ProtectedInputs`.
  That commit does not touch `scripts/test-test-selection.ps1`. No Review
  of that commit was found. Its selection is the live-lane census reader,
  not `TestSelectionTests`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Integration,
`tests/Antiphon.Tests/Scripts/TestSelectionTests.cs`, method
`C1313_SelectorLineEndings`. `NightlyCoreTests` and the live-lane census pin
do not execute `New-C1313CrlfScriptRoot`. Linux runs this method; the miss
is not a Windows-only path, case, or line-ending assumption. The native
Windows lane is the same class in the rc antiphon suite (group 123 on run
4e0d1159). An earlier Windows rerun of this class was not owed by name:
the introducing commits did not list it.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/lib/nightly-core.ps1` -> `/*/*/TestSelectionTests/*`

A one-method run of the failure is
`/*/*/TestSelectionTests/C1313_SelectorLineEndings*`.
The class is about ten minutes once the assembly is built. The attribute
witness `/*/*/SelectorLineEndingAttributeTests/*` only checks `eol=lf`; it
does not launch the disk selector, so it cannot see a missing copy.

## Exemption

same cause as an already written note: none
