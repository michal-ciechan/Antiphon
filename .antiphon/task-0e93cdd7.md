# CARD-1423 repair DEF-1 (userinfo masking): Code report (task 0e93cdd7)

Outcome: DEF-1 from Final Review 3a27a954 is repaired on `feat/card-task-0e93cdd7` (descendant of `feat/card-task-173228b3` tip `3a74807d442b5ddac35b46f1ebc9917b0219b0ce`, master-only, own landing owner). Red witnesses first (`787a9e9643d851cb759a339c3e385014dcddc163`), then the fix (`2db914c83999864e906643cd8d2c81b98c43c9dc`); CP-1 is GREEN at the fix commit through the checkpoint tool (208 harness assertions, 0 failed), its receipt validates against that SHA, the evidence policy check reports 0 violations and `git merge-tree --write-tree origin/master HEAD` (master `d1693f6eeb3b8aea7e33cedbf174591e280f92c0`) is clean. Worktree `/work/worktrees/task-0e93cdd7`, runner mirror on server2. Plan: `docs/superpowers/plans/2026-10-22-card-1423-monitor-followups-plan.md` (D-1, D-2, V-1 amended; V-6 added; CP-1 covers V-6).

## What changed (`scripts/postgres-cutover-monitor.ps1`)

- **Userinfo rule (D-1 amended).** `Hide-CutoverText` masks from the first `scheme://` to the LAST `@` on the same line (`[^\r\n]*@`); a newline is the only terminator, so `'`, `"`, space, `<`, `>`, `/` and `@` in a password are covered. `Test-CutoverSecretValue` (pool-diagnostics strings) uses the same rule. Over-masking accepted: a URL followed by whitespace and a later `@` on the same line, a literal `@` in a path, two URLs on one line become one masked span.
- **Known userinfo forms (DEF-1 "raw AND escaped userinfo of [uri] parses").** New `Get-CutoverUserinfoForms`: for each given URL the raw text between `://` and the last `@`, its `EscapeDataString`/`UnescapeDataString` spellings and the `[uri]` parse's `UserInfo`. `Invoke-PostgresCutoverMonitor` registers the forms of `-ServerBaseUrl`, `-PipelineUrl`, `-TaskListUrl` for its duration (`$script:CutoverKnownUserinfo`, cleared in `finally`); `Invoke-CutoverHttpGet`'s catch adds the requested URL's forms; `Hide-CutoverText` replaces every form, longest first, on every surface (PLAN/MEASURED lines, result JSON through `-JsonText`, which also matches the JSON-escaped spelling, request errors, the outer-catch exception text). Forms always carry the `user:` part, so a bare number in a REASON or MEASURED line is never hit.
- **Quoted connection-string values.** A quoted value (`"..."` or `'...'`) of a secret-looking key is masked whole with its quotes; a plain key's quoted value is masked whole only when it percent-decodes to a secret. Skipped in `-JsonText` mode (a raw quote there is structure).
- **Pair value class.** Raw text: `(?:[^&;\s\\]|\\.)+` (now includes `'`, `"`, `<`, `>`; a backslash escape is part of the value). JSON text: the same minus `"`. A plain value is recursively searched for pairs of its own (a URL inside a value, a `#` fragment; replaces the old `#` split).
- **Encoded values (D-2 amended).** `Test-CutoverEncodedSecret` checks every decoding round with `+` read as a space too (`x=y+token%3DS`, `x=y+token=S` masked like `x=y%20token%3DS`), and a value still percent-encoded after four rounds is masked whole (5x and 6x now masked; a benign value encoded 5x is over-masked; a benign 2x value is kept).
- `Write-CutoverJson` masks the serialized text with `-JsonText`. Header DESCRIPTION updated. The monitor stays GET-only and the request uses the unmasked URL.

## Red witnesses and green (`scripts/test-postgres-cutover-monitor.ps1`)

