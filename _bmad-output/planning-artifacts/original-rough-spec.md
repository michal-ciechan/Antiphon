# Flow.io - AI Agent Orchestration Platform

## Specification Document

**Version:** 0.2.0-draft
**Date:** 2026-03-14
**Status:** Draft - Architecture Decision: Microsoft Agent Framework + C#

---

## 1. Problem Statement

Current AI coding agents (Claude Code, Cursor, Aider, etc.) operate in single-user, single-session modes with ad-hoc planning. When the AI goes wrong, the only recourse is to re-prompt or start over. There is no structured way to:

- Define requirements and test plans **before** implementation begins
- Gate progression between phases with human review/approval
- Collaborate across a team on spec refinement and code review
- Course-correct by modifying specs when the AI produces incorrect output
- Track cost, quality, and progress across multiple projects and users

**Flow.io** is a multi-user web dashboard that orchestrates AI coding agents through structured BMAD-methodology phases, with approval gates, spec-driven iteration, and multi-model routing.

---

## 2. Goals

1. **Structured workflow** - Enforce plan/review/test-plan/design/implement phases with approval gates
2. **Spec as source of truth** - Specs are persistent, versioned, editable artifacts that drive all AI work
3. **Course correction** - When the AI goes wrong, users edit specs/plans and regenerate downstream phases
4. **Multi-user collaboration** - Multiple users can review, approve, comment on, and edit artifacts
5. **Model-agnostic** - Route different phases to different models based on cost/capability
6. **Cost visibility** - Track spend per user, project, phase, and model
7. **Self-hosted** - Deployable on-premises with no external dependencies beyond LLM API keys

---

## 3. Non-Goals (v1)

- IDE integration (VS Code/JetBrains plugins) - future
- Git hosting (uses existing repos)
- CI/CD pipeline execution (triggers only)
- Custom agent development UI (use BMAD tooling directly)
- Real-time collaborative editing (Google Docs-style) - future
- Mobile support

---

## 4. User Roles

| Role              | Permissions                                                  |
| ----------------- | ------------------------------------------------------------ |
| **Admin**         | Manage users, configure models/providers, view all projects, set org-wide cost limits |
| **Project Owner** | Create projects, configure agents/phases, manage project members, approve any phase |
| **Contributor**   | Create/edit specs, trigger phases, approve phases they're assigned to review |
| **Viewer**        | Read-only access to projects, phases, artifacts, and cost data |

---

## 5. Core Concepts

### 5.1 Project

A project maps to a git repository. It contains:
- A git repo URL and branch configuration
- BMAD configuration (constitution, agent personas, workflow settings)
- One or more **Workflows** (feature requests, bug fixes, etc.)
- Member list with roles
- Model routing configuration
- Cost budget and limits

### 5.2 Workflow

A workflow is a single unit of work (feature, bug fix, refactor) that progresses through phases. Equivalent to a BMAD "epic" or "story" depending on scale.

### 5.3 Phase

A discrete stage in the workflow lifecycle. Each phase:
- Has an assigned BMAD agent persona (PM, Architect, Developer, etc.)
- Produces one or more **Artifacts**
- Requires approval from designated reviewers before advancing
- Has a configured model and cost budget
- Can be re-run with modified inputs (spec iteration)

### 5.4 Artifact

A versioned document produced by a phase. Types:
- `product-brief` - Initial analysis/research output
- `prd` - Product Requirements Document
- `test-plan` - Test strategy and acceptance criteria
- `architecture` - Technical design document
- `readiness-report` - Implementation readiness assessment (PASS/CONCERNS/FAIL)
- `story` - Hyper-detailed implementation story
- `code-diff` - Proposed code changes
- `review` - Code review feedback

Artifacts are stored as markdown/YAML files in the project repo under `_flow-io/artifacts/`.

### 5.5 Approval Gate

A checkpoint between phases where designated reviewers must:
- **Approve** - Advance to the next phase
- **Request Changes** - Return to current phase with feedback (the feedback is injected into the next agent invocation)
- **Edit Spec** - Modify the artifact directly, then optionally re-run the phase

### 5.6 Constitution

A persistent project-level document (`project-context.md`) that defines immutable rules all agents must follow: tech stack, coding standards, forbidden patterns, testing requirements. Loaded into every agent invocation as system prompt context.

---

## 6. Workflow Phases

### 6.1 Default Phase Sequence (Full - BMAD Scale Level 3+)

```
1. Analysis          [Analyst agent]      → product-brief
       ↓ approval gate
2. Requirements      [PM agent]           → prd
       ↓ approval gate
3. Test Strategy     [TEA/QA agent]       → test-plan
       ↓ approval gate
4. Architecture      [Architect agent]    → architecture
       ↓ approval gate
5. Readiness Check   [Architect agent]    → readiness-report (PASS/CONCERNS/FAIL)
       ↓ auto-gate (PASS) or approval gate (CONCERNS)
6. Story Creation    [Scrum Master agent] → story-*.md (one per story)
       ↓ approval gate (per story)
7. Implementation    [Developer agent]    → code changes + tests
       ↓ approval gate (per story)
8. Code Review       [Reviewer agent]     → review feedback
       ↓ approval gate
9. Done
```

### 6.2 Quick Flow (BMAD Scale Level 0-1)

