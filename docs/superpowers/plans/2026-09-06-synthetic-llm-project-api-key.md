# Synthetic LLM Project API Key Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Grok, Codex, and Claude select an llm-key-proxy project through the API credential they already transmit, bypassing the browser hold while ensuring only the project's real key reaches Maven's upstream proxy.

**Architecture:** Introduce a deliberately non-secret `llm-project:<project-reference>` client credential. The proxy recognizes it in `Authorization: Bearer ...` or `x-api-key`, resolves the reference with the existing `ProjectNameMatch`, binds the session without a hold, resolves the project's real key, and replaces all inbound client authentication before forwarding. The canonical launchers emit the resolved project GUID as the reference; legacy headers and unbound picker behavior remain compatible.

**Tech Stack:** ASP.NET Core/.NET 9, Entity Framework Core, NUnit/Shouldly, PowerShell 7, Pester, Antiphon launch environments and Herdr.

**Spec:** Antiphon `CARD-0001` and its approved task brief; existing proxy design at `D:\src\Mikeys.Tools\docs\superpowers\specs\2026-08-17-llm-key-proxy-design.md`.

## Global constraints

- This is a two-repository implementation: proxy/launcher changes belong in `D:\src\Mikeys.Tools`; Antiphon documentation changes belong in `D:\src\Antiphon`. Do not mix their commits.
- Do not kill or restart the live proxy on `localhost:10746` to compile or test. Every .NET command uses a fresh `--artifacts-path` under `%TEMP%`.
- The synthetic value is a project selector, not a secret and not a valid upstream/LiteLLM key. Never log or return the real project key.
- Never forward the synthetic credential, the dummy `llm-key-proxy`, inbound `Authorization`, inbound `x-api-key`, or any `X-Llm-*` header upstream. Forward only `Authorization: Bearer <resolved real project key>`.
- Real credentials, the exact dummy `llm-key-proxy`, and missing credentials retain today's bound-session/process-reuse/hold/picker/cwd behavior.
- Unknown, ambiguous, empty, or malformed synthetic project references fail closed without creating a hold or opening a browser. Unknown and ambiguous references use the same HTTP status/error/similar-project shape as a bad `X-Llm-Project`.
- `OpenBrowserOnHold` remains `true` by default; the hold timeout, matcher thresholds, cwd matching, project schema, and port `10746` do not change.
- The launcher rollout is after the proxy rollout. An old proxy cannot interpret the new credential and could reopen the picker for clients whose legacy header is absent.
- Do not edit generated files under `D:\src\Antiphon\docs\cards`.

## Chosen wire scheme

The exact credential grammar is:

```text
llm-project:<project-reference>
```

- Match the `llm-project:` prefix case-insensitively, but emit it in lowercase.
- Trim the whole credential and the reference. Reject an empty reference, control characters, or a reference longer than 200 characters as `validation_error`.
- Pass the reference unchanged to `ProjectNameMatch.Resolve`, which already implements GUID, exact name, normalized name/slug, unique containment, ambiguity, and similar suggestions.
- Canonical launchers emit the resolved project GUID from `X_LLM_PROJECT`, for example `llm-project:8e5a6d18-...`. A GUID is stable, header-safe, unambiguous, and avoids a second escaping convention. Manually configured clients may use a header-safe name or normalized slug such as `SoftwareFactory`.

This scheme is readable during local diagnosis, cannot be mistaken for an `sk-...` LiteLLM key, and requires no signing or secret distribution. If it bypasses `localhost:10746`, the upstream must reject it rather than charge an unintended project.

## Bind and authentication order

Preserve the current sticky-session behavior and make the new signal explicit:

1. Reuse an existing **Bound** session by `X-Llm-Session-Id`.
2. Reuse an existing **Bound** session on the same connection.
3. For a new/unbound session, use nonblank `X-Llm-Project` (legacy signal).
4. If that header is absent, parse a synthetic credential from Bearer `Authorization`, then `x-api-key`.
5. Try `X-Llm-Key`.
6. Try process-identity reuse.
7. Otherwise create the normal hold/picker flow.

`X-Llm-Project` intentionally wins when both signals are present, preserving today's explicit header contract. Launchers set both to the same resolved project when the client supports the header. An already-bound client-session/connection remains sticky; changing project uses the existing rebind/new-session mechanism rather than silently switching a live session.

## File map

### Mikeys.Tools

