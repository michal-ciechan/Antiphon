# CARD-0007 — Numeric `modelLevel` on agent create: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0007 (`95b5fe35-14ed-49fd-8860-641f2b892b41`) — `POST /api/agents` with
`"modelLevel": 0` was documented as returning 200 with `High` instead of `Frontier` or a 400.
**Precedent:** CARD-0016 already removed the EF `HasDefaultValue(1)` on `Agents.ModelLevel`
(`AppDbContext.cs:730-736`, migration `20260809214641_SessionDelegationTokenAndAgentModelLevelFix`).
That was the 2026-08-09 persistence miss: Frontier is CLR 0, so EF omitted it from the INSERT and
Postgres wrote High. It is not this card.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The card's 2026-08-09 diagnosis is stale; the remaining bug is real and is the converter.**

Live against the 2026-08-19 server (`POST http://localhost:17202/api/agents`):

| Body | Status | `modelLevel` in the response |
|---|---|---|
| omitted | 201 | `"High"` (the `CreateAsync` default) |
| `"Frontier"` | 201 | `"Frontier"` |
| `0` (number) | 201 | `"Frontier"` |
| `1` (number) | 201 | `"High"` |
| `99` (number) | 201 | `99` (a number, not a string) |
| `"99"` (string) | 201 | `99` |
| `"nope"` | **500** | ExceptionMiddleware generic body |

`CreateAgentRequest.ModelLevel` (`AgentDtos.cs:170`, `AgentModelLevel?`, default `null`) is not
"failing to bind". `Program.cs:197` registers `new JsonStringEnumConverter()` with the default
`allowIntegerValues: true`, so a number is a legal enum token. `0` is `Frontier`. `99` is an
undefined enum value that round-trips as a JSON number. `AgentService.CreateAsync`
(`AgentService.cs:262`) only sees `request.ModelLevel ?? AgentModelLevel.High` — omitted and
explicit-null are the High default; a supplied integer never gets there as null.

The card's stated fix still stands: **reject an unbindable `modelLevel` with 400**. Do not map
`0` → Frontier as a feature. The string-only wire (`"Frontier"` / `"High"` / `"Medium"` / `"Low"`)
is the contract; the client already uses a string union (`client/src/api/agents.ts:56`) and
`delegate.ps1 -Level` is a `ValidateSet` of those names.

The service cannot tell omitted from a bad token once bind has run. The fix is the JSON converter
plus making bind failures actually 400 (today `"nope"` is 500 because `ExceptionMiddleware` only
special-cases `HttpException`).

One Code+Docs slice.

## 1. Current shape (verified 2026-08-19)

### 1.1 Bind path

```
POST /api/agents
  └─ AgentEndpoints.MapPost (AgentEndpoints.cs:78)  // pass-through
       └─ STJ + JsonStringEnumConverter()           // Program.cs:197, allowIntegerValues: true
            └─ CreateAgentRequest.ModelLevel        // AgentModelLevel?, default null
                 └─ AgentService.CreateAsync        // ModelLevel = request.ModelLevel ?? High
```

`UpdateAgentRequest.ModelLevel` (`AgentDtos.cs:211`) is the same nullable enum; `null` means leave
unchanged (`AgentService.cs:346-347`). Live `PATCH` with `"modelLevel": 0` **sets Frontier** — it
does not no-op. Same converter, same hole.

`CreateAgentTaskRequest.ModelLevel` (`AgentTaskDtos.cs:16`) is the same type. A numeric `0` on
`POST /api/agent-tasks` overrides the role policy with Frontier. `delegate.ps1` sends a string
today; a hashtable `modelLevel = 0` would not.

### 1.2 Why 0 is not High anymore

`AgentModelLevel.Frontier = 0`, `High = 1` (`AgentModelLevel.cs:11-16`). CARD-0016 stopped EF
treating 0 as "unset". The orchestration-loop warning (`docs/orchestration-loop.md:103-105`) and
`TODO.md` still describe the pre-0016 symptom. CARD-0088 S3 added that warning and explicitly
left this card to fix the API.

### 1.3 Why `"nope"` is 500, not 400

`JsonStringEnumConverter` *does* throw `JsonException` on an unknown name. Minimal APIs wrap body
bind failures as `BadHttpRequestException` (400). `ExceptionMiddleware` (`ExceptionMiddleware.cs:42`)
maps only `HttpException` (the app type) to its status; everything else, including
`BadHttpRequestException` and raw `JsonException`, becomes 500 with
`"An unexpected error occurred."`. The 400 Problem Details titles already exist (`:84`, `:94`) and
are unused for this path.

Flipping `allowIntegerValues` without the middleware change turns `"modelLevel": 0` into a 500.
The card asked for 400.