```
1. Tech Spec    [Quick Flow Solo Dev]  → tech-spec (includes adversarial review)
       ↓ approval gate
2. Implement    [Developer agent]      → code changes + tests
       ↓ approval gate
3. Done
```

### 6.3 Phase Configuration

Projects can customize:
- Which phases are included/skipped
- Which agent persona handles each phase
- Which model is used per phase (e.g., Opus for architecture, Sonnet for implementation)
- Who must approve at each gate (specific users, roles, or "any contributor")
- Cost budget per phase
- Auto-advance rules (e.g., readiness check auto-advances on PASS)

---

## 7. Architecture

### 7.1 Architecture Decision: C# with Microsoft Agent Framework

**Decision:** Build the entire backend in C# using Microsoft Agent Framework (RC, GA expected Q1-Q2 2026) as the agent orchestration layer. This replaces the previous OpenCode/Node.js approach.

**Rationale:**
- C# is the primary development ecosystem (existing bots, tools, team expertise)
- Microsoft Agent Framework has first-class Anthropic Claude support (`Microsoft.Agents.AI.Anthropic`)
- Built-in graph-based workflows with checkpointing map directly to BMAD phases
- Native human-in-the-loop with checkpoint + resume is exactly what approval gates need
- Multi-model routing via `Microsoft.Extensions.AI` `IChatClient` abstraction
- Full control over the agent loop, tools, and prompts - no external process dependencies
- MIT license, Microsoft-backed, active development

### 7.2 Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Web Frontend (React)                      │
│  - Project dashboard                                        │
│  - Workflow phase view with approval gates                  │
│  - Artifact viewer/editor (markdown + diff)                 │
│  - Real-time streaming of agent output (SignalR)            │
│  - Cost/usage dashboard                                     │
│  - User management                                          │
└──────────────────────────┬──────────────────────────────────┘
                           │ REST + SignalR (WebSocket)