- Create `Mikey.LlmKeyProxy/Services/SyntheticProjectCredential.cs`: credential grammar and request-carrier parser; no database access.
- Modify `Mikey.LlmKeyProxy/Services/SessionService.cs`: select legacy header versus synthetic credential, bind or return a typed project-resolution error, and keep normal hold behavior unchanged.
- Modify `Mikey.LlmKeyProxy/Services/LlmProxyMiddleware.cs`: render the generic explicit-project error using the query/source returned by `SessionService`; do not re-extract only `X-Llm-Project`.
- Retain `Mikey.LlmKeyProxy/Services/UpstreamProxyService.cs` unchanged: current source already strips both inbound auth carriers and writes only the resolved real key; add a regression test for that contract.
- Create `Mikey.LlmKeyProxy.Tests/SyntheticProjectCredentialTests.cs` and `Mikey.LlmKeyProxy.Tests/UpstreamProxyServiceTests.cs`.
- Modify `Mikey.LlmKeyProxy.Tests/SessionServiceTests.cs`.
- Modify `scripts/llm-launchers/llm-key-proxy-common.ps1`, `gkp.ps1`, `cxp.ps1`, and `clproxy.ps1`.
- Modify `scripts/llm-launchers/tests/LlmNamedSessions.Tests.ps1`; retain `GkCommon.Tests.ps1` header coverage.
- Modify `Mikey.LlmKeyProxy/README.md` and `scripts/llm-launchers/README.md`.
- No functional change to `Mikey.LlmKeyProxy.Cli`: it already resolves a selected project and returns `--project <name>` to the launcher, which then creates the synthetic credential.

### Antiphon

- Modify `docs/agent-credentials.md`, `docs/ai-agent-tui-configuration.md`, and `docs/herdr-sessions.md` to distinguish the stored bootstrap env from the credential emitted by the wrapper.
- Cite, but do not change, `src/Antiphon.SessionRunner/HerdrGkpLaunchGuard.cs` unless tests prove it checks the exact dummy value. Current code only requires a nonblank value in `XAI_API_KEY` or `GROK_CODE_XAI_API_KEY`.
- Do not persist `llm-project:...` in agent `launchEnv`: per `docs/agent-credentials.md` §2, agent/project/inherited env is merged before the wrapper runs. Keep `X_LLM_PROJECT` plus the current bootstrap credential there; `gkp.ps1`/`cxp.ps1`/`clproxy.ps1` computes and overwrites the client credential after project resolution.

---

### Task 1: Parse the synthetic credential independently

**Files:**

- Create: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy\Services\SyntheticProjectCredential.cs`
- Create: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\SyntheticProjectCredentialTests.cs`

**Interfaces:**

- Produces `SyntheticProjectCredential.Prefix = "llm-project:"`.
- Produces `SyntheticProjectCredential.Parse(HttpRequest)` returning `None`, `Valid(reference, carrier)`, or `Invalid(message, carrier)`.
- Carrier is `authorization` or `x-api-key`; no result contains a real credential value.

- [ ] **Step 1: Add failing parser tests**

Cover Bearer Authorization and `x-api-key`, case-insensitive prefix, surrounding whitespace, GUID/name/slug references, Authorization-first carrier order, dummy/real/missing values returning `None`, and prefix-only/control/overlength references returning `Invalid`.

```csharp
[TestCase("Bearer llm-project:CostAllocation", null, "CostAllocation", "authorization")]
[TestCase("Bearer LLM-PROJECT:software-factory", null, "software-factory", "authorization")]
[TestCase(null, "llm-project:8e5a6d18-4d55-458f-8bb7-3ebaa81edc01",
    "8e5a6d18-4d55-458f-8bb7-3ebaa81edc01", "x-api-key")]
public void Parse_valid_carriers(string? authorization, string? apiKey, string expected, string carrier)
{
    var request = new DefaultHttpContext().Request;
    if (authorization is not null) request.Headers.Authorization = authorization;
    if (apiKey is not null) request.Headers["x-api-key"] = apiKey;

    var result = SyntheticProjectCredential.Parse(request);

    result.Status.ShouldBe(SyntheticProjectCredentialStatus.Valid);
    result.ProjectReference.ShouldBe(expected);
    result.Carrier.ShouldBe(carrier);
}
```

- [ ] **Step 2: Run the focused test and confirm red**