## 2. Decisions

| Option | Decision | Why |
|---|---|---|
| Map `0` → Frontier and accept integers | **Reject** | The card: silent coercion of a value the caller supplied is the bug; string-only is fine. `99` already persists garbage. |
| Validate in `AgentService.CreateAsync` | **Reject** | After bind, omitted and failed-token are both `null`. Cannot 400 a token the service never saw. |
| Converter only on `CreateAgentRequest.ModelLevel` | **Reject** | Same type on PATCH and on `CreateAgentTaskRequest`. A property-level converter misses them. |
| `new JsonStringEnumConverter(allowIntegerValues: false)` in `ConfigureHttpJsonOptions` | **Take this** | One line; matches how the API already *serialises* enums. Unknown names already throw. Numeric `"99"` is rejected once integer tokens are off (it is not a member name). |
| `ValidationException` (422) from the service | **Reject** | Bind failures are 400. 422 is for business rules after a body has bound. |
| Map every `JsonException` in the process to 400 | **Reject** | A service parsing a stored file should stay 500. Map `BadHttpRequestException` (and raw `JsonException` only if the HTTP test still sees one). |

Blast radius of the global converter flag: every enum on the HTTP API. The TypeScript client and
`delegate.ps1` already send strings. HTTP tests that `PostAsJsonAsync` a **C# enum** without a
string converter will start 400-ing — those tests are sending numbers. Fix the tests to send
strings (or share the server's options). Do not leave integers accepted for "compat".

Out of scope: `AgentService` default High, DTO nullability, the client, messaging-service
`JsonStringEnumConverter`, re-opening CARD-0016's EF model.

## 3. The slice (one Code+Docs)

### 3.1 Converter — `server/Program.cs:197`

```csharp
options.SerializerOptions.Converters.Add(
    new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
```

Keep `namingPolicy: null` (member names `Frontier`, not `frontier`). The client sends PascalCase.

### 3.2 Bind failures are 400 — `server/Api/Middleware/ExceptionMiddleware.cs`

In `HandleExceptionAsync`, treat `Microsoft.AspNetCore.Http.BadHttpRequestException` as
`bad.StatusCode` (400) and put a useful `detail` on the Problem Details (the exception message,
or the inner `JsonException` message which names `$.modelLevel`). If a raw `JsonException`
still escapes from body bind (the live `"nope"` 500 must become 400 either way), map that the
same way **on this path only** — do not reclassify service-layer parse failures.

Do not throw `ValidationException` from the converter.

### 3.3 HTTP tests — `tests/Antiphon.Tests/Application/AgentModelLevelBindTests.cs`

Use `AntiphonWebAppFactory` (`[NotInParallel]`, `ClassDataSource` per test session) like
`CardCorrectionApiTests`. Post **raw JSON** (`StringContent`), not `PostAsJsonAsync` of a C#
enum — that is how a script sends `0`.

Pin, against `POST /api/agents`:

| Body `modelLevel` | Expect |
|---|---|
| omitted | 201, `"High"` |
| `"Frontier"` | 201, `"Frontier"` |
| `0` | **400**, no row |
| `99` | **400**, no row |
| `"99"` | **400**, no row |
| `"nope"` | **400** (not 500), no row |

One `PATCH /api/agents/{id}` case: `"modelLevel": 0` is 400 and the stored tier is unchanged.
Omitted `modelLevel` on PATCH still leaves the value (existing null-means-unchanged).

Do not add an AgentService unit test for this — it cannot see the token.

Optional cheap extra (same class, same factory): `POST /api/agent-tasks` with `"modelLevel": 0`
is 400. Not required to close the card; the converter is global.

### 3.4 Docs that currently lie

- `docs/orchestration-loop.md` §2 "Launching an agent": numeric `0` is a **400**, not a silent
  High. Keep "send the string `"Frontier"`".
- `TODO.md` bullet "A numeric `modelLevel` is silently ignored on agent create": delete it.
  The card is the record.
- `.claude/skills/antiphon-delegate/SKILL.md` does **not** contain this note (the brief's
  pointer was the orchestration-loop paragraph CARD-0088 S3 added). Do not add one.

## 4. What the Code agent runs

```
dotnet run --project tests/Antiphon.Tests --treenode-filter /*/*AgentModelLevelBindTests/* --property:OutputPath=bin-card0007/
```

Forward slash on `OutputPath`. Delete the `bin-card0007/` directories after. If other HTTP tests
go red because they posted numeric enums, that is this slice — fix the payloads, do not reopen
integers.

## 5. Commit

`fix(api): CARD-0007 - reject numeric modelLevel with 400 instead of binding the enum ordinal`