┌──────────────────────────▼──────────────────────────────────┐
│              ASP.NET Core Backend (C#)                        │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  API Layer (ASP.NET Core Minimal APIs)                 │ │
│  │  - REST endpoints for projects, workflows, artifacts   │ │
│  │  - SignalR hub for streaming agent output               │ │
│  │  - Auth (OIDC/LDAP via ASP.NET Identity or Keycloak)   │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Workflow Engine                                       │ │
│  │  - Phase state machine (Agent Framework Workflows)     │ │
│  │  - Approval gate engine (checkpoint + resume)          │ │
│  │  - Cost aggregation and budget enforcement             │ │
│  │  - Notification dispatch (Teams, email, webhook)       │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  Agent Execution Layer (Microsoft Agent Framework)     │ │
│  │  - AIAgent per BMAD persona (PM, Architect, Dev, etc.) │ │
│  │  - Per-phase model routing (Opus/Sonnet/Haiku/GPT)     │ │
│  │  - Tool plugins (FileRead, FileWrite, FileEdit, Bash,  │ │
│  │    GlobSearch, GrepSearch, GitDiff)                     │ │
│  │  - BMAD persona prompt injection per phase             │ │
│  │  - Streaming via IAsyncEnumerable → SignalR            │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │  LLM Providers (via IChatClient / Agent Framework)     │ │
│  │  - Anthropic Claude (Opus, Sonnet, Haiku)              │ │
│  │  - OpenAI GPT-4o (optional fallback)                   │ │
│  │  - Ollama (local models, optional)                     │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────────┬──────────────────────────────────┘
                           │
              ┌────────────▼────────────┐
              │      PostgreSQL 16      │
              │  - Users/roles          │
              │  - Projects             │
              │  - Workflows/phases     │
              │  - Artifacts (content)  │
              │  - Approvals            │
              │  - Cost records         │
              │  - Checkpoints          │
              │  - Audit log            │
              └─────────────────────────┘
```

### 7.3 Agent Framework Integration

#### BMAD Agents as AIAgent Instances

Each BMAD persona becomes an `AIAgent` configured with the appropriate system prompt, model, and tools:

```csharp
// Agent creation per BMAD phase
public class BmadAgentFactory
{
    private readonly AnthropicClient _anthropic;
    private readonly BmadPersonaStore _personas;

    public AIAgent CreateAgent(PhaseType phase, ProjectConfig config)
    {
        var persona = _personas.Load(phase);     // loads BMAD .agent.md
        var model = config.GetModelForPhase(phase);
        var tools = GetToolsForPhase(phase);

        return _anthropic.AsAIAgent(
            model: model,                         // e.g., "claude-sonnet-4-5"
            name: persona.Name,                   // e.g., "BMADArchitect"
            instructions: BuildSystemPrompt(persona, config),
            tools: tools
        );
    }

    private string BuildSystemPrompt(BmadPersona persona, ProjectConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine(persona.SystemPrompt);       // BMAD agent persona
        sb.AppendLine();
        sb.AppendLine("## Project Constitution");
        sb.AppendLine(config.Constitution);         // project-context.md
        return sb.ToString();
    }

    private object[] GetToolsForPhase(PhaseType phase) => phase switch
    {
        // Planning phases: read-only
        PhaseType.Analysis or
        PhaseType.Requirements or
        PhaseType.TestStrategy => [ReadFile, GlobSearch, GrepSearch],

        // Architecture: read + write artifacts
        PhaseType.Architecture or
        PhaseType.StoryCreation => [ReadFile, WriteFile, GlobSearch, GrepSearch],

        // Implementation: full access
        PhaseType.Implementation => [ReadFile, WriteFile, EditFile,
                                     RunBash, GlobSearch, GrepSearch, GitDiff],

        // Review: read-only
        PhaseType.CodeReview => [ReadFile, GlobSearch, GrepSearch, GitDiff],

        _ => [ReadFile, GlobSearch]
    };
}
```

#### Tool Implementations

Custom C# tools that give the AI agent file system and shell access (scoped to the project workspace):

```csharp
public class AgentTools
{
    private readonly string _workspacePath;

    public AgentTools(string workspacePath)
    {
        _workspacePath = workspacePath;
    }

    [Description("Read the contents of a file")]
    public string ReadFile(
        [Description("Path relative to project root")] string path)
    {
        var fullPath = ResolvePath(path);
        return File.ReadAllText(fullPath);
    }

    [Description("Write content to a file, creating it if it doesn't exist")]
    public void WriteFile(
        [Description("Path relative to project root")] string path,
        [Description("Content to write")] string content)
    {
        var fullPath = ResolvePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Description("Replace a specific string in a file (exact match)")]
    public string EditFile(
        [Description("Path relative to project root")] string path,
        [Description("The exact text to find")] string oldString,
        [Description("The replacement text")] string newString)
    {
        var fullPath = ResolvePath(path);
        var content = File.ReadAllText(fullPath);
        if (!content.Contains(oldString))
            return $"Error: '{oldString}' not found in {path}";
        File.WriteAllText(fullPath, content.Replace(oldString, newString));
        return "Edit applied successfully";
    }

    [Description("Search for files matching a glob pattern")]
    public string[] GlobSearch(
        [Description("Glob pattern (e.g. **/*.cs, src/**/*.ts)")] string pattern)
    {
        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        return matcher.GetResultsInFullPath(_workspacePath).ToArray();
    }

    [Description("Search file contents for a regex pattern")]
    public SearchResult[] GrepSearch(
        [Description("Regex pattern to search for")] string pattern,
        [Description("Optional glob to filter files")] string? fileGlob = null)
    {
        // Implementation using regex over file contents
        // Scoped to _workspacePath
    }

    [Description("Run a shell command in the project directory")]
    public async Task<BashResult> RunBash(
        [Description("The command to execute")] string command,
        [Description("Timeout in seconds")] int timeoutSeconds = 120)
    {
        var psi = new ProcessStartInfo("bash", $"-c \"{command}\"")
        {
            WorkingDirectory = _workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(timeoutSeconds)
        );
        await proc.WaitForExitAsync(cts.Token);
        return new BashResult(
            await proc.StandardOutput.ReadToEndAsync(),
            await proc.StandardError.ReadToEndAsync(),
            proc.ExitCode
        );
    }

    [Description("Show git diff for uncommitted changes")]
    public async Task<string> GitDiff()
    {
        var result = await RunBash("git diff", 30);
        return result.Stdout;
    }

    private string ResolvePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_workspacePath, relativePath));
        if (!full.StartsWith(_workspacePath))
            throw new SecurityException($"Path escapes workspace: {relativePath}");
        return full;
    }
}
```

#### Workflow Graph with Checkpointing

The BMAD phase pipeline as an Agent Framework workflow graph with approval gates via checkpoints:

```csharp
public class BmadWorkflowBuilder
{
    public Workflow BuildFullWorkflow(WorkflowConfig config)
    {
        var builder = new WorkflowBuilder("BMADFullWorkflow");

        // Phase executors
        var analysis    = builder.AddExecutor("Analysis",    RunPhase(PhaseType.Analysis));
        var requirements= builder.AddExecutor("Requirements",RunPhase(PhaseType.Requirements));
        var testStrategy= builder.AddExecutor("TestStrategy",RunPhase(PhaseType.TestStrategy));
        var architecture= builder.AddExecutor("Architecture",RunPhase(PhaseType.Architecture));
        var readiness   = builder.AddExecutor("Readiness",   RunPhase(PhaseType.ReadinessCheck));
        var stories     = builder.AddExecutor("Stories",     RunPhase(PhaseType.StoryCreation));
        var implement   = builder.AddExecutor("Implement",   RunPhase(PhaseType.Implementation));
        var review      = builder.AddExecutor("CodeReview",  RunPhase(PhaseType.CodeReview));

        // Approval checkpoints between each phase
        // Workflow pauses here until human resumes via dashboard API
        builder.AddEdge(analysis,     checkpoint: true, next: requirements);
        builder.AddEdge(requirements, checkpoint: true, next: testStrategy);
        builder.AddEdge(testStrategy, checkpoint: true, next: architecture);
        builder.AddEdge(architecture, checkpoint: true, next: readiness);

        // Readiness: auto-advance on PASS, checkpoint on CONCERNS
        builder.AddConditionalEdge(readiness,
            condition: result => result.Status == "PASS",
            ifTrue:  stories,          // auto-advance
            ifFalse: checkpoint(stories) // human reviews CONCERNS
        );

        builder.AddEdge(stories,  checkpoint: true, next: implement);
        builder.AddEdge(implement,checkpoint: true, next: review);
        builder.AddEdge(review,   checkpoint: true, next: builder.End);

        return builder.Build();
    }
}
```

#### Phase Execution with Streaming

```csharp
public class PhaseRunner
{
    private readonly BmadAgentFactory _agentFactory;
    private readonly IHubContext<AgentHub> _signalR;
    private readonly ArtifactStore _artifacts;