```powershell
$tests = 'D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\Mikey.LlmKeyProxy.Tests.csproj'
$art = Join-Path $env:TEMP ('llm-key-proxy-art-' + [guid]::NewGuid().ToString('N'))
dotnet test $tests --artifacts-path $art --nologo --filter FullyQualifiedName~SyntheticProjectCredentialTests
```

Expected failure: the parser type does not exist.

- [ ] **Step 3: Implement the smallest pure parser**

Strip `Bearer ` only from Authorization; do not interpret other auth schemes. Examine Authorization first and then `x-api-key`, but continue to `x-api-key` when Authorization is ordinary/dummy rather than synthetic. A value that begins with the reserved prefix is always `Valid` or `Invalid`, never `None`.

```csharp
internal enum SyntheticProjectCredentialStatus { None, Valid, Invalid }

internal sealed record SyntheticProjectCredentialResult(
    SyntheticProjectCredentialStatus Status,
    string? ProjectReference,
    string? Carrier,
    string? Message);

internal static class SyntheticProjectCredential
{
    internal const string Prefix = "llm-project:";

    internal static SyntheticProjectCredentialResult Parse(HttpRequest request)
    {
        var candidates = new (string Carrier, string Raw, bool Bearer)[]
        {
            ("authorization", request.Headers["Authorization"].ToString(), true),
            ("x-api-key", request.Headers["x-api-key"].ToString(), false),
        };

        foreach (var (carrier, raw, bearer) in candidates)
        {
            var value = raw.Trim();
            if (bearer)
            {
                const string bearerPrefix = "Bearer ";
                if (!value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                value = value[bearerPrefix.Length..].Trim();
            }

            if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var reference = value[Prefix.Length..].Trim();
            if (reference.Length is 0 or > 200 || reference.Any(char.IsControl))
            {
                return new(SyntheticProjectCredentialStatus.Invalid, null, carrier,
                    "Synthetic project credential contains an invalid project reference.");
            }

            return new(SyntheticProjectCredentialStatus.Valid, reference, carrier, null);
        }

        return new(SyntheticProjectCredentialStatus.None, null, null, null);
    }
}
```

Do not log raw headers in this class.

- [ ] **Step 4: Re-run the focused parser tests and confirm green** with a new temporary artifact directory.

- [ ] **Step 5: Commit the parser and tests**

```powershell
git add Mikey.LlmKeyProxy/Services/SyntheticProjectCredential.cs Mikey.LlmKeyProxy.Tests/SyntheticProjectCredentialTests.cs
git commit -m "feat(llm-key-proxy): parse synthetic project credentials"
```

### Task 2: Bind without a hold and preserve fail-closed errors

**Files:**

- Modify: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy\Services\SessionService.cs`
- Modify: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy\Services\LlmProxyMiddleware.cs`
- Modify: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\SessionServiceTests.cs`
- Create: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\LlmProxyHandlerTests.cs` for handler/error assertions.

**Interfaces:**

- Extend `SessionBindResult` with the attempted project query/source used for error rendering; rename `ForcedProjectError` only if all call sites/tests are updated in this task.
- The middleware continues mapping `validation_error` to 400, `project_ambiguous` to 409, and `project_not_found` to 404 with `similar[]` and `proxy_session_id`.

- [ ] **Step 1: Add failing session tests**

Add explicit cases for:

- Authorization synthetic credential binds name/GUID without a hold.
- `x-api-key` synthetic credential binds a normalized slug without a hold.
- unknown and ambiguous references return `NeedsHold=false`, no key, and the existing `ProjectNameMatch.Result` error/suggestions.
- `llm-project:` returns `validation_error` and never falls through to a hold (this needs an explicit `IsPresent` flag; whitespace cannot be represented only by `string? ForcedProject`).
- real `sk-...`, exact dummy `llm-key-proxy`, and no key still produce `NeedsHold=true` for a new session.
- `X-Llm-Project` wins over a conflicting synthetic reference.
- an already-bound client session remains sticky.

```csharp
var context = CreateHttpContext(new()
{
    ["Authorization"] = "Bearer llm-project:SoftwareFactory",
});
var result = await service.ResolveOrCreateAsync(context, "{}"u8.ToArray());
result.NeedsHold.ShouldBeFalse();
result.ProjectId.ShouldBe(projectId);
result.KeyId.ShouldBe(keyId);
result.ForcedProjectError.ShouldBeNull();
```

