# Story 1.1: Project Scaffold & Development Environment

Status: ready-for-dev

## Story

As a **developer**,
I want a working monorepo with backend and frontend projects, Docker Compose for PostgreSQL, and AI agent context files,
so that I can start building features with a consistent, documented foundation.

## Acceptance Criteria

1. **Given** a fresh clone of the repository, **When** I run `docker compose -f docker-compose.dev.yml up` and `dotnet run` and `npm run dev`, **Then** the backend serves API at `/api` and the frontend loads at `localhost:5173` with Vite proxy forwarding `/api/*` and `/hubs/*` to the backend.

2. **Given** the solution exists, **Then** the solution structure follows the architecture: `/server` (Domain, Application, Infrastructure, Api, Migrations) + `/client` (features, shared, stores, api, hooks).

3. **Given** the project is scaffolded, **Then** `docs/project-context.md` exists with extracted enforcement guidelines from the architecture document.

4. **Given** the project is scaffolded, **Then** `AGENTS.md` exists at the project root, referencing `docs/project-context.md` as the primary conventions source.

5. **Given** the project is scaffolded, **Then** `CLAUDE.md` exists at the project root, pointing at `AGENTS.md` for all agent context.

6. **Given** the project is scaffolded, **Then** `.editorconfig`, `.gitignore`, `appsettings.json.example`, and `Antiphon.sln` are configured.

7. **Given** the project is scaffolded, **Then** `docker-compose.yml` (production) and `docker-compose.dev.yml` (dev — PostgreSQL only) exist.

## Tasks / Subtasks