    public async Task<PhaseResult> RunPhaseAsync(
        Workflow workflow,
        Phase phase,
        CancellationToken ct)
    {
        // 1. Build context from upstream artifacts
        var context = await BuildPhaseContext(workflow, phase);

        // 2. Create agent with BMAD persona
        var agent = _agentFactory.CreateAgent(
            phase.PhaseType,
            workflow.Project.Config
        );

        // 3. Run agent with streaming to SignalR
        var response = new StringBuilder();
        await foreach (var chunk in agent.RunStreamingAsync(context, ct))
        {
            response.Append(chunk);

            // Stream to connected dashboard clients
            await _signalR.Clients
                .Group($"workflow-{workflow.Id}")
                .SendAsync("AgentOutput", new
                {
                    phaseId = phase.Id,
                    delta = chunk,
                    timestamp = DateTimeOffset.UtcNow
                }, ct);
        }

        // 4. Extract and save artifact
        var artifact = await _artifacts.SaveAsync(new Artifact
        {
            PhaseId = phase.Id,
            WorkflowId = workflow.Id,
            ArtifactType = phase.PhaseType.ToArtifactType(),
            Content = response.ToString(),
            Version = await _artifacts.GetNextVersion(phase.Id)
        });

        // 5. Record cost
        // Agent Framework provides token usage in response metadata

        return new PhaseResult(artifact, phase.PhaseType);
    }

    private async Task<string> BuildPhaseContext(Workflow workflow, Phase phase)
    {
        var sb = new StringBuilder();

        // Include artifacts from all completed upstream phases
        var upstreamArtifacts = await _artifacts.GetUpstreamArtifacts(
            workflow.Id, phase.Sequence
        );
        foreach (var artifact in upstreamArtifacts)
        {
            sb.AppendLine($"## {artifact.ArtifactType} (from previous phase)");
            sb.AppendLine(artifact.Content);
            sb.AppendLine();
        }

        // Include review feedback if this is a re-run
        if (phase.ReviewFeedback is not null)
        {
            sb.AppendLine("## Review Feedback (address these concerns)");
            sb.AppendLine(phase.ReviewFeedback);
            sb.AppendLine();
        }

        // Include the phase-specific BMAD workflow prompt
        sb.AppendLine(GetBmadWorkflowPrompt(phase.PhaseType));

        return sb.ToString();
    }
}
```

#### Approval Gate via Checkpoint Resume

```csharp
// API endpoint: user approves a phase
app.MapPost("/api/gates/{gateId}/approve", async (
    Guid gateId,
    ApprovalRequest request,
    WorkflowEngine engine,
    CheckpointManager checkpoints) =>
{
    var gate = await engine.GetGate(gateId);
    gate.Status = ApprovalStatus.Approved;
    gate.ApprovedBy = request.UserId;
    gate.DecidedAt = DateTimeOffset.UtcNow;
    await engine.SaveGate(gate);

    // Resume workflow from checkpoint - triggers next phase
    await foreach (var output in InProcessExecution.ResumeAsync(
        gate.CheckpointId,
        checkpoints,
        new ApprovalResult { Approved = true }))
    {
        // Workflow continues to next phase
    }

    return Results.Ok();
});

// API endpoint: user requests changes
app.MapPost("/api/gates/{gateId}/request-changes", async (
    Guid gateId,
    ChangesRequest request,
    WorkflowEngine engine) =>
{
    var gate = await engine.GetGate(gateId);
    gate.Status = ApprovalStatus.ChangesRequested;
    gate.Feedback = request.Feedback;
    gate.DecidedAt = DateTimeOffset.UtcNow;
    await engine.SaveGate(gate);

    // Phase can now be re-run with feedback injected
    return Results.Ok();
});
```

### 7.4 Project Workspace Isolation

Each project gets its own workspace directory on the server filesystem:

```
/var/flowio/workspaces/
├── {project-id-1}/
│   ├── repo/                    # git clone of the project
│   │   ├── _flow-io/
│   │   │   ├── artifacts/       # generated artifacts
│   │   │   └── constitution.md  # project constitution
│   │   └── ... (project source code)
│   └── worktrees/               # git worktrees for concurrent workflows
│       ├── workflow-{id-a}/
│       └── workflow-{id-b}/
├── {project-id-2}/
│   └── ...
```

**Concurrent workflows** within the same project use git worktrees for isolation.
The `AgentTools` instance is scoped to the workflow's worktree path.

### 7.5 Multi-Model Routing via IChatClient

```csharp
// Register multiple providers in DI
services.AddSingleton<IChatClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var provider = config["DefaultProvider"]; // anthropic, openai, ollama

    return provider switch
    {
        "anthropic" => new AnthropicClient(config["Anthropic:ApiKey"])
            .AsIChatClient(config["Anthropic:DefaultModel"])
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            .Build(),

        "openai" => new OpenAIClient(config["OpenAI:ApiKey"])
            .AsChatClient(config["OpenAI:DefaultModel"])
            .AsBuilder()
            .UseFunctionInvocation()
            .UseOpenTelemetry()
            .Build(),

        "ollama" => new OllamaChatClient(
            new Uri(config["Ollama:Endpoint"]),
            config["Ollama:DefaultModel"])
            .AsBuilder()
            .UseFunctionInvocation()
            .Build(),

        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };
});