- [ ] **Step 2: Run `SessionServiceTests` and confirm the new cases fail** using `dotnet test ... --artifacts-path $art --filter FullyQualifiedName~SessionServiceTests`.

- [ ] **Step 3: Integrate the parser in `ResolveOrCreateAsync`**

After the two existing Bound-session reuse checks, compute one explicit project attempt:

```csharp
var headerProject = hints.ForcedProject;
var synthetic = SyntheticProjectCredential.Parse(request);
var hasExplicitProject = !string.IsNullOrWhiteSpace(headerProject)
    || synthetic.Status is SyntheticProjectCredentialStatus.Valid or SyntheticProjectCredentialStatus.Invalid;
var projectQuery = !string.IsNullOrWhiteSpace(headerProject)
    ? headerProject
    : synthetic.ProjectReference;
var projectSource = !string.IsNullOrWhiteSpace(headerProject)
    ? "x-llm-project"
    : synthetic.Carrier;
```

When `hasExplicitProject`, call `ProjectNameMatch.Resolve(projectQuery, catalog)`. For a valid match, set Bound status/project/key/counters and return `NeedsHold=false`. For an invalid or unresolved match, persist the diagnostic session exactly as the header path does and return `NeedsHold=false` plus the match error, query, and source. This is before `X-Llm-Key`, process reuse, and hold creation.

- [ ] **Step 4: Generalize the middleware's existing forced-project error block**

Use the query/source carried on `SessionBindResult`; do not call `SessionHintExtractor.Extract(...).ForcedProject` again because that loses credential-origin queries. Keep the existing public error type/status/similar fields, adding `source` only if it contains the fixed carrier label and no credential.

- [ ] **Step 5: Add/execute focused handler tests** proving an unknown synthetic reference has the same status/body shape as the equivalent bad `X-Llm-Project`, and that no `IHoldService.CreateAndWaitAsync` call occurs.

- [ ] **Step 6: Re-run parser, session, project-name-match, and handler tests; then commit**

```powershell
git add Mikey.LlmKeyProxy/Services/SessionService.cs Mikey.LlmKeyProxy/Services/LlmProxyMiddleware.cs Mikey.LlmKeyProxy.Tests/SessionServiceTests.cs Mikey.LlmKeyProxy.Tests/LlmProxyHandlerTests.cs
git commit -m "feat(llm-key-proxy): bind projects from synthetic credentials"
```

### Task 3: Pin upstream credential replacement and non-leakage

**Files:**

- Read-only production contract: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy\Services\UpstreamProxyService.cs`
- Create: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\UpstreamProxyServiceTests.cs`

**Interfaces:**

- `ProxyAsync(HttpContext context, string apiKey, Guid proxySessionId, ISessionService sessions)` continues receiving only the already-resolved real key.

- [ ] **Step 1: Add a capturing `HttpMessageHandler` test**

Send a request containing both `Authorization: Bearer llm-project:...`, `x-api-key: llm-project:...`, and an `X-Llm-Project` header. Call `ProxyAsync` with a sentinel real key held only in the test.

Assert the captured upstream request has exactly `Authorization: Bearer <sentinel>`, has no `x-api-key`, has no `X-Llm-*`, and contains no synthetic value in any header or body. Also assert the response/usage path still completes.

- [ ] **Step 2: Run the focused test and inspect red/green honestly**

The current implementation already filters both carriers and assigns a new Bearer header, so the expected result is PASS immediately. Retain the test as the regression pin and do not manufacture a production change. A failure means the observed source contract has drifted; stop this task at red and revise the plan against that current source before editing production forwarding behavior.

- [ ] **Step 3: Run the full proxy test project with a fresh artifact directory**

```powershell
$tests = 'D:\src\Mikeys.Tools\Mikey.LlmKeyProxy.Tests\Mikey.LlmKeyProxy.Tests.csproj'
$art = Join-Path $env:TEMP ('llm-key-proxy-art-' + [guid]::NewGuid().ToString('N'))
dotnet test $tests --artifacts-path $art --nologo
```

- [ ] **Step 4: Commit the upstream contract test and any required fix**

```powershell
git add Mikey.LlmKeyProxy.Tests/UpstreamProxyServiceTests.cs
git commit -m "test(llm-key-proxy): prevent client credential leakage upstream"
```

### Task 4: Make every canonical launcher send the synthetic credential

**Files:**

