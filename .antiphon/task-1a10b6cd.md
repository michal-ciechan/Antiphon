# Review 1a10b6cd — CARD-1822 round 1 (Final): DEFECT D-1 (blocker)

Subject: origin/feat/card-task-f246b42b tip 47b55834a299a59dbe107d5f407628cfeeddb358 (Grok Code author), base master 72fada66545c2dc96a0710827fbc57558700dd61.
Diff: client/src/features/settings/providerEndpointModel.ts (+29/-3) and its test (+9). No plan file and no `### Checkpoints` table; ordinary scope is the one the brief lists.

## Evidence (all at 47b55834a, each driver under its own build-slot lease)

| Check | Result |
|---|---|
| scripts/test-client.ps1 providerEndpointModel (typecheck, lint, vitest) | typecheck 0, lint 0, vitest 43/43 passed, exit 0 |
| Red: base source 72fada665 with the new tests | 7 failed / 36 passed (43), matches the claim |
| Mutant: mailto check forced false | 2 failed (43), caught |
| Mutant: `doubleSlashAuthorityCarriesCredential` call forced false | 5 failed (43), caught |
| Guard batch in Antiphon.Tests: (TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*) | 53/53, 0 failed, 0 skipped. The author's 78 is not reproduced in this assembly. The change is client-only, so this does not block. |
| check-evidence-diff 72fada665..47b55834a | commits=2 entries=0 violations=0 |
| merge-tree origin/master (72fada665) vs HEAD | clean (exit 0) |

Mutants and the red run used temporary in-place source edits. Each file was restored byte-for-byte from a saved copy, and `git status` was clean afterwards.

## Card strings (CARD-1822 A-1 and A-2): all seven are covered and refused

`"https://u:S@…"`, `<https://…>`, ZWSP + `https://…`, `BASE_URL=https://…`, `https ://…`, `mailto:op:S@host`, `MAILTO://op:S@host/v1`.

## Bypass probe (48 variants, node --experimental-strip-types on a copy of the model)

Refused: single quote, backtick, smart quotes, BOM, NBSP, single-slash `https:/u:pw@h`, scheme-less `https:u:pw@h`, protocol-relative `//u:pw@h`, backslash and mixed-slash authority, `%3A` colon, uppercase scheme, tab or newline inside the scheme or the authority, a path-looking prefix `/api/https://…`, IPv6 host with userinfo, fullwidth colon or slashes, `mailto::pw@h`, `mailto:\\op:pw@h`.

No regressions. Every legitimate value still passes: `host:port`, `localhost:11434/v1`, `http://localhost:8080/v1`, `http://[::1]:8443/v1`, `@` in a path, query or fragment, `https://api.x.ai/v1`, plain `mailto:x@y`, `proxy.test/v1`.

### D-1 (blocker): a scheme-shaped prefix still puts the credential into the request

- **Where:** providerEndpointModel.ts:71, `return scheme === null && doubleSlashAuthorityCarriesCredential(baseUrl)`.
- **Failure:** these values all return the endpoint, so the PUT or PATCH body carries `u:<password>`:
  - `baseUrl: https://u:pw@proxy.test/v1` (YAML or config-line paste)
  - `url: https://u:pw@…`
  - `endpoint:https://u:pw@…`
  - `x:"https://u:pw@…"`
  - `https:https://u:pw@…`
  - `data:text/plain,https://u:pw@h`

  `schemeOf` reads the prefix (`baseUrl`, `url`, …) as a scheme. The new check runs only when no scheme is found, and `rawAuthorityCarriesCredential` looks only at the text right after `scheme:`.
- **Why:** this is the same class as A-1: text before the real scheme hides the authority. `BASE_URL=` is refused only because `_` cannot appear in a scheme; `baseUrl:` and `url:` get through. The brief says any bypass that leaks userinfo into a request is a blocker. The server still refuses every one of these values (whitespace rule or non-http(s) scheme rule, never echoing the value), so this is a leak into the request, not a stored secret.
- **Fix (probed):** run the `//` authority check whenever the scheme's own text is not followed by `//`. The changed line:

  ```
  return (scheme === null || !baseUrl.startsWith('//', scheme.length + 1)) && doubleSlashAuthorityCarriesCredential(baseUrl)
  ```

  With this change vitest stays 43/43, all six strings above are refused, and every legitimate value still passes. Dropping `scheme === null` entirely is wrong: it turns "still accepts a special-scheme backslash that places @ in the path" red. Add the six strings as refusal tests, and commit the red before the fix.

## Advisories (do not hold the land)

- A-1: these still pass, but none reads as userinfo to any parser, so they are left as advisories:
  - two `//` with the first in the prefix (`x//y "https://u:pw@…"`, `see //docs https://u:pw@…`); fixing this needs a scan of every `//`, not just the first
  - `mailto:op%3Apw@h`
  - `mailto:x@y?to=op:pw@h`
  - `%40` or fullwidth `＠` in place of `@`
- A-2: the guard count is 53 in Antiphon.Tests against the 78 claimed. The brief's 78 probably includes the tests/Shared guard classes compiled into other assemblies.

## Checks (b) and (c)

- (b) The server boundary is unchanged and independent. `ProviderEndpointSettings.ValidateBaseUrl` (server/Application/Services/ProviderEndpointSettings.cs:90-115) refuses whitespace and control characters, `?` and `#`, non-absolute and non-http(s) URLs, and `uri.UserInfo`. The client only adds an earlier refusal and changes nothing on the server.
- (c) The client error is a static string (`userInfoError`) built from the display name only. The diff adds no logging, and neither server message echoes the value.