// Per-phase model selection
public IChatClient GetClientForPhase(PhaseType phase, ProjectConfig config)
{
    var (provider, model) = config.ModelRouting.GetValueOrDefault(
        phase,
        ("anthropic", "claude-sonnet-4-5")  // default
    );

    return _clientFactory.Create(provider, model);
}
```

---

## 8. Data Model

### 8.1 Core Entities

```
User
  id: uuid
  email: string
  display_name: string
  role: enum(admin, user)
  auth_provider: string
  created_at: timestamp

Project
  id: uuid
  name: string
  slug: string
  git_url: string
  default_branch: string
  constitution_path: string          -- path to project-context.md
  phase_config: jsonb                -- which phases, models, agents per phase
  model_routing: jsonb               -- default model per phase/agent
  budget_monthly_usd: decimal
  owner_id: uuid -> User
  created_at: timestamp

ProjectMember
  project_id: uuid -> Project
  user_id: uuid -> User
  role: enum(owner, contributor, viewer)

Workflow
  id: uuid
  project_id: uuid -> Project
  title: string
  description: text
  scale_level: int (0-4)             -- BMAD scale level
  current_phase: string              -- enum of phase names
  status: enum(active, paused, completed, failed)
  created_by: uuid -> User
  created_at: timestamp
  completed_at: timestamp?

Phase
  id: uuid
  workflow_id: uuid -> Workflow
  phase_type: string                 -- analysis, requirements, test_strategy, etc.
  sequence: int
  agent_persona: string              -- BMAD agent name
  model_provider: string
  model_id: string
  status: enum(pending, running, awaiting_approval, approved, rejected, skipped)
  cost_usd: decimal
  tokens_in: int
  tokens_out: int
  duration_ms: int
  session_id: string                 -- OpenCode/Claude session ID
  started_at: timestamp?
  completed_at: timestamp?

Artifact
  id: uuid
  phase_id: uuid -> Phase
  workflow_id: uuid -> Workflow
  artifact_type: string              -- prd, architecture, test_plan, code_diff, etc.
  version: int                       -- auto-incremented on re-generation
  content: text                      -- markdown content
  file_path: string                  -- path in repo (e.g., _flow-io/artifacts/prd.md)
  created_at: timestamp

ApprovalGate
  id: uuid
  phase_id: uuid -> Phase
  status: enum(pending, approved, changes_requested, editing)
  required_approvers: uuid[]         -- list of User IDs
  approved_by: uuid? -> User
  feedback: text?                    -- reviewer comments (injected into next run)
  decided_at: timestamp?

CostRecord
  id: uuid
  project_id: uuid -> Project
  workflow_id: uuid -> Workflow
  phase_id: uuid -> Phase
  user_id: uuid -> User
  model_provider: string
  model_id: string
  tokens_in: int
  tokens_out: int
  cost_usd: decimal
  recorded_at: timestamp

AuditLog
  id: uuid
  project_id: uuid -> Project
  user_id: uuid -> User
  action: string                     -- phase.started, phase.approved, artifact.edited, etc.
  details: jsonb
  created_at: timestamp
