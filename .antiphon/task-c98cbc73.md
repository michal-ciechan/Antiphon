# Review c98cbc73 — CARD-1509 round 1 (Final, Full)

Subject: Code 518c63e8 (Grok author), feat/card-task-518c63e8 tip 5f4079c06de409b88a05da6e071e08a2913c9c5d,
range 965ab080395310c957b04e3ea9088c5f04534670..5f4079c06de409b88a05da6e071e08a2913c9c5d. Verdict: BLOCKER (D-1).

## Evidence (all at the reviewed SHA unless noted; one build-slot lease per command)
- Touched client tests (providerEndpointModel.test.ts + ProviderEndpointFields.test.tsx): 28/28 green; typecheck 0, lint 0.
- Red: model file restored to 965ab0803 with the new tests: 12 failed / 16 passed (28). The 10 model rows fail on
  `endpoint` not null / wrong error / map still carries Grok; the 2 form rows fail on the refusal text never appearing.
- Mutant (drop `|| url.password.length > 0`): 1 failed ("refuses password only") / 27 passed — killed. File restored.
- C# guard batch (TestClassificationPolicy*|TestClassificationGuard*|SlowTestRegistryMerge*|SlowTestTripwire*|
  NativeLinuxPolicyWitnessTests*|TestLaneCategoryGuardTests*) in Antiphon.Tests: 53 total, 53 passed, 0 skipped.
- check-evidence-diff 965ab0803..HEAD: commits=2 entries=0 violations=0.
- merge-tree vs origin/master 9e050cf9600aef15b77d67957fc0cd68826d6b35: clean.
- No plan / ### Checkpoints table exists for CARD-1509; no PCs listed.

## Adversarial scratch (22 strings, scratch file deleted)
Refused correctly: HTTPS:// uppercase, `https:user:pass@host`, tab inside authority, `http:\\user:pass@host`,
percent-encoded userinfo, leading newline, scheme-less `user:pass@host:port/path`, scheme-less IPv6 `user:pass@[::1]:8080`,
unclosed `http://user:pass@[::1`, scheme-less `token@host?x=1`.
Accepted correctly: `http://good.example\@evil` (WHATWG: host good.example, path /@evil), `@` in query, fragment, `%40` in path.
SENT WITH THE CREDENTIAL (endpoint returned, PUT/PATCH proceeds):
1. `ftp://operator:SENTINEL1509@proxy.test/v1`
2. `ws://operator:SENTINEL1509@proxy.test`
3. `file://operator:SENTINEL1509@host/`
4. `git+https://operator:SENTINEL1509@proxy.test`
5. `//operator:SENTINEL1509@proxy.test/v1` (scheme-relative)
6. `SENTINEL1509@proxy.test/v1` (scheme-less token@host/path)
7. `SENTINEL1509@proxy.test:8080` (scheme-less token@host:port)
(`mailto:operator:S@proxy.test` also passes; that is the documented mailto exception, not counted.)
By the same branch, a scheme typo such as `htps://user:pass@host` is sent too.

## D-1 (blocker)
Where: client/src/features/settings/providerEndpointModel.ts:60-74 (`baseUrlCarriesUserInfo`).
Failure: the seven strings above make `endpointFromDraft` return the endpoint with `error: null`; project PUT /
agent PATCH then carry the credential once before the server's 422.
Why: url.username/password are consulted only for http:/https:; any other parsed scheme containing `://` returns false
("leave for the server scheme refusal"), and the unparsable/scheme-less fallback regex `^[^:/@\s]+@[^:/@\s]+$`
rejects any `/` or `:` after the `@`, so `token@host/v1`, `token@host:8080` and `//u:p@h` pass. The card's acceptance is
"reject endpoint URLs with username or password ... no request is sent for userinfo"; the server refusing them after
receipt is exactly the disclosure the card removes. The commit claims scheme-less user@host and unparsed authorities
are refused; only the bare forms are.
Fix: refuse whenever the authority carries `@`, independent of scheme: for any parsed URL refuse on non-empty
username/password (not only http/https), and on the raw text refuse when the segment after an optional `scheme:` and
`//` (or a leading `//`), up to the first `/ ? #` (and `\` for special schemes), contains `@`; for scheme-less text
(no `//`) refuse when an `@` precedes the first `/ ? #`. Keep mailto and `@`-in-path/query/fragment accepted. Add
these seven strings (plus `htps://u:p@h`) as refusal rows.

## Checks with no defect
- Error text names only the display name; no console/localStorage/notification/aria echo in the changed files or the
  two callers (AgentSettingsModal.tsx:200-203 and ProjectConfig.tsx:207-210 return before mutate). Form tests assert
  `put`/`patch` not called and the field keeps the typed value.
- Server: ProviderEndpointSettings.ValidateBaseUrl refuses non-http(s), non-absolute and userinfo URLs with a
  message that does not echo the value, so every passing string above is refused server-side; no server advisory.
- Preservation rows (http/https, loopback, path `@`, `http://@host`, mailto, missing key name, blank draft, server 422
  display) green.