- Modify: `D:\src\Mikeys.Tools\scripts\llm-launchers\llm-key-proxy-common.ps1`
- Modify: `D:\src\Mikeys.Tools\scripts\llm-launchers\gkp.ps1`
- Modify: `D:\src\Mikeys.Tools\scripts\llm-launchers\cxp.ps1`
- Read-only call site: `D:\src\Mikeys.Tools\scripts\llm-launchers\clproxy.ps1` (it already calls `Set-LlmProxyClientEnv` after project parsing and does not overwrite the token afterward).
- Modify: `D:\src\Mikeys.Tools\scripts\llm-launchers\tests\LlmNamedSessions.Tests.ps1`

**Interfaces:**

- Add `Get-LlmProxySyntheticProjectCredential`, returning `$null` without `X_LLM_PROJECT` and `llm-project:<resolved-guid>` otherwise.
- `Set-LlmProxyClientEnv` remains the single owner of client-specific base URL and credential env.

- [ ] **Step 1: Add failing Pester cases**

Dot-source `llm-key-proxy-common.ps1` and verify:

- `X_LLM_PROJECT=<guid>` creates exactly `llm-project:<guid>`.
- missing/blank project creates no synthetic credential.
- Grok gets synthetic `XAI_API_KEY` when bound, otherwise the current dummy.
- Codex gets synthetic `OPENAI_API_KEY` when bound, otherwise the current dummy.
- Claude overwrites its current `ANTHROPIC_AUTH_TOKEN` with the synthetic value when bound; when unbound it preserves a real preexisting token and only supplies the dummy when no token exists.
- source-order assertions prove `Get-OptionalProxyProjectArgs` resolves the project before `Set-LlmProxyClientEnv`.

```powershell
$env:X_LLM_PROJECT = '8e5a6d18-4d55-458f-8bb7-3ebaa81edc01'
Set-LlmProxyClientEnv -Client codex
$env:OPENAI_API_KEY | Should Be 'llm-project:8e5a6d18-4d55-458f-8bb7-3ebaa81edc01'
```

- [ ] **Step 2: Run Pester and confirm red**

```powershell
Invoke-Pester -Path 'D:\src\Mikeys.Tools\scripts\llm-launchers\tests\LlmNamedSessions.Tests.ps1' -Output Detailed
```

- [ ] **Step 3: Implement the common helper and environment rules**

```powershell
function Get-LlmProxySyntheticProjectCredential {
    if ([string]::IsNullOrWhiteSpace($env:X_LLM_PROJECT)) { return $null }
    return "llm-project:$($env:X_LLM_PROJECT.Trim())"
}
```

In `Set-LlmProxyClientEnv`, calculate it only after `Get-OptionalProxyProjectArgs` has resolved the project. Grok/Codex use synthetic-or-dummy. Claude uses synthetic when present, otherwise keeps its existing real/dummy fallback behavior.

Remove the later hard-coded dummy assignments that currently overwrite the result:

- `gkp.ps1`: delete the second `$env:XAI_API_KEY = 'llm-key-proxy'`; call `Invoke-Gk -ApiKey $env:XAI_API_KEY`.
- `cxp.ps1`: delete `$env:OPENAI_API_KEY = 'llm-key-proxy'`; keep `Invoke-Cx -ApiKey $env:OPENAI_API_KEY`.
- `clproxy.ps1`: rely on the common function after its existing project/key argument parsing; do not resolve or expose the real upstream project key.

Keep best-effort `X-Llm-Project` header configuration in Grok/Codex as defense in depth even though grok-shell 1.0.13 did not send it in the measured failure.

- [ ] **Step 4: Re-run `LlmNamedSessions.Tests.ps1` and `GkCommon.Tests.ps1`**, with no live Grok/Codex/Claude process and no request to `:10746`.

- [ ] **Step 5: Commit canonical launcher changes**

```powershell
git add scripts/llm-launchers/llm-key-proxy-common.ps1 scripts/llm-launchers/gkp.ps1 scripts/llm-launchers/cxp.ps1 scripts/llm-launchers/tests/LlmNamedSessions.Tests.ps1
git commit -m "feat(llm-launchers): send project through client credentials"
```

Do not hand-edit `C:\Users\mike.ciechan\.local\bin` in the implementation commit. It is the installed copy; update it during rollout with the canonical installer and verify hashes.

### Task 5: Document the contract and Antiphon environment ownership

**Files:**