```

### 8.2 Agent Execution State

Agent conversation history and tool call logs are stored in the Agent Framework's checkpoint system, persisted to PostgreSQL. The `Phase.checkpoint_id` references the checkpoint for workflow resumption after approval gates.

Checkpoint data includes:
- Full conversation history (messages sent/received)
- Tool call logs (which tools were invoked, inputs, outputs)
- Workflow graph position (which node/edge is current)
- Cost accumulator (tokens in/out, USD spent so far)

---

## 9. API Design (Backend)

### 9.1 Project Management

```
POST   /api/projects                    Create project
GET    /api/projects                    List user's projects
GET    /api/projects/:id                Get project details
PATCH  /api/projects/:id                Update project settings
DELETE /api/projects/:id                Archive project
POST   /api/projects/:id/members        Add member
DELETE /api/projects/:id/members/:uid   Remove member
GET    /api/projects/:id/cost           Get cost summary
```

### 9.2 Workflow Management

```
POST   /api/projects/:id/workflows              Create workflow
GET    /api/projects/:id/workflows               List workflows
GET    /api/workflows/:id                        Get workflow with phases
PATCH  /api/workflows/:id                        Update workflow
POST   /api/workflows/:id/phases/:phase/run      Trigger phase execution
POST   /api/workflows/:id/phases/:phase/rerun    Re-run phase (with modified artifact)
POST   /api/workflows/:id/phases/:phase/skip     Skip phase
GET    /api/workflows/:id/phases/:phase/stream   SSE stream of agent output
POST   /api/workflows/:id/phases/:phase/abort    Abort running phase
```

### 9.3 Artifacts

```
GET    /api/workflows/:id/artifacts              List all artifacts
GET    /api/artifacts/:id                        Get artifact (latest version)
GET    /api/artifacts/:id/versions               List versions
GET    /api/artifacts/:id/versions/:v            Get specific version
PUT    /api/artifacts/:id                        Update artifact content (user edit)
GET    /api/artifacts/:id/diff/:v1/:v2           Diff between versions
```

### 9.4 Approval Gates

```
GET    /api/gates/pending                        List pending approvals (for current user)
POST   /api/gates/:id/approve                    Approve phase
POST   /api/gates/:id/request-changes            Request changes with feedback
POST   /api/gates/:id/edit                       Enter edit mode (user edits artifact directly)
```

### 9.5 Cost & Admin

```
GET    /api/cost/summary                         Org-wide cost summary
GET    /api/cost/by-project                      Cost breakdown by project
GET    /api/cost/by-user                         Cost breakdown by user
GET    /api/cost/by-model                        Cost breakdown by model
GET    /api/admin/users                          List users (admin)
GET    /api/admin/providers                      List configured LLM providers
PATCH  /api/admin/providers/:id                  Update provider config
```

---

## 10. Frontend Views

### 10.1 Dashboard

- List of projects with status indicators (active workflows, pending approvals)
- Quick stats: total cost this month, active workflows, pending reviews
- "My Pending Approvals" widget

### 10.2 Project View

- Project settings (git repo, constitution, phase config, model routing)
- Member management
- Workflow list with status/phase indicators
- Project cost breakdown

### 10.3 Workflow View (Primary Work Surface)

```
┌─────────────────────────────────────────────────────────────┐
│  Workflow: "Add user authentication"              [Active]  │
├──────────┬──────────────────────────────────────────────────┤
│ Phases   │  Phase: Architecture                             │
│          │                                                  │
│ ✅ Anal. │  ┌─────────────────────────────────────────────┐ │
│ ✅ Reqs  │  │ Artifact: architecture.md (v2)              │ │
│ ✅ Tests │  │                                             │ │
│ 🔵 Arch ◄│  │ [Rendered Markdown View]                    │ │
│ ⬜ Ready │  │                                             │ │
│ ⬜ Story │  │ ## System Architecture                      │ │
│ ⬜ Impl  │  │ ### Authentication Flow                     │ │
│ ⬜ Revw  │  │ ...                                         │ │
│          │  │                                             │ │
│          │  └─────────────────────────────────────────────┘ │
│          │                                                  │
│          │  [Edit] [Re-run Phase] [View Diff v1↔v2]        │
│          │                                                  │
│          │  ── Approval Gate ──────────────────────────────  │
│          │  Reviewers: @alice, @bob                         │
│          │  [✅ Approve]  [🔄 Request Changes]  [✏️ Edit]   │
│          │                                                  │
│          │  ── Agent Output Stream ────────────────────────  │
│          │  > Reading existing auth modules...              │
│          │  > Analyzing dependency graph...                 │
│          │  > Proposing JWT + refresh token architecture... │
│          │  Cost: $0.42 | Tokens: 12,340 in / 8,210 out    │
├──────────┴──────────────────────────────────────────────────┤
│  💬 Comments                                                │
│  @alice: "Should we consider OAuth2 instead of JWT-only?"  │
│  @bob: "Agreed, updated the requirements. Re-running."     │
└─────────────────────────────────────────────────────────────┘
```

### 10.4 Cost Dashboard

- Time-series chart of spend by project/model
- Budget utilization gauges
- Per-phase cost breakdown
- Model routing efficiency (cost per phase type)

---

## 11. Agent Invocation Flow

### 11.1 Triggering a Phase

```
User clicks "Run Phase" on Workflow View
    │
    ▼
Backend validates:
  - Previous phase is approved (or skipped)
  - User has contributor+ role
  - Project budget not exceeded
    │
    ▼
Backend assembles agent invocation:
  1. Load BMAD agent persona for this phase type
  2. Load project constitution (project-context.md)
  3. Load artifacts from previous phases as context
  4. Determine model from phase config / model routing
  5. Instantiate AgentTools scoped to project workspace
    │
    ▼
PhaseRunner creates AIAgent via BmadAgentFactory:
  - AnthropicClient.AsAIAgent(model, instructions, tools)
  - instructions = BMAD persona + constitution
  - tools = C# tool methods scoped to workspace
    │
    ▼
Agent runs with streaming via IAsyncEnumerable:
  - Each chunk pushed to SignalR hub → connected dashboard clients
  - Agent reads/writes files via AgentTools (scoped to workspace)
  - Tool calls are automatic (Agent Framework handles the loop)
    │
    ▼
Agent completes:
  - Extract artifact from agent output
  - Save artifact to PostgreSQL with version number
  - Commit artifact to git repo in workspace
  - Record cost (tokens, USD) from response metadata
  - Workflow checkpoints at approval gate
  - Notify assigned reviewers (Teams/email/webhook)
    │
    ▼
Phase status → awaiting_approval
Workflow paused at checkpoint (persisted to PostgreSQL)
```

### 11.2 Approval Flow

```
Reviewer opens pending approval
    │
    ├── [Approve] → Phase status → approved
    │                Next phase becomes runnable
    │
    ├── [Request Changes] → Phase status → rejected
    │   Feedback text stored on ApprovalGate
    │   On re-run, feedback is prepended to agent prompt:
    │   "Previous review feedback: {feedback}. Address these concerns."
    │
    └── [Edit] → User edits artifact in browser
        On save: new artifact version created
        User can then [Approve] the edited version
        Or [Re-run] the phase with the edited artifact as input
