# CARD-1458: pool-diagnostics masking follow-ups

Follow-ups to CARD-1423 (Final Review 9229aca3, clean) on `scripts/postgres-cutover-monitor.ps1`. Base `ac6d00022e940ee3b90584c7d313567b4491c498`. Script and harness only: no Antiphon.Tests change, no database, no Docker, no live HTTP, no real secret. Every credential in the harness is a sentinel.

## Decisions

- **D-1 - Nested JSON in a pool string is masked in place.** A pool-diagnostics field whose value is a string of JSON text (`{"password":"SENT","n":1}`) is not a `Test-CutoverSecretValue` shape (`password=` or `scheme://...@`), so `Hide-CutoverSecrets` kept it. Serialization then escaped the quotes, and the later `-JsonText` pass did not see the pair. A string that is itself a credential shape is still dropped whole. Every other string goes through `Hide-CutoverText` before it is serialized, so the password pair becomes `<redacted>` and the plain field stays.
- **D-2 - A short credential inside its URL is already masked. Not changed.** `http://u:p@fake.invalid/x` is `http://<redacted>@fake.invalid/x` by the userinfo rule, and a Plan with `-ServerBaseUrl http://u:p@fake.invalid` prints no `u:p`. The readability gate still refuses a known form shorter than four characters, so the raw userinfo `u:p` is not replaced when it appears apart from `scheme://...@`. Its percent-escaped spelling `u%3Ap` is registered because that spelling is five characters. Owner decision, not taken here: whether a short `user:password` form should be a known form of its own.
- **D-3 - A parenthesised pair keeps its closer.** A non-query secret pair runs to the next `;`, `&` or end of line (CARD-1423 R-DEF-1, so `Password=SE NT` stays one value). That run consumed the wrapper of `(Password=S)` and the text after it. When the character immediately before the pair is `(`, the value now stops before the next `)`. A bare `Password=SENT)TAIL` still runs through the `)` to the end of the line, so a `)` inside the secret stays masked. Narrowing the bare run so a free-text tail after a space is kept would undo R-DEF-1; that is an owner decision, not taken here.
- **D-4 - The secret-key list is not extended.** Out of contract and unmasked today: `pass=`, `pw=`, `pin=`, `otp=`, `session=`, a scheme-less `u:x@h`, `Authorization: Bearer` and `Authorization: Basic` header forms, and a base64 blob. Owner decision: whether any of those shapes join the secret-key list. This card does not guess.

## Verification design

- V-1 (D-1) case `C1458_NestedJsonInPoolStringMasked`. `Hide-CutoverSecrets` on a note `{"password":"SENTNEST1458","n":1}` returns `{"password":"<redacted>","n":1}` and keeps `MaxPoolSize`. A plain JSON string is kept. A URL with userinfo is still `<redacted>` whole. A Baseline whose pool diagnostics carry that note writes neither the baseline nor the result JSON with `SENTNEST1458`; both contain the escaped `<redacted>` pair and `"NoResetOnClose": true`.
- V-2 (D-3) case `C1458_ParenthesisedPairKeepsCloser`. `(Password=SENTOM1458)`, `(Password=S)` and `(Password=SENTOM1458) medianMs=5000` keep the closer and the trailing text. `Password=SENT)TAIL1458` is `Password=<redacted>` with no tail. `Host=h;Password=SE NTCS1458;Database=kept` still stops at the semicolon. The same parenthesised pair inside `-JsonText` keeps the closer.

Red witnesses: both cases were committed and run with `-Case` against the unfixed monitor (nested passed=3 failed=2 exit 1; paren passed=3 failed=4 exit 1). The green run is the whole harness on the fix tip. One named mutant per witness: returning the raw string from the non-secret branch turns V-1 red; removing the closer stop turns V-2 red.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | pool-masking | `pwsh -NoProfile -File scripts/test-postgres-cutover-monitor.ps1` | V-1, V-2, the existing C1408 and C1423 harness | 0 failed, exit 0 | n/a | 3 |

Script-test row. No `-Runner` and no `-Platform`: the script is portable PowerShell and the assigned checkout is the server2 mirror. The guard batch (policy sweep, mechanical contract, `TaskPlatformGuidanceTests`, layering, `TestLaneCategoryGuardTests`, `ProcessSpawnLimitTests`) is the ordinary Final batch from `docs/testing-and-build.md`, not a second product checkpoint. No OS-gated branch changes. No PCs are commissioned; SourceLanding Mutation may add method-scoped mutants of D-1 and D-3.
