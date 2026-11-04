# Review 1ed0ea30: CARD-1689 witnesses and the X-6 private-origin fix (round 1 of 3)

**Verdict: Clean.** Tested code commit `9d0402c19ede2de3e5489431ffb6a571d2d9ad5f`. Reviewed tip
`c0666337845eb7d08078384b5aa04300940b04df`, which adds only `.antiphon/task-55b2b668.md`. No request-line
shape I could build gets past the registry membership check. No legitimate registered origin is newly refused.

- Subject: `feat/card-task-55b2b668`, owner task `55b2b668-86cc-4eb0-9ef4-c3aacef8d8ce`.
- Base: the branch's real fork point is `97e77f9f1`, not `591fafecb`. `591fafecb` is the current
  `origin/master` and is not an ancestor. `git diff 591fafecb..9d0402c19` therefore shows unrelated master
  drift. The real branch diff is 3 files.

## (1) Is the fix correct and complete?

I read the whole function `c1609_export_trusted_origins`. The steps run in this order:

1. Registry missing → refuse.
2. `c1609_read_canonical`.
3. Shape check: keys are exactly `projects,schema`; `schema==1`; each project key is a GUID; each entry's keys are
   exactly `origins`; `origins` is an array of unique, non-empty strings.
4. The project must be present.
5. Membership: `all(lines; first(capture(re).origin, null) as $o | $o!=null and (origins|index($o)!=null))`.

Why the membership check is exact:
- Step 3 forces `origins` to be an array. `index` on an array of strings is exact element equality (`.[[$o]]`).
  The substring meaning of `index` cannot apply.
- The regex is anchored with `^`/`$`. jq's Oniguruma Perl_NT syntax treats these as whole-string anchors.
- Lines are split on `\n` first.
- `first(..., null)` turns a capture that matches nothing into `null`, and `null` is refused.
- If a jq error is raised, `-e` exits nonzero and the line is refused.

**Scratch vectors (not committed).** I extracted the function from the script at 9d0402c1 and stubbed
`c1609_read_canonical` and `c1609_persist`. The registry held the real origin
`https://github.com/michal-ciechan/Antiphon.git` plus a second project's origin. I ran 25 vectors against the
fixed function and the same 25 against the pre-fix function.

**Approved by the fixed function (4 vectors), as expected:**
- the exact registered line, with and without a trailing EOL
- the line repeated
- a file of blank lines only (see A-2)

**Refused by the fixed function (21 vectors), all with `unapproved-origin`:**
- Near-misses on the origin:
  - without `.git`
  - with a trailing `/`
  - case-changed
  - prefix
  - extra suffix
  - registered origin embedded as a suffix (`https://evil.example/<O>`)
  - the other project's origin
  - empty `origin=`
  - Cyrillic `і` lookalike
- Control characters:
  - trailing `\r` on the line
  - `\r`, `\t` or NUL inside the origin
- Line framing:
  - the good line followed by a garbage line
  - the good line followed by an unregistered line
  - an extra space-separated suffix
  - an uppercase hash
  - a leading space
  - two lines joined
  - a form-feed line
  - a U+2028 tail

**Pre-fix function:** it approved 9 of the 21 malformed or unregistered shapes above:
- empty origin
- trailing CR
- extra suffix
- good line + garbage line
- uppercase hash
- leading space
- joined lines
- form feed
- U+2028

That confirms the gap was real and that the fix closes it. The registry match is exact: `.git` and trailing-slash
variants pass only if the registry lists them. That was already true before the fix; it is not new.

## (2) Scope

`git diff --stat 97e77f9f1..c0666337`:
- `scripts/c1008-retirement.sh` +3/−2: one comment line, plus the capture line and the condition line, which
  together form one jq expression.
- `VolumeRetirementContentTests.cs` +140
- `WindowsUnitSkipRoster.cs` +4
- `.antiphon/task-55b2b668.md`

Nothing in `src/` or `server/`.

## (3) Mutants: each re-applied by me, one lease each, restored to blob `7dbb9cea`

| Mutant | Test | Result | Failing assertion |
|---|---|---|---|
| fix reverted (2 jq lines to base) | `Exchange_malformed_private_origin_lines_are_never_approved` | 1/1 failed | `Diagnosis` expected `reason=unapproved-origin detail=…/private-origins.txt`, got `reason=proof-exchange stage=response` |
| X-3: shape check `\|\| return 1` → `\|\| true` | `Exchange_registry_with_a_wrong_shape_is_invalid_before_the_exchange` | 1/1 failed | expected `reason=registry-invalid detail=…/retirement-origins.json`, got `proof-exchange stage=response` |
| X-7: export `origins:[.projects[].origins[]]` | `Exchange_exports_only_this_projects_registered_origins` | 1/1 failed | `Encoding.UTF8.GetString(exported)` byte mismatch |
| X-8: `sed -n '1p'` | `Exchange_inventory_diagnosis_skips_git_error_lines` | 1/1 failed | `diagnosis` got `RecycleGitAuditUnknown fatal: unable to access 'https://user@…` |

Leases: `d84df95e`, `29058ea1`, `ac0ee28b`, `fa433da5`.

## (4) and (5) Roster and the green run

- **Build:** one lease `38157ec3` (155 s): `dotnet build tests/Antiphon.Tests -p:UseAppHost=false`, native output,
  at 9d0402c1.
- **Run:** one lease `ef98d62d` (313 s), same properties, `--no-build`. **11 total, 11 passed, 0 failed,
  0 skipped.** It covered:
  - the 7 `Exchange_*` methods
  - `Private_origin_advanced_head_uses_real_host_proof`
  - `C1008_Host_proof_admits_only_an_authenticated_private_origin`
  - `C1210_Roster_closure_matches_source_gates`
  - `Temp_retirement_trusted_origins_registry_is_documented`
- The 4 roster rows follow the sibling CARD-1609 row form. C1210 passes. No policy or pin change.

## Checks

- `check-evidence-diff.ps1 -BaseRef 97e77f9f1 -HeadRef c0666337`: commits=3, entries=1, violations=0.
- `git merge-tree --write-tree origin/master(591fafecb) c0666337`: clean, exit 0. Also clean at 9d0402c1.

## Advisories (non-blocking)

- **A-1:** The brief says "exactly one script line". The change is really 2 changed lines plus 1 comment line in
  that same jq expression. No other production change.
- **A-2:** A non-empty request file containing only blank lines is approved, and the export is written. No origin
  is being requested, and the real producer only writes lines that `awk` has filtered to start with
  `C1008_PRIVATE_ORIGIN `, so this is not a bypass.
- **A-3:** The host is now stricter than the desktop reader on a trailing `\r`: the desktop does
  `TrimEnd("\r")`, the host refuses. This fails closed. The producer writes `\n` lines with `printf`.
- **A-4:** The X-6 witness has no positive control inside the test itself. X-7 and the X-3 control cover the
  well-formed path.

## Scope note

The brief asked for an Interim scope (named rows only, which the operator accepted for master-only work). The
attached profile says Final. I ran only the named rows; I did not run the Unit lane. So
`ordinaryScopeCompleted` is Interim. `reviewedSourceClean` is false because I used no checkpoint-tool receipts.

PCs stay pending for SourceLanding Mutation: X-3a, X-3b, X-6, X-7, X-8, and the fix revert.

I cleaned up all bin/obj outputs using literal paths, and no `/tmp/c1689-exchange-*` remains.