```

### 11.3 Spec Iteration (Course Correction)

When the AI produces incorrect output:

```
1. User identifies issue in artifact (e.g., wrong architecture choice)
2. User can:
   a. Edit the CURRENT phase's artifact → approve edited version → continue
   b. Go BACK to an earlier phase → edit THAT artifact → re-run from there
      (downstream phases are invalidated and must re-run)
   c. Edit the constitution → affects ALL future phase runs
3. Re-running a phase:
   - Increments artifact version
   - Previous version preserved (full history)
   - Agent receives: original prompt + edited upstream artifacts + review feedback
```

---

## 12. Tech Stack

| Layer                  | Technology                                          | Rationale                                              |
| ---------------------- | --------------------------------------------------- | ------------------------------------------------------ |
| **Backend**            | ASP.NET Core 9 (C#)                                 | Primary ecosystem, existing expertise                  |
| **Agent Framework**    | Microsoft.Agents.AI + Microsoft.Agents.AI.Anthropic | First-class Claude, graph workflows, checkpointing     |
| **AI Abstraction**     | Microsoft.Extensions.AI                             | IChatClient for model portability                      |
| **Claude SDK**         | Anthropic.SDK (tghamm, v5.10.0)                     | IChatClient adapter, proven with SK                    |
| **Database**           | PostgreSQL 16 + EF Core                             | JSONB for flexible config, proven reliability          |
| **Real-time**          | SignalR                                             | WebSocket streaming to frontend, built into ASP.NET    |
| **Auth**               | ASP.NET Identity + OIDC/Keycloak                    | Enterprise SSO, role-based access                      |
| **Background Jobs**    | Hosted services / Hangfire                          | Long-running phase execution                           |
| **Observability**      | OpenTelemetry (via M.E.AI middleware)               | Free tracing/metrics on all LLM calls                  |
| **Frontend**           | React (Vite) or Blazor                              | TBD - React for richest ecosystem, Blazor for C# unity |
| **UI Components**      | shadcn/ui + Tailwind (React) or MudBlazor (Blazor)  | Standard, customizable                                 |
| **Markdown Rendering** | react-markdown or Markdig (Blazor)                  | Artifact display                                       |
| **Diff View**          | monaco-editor (diff mode)                           | Code and markdown diffs                                |
| **Notifications**      | Microsoft Teams MCP + email                         | Approval notifications                                 |
| **File Storage**       | Git (artifact commits) + local filesystem           | Versioned artifacts                                    |
| **Containerization**   | Docker Compose (dev) / K8s (prod)                   | Deployment                                             |

---

## 13. Deployment

### 13.1 Development

```bash
docker compose up -d postgres    # PostgreSQL
dotnet run --project src/FlowIo.Api   # ASP.NET Core backend
cd src/FlowIo.Web && npm run dev      # React frontend (if React)
# OR: backend serves Blazor frontend automatically
```

### 13.2 Production

```
┌─────────────────────────────────────┐
│  Kubernetes Cluster                 │
│                                     │
│  ┌─────────────────────────────┐   │
│  │ flow-io-api (Deployment)    │   │
│  │ ASP.NET Core backend        │   │
│  │ + SignalR hub               │   │
│  │ Replicas: 2+                │   │
│  │ PVC: /var/flowio/workspaces │   │
│  └─────────────────────────────┘   │
│                                     │
│  ┌─────────────────────────────┐   │
│  │ flow-io-web (Deployment)    │   │
│  │ React SPA (nginx)           │   │
│  │ OR: served by API pod       │   │
│  └─────────────────────────────┘   │
│                                     │
│  ┌─────────────────────────────┐   │
│  │ flow-io-db (StatefulSet)    │   │
│  │ PostgreSQL 16               │   │
│  └─────────────────────────────┘   │
└─────────────────────────────────────┘
```

### 13.3 Workspace Management

```
Project created → git clone into /var/flowio/workspaces/{project-id}/repo
Workflow started → git worktree add for isolation
Phase triggered → PhaseRunner executes in-process (no external containers)
  - AgentTools scoped to worktree path
  - Agent Framework handles LLM calls directly
  - No subprocess spawning, no container lifecycle
