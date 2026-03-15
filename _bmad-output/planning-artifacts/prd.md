---
stepsCompleted:
  - step-01-init
  - step-02-discovery
  - step-02b-vision
  - step-02c-executive-summary
  - step-03-success
  - step-04-journeys
  - step-05-domain
  - step-06-innovation
  - step-07-project-type
  - step-08-scoping
  - step-09-functional
  - step-10-nonfunctional
  - step-11-polish
  - step-12-complete
inputDocuments:
  - _bmad-output/planning-artifacts/original-rough-spec.md
documentCounts:
  briefs: 0
  research: 0
  brainstorming: 0
  projectDocs: 0
  other: 1
classification:
  projectType: web_app_b2b_hybrid
  domain: developer_tooling_ai_orchestration
  complexity: medium
  projectContext: greenfield
workflowType: 'prd'
---

# Product Requirements Document - Antiphon

**Author:** Mike
**Date:** 2026-03-15

## Table of Contents

- [Executive Summary](#executive-summary)
- [Project Classification](#project-classification)
- [Success Criteria](#success-criteria)
- [Product Scope](#product-scope)
- [User Journeys](#user-journeys)
- [Innovation & Novel Patterns](#innovation--novel-patterns)
- [Web App / B2B Platform Requirements](#web-app--b2b-platform-requirements)
- [Project Scoping & Phased Development](#project-scoping--phased-development)
- [Functional Requirements](#functional-requirements)
- [Non-Functional Requirements](#non-functional-requirements)

## Executive Summary

Antiphon is a spec-driven AI workflow orchestration platform that brings structure, visibility, and human governance to AI-assisted development. It replaces the current pattern where AI coding agents operate in isolation — planning happens locally, specs never get checked in, and teammates never review requirements or architecture before implementation begins.

V1 targets developer workflows: teams define specs (requirements, architecture, test plans, implementation stories) as versioned, reviewable artifacts that progress through a configurable graph of stages. At each stage, an AI agent executes work (analysis, code generation, testing) and produces an output artifact. Human reviewers see the output, provide free-text feedback or prompts, and control progression through approval gates. In v1.1, agents also propose structured next actions rendered as interactive buttons. Specs are committed to git and optionally reviewed via GitHub PRs.

The platform is built on a generic workflow engine with abstract executors — AI agents, scripts, and humans are all first-class stage runners. This enables expansion beyond dev workflows into CI/CD pipelines with AI-assisted testing and deployment stages, ops team data workflows, and business process automation — all using the same engine primitives, the same adaptive gate pattern, and the same human-AI collaboration model.

Built in C# on ASP.NET Core (.NET 10) with Microsoft Agent Framework and a React frontend, Antiphon brings AI orchestration to the enterprise .NET ecosystem. It is self-hosted (single-tenant, one instance per org), supports multi-model routing (Claude, GPT, Ollama), and provides full cost tracking per user, project, stage, and model.

### What Makes This Special

- **Spec-first, not code-first.** Planning artifacts are first-class versioned objects — reviewed before implementation starts, not after. Teams review requirements and architecture instead of raw code diffs.
- **AI-suggested actions at gates (v1.1).** Agents don't just produce output — they analyze what happened and propose structured next moves rendered as interactive buttons. Gates become intelligent decision points, not binary pass/fail checkboxes. MVP gates use free-text prompting; structured action buttons arrive in v1.1.
- **Generic workflow engine from day one.** Abstract executor interface means BMAD dev workflows, CI/CD pipelines, and ops processes are all configurations of the same graph. V1 ships dev workflows; expansion requires no engine rewrite.
- **C# + Microsoft Agent Framework showcase.** Proves AI agent orchestration belongs in the enterprise .NET ecosystem, leveraging checkpoint/resume, graph-based workflows, and multi-model routing via IChatClient.
- **Human + AI collaboration model.** AI does the work and proposes options. Humans steer via intelligent decision points with comments, prompts, and artifact review. Neither operates in a vacuum.

## Project Classification

- **Project Type:** Web application / B2B hybrid — React SPA with real-time streaming, RBAC, team workflows, cost tracking. Single-tenant self-hosted.
- **Domain:** Developer tooling / AI agent orchestration — software engineering productivity platform.
- **Complexity:** Medium — no regulatory burden, but technically ambitious (pluggable workflow graph engine, multi-model AI orchestration, checkpoint/resume, real-time streaming, plugin system).
- **Project Context:** Greenfield.

## Success Criteria

### User Success

- A developer creates a feature workflow from a markdown spec, has AI generate requirements and architecture artifacts, and a teammate reviews the spec via approval gate — **before any code is written**.
- A team lead sees all active workflows, their current stage, and can approve/reject gates from the dashboard — replacing ad-hoc Slack/Teams requests for review.
- Users can watch AI work in real-time and drill into a complete audit trail of every tool call, prompt, input, and output for any historical stage execution.

### Business Success

- 2-3 developers actively running features through Antiphon within 3 months of deployment.
- At least one team lead promoting Antiphon as the standard approach for new features/changes.
- >80% of new features go through spec review before implementation within 6 months — measuring the cultural shift, not just tool adoption.
- Demonstrated proof point that AI agent orchestration works in C# / .NET ecosystem using Microsoft Agent Framework.

### Technical Success

- Generic workflow engine executes structured YAML stage graphs — not hardcoded to BMAD or any specific methodology. AI-assisted workflow authoring (free-form → YAML) in v1.1.
- Built-in agent tools (file, bash, glob, grep, git). MCP tool registration protocol deferred to v1.1; MVP agents use built-in tools only.
- GitHub integration built into the single binary, enabled/disabled via feature flags per environment.
- Two-tier cost and audit storage: lightweight cost ledger (stage, model, tokens, USD — kept indefinitely) linked to full audit content (prompts, responses, tool calls — archivable after configurable retention period).
- Real-time visibility of agent execution plus full historical audit trail.
- Single deployable binary: ASP.NET Core serves React SPA as static files. Feature flags control optional integrations.

### Measurable Outcomes

| Metric | Target |
|--------|--------|
| Time from spec to reviewable artifacts | < 30 min for standard feature |
| Spec iteration cycles before approval | < 3 on average |
| Stage re-run rate (AI got it wrong) | < 25% |
| Developer adoption | 2-3 devs actively using within 3 months |
| Spec review before implementation | > 80% of new features within 6 months |
| Audit completeness | 100% of LLM calls and tool invocations logged with cost |

## Product Scope

*Summary. See [Project Scoping & Phased Development](#project-scoping--phased-development) for detailed breakdown, risk analysis, and phase roadmap.*

### MVP (Phase 1)

- Workflow engine: structured YAML stage graphs with approval gates (handcrafted definitions)
- AI agent execution per stage with BMAD bundled as default. Built-in tools only (file, bash, glob, grep, git).
- Two-tier git branching: `antiphon/workflow-{id}/master` + `antiphon/workflow-{id}/stage-{name}`. Tags for audit.
- Diff-based course correction via git diff between stage tags. Core differentiator.
- React dashboard (served by ASP.NET): workflow list, stage progression, artifact viewer, live agent output stream with activity status line
- Approval gates with free-text prompting. Approve, reject with feedback, go back to previous stage.
- GitHub integration (feature-flagged): PR creation, monitoring, comment/feedback feeding to agents
- External change detection: polling, auto-pull, path-based cascade triggers
- Two-tier audit and cost storage: cost ledger (permanent) + full audit content (archivable, 90-day default retention)
- Multi-model routing (Claude, GPT, Ollama) via IChatClient
- No auth — default admin user + IP logging. Single binary deployment with feature flags.

### v1.1 (Pre-team deployment)

- AI-suggested actions at gates (structured text → buttons)
- MCP over HTTP tool registration (webhook-style)
- AI-assisted workflow authoring (free-form → structured YAML)
- Hierarchical knowledge system with pluggable sources
- PR build failure analysis and suggested fixes
- Jira MCP tool

### Growth (Post team adoption)

- OIDC auth, multi-user, RBAC
- Notification integrations (Teams, email, Slack)
- Cost dashboard with reporting and budget enforcement
- Non-dev workflow templates
- Kubernetes deployment
- Audit archival to cold storage

### Vision (Future)

- CI/CD pipeline orchestration with AI-assisted testing/deployment
- Visual workflow builder UI
- Marketplace for workflow templates and MCP tool plugins
- Multi-repo workflow support
- AI-powered workflow optimization
- IDE integration (VS Code / JetBrains)

## User Journeys

*Journeys describe the full product vision, not just MVP. MVP coverage noted per journey so architects and designers understand what's built first vs. later.*

### Journey 1: Mike the Developer — New Feature, Happy Path
**MVP coverage: partial** — spec generation, AI execution, gates, git integration, GitHub PRs. Deployment pipeline stages and Jira MCP are post-MVP.

**Opening Scene:** It's Monday morning. Mike opens Antiphon's dashboard and sees his team's active workflows. He has a Jira ticket (TRADE-4521: "Add risk limit breach notifications") that needs implementation. He clicks "New Workflow," selects the team's standard dev methodology, points it at the trading platform repo, and enters the Jira ticket ID. Antiphon's Jira MCP tool pulls the ticket details automatically.

**Rising Action:** The AI analyst agent reads the Jira ticket, the repo's project context, and existing notification code, then generates a product spec. Mike sees the agent working in real-time — reading files, analyzing patterns, drafting the spec. When it finishes, the spec appears as a reviewable artifact. The AI also returns three suggested actions: [Approve spec] [Ask AI to expand error handling section] [Request changes]. Mike reads it, thinks the scope is right but wants more detail on the notification channels. He types "expand the section on notification delivery — we need Teams, email, and in-app" in the comment box and hits send. The agent revises the spec.

Mike approves. The workflow advances to test plan generation. The AI produces acceptance criteria and test scenarios. Mike's teammate Sarah gets a notification — she's assigned as reviewer for this gate. She reviews, clicks [Approve test plan]. Next: architecture. The AI proposes a high-level implementation plan — new classes, modified interfaces, integration points. Both Mike and Sarah review. Sarah clicks [Ask AI to reconsider the event bus approach — we should use the existing SignalR hub]. The agent revises. They approve.

**Climax:** Implementation begins. The AI developer agent writes code, creates tests, runs them, and opens a GitHub PR. The PR triggers the existing CI pipeline. Antiphon monitors the PR — when a build fails, the AI analyzes the failure and suggests a fix: [Apply suggested fix to serialization issue] [View full build log] [Ask AI to investigate]. Mike clicks the fix button. The agent pushes a commit. Build passes. Sarah approves the PR. It merges to master.

**Resolution:** The workflow advances to deployment staging. Antiphon deploys to dev environment (or prompts Mike to confirm it's deployed). AI runs regression checks, then asks Mike: [Confirm new feature works in dev] [Report issue] [Skip to stage deployment]. Mike confirms. Antiphon generates deployment paperwork — release notes, change summary, risk assessment — and routes it for approval. The team lead approves. Antiphon schedules the release, sends reminders as the date approaches. On release day, Mike manually deploys and marks it complete. The progress bar shows 100%. Release notes are updated in GitHub via MCP. The Jira ticket moves to Done.

**This journey reveals:** Workflow creation from Jira, AI spec generation, multi-reviewer gates, AI-suggested actions as buttons, free-text prompting at gates, PR creation and monitoring, deployment pipeline stages, release paperwork generation, progress tracking.

### Journey 2: Mike the Developer — Course Correction
**MVP coverage: full** — diff-based cascade updates, go-back, version tracking, audit trail.

**Opening Scene:** Mike is three stages into a workflow — spec and test plan are approved, and the AI just produced an architecture document. He reads it and realizes the AI chose a microservice approach when the team standard is monolith-first. The architecture is wrong.

**Rising Action:** Mike looks at the architecture artifact. The AI-suggested actions say [Approve] [Request changes] [Edit spec directly]. Mike types in the comment box: "Wrong approach. We follow monolith-first. Rewrite using the existing service layer pattern, not a new microservice." The agent regenerates the architecture. Architecture is now v2 — monolith-first, service layer pattern.

**Climax:** The system detects that upstream stages (spec, test plan) were written when the architecture didn't exist yet, but now the architecture has changed significantly. It asks Mike: "The test plan (v1) references microservice endpoints that no longer match the architecture (v2). Would you like to: [Update test plan based on architecture changes] [Keep test plan as-is] [Regenerate test plan from scratch]?" Mike clicks [Update test plan based on architecture changes]. The agent receives the diff between architecture v1 and v2, plus the current test plan, and intelligently patches the test plan — replacing microservice endpoint tests with service layer call tests while preserving the acceptance criteria and test scenarios that are still valid. Mike reviews the updated test plan (now v2), confirms it looks right, approves.

The workflow advances to the next stage. If an implementation plan had already been generated, the system would ask the same question: "Implementation plan was based on architecture v1. [Update based on changes] [Regenerate] [Keep as-is]." Each stage keeps its v1 snapshot, so the system always knows the delta.

**Resolution:** Mike approves the updated artifacts. The workflow continues forward. The progress bar shows versioned stages — test plan (v2, updated), architecture (v2, corrected). The audit trail shows the full history: original prompt, AI's first attempt, Mike's correction, the diff that drove the upstream update, and the final versions. Total time lost to course correction: 10 minutes. No artifacts were wiped — everything was intelligently patched based on what actually changed.

**This journey reveals:** Free-text prompting at gates, intelligent cascade updates (not wipe-and-regenerate), diff-based artifact patching, v1 snapshots preserved per stage, user choice between update/regenerate/keep at each affected stage, version tracking on artifacts, full audit trail of corrections and diffs.

### Journey 3: Sarah the Team Lead — Oversight and Adoption
**MVP coverage: partial** — dashboard overview, approval gates, progress tracking. Notifications, deployment approval, and release scheduling are post-MVP.

**Opening Scene:** Sarah opens Antiphon's dashboard on Tuesday morning. She sees the team's board: 4 active workflows, 2 pending her review, 1 in implementation, 1 awaiting deployment approval. Each shows a progress bar — she can see at a glance that Mike's notification feature is at "PR Review" (80% through) while James's refactor is stuck at "Architecture Review" waiting for her.

**Rising Action:** Sarah clicks James's workflow. The architecture artifact is open. She reads the AI's proposal — it's solid but misses the caching layer. She types feedback: "Add Redis caching for the hot path — we discussed this in sprint planning." She clicks [Request changes]. James gets a notification. The AI regenerates with caching included. Sarah reviews again, approves.

She switches to her "Pending Approvals" view. Mike's deployment paperwork is ready. She reviews the auto-generated release notes, change summary, and risk assessment. Everything looks clean. She clicks [Approve for release] and sets the release date for Thursday. Antiphon schedules reminders for the team.

**Climax:** At the weekly standup, Sarah shares the Antiphon dashboard on screen. The team can see every in-flight feature, where it is in the pipeline, what's blocked, and what's next. She says: "Every feature goes through spec review before code from now on. No more surprise PRs with architectural decisions nobody discussed."

**Resolution:** Over the next month, the team's spec-review-before-implementation rate climbs from 20% to 85%. Sarah can see cost data per workflow — the AI-assisted features are cheaper and faster than the old manual planning approach. She presents the results to her director and recommends expanding to the adjacent team.

**This journey reveals:** Dashboard overview of all workflows, pending approval queue, progress bars per workflow, deployment approval flow, release scheduling with reminders, team-wide visibility, adoption metrics.

### Journey 4: Admin Dave — Instance Setup and Configuration
**MVP coverage: partial** — single binary deployment, LLM config, project setup, workflow templates. OIDC, multi-user RBAC, MCP tool registration, and cost alerts are post-MVP.

**Opening Scene:** Dave is the platform engineer tasked with deploying Antiphon for the team. He pulls the single Docker image, creates a `docker-compose.yml` with Antiphon + PostgreSQL, and sets environment variables: OIDC provider URL, Anthropic API key, GitHub token. He runs `docker compose up`. Antiphon starts, serves the React UI, runs database migrations automatically.

**Rising Action:** Dave logs in as the first user (auto-promoted to Admin). He configures: LLM providers (Anthropic Claude as primary, OpenAI as fallback), default model routing (Opus for architecture, Sonnet for implementation, Haiku for summaries), and org-wide cost tracking. He creates the first project pointing at the trading platform repo, sets up RBAC — Mike and James as Contributors, Sarah as Project Owner. He enables the GitHub integration feature flag and configures the GitHub App credentials.

He also registers the team's Jira MCP tool — the Jira bridge service POSTs its tool schema to Antiphon's MCP registration endpoint. Dave verifies the tools appear in the admin panel: Jira (read tickets, create tickets, update status), GitHub (PRs, comments, CI status), plus the built-in tools (file read/write, bash, glob, grep).

**Climax:** Dave imports the team's BMAD workflow template as the default methodology. He also creates a custom "quick-fix" workflow template with just three stages: spec → implement → PR. The team now has two workflow types to choose from when starting new work.

**Resolution:** The instance is live. Dave sets up monitoring — Antiphon exposes OpenTelemetry traces so he can see LLM call latencies and error rates in Grafana. He sets a monthly cost alert at $500. The team is onboarded in an afternoon.

**This journey reveals:** Single binary deployment, auto-migrations, OIDC auth setup, LLM provider configuration, model routing, MCP tool registration, RBAC management, workflow template management, feature flags, OpenTelemetry observability, cost alerting.

### Journey 5: MCP Tool Provider — Jira Integration
**MVP coverage: none** — MCP tool registration is v1.1. This journey describes the extensibility vision.

**Opening Scene:** The team wants Antiphon agents to read Jira tickets and update their status. Dave deploys a lightweight Jira bridge service (a standalone app that wraps Jira's REST API as MCP tools).

**Rising Action:** On startup, the Jira bridge POSTs its tool manifest to Antiphon's MCP registration endpoint: `jira.getTicket(ticketId)` returns ticket title, description, acceptance criteria, comments; `jira.updateStatus(ticketId, status)` moves ticket through workflow; `jira.addComment(ticketId, comment)` adds a comment to the ticket. Antiphon validates the schema and makes these tools available to agents in any workflow that has Jira integration enabled.

**Climax:** When Mike starts a workflow with Jira ticket TRADE-4521, the analyst agent calls `jira.getTicket("TRADE-4521")` to pull context. When the workflow completes and deploys, the deployment agent calls `jira.updateStatus("TRADE-4521", "Done")` and `jira.addComment("TRADE-4521", "Deployed via Antiphon workflow #47")`.

**Resolution:** The Jira bridge runs as a sidecar container. Adding new Jira capabilities means updating the bridge — no changes to Antiphon core. The same pattern works for Confluence, Slack, or any internal API.

**This journey reveals:** MCP tool registration protocol, tool schema validation, runtime tool discovery by agents, external service integration pattern, sidecar deployment model.

### Journey Requirements Summary

| Journey | Key Capabilities Revealed |
|---------|--------------------------|
| **Mike — Happy Path** | Workflow creation from Jira, AI spec generation, multi-reviewer gates, AI-suggested action buttons, free-text prompting, PR creation/monitoring, deployment pipeline, release paperwork, progress tracking |
| **Mike — Course Correction** | Intelligent cascade updates (not wipe-and-regenerate), diff-based artifact patching, v1 snapshots per stage, user choice between update/regenerate/keep, version tracking, correction audit trail |
| **Sarah — Team Lead** | Dashboard overview, pending approval queue, progress bars, deployment approval, release scheduling/reminders, team visibility, adoption metrics |
| **Admin Dave** | Single binary deployment, OIDC setup, LLM provider config, model routing, MCP tool registration, RBAC, workflow templates, feature flags, observability, cost alerts |
| **MCP Tool Provider** | Tool registration protocol, schema validation, runtime discovery, sidecar integration pattern |

## Innovation & Novel Patterns

### Detected Innovation Areas

**1. AI-First Workflow Orchestration (New Category)**
Existing tools fall into two camps: project trackers (Jira, Linear, Azure DevOps) that track work but don't do it, and AI assistants (Claude Code, Cursor, Copilot) that do work but have no structure, governance, or collaboration. Antiphon creates a new category: an AI-first platform that both orchestrates and executes work through structured stages with human governance. It's what Jira would be if it was built AI-first and actually helped implement, test, deploy, and release — not just track status.

**2. AI as the Integration Layer**
Traditional tools need structured integrations with rigid schemas and APIs. Antiphon's agents consume structured input (Jira tickets via MCP), semi-structured input (Confluence docs, Slack threads), and unstructured input (emails, meeting notes, free-text descriptions) — and synthesize them into specs with human confirmation. The integration barrier drops to near zero because AI is the integration layer. This is fundamentally different from competitors who sprinkle AI onto rigid existing architecture. Antiphon is AI-native — AI IS the structure, and it adapts to existing infrastructure rather than demanding infrastructure adapt to it.

**3. AI-Suggested Actions at Decision Points**
No existing workflow or CI/CD tool offers adaptive, AI-generated decision points. Every competitor has static gates (approve/reject). Antiphon agents analyze what they just produced and propose structured next moves — rendered as interactive buttons alongside free-text prompting. The gate becomes an intelligent conversation, not a checkbox.

**4. Diff-Based Cascade Updates (Course Correction)**
Course correction in existing tools means starting over or manually propagating changes. Antiphon preserves v1 snapshots at each stage and offers three options when upstream changes occur: update based on diff, regenerate from scratch, or keep as-is. The AI intelligently patches downstream artifacts based on what actually changed. This preserves valid work while fixing what's broken.

**5. AI-Interpreted Workflow Definition (v1.1)**
MVP uses handcrafted structured YAML workflow definitions. In v1.1, workflows are defined as free-form markdown specs, generated collaboratively through prompting (user describes intent → AI generates structured YAML → user reviews → iterate). Keywords in the spec become system prompts for each stage's agent. This means anyone — including non-devs — can define workflows in natural language. Workflow templates themselves go through review gates, just like any other artifact.

**6. Hierarchical Evolving Knowledge System**
Agents draw from a layered knowledge stack: org-level standards → team-level conventions → repo/project constitution → workflow-specific context. Each level overrides or augments the one above. Knowledge evolves over time — when a team discovers a pattern that works, they add it to their constitution. When an agent keeps getting something wrong, the team adds a rule. External knowledge bases (Confluence, SharePoint, docs repos) are hookable via MCP tools. This is the long-term mitigation for agent quality — constitutions get smarter with use.

**7. MCP-Native Tool Extensibility**
Building on the emerging Model Context Protocol standard rather than a proprietary plugin API. External services register as MCP tool providers via HTTP. Contrast with Jira's notoriously difficult extension model — Antiphon is developer-extensible by design.

### Market Context & Competitive Landscape

| Category | Examples | What They Do | What They Don't Do | Can They Catch Up? |
|----------|----------|-------------|-------------------|-------------------|
| Project Trackers | Jira, Linear, Azure DevOps | Track work status, manage backlogs | Don't execute work, don't use AI for implementation, hard to extend | Adding AI features (Atlassian Intelligence) but can't retrofit AI-native architecture onto 20-year-old issue trackers. Sprinkle AI, not AI-first. |
| AI Coding Assistants | Claude Code, Cursor, Copilot, Aider | Generate code in single sessions | No structure, no collaboration, no governance, no lifecycle management | Could add workflow features but fundamentally single-user, single-session tools. No multi-stage governance model. |
| CI/CD Pipelines | GitHub Actions, Azure Pipelines, Jenkins | Automate build/test/deploy | Static pipelines, no AI agents, no spec-driven iteration | Adding AI features (Copilot in Actions) but YAML pipeline runners can't add human-in-the-loop AI agent execution. |
| AI Agent Frameworks | Microsoft Agent Framework, LangGraph, CrewAI | Build custom AI agents | Frameworks, not products — require custom development | Not competitors — Antiphon is built ON these. |

**Antiphon's position:** Occupies the empty quadrant — structured workflow execution with AI agents AND human governance across the full feature lifecycle. Not a tracker, not an assistant, not a pipeline, not a framework. A platform.

**Internal competition:** The biggest competitor is the dev who says "I'll just use Claude Code directly." The value proposition works when the overhead of structure is less than the cost of unstructured AI chaos. For a solo dev on a trivial feature, raw Claude Code wins. For anything involving review, collaboration, or deployment — Antiphon wins. That's the positioning boundary.

### Validation Approach

- **Dogfooding:** Build Antiphon using Antiphon (once MVP is functional). The team's own feature development proves the workflow model.
- **Single-team pilot:** 2-3 developers on Mike's team for 3 months. Measure spec-review-before-implementation rate, time-to-reviewable-artifacts, and developer satisfaction.
- **Quantitative metrics baked into audit system from day one:**
  - **Iteration count per stage** — derived from artifact version numbers. Tracks whether agents improve as constitutions evolve.
  - **Time-in-gate** — derived from stage completion timestamp → approval/rejection timestamp. Surfaces human bottlenecks.
  - **Course correction frequency** — "go back to stage" and "update based on diff" tracked as first-class audit events. High rate signals agent quality or spec ambiguity issues.
  - **Cost per completed workflow** — summed from cost ledger entries per workflow.
- **Adjacent team expansion:** If pilot succeeds, onboard a second team to validate workflow templates transfer.
- **Non-dev validation:** Onboard one non-dev user to validate generic workflow engine works beyond software development.

**Kill metric:** If after 6 weeks the team voluntarily reverts to direct Claude Code usage for >50% of features, the workflow overhead is too high. **Pivot option:** Narrow to CD/deployment pipeline only (release management, deployment paperwork, scheduling, reminders, release notes) as beachhead — this is a pain point with less behavioral change required, then expand backward into dev stages.

### Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Microsoft Agent Framework instability (RC → GA) | Fallback: raw Anthropic.SDK + custom state machine. IChatClient abstraction isolates LLM layer. |
| MCP protocol evolving/breaking | Webhook-style registration is simple HTTP — minimal surface area. Upgrade path isolated to tool bridge layer. |
| AI-suggested actions quality | Structured text format keeps it simple. Bad suggestions harmless — user can always free-text prompt. |
| Diff-based cascade complexity | V1 falls back to "regenerate from scratch" if patching produces poor results. User always has choice. |
| Agent prompt quality ceiling | Hierarchical knowledge system (org → team → repo → workflow) constrains and improves agent behavior. Constitutions evolve with team learning. |
| Adoption friction (full pipeline too much) | Pivot to deployment pipeline beachhead if full pipeline adoption faces resistance. Less behavioral change required. |
| "New category" = no existing market | Dogfooding + internal adoption validates demand. Kill metric triggers pivot before over-investment. |

## Web App / B2B Platform Requirements

### Project-Type Overview

Antiphon is a React SPA with client-side routing served by an ASP.NET Core (.NET 10) backend. Single-tenant deployment, one instance per org. No SSR, no SEO requirements (internal tool). Modern evergreen browsers only (Chrome, Edge, Firefox). No accessibility compliance target for v1.

### Permission Model

V1 minimal auth:
- Single default admin user — no login flow, no OIDC, no user management
- All requests attributed to the default admin user
- Client IP addresses logged on every request for audit trail
- Clean auth abstraction from day one: `ICurrentUser` interface. V1 resolves to hardcoded admin + request IP. OIDC swaps implementation later with zero refactoring to audit, workflow, or API code.
- Full auth (OIDC, multi-user, RBAC) deferred to Growth

### Real-Time Architecture

SignalR powers all real-time communication via a single connection per browser tab with a client-side event bus. Components subscribe to relevant events.

- **Agent output streaming** — `AgentTextDelta` events pushed immediately for responsive token-by-token rendering
- **Agent activity status line** — `AgentActivityUpdate` events debounced at 500ms server-side. Single status line showing: current action (tool name + input), cumulative tokens in/out, tool call count, elapsed time. No silent periods — user always sees what the agent is doing.
- **Dashboard updates** — Workflow status changes, stage progressions, and approval events push to all connected clients in real-time. No polling, no manual refresh.
- **Page-level updates** — Any page showing a workflow, artifact, or approval gate updates live as backend state changes.

Clients join SignalR groups based on what they're viewing (e.g., `workflow-{id}`, `dashboard`). No broadcast-everything-to-everyone.

### Technical Architecture Considerations

- **SPA:** React with client-side routing (React Router). No SSR. Vite for dev/build tooling.
- **API:** ASP.NET Core Minimal APIs. REST for CRUD operations. SignalR hub for all real-time events.
- **Single binary:** React build output in `wwwroot/`, served by ASP.NET with SPA fallback routing. `dotnet publish` produces one deployable artifact.
- **Database:** PostgreSQL 16 with EF Core. JSONB for flexible config (workflow specs, model routing, feature flags).
- **Auth:** V1: no auth — `ICurrentUser` interface resolves to default admin + client IP. Future: OIDC via ASP.NET Identity, swap implementation only.
- **Feature flags:** Environment variables or config file. Controls GitHub integration, Jira integration, notification channels.

### Integration Requirements

**V1 Integrations:**

| Integration | Type | Purpose | MVP Priority |
|-------------|------|---------|-------------|
| GitHub | Built-in (feature-flagged) | PR creation, CI status monitoring, PR comment monitoring, code merge | Must-have |
| Jira | MCP tool (optional) | Read tickets (title, description, acceptance criteria, comments). Optionally update status on workflow completion. | Nice-to-have (workflows are input-agnostic — user can paste details or describe requirements instead) |
| LLM Providers | Built-in | Anthropic Claude, OpenAI, Ollama via IChatClient | Must-have |

**Future Integrations (via MCP):**
- Confluence (read/write documentation)
- Slack/Teams (notifications, thread reading)
- Internal APIs (custom MCP tool bridges)

### Implementation Considerations

- **No subscription/billing model** — Internal tool, no tiers. Single-tenant self-hosted.
- **No mobile support** — Desktop browser only.
- **No offline support** — Always-connected internal tool.
- **No SEO** — Internal tool, no public pages.
- **No i18n** — English only for v1.
- **No accessibility** — Best-effort only, no WCAG target.
- **Performance targets:** Dashboard loads in <2s. Agent streaming latency <500ms from LLM response to UI render. Agent activity status line debounced at 500ms. SignalR push for state changes <1s.

## Project Scoping & Phased Development

### MVP Strategy & Philosophy

**MVP Approach:** Platform MVP — the generic workflow engine is the product. BMAD ships as the first bundled methodology template (handcrafted YAML) to prove the engine works. Single developer (Mike) building with AI assistance. Target: working platform in ~3 months, willing to course-correct if adoption is low after that.

**Resource Requirements:** Solo developer with AI-assisted development (Claude Code / Antiphon self-dogfooding once viable). No external dependencies beyond LLM API keys.

### MVP Feature Set (Phase 1)

*This is a summary. See [Functional Requirements](#functional-requirements) for the authoritative capability contract (71 FRs).*

**Core User Journeys Supported:**
- Journey 1 (Developer happy path) — partial: spec generation, AI execution, gates, git integration. No deployment pipeline stages yet.
- Journey 2 (Course correction) — full: diff-based cascade via git branching + tagging strategy.
- Journey 4 (Admin setup) — simplified: single binary, default admin user, LLM provider config.

**Must-Have Capabilities:**

| Capability | Notes |
|-----------|-------|
| Workflow engine | Executes structured YAML workflow definitions. Handcrafted definitions for MVP (BMAD full, BMAD quick, etc.). Deterministic, testable. |
| AI agent execution | One agent per stage. BMAD bundled as default methodology. Microsoft Agent Framework. |
| Two-tier git branching | `antiphon/workflow-{id}/master` (durable) + `antiphon/workflow-{id}/stage-{name}` (ephemeral). Stage branches merge to workflow master on gate approval. Final PR: workflow master → main. Namespaced for clean git UI. |
| Git tagging for audit | `antiphon/workflow-{id}/{stage}-v{version}` tags on stage branches before merge. Audit DB indexes tags. Tags survive branch deletion. |
| Artifacts directory | `_antiphon/artifacts/workflow-{id}/` in repo. Path-filtered diffs (`git diff tag1..tag2 -- _antiphon/artifacts/`) for clean cascade updates. |
| Diff-based course correction | Compare stage tags via git diff. Offer: update based on diff, regenerate, or keep as-is. Core differentiator. |
| Project constitution | Load project-context.md (single file or folder) from repo into agent system prompts. Simple file-based. |
| Approval gates | Free-text prompt box. User can approve, reject with feedback, or go back to previous stage. |
| React dashboard | Workflow list, stage progression, artifact viewer (rendered markdown), live agent output stream with activity status line. |
| Real-time updates | SignalR: agent text streaming (immediate), activity status (debounced 500ms), dashboard state changes. |
| Two-tier audit & cost storage | Cost ledger (permanent): tokens, USD per call. Full audit content (archivable): prompts, responses, tool calls. Client IP logged. Stage execution records reference git tags. |
| Multi-model routing | Claude, GPT, Ollama via IChatClient. Per-stage model configuration. |
| Built-in agent tools | File read/write/edit, bash, glob, grep, git (clone, checkout, branch, commit, diff, push, tag). |
| Single binary deployment | ASP.NET serves React SPA. Docker Compose. Feature flags via config. |

**Deferred from MVP (explicitly):**
- AI-suggested actions as buttons (v1.1)
- MCP tool registration protocol (v1.1)
- GitHub PR integration (v1.1)
- AI-assisted workflow authoring: free-form markdown → structured YAML (v1.1)
- Hierarchical knowledge system with pluggable sources (v1.1)
- Jira integration (Growth)
- OIDC auth / multi-user / RBAC (Growth)
- Notifications (Growth)
- Visual workflow builder (Growth)

### Post-MVP Features

**Phase 1.1 (Pre-team deployment):**
- AI-suggested actions at gates (structured text → buttons)
- MCP over HTTP tool registration (webhook-style)
- GitHub integration (feature-flagged): PR creation from stage branches, CI monitoring, merge flow
- AI-assisted workflow authoring: user describes intent in free-form → AI generates structured YAML definition → user reviews and iterates
- Hierarchical knowledge system: pluggable knowledge sources (git repos of markdown files, vector DB/knowledge base via REST or MCP). Org → team → repo → workflow layers. Configurable per project.
- Jira MCP tool (read tickets)

**Phase 2 (Growth — post team adoption):**
- OIDC auth, multi-user, RBAC (Project Owner, Contributor, Viewer)
- Notification integrations (Teams, email, Slack — feature-flagged)
- Cost dashboard with reporting and budget enforcement
- Non-dev workflow templates
- Additional bundled methodology plugins
- Audit content archival to cold storage
- Kubernetes deployment

**Phase 3 (Vision):**
- CI/CD pipeline orchestration with AI-assisted testing/deployment
- Visual workflow builder UI
- Marketplace for workflow templates and MCP tool plugins
- Multi-repo workflow support
- AI-powered workflow optimization
- IDE integration (VS Code / JetBrains)

### Risk Mitigation Strategy

**Technical Risks:**

| Risk | Mitigation |
|------|-----------|
| Microsoft Agent Framework RC → GA breaking changes | IChatClient abstraction isolates LLM layer. Fallback: raw Anthropic.SDK + custom state machine. |
| Git branching complexity | Two-tier model keeps it manageable. Tags are the audit backbone. If branching gets messy, simplify to single branch + tagged commits. |
| Solo developer bottleneck | AI-assisted development. Lean MVP scope. Aggressive deferral of non-essential features. |
| Diff-based cascade quality | Git diff is deterministic. AI interpretation of diff is the variable; fallback to "regenerate from scratch" always available. |

**Market Risks:**

| Risk | Mitigation |
|------|-----------|
| Team doesn't adopt (prefers raw Claude Code) | Kill metric: >50% revert after 6 weeks → pivot to deployment pipeline beachhead. |
| Workflow overhead exceeds value for small features | Support "quick flow" templates (2-3 stages) alongside full methodology templates. |

**Resource Risks:**

| Risk | Mitigation |
|------|-----------|
| Solo build takes too long | Scope aggressively trimmed. V1.1 handles most features before team deployment. 3-month target with monthly reassessment. |
| Burnout / competing priorities | Antiphon is the dogfooding tool — building it validates the product. Each milestone is independently useful. |

## Functional Requirements

### Workflow Management

- FR1: User can create a new workflow by selecting a YAML workflow template and pointing it at a git repository
- FR2: User can provide initial context for a workflow (free-text description, pasted ticket details, or other input)
- FR3: User can view all active workflows with their current stage and progress status
- FR4: User can view the full stage progression of a workflow as a visual progress indicator
- FR5: User can pause, resume, or abandon a workflow
- FR6: System executes workflow stages sequentially according to the YAML definition

### Workflow Definition

- FR7: System can load and execute structured YAML workflow definitions
- FR8: Admin can add new YAML workflow templates to the system
- FR9: System ships with bundled BMAD workflow templates (full and quick variants)
- FR10: Workflow definitions specify stages, stage ordering, executor type (AI agent), model routing, and gate configuration per stage

### AI Agent Execution

- FR11: System can execute an AI agent at each workflow stage using the configured LLM model
- FR12: AI agent receives upstream artifacts, project constitution, and stage-specific instructions as context
- FR13: AI agent can read, write, and edit files in the project workspace using built-in tools
- FR14: AI agent can execute shell commands in the project workspace
- FR15: AI agent can search files by pattern (glob) and search file contents (grep)
- FR16: AI agent can perform git operations (clone, checkout, branch, commit, diff, push, tag)
- FR17: User can view AI agent output in real-time as it streams
- FR18: User can view a live activity status line showing current tool call, cumulative token count, tool call count, and elapsed time (debounced at 500ms)
- FR19: System can route different stages to different LLM models (e.g., Opus for architecture, Sonnet for implementation)
- FR20: Admin can configure available LLM providers and API keys (Anthropic, OpenAI, Ollama)

### Approval Gates

- FR21: System pauses workflow execution at configured gate points and waits for user action
- FR22: User can approve a gate to advance the workflow to the next stage
- FR23: User can reject a gate with free-text feedback that is injected into the next agent invocation
- FR24: User can go back to a previous stage from the current gate
- FR25: When user goes back to a previous stage, system identifies downstream stages affected by the change
- FR26: For each affected downstream stage, user can choose to update based on diff, regenerate from scratch, or keep as-is
- FR27: User can provide free-text prompts to the agent at any gate to request modifications to the current artifact

### Git-Backed Artifact Management

- FR28: System creates a namespaced workflow branch (`antiphon/workflow-{id}/master`) when a workflow starts
- FR29: System creates ephemeral stage branches (`antiphon/workflow-{id}/stage-{name}`) for each stage execution
- FR30: Agent commits changes to the stage branch at each gate point, with `[antiphon]` trailer to identify system commits
- FR31: System tags stage commits with versioned tags (`antiphon/workflow-{id}/{stage}-v{version}`) before merging
- FR32: System merges stage branches into the workflow master branch on gate approval
- FR33: System stores artifacts in a dedicated directory (`_antiphon/artifacts/workflow-{id}/`) in the repo
- FR34: System can compute path-filtered git diffs between stage tags for cascade update context
- FR35: User can view artifact content (rendered markdown) in the dashboard
- FR36: User can view diff between artifact versions

### Course Correction

- FR37: User can trigger re-execution of the current stage with modified feedback
- FR38: When a stage artifact is updated, system detects upstream/downstream stages that may be affected
- FR39: System presents affected stages with options: update based on diff, regenerate, or keep as-is
- FR40: System uses git diff between version tags to provide context for AI-driven artifact updates
- FR41: System preserves all artifact versions (no destructive overwrites)
- FR42: Audit trail captures the full correction history: original, feedback, diff, and updated versions

### Project Configuration

- FR43: Admin can create a project by pointing at a git repository URL
- FR44: Admin can configure model routing per stage (which LLM model for which stage)
- FR45: System loads project constitution (project-context.md file or folder) from the repo and injects it into agent system prompts
- FR46: Admin can configure feature flags to enable/disable optional integrations (GitHub, notifications)

### Audit & Cost Tracking

- FR47: System records token count (in/out) and approximate USD cost for every LLM call
- FR48: System records full audit content (prompts, responses, tool call inputs/outputs) for every agent execution
- FR49: Cost ledger records are kept indefinitely; full audit content is stored separately and archivable
- FR50: System logs client IP address on every request
- FR51: Stage execution audit records reference git tags for traceability
- FR52: User can view audit history for any workflow or stage execution
- FR53: "Go back to stage" and "update based on diff" events are recorded as first-class audit events

### Dashboard & Real-Time UI

- FR54: User can view a dashboard listing all workflows with status, current stage, and progress indicators
- FR55: User can view a workflow detail page showing stage progression, current artifact, and gate controls
- FR56: Dashboard and all pages update in real-time via SignalR when backend state changes (no manual refresh)
- FR57: User can view rendered markdown artifacts with version history
- FR58: User can view the activity status line during agent execution (current action, tokens, tool calls, elapsed time)

### GitHub Integration

- FR59: System can create a GitHub PR from a stage branch to the workflow master branch
- FR60: System can create a GitHub PR from the workflow master branch to main
- FR61: System can monitor a GitHub PR for new comments, review feedback, and build status
- FR62: When a GitHub PR receives comments or review feedback, system can feed that feedback to the AI agent for response or artifact update
- FR63: System can push commits to stage branches (agent-generated fixes in response to PR feedback)
- FR64: GitHub integration is feature-flagged and can be disabled per environment

### External Change Detection

- FR65: System polls workflow branches for external commits via `git fetch` at configurable interval (default 30s)
- FR66: System distinguishes Antiphon commits (marked with `[antiphon]` trailer) from external commits to prevent cascade loops
- FR67: When external commits are detected, system automatically pulls and updates local state
- FR68: If external changes touch files in `_antiphon/artifacts/`, system triggers path-based cascade detection to identify affected downstream stages
- FR69: For affected downstream stages, system automatically triggers the cascade update flow (update based on diff)
- FR70: Code-only external changes (outside `_antiphon/artifacts/`) update local state without triggering cascade
- FR71: Audit trail records all external change events with commit details, author, diff, and any triggered cascades

### Deferred Functional Requirements (v1.1)

- When a PR build fails, system feeds failure details to the AI agent for analysis and suggested fixes
- AI-suggested actions at gates (structured text rendered as buttons)
- MCP over HTTP tool registration protocol
- AI-assisted workflow authoring (free-form → structured YAML)
- Hierarchical knowledge system with pluggable sources

## Non-Functional Requirements

### Performance

- NFR1: Dashboard page load completes within 2 seconds
- NFR2: Agent text streaming latency is under 500ms from LLM response to UI render
- NFR3: Agent activity status line updates debounced at 500ms server-side
- NFR4: SignalR state change pushes delivered to connected clients within 1 second
- NFR5: Git operations (branch, commit, tag, diff) complete within 5 seconds for repositories under 1GB
- NFR6: Workflow YAML definitions parse and validate within 1 second

### Security

- NFR7: LLM API keys are stored in server-side configuration only (environment variables, secrets files). Never exposed to frontend, agent context, or audit logs.
- NFR8: Agent file tools (read, write, edit, glob, grep) are scoped to the project's git worktree directory. Path traversal attempts are blocked.
- NFR9: Agent bash tool runs with working directory set to the scoped workspace. Not truly sandboxed for MVP — agent can execute arbitrary commands within the host context. True sandboxing (containerized execution) deferred to v1.1 hardening.
- NFR10: Git credentials (tokens, SSH keys) are server-side only. Agents access repos through scoped git tools, never with direct credentials.
- NFR11: Client IP is logged on every API request for audit trail

### Reliability

- NFR12: Agent Framework checkpoints persist after every tool call completion (continuous recovery mechanism). Git commits at gate points are the durable artifact snapshots. On crash recovery, agent resumes from last checkpoint, not from beginning of stage.
- NFR13: If an agent execution fails (LLM timeout, tool error), the stage is marked as failed with full error details. User can retry from the last checkpoint or from the last gate.
- NFR14: Git tags and branch state are the source of truth. Database state can be reconstructed from git history if needed.
- NFR15: PostgreSQL database uses standard transaction isolation. No concurrent writes to the same workflow stage.

### Observability

- NFR16: Every LLM call is recorded with model, tokens in/out, cost estimate, and duration
- NFR17: Every tool invocation is recorded with tool name, inputs, outputs, and duration
- NFR18: All audit data is queryable by workflow, stage, time range, and cost
- NFR19: Structured logging with correlation IDs: every log line carries workflowId, stageId, and executionId for full traceability
- NFR20: OpenTelemetry traces emitted for all LLM calls via Microsoft.Extensions.AI middleware
- NFR21: Health check endpoint (`/health`) reports status of: database connectivity, LLM provider reachability, git repository accessibility, and workspace disk space

### Data Retention

- NFR22: Cost ledger records (tokens, USD, model, stage) are retained indefinitely
- NFR23: Full audit content (prompts, responses, tool call details) is retained for a configurable period (default 90 days)
- NFR24: After retention period, full audit content is eligible for deletion. Manual cleanup via admin API in MVP. Automatic archival to cold storage in Growth.