`Invoke-C1423MaskingForms` gains an optional `Diag` body (pool-diagnostics JSON surface), an in-process JSON assertion and doubles apostrophes for the child command. The old assertion "a URL followed by whitespace and a later @ is left intact" moved to the space case, inverted (newline is the only terminator; the two-line form stays untouched). All `-Case` runs below went through `scripts/build-slot.ps1`.

| Case | Red at 787a9e964 (unfixed monitor) | Green at 2db914c83 |
|---|---|---|
| C1423_ApostropheInUserinfoMasked | passed=2 failed=7 | passed=9 failed=0 |
| C1423_DoubleQuoteInUserinfoMasked | passed=2 failed=7 | passed=9 failed=0 |
| C1423_SpaceInUserinfoMasked (plus the later-@ and two-line assertions) | passed=3 failed=8 | passed=11 failed=0 |
| C1423_LessThanInUserinfoMasked | passed=2 failed=7 | passed=9 failed=0 |
| C1423_GreaterThanInUserinfoMasked | passed=2 failed=7 | passed=9 failed=0 |
| C1423_QuotedConnectionStringPasswordMasked (raw and encoded, both quote kinds, spaces inside quotes, plain quoted value kept) | passed=6 failed=23 | passed=29 failed=0 |
| C1423_PlusAsSpaceInEncodedValueMasked | passed=4 failed=7 | passed=11 failed=0 |
| C1423_FiveTimesEncodedValueMasked (1x-6x, benign 2x kept) | passed=7 failed=7 | passed=14 failed=0 |
| C1423_VerdictLinesReadableWithMaskedUserinfo (password `SENTREAD1423 x'y"<z>`; D-11 REASON, ROLLBACK, CUTOVER-MONITOR, MEASURED numbers, JSON server/medianMs, request-error prefix) | passed=4 failed=4 (the four sentinel-absence assertions; the readability assertions passed before and after) | passed=8 failed=0 |

Adjacent cases re-run green at 2db914c83 in the same lease: C1423_SlashInUserinfoMasked 8/0, C1423_EncodedValueSecretsMasked 21/0, C1408_MaskingBypassForms 10/0, C1408_UrlSecretsNeverPrinted 6/0.

Per character the surfaces are: `Hide-CutoverText` on a PLAN line, `Test-CutoverSecretValue`, the `[uri]` exception text (`AbsoluteUri` + `OriginalString`), in-process Plan/Baseline/Watch lines, in-process baseline/result JSON including the pool-flag string, child pwsh stdout/stderr with a throwing seam, and the child's recorded JSON.

## Checkpoint (closed list, one row, one run)

Run `20261022-045044-0db7`, `dotnet tools/Antiphon.Checkpoints/bin-c1423r/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-22-card-1423-monitor-followups-plan.md --rows CP-1 --expected-source-sha 2db914c83999864e906643cd8d2c81b98c43c9dc` (exit 0 in one wait, no 75):

```
CHECKPOINT CP-1 commit=2db914c83999864e906643cd8d2c81b98c43c9dc build=n/a filter=pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=105s dirty=0 source=2db914c83999864e906643cd8d2c81b98c43c9dc sourceState=clean buildSource=notApplicable
```

A command row prints `executed=n/a`; the harness's own counts are in `.antiphon/checkpoints/20261022-045044-0db7/rows/CP-1/console.log`: `POSTGRES-CUTOVER-MONITOR TESTS passed=208 failed=0`, exit 0 (Expect at least 80 passed, 0 failed; 26 cases). `validate --evidence .../report.json --expected-source-sha 2db914c83... --rows CP-1`: `CHECKPOINT SOURCE VALID source=2db914c83999864e906643cd8d2c81b98c43c9dc rows=1`, exit 0. Report `.antiphon/checkpoints/20261022-045044-0db7/report.md`. Reruns 0; repetitions of the proof selection 1.

## Unlisted runs (all through `scripts/build-slot.ps1`, every lease `slot=granted`)