Workflow completed → git worktree remove, merge artifacts to branch
Project archived → remove workspace directory
```

**Key simplification vs. OpenCode approach:** No container-per-project. No SQLite issues. No subprocess management. The agent runs in-process within the ASP.NET Core application. LLM calls go directly to provider APIs. Tool execution is C# method calls. This dramatically reduces operational complexity.

---

## 14. Security Considerations

- **Agent sandboxing:** AgentTools are scoped to the project's git worktree directory. Path traversal checks prevent agents from accessing files outside the workspace. Bash tool uses a configurable allowlist/blocklist for commands.
- **Secrets:** LLM API keys stored in backend configuration (ASP.NET user secrets / Azure Key Vault / K8s secrets). Never exposed to frontend or agent context.
- **Git credentials:** Backend clones repos via service account token. Agents access the local clone only through scoped AgentTools; no direct git credentials in agent context.
- **Tool restrictions:** Implementation phase allows bash; planning phases restrict to read-only tools (ReadFile, GlobSearch, GrepSearch). Tool availability is configurable per phase type in project settings.
- **Process isolation:** Each workflow runs in a dedicated git worktree. No shared mutable state between concurrent workflows. Agent execution is in-process but tool invocations are scoped.
- **Audit logging:** All phase runs, approvals, artifact edits, and cost events logged with user attribution via EF Core + PostgreSQL.
- **Cost guardrails:** Per-phase budget limits enforced before invocation. Per-project monthly limits with alerts at 80%. Token usage tracked from response metadata on every LLM call.

---

## 15. Open Questions

1. **Naming:** "Flow.io" is a working name. Needs trademark check.

2. **Agent Framework maturity:** Microsoft Agent Framework is in RC2 (GA expected Q1-Q2 2026). Need to track for breaking changes between RC and GA. Fallback: raw Anthropic.SDK + custom state machine (Option 3 from architecture analysis).

3. **BMAD persona loading:** BMAD agent `.md` files are designed for interactive IDE use with step-file just-in-time loading. Need to validate that injecting them as system prompt instructions to the AIAgent produces equivalent quality. May need to flatten/transform persona files into consolidated system prompts per phase.

4. **Git workflow:** Should artifacts be committed to the project's main branch, a dedicated `flow-io` branch, or a separate artifact repo? Implications for PRs and merge conflicts.

5. **Concurrent workflows:** Solved architecturally via git worktrees (one worktree per active workflow). Need to validate worktree limits and cleanup strategy for long-lived workflows.

6. **Offline/local models:** IChatClient abstraction supports Ollama via `Microsoft.Extensions.AI.Ollama`. Should v1 support local models, or defer to v2?

7. **Notification integrations:** Which channels are essential for v1? (Email, Teams, Slack, webhook?)

8. **Phase customization depth:** How far can users customize the phase pipeline? Add custom phases? Reorder? Or only toggle on/off from the default set?

9. **Artifact format:** Pure markdown? MDX with components? YAML frontmatter + markdown body?

10. **Multi-repo projects:** Some features span multiple repositories. Should a workflow support multiple project repos?

11. **Frontend framework:** React (Vite) vs Blazor. React has richer ecosystem (monaco-editor, react-markdown, shadcn/ui). Blazor keeps everything in C# but has fewer mature component libraries for code editing/diffing.

12. **Scaling agent execution:** In-process agent execution ties agent concurrency to the backend pod's resources. For heavy workloads, should agent execution be offloaded to background worker pods with a message queue (e.g., RabbitMQ/Redis)?

---

## 16. Success Metrics

| Metric                                          | Target                                              |
| ----------------------------------------------- | --------------------------------------------------- |
| Time from requirements to approved architecture | < 30 min (vs hours manually)                        |
| Spec iteration cycles before approval           | < 3 on average                                      |
| Phase re-run rate (AI got it wrong)             | < 25% of phases need re-run                         |
| Cost per completed workflow                     | Trackable, trending down over time                  |
| User adoption                                   | Team actively using for new features within 1 month |

---

## 17. Milestones

### M1: Foundation (Weeks 1-3)
- [ ] ASP.NET Core 9 project scaffolding (solution structure, DI, configuration)
- [ ] PostgreSQL schema + EF Core migrations
- [ ] Auth (ASP.NET Identity + single OIDC provider)
- [ ] Project CRUD + git clone into workspace directory
- [ ] Basic AgentTools (ReadFile, WriteFile, GlobSearch, RunBash)
- [ ] BmadAgentFactory: instantiate one AIAgent with hardcoded persona

### M2: Workflow Engine (Weeks 4-6)
- [ ] Phase state machine with EF-backed transitions
- [ ] BmadWorkflowBuilder: graph with checkpoint edges
- [ ] PhaseRunner: execute agent, stream output via SignalR
- [ ] BMAD persona loading per phase (persona store from .md files)
- [ ] Artifact extraction, creation, and versioning in PostgreSQL
- [ ] Approval gates (approve/reject/request-changes with checkpoint resume)

### M3: Dashboard UI (Weeks 7-9)
- [ ] Frontend framework decision and scaffolding (React or Blazor)
- [ ] Project list and detail views
- [ ] Workflow view with phase progression sidebar
- [ ] Artifact markdown viewer with version history
- [ ] Real-time agent output stream (SignalR client)
- [ ] Approval gate UI (approve/reject/edit)

### M4: Collaboration (Weeks 10-12)
- [ ] Multi-user roles and permissions (Owner/Admin/Contributor/Reviewer)
- [ ] Approval assignments and notifications (Teams webhook)
- [ ] Artifact editing in browser (monaco-editor or equivalent)
- [ ] Spec iteration flow (edit upstream artifact + re-run downstream phases)
- [ ] Comments on artifacts

### M5: Polish & Cost (Weeks 13-15)
- [ ] Cost tracking per LLM call (tokens, USD from response metadata)
- [ ] Cost dashboard (by project, user, model, phase type)
- [ ] Budget enforcement (per-phase and per-project limits)
- [ ] Model routing configuration UI (per-phase model selection)
- [ ] Constitution editor (project-context.md in browser)
- [ ] Phase configuration UI (enable/disable/reorder phases)

### M6: Hardening (Weeks 16-18)
- [ ] Git worktree lifecycle reliability (cleanup, conflict handling)
- [ ] Error handling and recovery (agent failures, LLM timeouts, partial completions)
- [ ] Concurrent workflow isolation validation
- [ ] Performance optimization (agent execution, SignalR throughput)
- [ ] OpenTelemetry integration (traces on all LLM calls via M.E.AI middleware)
- [ ] Documentation and onboarding guide