- Modify: `D:\src\Mikeys.Tools\Mikey.LlmKeyProxy\README.md`
- Modify: `D:\src\Mikeys.Tools\scripts\llm-launchers\README.md`
- Modify: `D:\src\Antiphon\docs\agent-credentials.md`
- Modify: `D:\src\Antiphon\docs\ai-agent-tui-configuration.md`
- Modify: `D:\src\Antiphon\docs\herdr-sessions.md`

- [ ] **Step 1: Document the scheme, bind order, fail-closed behavior, and non-secret status** in the proxy/launcher READMEs. State that the project GUID is canonical launcher output and that real/dummy/missing credentials remain legacy/unbound inputs.

- [ ] **Step 2: Document Antiphon's two phases**

Use `docs/agent-credentials.md` §2 as the source of truth: profile → managed secrets → project default → inherited caller → agent launch env → launch override → `ExtraEnv`, with later layers winning. Explain that Herdr's generated launch script applies the merged bootstrap env to the wrapper; the wrapper subsequently resolves `X_LLM_PROJECT` and replaces the client credential before starting the actual TUI.

- [ ] **Step 3: Keep the CARD-0341 guard accurate**

The stored gkp environment still needs project, local base URL, and a nonblank bootstrap key so a malformed wrapper launch fails loudly. Do not require the stored value itself to be synthetic and do not change `HerdrGkpLaunchGuard` unless a focused Antiphon test demonstrates an exact-value dependency.

- [ ] **Step 4: Commit docs separately in their owning repositories**

```text
Mikeys.Tools: docs(llm-key-proxy): document synthetic project credentials
Antiphon:     docs(agents): document proxy credential handoff
```

### Task 6: Verify, roll out proxy first, then install launchers

- [ ] **Step 1: Run the full isolated proxy suite and launcher suites**

Use a new `%TEMP%` artifacts directory, record discovered/passed/failed counts, and do not call the live proxy.

- [ ] **Step 2: Publish/deploy the new proxy parser to the existing local run directory** using the supported `llm-key-proxy.ps1` path. This is the first point at which restarting `:10746` is allowed. Verify `GET http://localhost:10746/health` against the individual resource, not only the Aspire dashboard.

- [ ] **Step 3: Install canonical launchers**

```powershell
pwsh -NoProfile -File 'D:\src\Mikeys.Tools\scripts\llm-launchers\install.ps1' -Destination 'C:\Users\mike.ciechan\.local\bin'
```

Compare SHA-256 hashes for `llm-key-proxy-common.ps1`, `gkp.ps1`, `cxp.ps1`, and `clproxy.ps1` between source and installed copies.

- [ ] **Step 4: Run one controlled smoke per client path**

Use a disposable/new proxy session and `--require-project --project CostAllocation` (or an operator-approved low-cost test project). Verify from `/api/sessions` and `/api/holds` that the new session is Bound to the expected project, no hold was created, and no `/select/{holdId}` browser opened. Verify upstream success without printing any credential.

- [ ] **Step 5: Recheck legacy behavior**

With an isolated HTTP test client, prove dummy/missing/real non-synthetic values still enter the normal hold path. Cancel the test hold; do not wait five minutes and do not disturb live agent sessions.

## Launcher and Antiphon environment matrix

| Path | Project input | Client credential actually sent | Base URL owner | Stored Antiphon env |
|---|---|---|---|---|
| Grok `gkp.ps1` | resolved `X_LLM_PROJECT` GUID | `XAI_API_KEY=llm-project:<guid>`; dummy only when unbound | `GROK_BASE_URL=http://localhost:10746/v1` plus current Grok proxy routing | project marker, local URL(s), nonblank bootstrap key; wrapper overwrites key |
| Codex `cxp.ps1` | resolved `X_LLM_PROJECT` GUID | `OPENAI_API_KEY=llm-project:<guid>` | `Invoke-Cx` provider `base_url`; `OPENAI_BASE_URL` alone is insufficient | project marker and wrapper profile; wrapper supplies key |
| Claude `clproxy.ps1` | resolved `X_LLM_PROJECT` GUID | `ANTHROPIC_AUTH_TOKEN=llm-project:<guid>` (Authorization carrier) | `ANTHROPIC_BASE_URL` with `DONTOVERRIDE=1` | project marker and wrapper profile; wrapper replaces its current token |
| Direct Claude-compatible client | configured name/GUID/slug | `x-api-key: llm-project:<reference>` | client configuration | outside launcher scope |
| Legacy/unbound client | none | real key, `llm-key-proxy`, or missing | unchanged | normal hold/picker/cwd behavior |