- [ ] Task 1: Create monorepo directory structure (AC: #2)
  - [ ] 1.1: Create `/server`, `/client`, `/tests`, `/docs` directories
  - [ ] 1.2: Create `Antiphon.sln` solution file
  - [ ] 1.3: Create `.editorconfig` with C# and TypeScript conventions
  - [ ] 1.4: Create `.gitignore` (bin, obj, node_modules, appsettings.json, .antiphon-cache)
  - [ ] 1.5: Create `.gitattributes` if not already present

- [ ] Task 2: Scaffold backend (AC: #1, #2)
  - [ ] 2.1: Run `dotnet new webapi` in `/server` targeting net10.0
  - [ ] 2.2: Create layered folder structure: Domain/, Application/, Infrastructure/, Api/, Migrations/
  - [ ] 2.3: Create Domain subfolders: Entities/, Enums/, ValueObjects/, StateMachine/
  - [ ] 2.4: Create Application subfolders: Services/, Interfaces/, Settings/, Dtos/, Exceptions/
  - [ ] 2.5: Create Infrastructure subfolders: Data/, Git/, Agents/, Realtime/, GitHub/, ExternalChanges/
  - [ ] 2.6: Create Api subfolders: Endpoints/, Middleware/
  - [ ] 2.7: Create minimal Program.cs with DI structure stubs
  - [ ] 2.8: Create `appsettings.json.example` and `appsettings.Development.json`
  - [ ] 2.9: Add solution references (`dotnet sln add`)

- [ ] Task 3: Scaffold frontend (AC: #1, #2)
  - [ ] 3.1: Run `npm create vite@latest` with react-ts template in `/client` (Vite 8)
  - [ ] 3.2: Install core npm packages (Mantine 8.x, TanStack Query 5.x, Zustand 5.x, React Router 7.x, SignalR client, react-icons)
  - [ ] 3.3: Install Mantine PostCSS dependencies (postcss, postcss-preset-mantine, postcss-simple-vars)
  - [ ] 3.4: Create `postcss.config.cjs` for Mantine
  - [ ] 3.5: Create feature directory structure: api/, stores/, hooks/, features/, shared/, test/
  - [ ] 3.6: Create minimal App.tsx with MantineProvider, QueryClientProvider, BrowserRouter
  - [ ] 3.7: Import `@mantine/core/styles.css` in main.tsx
  - [ ] 3.8: Configure `vite.config.ts` with proxy for `/api/*` and `/hubs/*`
  - [ ] 3.9: Enable TypeScript strict mode in tsconfig.json

- [ ] Task 4: Docker Compose configuration (AC: #7)
  - [ ] 4.1: Create `docker-compose.dev.yml` (PostgreSQL 16 only)
  - [ ] 4.2: Create `docker-compose.yml` (production: Antiphon + PostgreSQL 16)

- [ ] Task 5: Documentation & agent context files (AC: #3, #4, #5)
  - [ ] 5.1: Create `docs/project-context.md` with all 13 enforcement rules from architecture
  - [ ] 5.2: Create `AGENTS.md` referencing `docs/project-context.md`
  - [ ] 5.3: Create `CLAUDE.md` pointing at `AGENTS.md`

- [ ] Task 6: Verify end-to-end connectivity (AC: #1)
  - [ ] 6.1: Start PostgreSQL via `docker compose -f docker-compose.dev.yml up`
  - [ ] 6.2: Run `dotnet run` — verify API responds at `/api`
  - [ ] 6.3: Run `npm run dev` — verify frontend loads and proxy forwards to backend

## Dev Notes

### Critical Architecture Constraints

**Layer Boundaries — MUST follow Onion Architecture:**
- `Domain/` — ZERO infrastructure dependencies. Pure C#: entities, value objects, enums, state machine only. No EF Core, no SignalR, no HTTP, no external packages.
- `Application/` — Depends on Domain only. Concrete classes by default. Interfaces ONLY for external I/O seams: `IEventBus`, `IGitService`, `ICurrentUser`, `IStageExecutor`. Contains Services, Settings (typed IOptions<T>), Dtos, Exceptions.
- `Infrastructure/` — Implements interfaces from Application. All external I/O: database, SignalR, git, LLM providers, GitHub API.
- `Api/` — Composition root. Minimal API endpoints and middleware. DI wiring in Program.cs.

**Do NOT create entities, services, or EF Core context in this story.** Only create the folder structure. Story 1.2 creates the database foundation, Story 1.3 creates error handling, etc.

### Technology Stack — Verified Latest Versions (March 2026)

**Backend:**
| Package | Version | Notes |
|---------|---------|-------|
| .NET SDK | 10.0.201 | LTS, GA Nov 2025 |
| ASP.NET Core | 10.0 | Minimal APIs pattern |
| PostgreSQL | 16 | Via Docker |

**Frontend:**
| Package | Version | Notes |
|---------|---------|-------|
| React | 19.2.x | Latest stable |
| Vite | 8.x | Latest — uses Rolldown (Rust-based bundler), 10-30x faster builds. `@vitejs/plugin-react` v6 (uses Oxc, no Babel) |
| @vitejs/plugin-react | 6.x | Matches Vite 8 |
| @mantine/core | 8.3.x | Latest stable. Supersedes architecture's 7.x spec — using latest per project policy |
| @mantine/hooks | 8.3.x | Match core version |
| @mantine/notifications | 8.3.x | Match core version |
| @mantine/code-highlight | 8.3.x | Match core version |
| @tanstack/react-query | 5.x | Latest v5 |
| @tanstack/react-query-devtools | 5.x | Match query version |
| zustand | 5.x | BREAKING from v4: named imports only, no default exports, use `createWithEqualityFn` for shallow |
| react-router | 7.x | Single package — do NOT install react-router-dom (merged in v7) |
| @microsoft/signalr | latest | JS client for SignalR |
| react-icons | latest | Unified icon library |
| postcss | latest | Required for Mantine |
| postcss-preset-mantine | latest | Required for Mantine |
| postcss-simple-vars | latest | Required for Mantine |

**Version Notes:**
- **Mantine 8.x:** Architecture originally specified 7.x, but 7.x is EOL (final: 7.17.8, June 2025). Using 8.x per project policy of latest stable. See [7.x to 8.x migration guide](https://mantine.dev/guides/7x-to-8x/) for any API differences from architecture/UX spec references.
- **Vite 8:** Replaces esbuild+Rollup with Rolldown. If CJS imports break, add `legacy.inconsistentCjsInterop: true`. `build.rollupOptions` → `build.rolldownOptions` for some options.
- **Zustand 5 gotchas:** No default exports. Import `create` as named import. `persist` middleware API changed. All online v4 tutorials are wrong.
- **React Router 7:** Import from `react-router`, NOT `react-router-dom`. The packages merged.

### Scaffold Approach — Manual Composition (NOT dotnet new react)

The architecture explicitly REJECTS `dotnet new react` because:
- Opinionated `.esproj` structure conflicts with custom SignalR/API patterns
- `SpaProxy` middleware is incompatible with real-time architecture
- Instead: independent `/server` and `/client` projects with Vite proxy

### Program.cs — Minimal Stub Only

Create a minimal `Program.cs` that starts the backend. Do NOT add services, middleware, or DI registrations beyond the bare minimum to respond to `/api` requests. Subsequent stories add each layer:
- Story 1.2: Database + EF Core + typed settings
- Story 1.3: ExceptionMiddleware + Serilog + health checks
- Story 1.4: CurrentUserMiddleware + ICurrentUser
- Story 1.5: SignalR hub + IEventBus

```csharp
// Minimal Program.cs for Story 1.1
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback for production (serves React build from wwwroot)
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
```

### Vite Proxy Configuration

```typescript
// vite.config.ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/hubs': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,  // WebSocket support for SignalR
      },
    },
  },
})
```

### App.tsx — Minimal Shell

```tsx
import '@mantine/core/styles.css'
import { MantineProvider, createTheme } from '@mantine/core'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Routes, Route } from 'react-router'

const theme = createTheme({
  // Dark theme configured in Story 1.7
})

const queryClient = new QueryClient()

export default function App() {
  return (
    <MantineProvider theme={theme} defaultColorScheme="dark">
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<div>Antiphon — Dashboard placeholder</div>} />
          </Routes>
        </BrowserRouter>
      </QueryClientProvider>
    </MantineProvider>
  )
}
```

### Docker Compose — Dev Configuration

```yaml
# docker-compose.dev.yml — PostgreSQL only for local development
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: antiphon
      POSTGRES_USER: antiphon
      POSTGRES_PASSWORD: antiphon_dev
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

### Docker Compose — Production Configuration

```yaml
# docker-compose.yml — Full deployment
services:
  antiphon:
    build: .
    ports:
      - "5000:8080"
    environment:
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=antiphon;Username=antiphon;Password=${POSTGRES_PASSWORD}
    depends_on:
      - postgres

  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: antiphon
      POSTGRES_USER: antiphon
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

### docs/project-context.md — Content Requirements

Extract these 13 enforcement rules from the architecture:

1. Follow naming conventions exactly (see tables in architecture: C# PascalCase, TypeScript camelCase, API kebab-case, DB PascalCase plural)
2. Use concrete classes by default — interfaces ONLY for: `IEventBus`, `IChatClient`, `IGitService`, `ICurrentUser`, `IStageExecutor`
3. Respect layer boundaries — `Domain/` has zero infrastructure dependencies
4. Use `HttpException` hierarchy for all error responses — never return status codes manually
5. Push events through `IEventBus` — never call SignalR hub directly from services
6. Use EF Core `AppDbContext` directly — no repository wrapper pattern
7. Use `IOptions<TSettings>` for configuration — never inject `IConfiguration` directly
8. Include `CancellationToken` as last parameter on every `Async` method
9. Create EF Core migrations via CLI only — `dotnet ef migrations add`
10. Use TanStack Query for all REST data — never raw `fetch` + `useEffect`
11. Use Zustand stores for client state — never React Context for frequently-changing state
12. No static state anywhere — everything through DI (exception: AsyncLocal for Serilog correlation)
13. Follow SignalR → Query Invalidation Mapping table for cache invalidation

Also include: tech stack summary, naming convention tables, and layer boundary descriptions.

### .gitignore Must Include

```
# .NET
bin/
obj/
*.user
appsettings.json
appsettings.*.json
!appsettings.json.example

# Node
node_modules/
dist/

# Antiphon
.antiphon-cache/

# IDE
.vs/
.vscode/
*.swp
```

### .editorconfig Essentials

- `indent_style = space`, `indent_size = 4` for C#
- `indent_style = space`, `indent_size = 2` for TypeScript/JSON/YAML
- `end_of_line = lf`
- `charset = utf-8`
- `trim_trailing_whitespace = true`
- `insert_final_newline = true`

### AGENTS.md Content

```markdown
# Antiphon Agent Context

All AI coding agents working on this project MUST read and follow:

- **Primary conventions:** [docs/project-context.md](docs/project-context.md)

This file contains naming conventions, layer boundaries, enforcement rules,
and architectural decisions that all code must comply with.
```

### CLAUDE.md Content

```markdown
# Claude Code Configuration

See [AGENTS.md](AGENTS.md) for all project conventions and context.
```

### Project Structure Notes

- `/server` and `/client` are independent projects — no `.esproj` coupling
- Production build: `npm run build` → output to `server/wwwroot/` → `dotnet publish` produces single binary
- Development: two separate processes (dotnet run + npm run dev) with Vite proxy
- Solution file `Antiphon.sln` includes: `server/Antiphon.Server.csproj` (tests projects added in Story 1.6)

### What This Story Does NOT Create

- No database connection (Story 1.2)
- No error handling middleware (Story 1.3)
- No auth middleware (Story 1.4)
- No SignalR hub (Story 1.5)
- No test infrastructure (Story 1.6)
- No Mantine theme/navbar/routing (Story 1.7)
- No entities, services, or business logic
- No NuGet packages beyond the default webapi template

### References

- [Source: _bmad-output/planning-artifacts/architecture.md — "Project Scaffold" section]
- [Source: _bmad-output/planning-artifacts/architecture.md — "Folder Structure" section]
- [Source: _bmad-output/planning-artifacts/architecture.md — "Enforcement Rules" section]
- [Source: _bmad-output/planning-artifacts/architecture.md — "Naming Conventions" section]
- [Source: _bmad-output/planning-artifacts/epics.md — Story 1.1 acceptance criteria]
- [Source: _bmad-output/planning-artifacts/prd.md — "Technical Architecture Considerations" section]

## Dev Agent Record

### Agent Model Used

{{agent_model_name_version}}

### Debug Log References

### Completion Notes List

### File List
