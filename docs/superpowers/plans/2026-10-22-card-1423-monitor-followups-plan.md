# CARD-1423: cutover monitor follow-ups (CARD-1408 Final Review advisories A1-A5)

Follow-ups to `scripts/postgres-cutover-monitor.ps1` (CARD-1408, CARD-1401 stage C3; plan `docs/superpowers/plans/2026-10-21-card-1408-cutover-monitor-plan.md`). Base `705026da81f9e00c6f9e6b901d9cc77fd1382873` (origin/master). Script and harness only: no Antiphon.Tests project, no database, no Docker, no live HTTP in tests. The monitor stays read-only and the tests never reach the production database, the real AppHost or a real secret; every credential in the harness is a sentinel string.

## Scope

- `scripts/postgres-cutover-monitor.ps1`: masking (A1, A2), the detail-cap reason and help text (A3), the known-effects header (A4).
- `scripts/test-postgres-cutover-monitor.ps1`: one `C1423_*` case per item, plus a scratch-root sweep (A5).
- `docs/superpowers/plans/2026-10-21-card-1408-cutover-monitor-plan.md` D-6: the A3 and A4 notes the Review asked for.
- server2 `/tmp/c1408-*`: the five scratch folders a stalled Review run left behind are removed by hand (A5, operational; reported, not tested).

## Decisions

- **D-1 - Userinfo runs to the last `@` before whitespace (A1).** `http://u:1234/S3@h/...` parses in .NET as host `u`, port `1234`, path `/S3@h/...`, so no credential is sent; but a value meant as userinfo whose password holds `/` printed unmasked in `PLAN`, `MEASURED`, `UNREADABLE`, `SAMPLE-ERROR` lines and the JSON. `Hide-CutoverText` now masks from `://` to the last `@` before whitespace, a quote or an angle bracket (`[^\s"'<>]*@`), and `Test-CutoverSecretValue` classifies a value by the same rule. Over-masking a URL that carries a literal `@` in its path or query is accepted; the request itself still uses the unmasked URL. A URL followed by whitespace and then an `@` elsewhere on the line is not touched.
- **D-2 - A plain value that decodes to a secret is a secret (A2).** `x=y%26token%3DS` reaches the server as `x=y&token=S`; the key `x` is not secret-looking, so the value printed whole. For a non-secret key the value is percent-decoded (repeatedly while it changes, so a double-encoded pair is found) and, when the decoded text would itself be masked (an embedded secret pair or a URL with userinfo), the whole value is `<redacted>`. A benign encoded value (`redirect=%2Fhome%3Ftab%3D1`) is kept.
- **D-3 - The detail cap names its remedy (A3).** `-MaxLandDetailRequests` (50) is sized for the default 30-minute window; at the observed dispatch rate it only bites with `-WindowMinutes` around 8 hours or more. The `INVALID` reason now says so and names the remedy (raise the cap or shorten the window); the script help documents it. The cap and its `INVALID`, never a partial count, are unchanged.
- **D-4 - Known window effect, documented not changed (A4).** A `Blocked` task whose land threw before the window carries `landRequest.terminalFailureCode` without a publication, so the failure-code rule counts it in every window the list returns it in. It counts equally in Baseline and Watch, so the delta is unaffected. Added to the script header's known-gaps text and to CARD-1408 plan D-6; no counting change.
- **D-5 - Scratch sweep (A5).** The harness records every scratch root it creates and removes any that remain in a `finally` after the cases, with one assertion that every case removed its own root. The five `/tmp/c1408-*` folders on server2 (each holding one result JSON from 2026-10-21 18:32) are removed by hand in this task.

## Verification design

- V-1 (D-1) a userinfo password containing `/` (`http://u:12/SENTSLASH1423@fake.invalid`) appears in no Plan, Baseline or Watch line, no recorded JSON, and no child `pwsh` stdout or stderr including the request's exception text, which quotes the URL .NET would send; the fake still routes the request by path. `Hide-CutoverText` leaves `http://h/x and mail a@b` unchanged.
- V-2 (D-2) one sentinel per form appears nowhere on the same surfaces: an encoded secret pair inside a plain value (`x=y%26token%3DSENT`), a double-encoded one (`x=y%2526token%253DSENT`), and an encoded URL with userinfo inside a plain value (`next=http%3A%2F%2Fu%3ASENT%40h%2Fdb`). A benign encoded value (`redirect=%2Fhome%3Ftab%3D1`) and `projectId=p1` are kept.
- V-3 (D-3) more land candidates than the cap still exits 4; the `UNREADABLE` line and the recorded `failures.unreadable` name `-WindowMinutes` and the cap; the script help has a `Detail cap:` paragraph naming when it bites.
- V-4 (D-4) a `Blocked` task whose land threw with every event before the window counts one failed land in Baseline and one in Watch, the delta is 0 and Watch exits 0 `OK`; the script header's known-gaps text names the Blocked case.
- V-5 (D-5) the harness's final assertion finds no scratch root left by a case; the sweep removes any that remain. The server2 `/tmp/c1408-*` removal is reported with the count, not tested.

Red witnesses: each `C1423_*` case is committed before its fix and run alone (`-Case`) against the unfixed monitor; these `-Case` runs are the unlisted runs of this task. The green run is CP-1 on the final tip.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S5 | n/a | monitor-followups | `pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1` | V-1, V-2, V-3, V-4, V-5 | at least 80 assertions passed, 0 failed, exit 0 | n/a | 3 |

Script-test rows pass no `-Runner` or `-Platform` (`GET /api/runner-defaults` revision 2 defaults all work to server2; `GET /api/session-runners` lists desktop and server2). The row runs on the assigned server2 mirror.

### Cost

Ordinary Code floor 3 minutes plus authoring and slot waits on a loaded host. No PCs are commissioned; SourceLanding Mutation may add deliberate mutants of the userinfo and encoded-value masking rules.