## Test list

| Layer | Test | Required assertion |
|---|---|---|
| Credential parser | Authorization table | Bearer prefix, prefix case, trim, GUID/name/slug, and carrier label |
| Credential parser | `x-api-key` table | Same reference parsing through the Claude-compatible carrier |
| Credential parser | non-synthetic table | real key, exact dummy, missing key, and non-Bearer Authorization return `None` |
| Credential parser | malformed table | empty, control-character, and overlength references return `Invalid` without retaining the raw value |
| Session binding | Authorization bind | correct project/key, `NeedsHold=false` |
| Session binding | `x-api-key` bind | normalized slug resolves through `ProjectNameMatch`, `NeedsHold=false` |
| Session binding | unknown/ambiguous | existing 404/409 error types and similar suggestions; no hold |
| Session binding | malformed | 400 `validation_error`; no hold/browser |
| Session compatibility | header precedence/sticky session | `X-Llm-Project` wins; existing Bound session remains stable |
| Session compatibility | legacy credentials | real/dummy/missing inputs retain the new-session hold path |
| Middleware | error-shape parity | synthetic failure matches bad-header status/body and never calls hold creation |
| Upstream | credential replacement | only resolved Bearer key is sent; inbound auth, `x-api-key`, `X-Llm-*`, and synthetic value are absent |
| Launcher common | credential derivation | resolved GUID produces exact `llm-project:<guid>`; blank project produces no synthetic value |
| Grok launcher | final env/argument | `XAI_API_KEY` and `Invoke-Gk -ApiKey` use synthetic-or-dummy consistently |
| Codex launcher | final env/argument | `OPENAI_API_KEY` and `Invoke-Cx -ApiKey` use synthetic-or-dummy consistently |
| Claude launcher | preservation/overwrite | bound project overwrites token with synthetic; unbound real token is preserved; absent token gets dummy |
| Installer | source/install parity | four installed launcher files hash-identical to canonical source |
| Rollout smoke | one new session per client | expected Bound project, zero new holds/picker, successful upstream response without credential output |

## Security checks

- Treat possession of the synthetic value like possession of `X-Llm-Project`: it selects attribution in the local-trust proxy but grants nothing without proxy-side key custody.
- Resolve and store only `ProjectId`/`KeyId`; do not persist raw Authorization or `x-api-key` values.
- Error responses may include the attempted project query and fixed carrier name, never a complete credential or resolved real key.
- Keep auth filtering centralized in `UpstreamProxyService` and regression-tested for both carriers.
- Existing key resolution logging may name a key record but must never print the secret, substituted header, or last-four value for this flow.

## Rollback

1. Reinstall the previous launcher revision first so clients send the legacy dummy/real credentials that both proxy versions understand.
2. Republish/restart the previous proxy revision only after launchers are back. Verify `:10746/health` directly.
3. Existing database sessions/projects/keys require no migration or cleanup; the feature adds no stored secret or schema.
4. A new launcher accidentally used against an old proxy must only fall back to the old hold/picker behavior; routing URLs still point to localhost, so the synthetic value must never reach Maven directly.

## Out of scope

- Implementing code in this Plan-stage task.
- Changing the project matcher, hold timeout, browser default, session persistence, key storage, or upstream URL.
- Making the legacy dummy `llm-key-proxy` itself a project signal.
- Teaching grok-shell to send `X-Llm-Project` reliably.
- Adding JWT/HMAC signing, multi-user authorization, Kubernetes, Vault changes, or new database columns.
- Changing LlmProxy.Cli selection UX or output; current source returns launcher/project arguments and does not write a client credential.
- Rewriting Antiphon agent records to store synthetic credentials; wrappers own the final conversion.

## Self-review checklist

- Scheme and canonical emitted reference are exact.
- Header-versus-synthetic and sticky-session order are explicit.
- Empty/malformed references cannot accidentally become a hold.
- Both credential carriers and upstream non-leakage are tested.
- Grok/Codex hard-coded dummy overwrites are removed; Claude's unbound real-token behavior is preserved.
- Proxy-before-launcher rollout, direct health verification, rollback, and installed-copy hash checks are included.
- No implementation placeholder, schema migration, live-proxy test dependency, or generated-card edit is required.
