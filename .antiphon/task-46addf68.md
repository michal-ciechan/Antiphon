# Review 46addf68 — CARD-1509 round 2 (Final, diff only)

Subject: Code cc67bb96 (Grok author, repair of Code 518c63e8), origin/feat/card-task-cc67bb96 tip
b7a21e9b646ad14585b7adc754806eb90a53d4d9. Round-2 range 5f4079c06de409b88a05da6e071e08a2913c9c5d..b7a21e9b646ad14585b7adc754806eb90a53d4d9
(round 1: Review c98cbc73, D-1). Verdict: CLEAN. Round-1 D-1 is fixed; no blocker, no regression. Two advisories.

## Evidence (at b7a21e9b6; one build-slot lease per command)
- Touched client tests (providerEndpointModel.test.ts + ProviderEndpointFields.test.tsx) via scripts/test-client.ps1
  with -TypecheckProject client -LintPath client/src/features/settings: 46/46 passed; typecheck 0; lint 0.
- Red: model file restored to 5f4079c06 with the new tests: 12 failed / 34 passed (46). The 12 failures are exactly
  the 12 new refusal rows. File restored.
- Mutant (delete the `value.startsWith('//')` scheme-relative branch): 1 failed ("refuses scheme-relative userinfo") /
  45 passed. Killed. File restored.
- C# guard batch in Antiphon.Tests (TestClassificationPolicy*|TestClassificationGuard*|SlowTestRegistryMerge*|
  SlowTestTripwire*|NativeLinuxPolicyWitnessTests*|TestLaneCategoryGuardTests*): total 53, passed 53, failed 0, skipped 0.
- check-evidence-diff 965ab0803..b7a21e9b6: commits=4 entries=0 violations=0.
- merge-tree vs origin/master 9e050cf9600aef15b77d67957fc0cd68826d6b35: clean (exit 0).
- No plan / `### Checkpoints` table exists for CARD-1509; no PCs listed. No checkpoint-tool receipt, so reviewedSourceClean is false.

## Adversarial scratch (63 strings through endpointFromDraft in vitest; scratch file deleted)
Refused (35), all correct:
- All 18 round-1 refusal targets: uppercase HTTPS, `https:` with no slashes, a tab in the authority, `http:\\`,
  percent-encoded userinfo, a leading newline, scheme-less `user:pass@host:port/path`, scheme-less IPv6, unclosed IPv6,
  scheme-less `token@host?q`, ftp, ws, file, git+https, `//u:p@h`, `token@host/v1`, `token@host:8080`, `htps://u:p@h`.
- New: CR inside the authority and inside the scheme, a newline in the password, all-uppercase, `%40` inside the
  userinfo, an IPv6 zone, a trailing-dot host, port-only, a double `@`, `http:/u:p@h`, `HtTp:\\`, `\\u:p@h`, IDN and
  punycode hosts, `git+https://good.example\@evil` (non-special, so `\` stays in the userinfo), NBSP in the userinfo,
  port overflow.
None of the refusal errors repeats the sentinel, the host or `@`.
Accepted, all correct: `mailto:user@host`, `http://@host`, `http://:@host`, `https://api.example.com/v1/@scope/pkg`,
`https://host:8080/path?x=a@b`, `https://host/a@b/c`, an `@` in the fragment, `%40` in the path and in the host,
`http://good.example\@evil`, `ftp://good.example\@evil`, an IDN host, a trailing dot, IPv6, `http://:8080/`,
`proxy.test/@scope`, and `http://proxy.test\u:S@evil` (the special-scheme backslash puts it in the path).
The 16 preservation rows still hold.

Accepted although the text holds a credential. The server refuses each one and does not echo it:
- A-1 (advisory, not a regression; 5f4079c06 behaved the same): text with a prefix before a valid scheme, such as
  `"https://u:S@proxy.test/v1"`, `'https://…'`, `<https://…>`, a U+200B zero-width space before the scheme,
  `https ://…`, `BASE_URL=https://…` and `1https://…`. Also the fullwidth `＠` (U+FF20) form, which no parser reads as
  userinfo. Where: providerEndpointModel.ts:85-95 (`rawAuthorityCarriesCredential`). Failure: `schemeOf` does not
  match, so the text is treated as scheme-less and cut at the first `/` of `//`. The `@` is never inspected, and a
  pasted quoted URL with userinfo reaches PUT/PATCH once before the server's 422. Why advisory: no URL parser
  (WHATWG or .NET Uri) reads these strings as URLs with userinfo, so they are outside the card's "URLs with username
  or password". Server ValidateBaseUrl rejects them as non-absolute or containing control characters, without echoing
  them. Fix (card): when the text has no valid scheme prefix but contains `//`, inspect the segment after the first `//`
  up to `/ ? #` for `@`. Alternatively, refuse any text whose `@` comes before the first `/ ? #` after the first `//`.
  Add the quoted-paste and ZWSP rows.
- A-2 (advisory, documented exception): `mailto:operator:S@proxy.test` and `MAILTO://operator:S@proxy.test/v1` are
  accepted by design ("mailto: stays a server scheme refusal"). The `//` form with a password is a credential. Fix (card):
  keep `mailto:user@host` accepted, but refuse mailto when the authority has a `:` password or the text has `//`.

## Server (advisory, unchanged)
server/Application/Services/ProviderEndpointSettings.cs:98-109 refuses control characters, query/fragment, a
non-absolute URL, a scheme other than http(s), and UserInfo, using messages that never echo the value. Every accepted
string in A-1 and A-2 is refused server-side. No server defect.

## Checks with no defect
- The round-2 diff touches only providerEndpointModel.ts and its test. The callers (AgentSettingsModal / ProjectConfig)
  are unchanged and still return before mutate. The round-1 form tests still assert that put/patch is not called.
- The new code has no console, storage or notification echo. The error text is fixed and names only the display name.