- `c1423-def1-red` (waited 0s, held 2s): the driver received the case list as one string and ran no case (exit 1, no harness output); a wasted lease, no evidence.
- `c1423-def1-red` (waited 0s, held 58s): the nine new cases red against the unfixed monitor (table above).
- `c1423-def1-green` (waited 0s, held 114s): the nine new cases plus four adjacent masking cases green at 2db914c83.
- `c1423-ckpt-build` (waited 45s, held 46s): `dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1423r/`; `bin-c1423r/` deleted after validation.

## Forms tested versus out of contract (for the Review)

In contract and tested: userinfo passwords holding `'`, `"`, space, `<`, `>` (DEF-1), `/`, `@` (A1); quoted `Password=` values with both quote kinds, raw in a query, inside an encoded value, with spaces inside the quotes; `+` as a space before an encoded or raw secret pair inside a plain value; a secret pair encoded 1x-6x inside a plain value; an encoded userinfo URL inside a plain value; percent-encoded secret keys; fragment pairs; readability of D-11 reasons, verdict and MEASURED lines beside masked userinfo.

Out of contract (not masked, by design or scope): secret keys outside `Test-CutoverSecretKey` (`token|key|password|passwd|pwd|secret|sig|signature|auth|credential` after percent-decoding), e.g. `pass=`, `pw=`, `pin=`, `otp=`, `session=`, `cookie=`; scheme-less userinfo (`u:x@h` without `://`); opaque or base64 values under a plain key (`x=U2VjcmV0`); a newline or carriage return inside a password (the line rule and the raw form both stop at it; `[uri]` escapes it to `%0A`); credentials inside server response bodies other than the pool-diagnostics strings and names `Hide-CutoverSecrets` drops; a credential typed as a bare `-ProjectId` value (it is percent-encoded into the query, where the plain-value decoding rule applies, and the JSON `projectId` field is text-masked by the pair rule only when it carries `key=value` shape).

Over-masking the Review should expect: `http://h/x and mail a@b` becomes `http://<redacted>@b`; two URLs on one line collapse to one masked span from the first `://` to the last `@`; a benign value percent-encoded five or more times is `<redacted>`; a request error that contains the given URL's userinfo text anywhere has that text replaced.

## Verification design outcomes

V-1, V-2 (as amended), V-6: passed inside CP-1. V-3, V-4, V-5: unchanged cases, green inside CP-1. The thirteen CARD-1408 cases stay green.

## Policy checks

- `scripts/check-evidence-diff.ps1 -BaseRef 3a74807d442b5ddac35b46f1ebc9917b0219b0ce -HeadRef HEAD`: `EVIDENCE result commits=2 entries=0 violations=0` at 2db914c83 (re-run over the report commit in the final message).
- `git merge-tree --write-tree origin/master HEAD` at master `d1693f6eeb3b8aea7e33cedbf174591e280f92c0`: exit 0, tree `d16ed0d29ca0730e6e8229380ea3466142e41de9`, no conflicts (the branch carries the whole 173228b3 series plus this repair; 173228b3 has not landed).
- Platform: `GET /api/runner-defaults` revision 2 (global server2); `GET /api/session-runners` lists desktop (windows) and server2 (linux) available and eligible; the script-test row pins neither `-Runner` nor `-Platform`.
- No `bin-*` directories remain; the tracked tree is clean.

## Pending for Mutation

No PCs commissioned. Candidate deliberate mutants (method-scoped, SourceLanding): the `[^\r\n]*@` class in `Hide-CutoverText` and `Test-CutoverSecretValue` (restore `[^\s"'<>]`), the quoted-value pass (drop one quote kind), the `+`-as-space check and the post-bound `return ($current -match '%..')` in `Test-CutoverEncodedSecret`, the `-KnownUserinfo` wiring in `Invoke-CutoverHttpGet`'s catch, the `$script:CutoverKnownUserinfo` registration in `Invoke-PostgresCutoverMonitor`, the JSON-escaped form spelling in `-JsonText` mode. Candidate doc: `docs/superpowers/mutations/`.

Restart: none (script only).
