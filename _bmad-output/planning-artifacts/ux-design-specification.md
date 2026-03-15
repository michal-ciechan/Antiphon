---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
inputDocuments:
  - _bmad-output/planning-artifacts/prd.md
  - _bmad-output/planning-artifacts/original-rough-spec.md
documentCounts:
  prd: 1
  briefs: 0
  projectContext: 0
  other: 1
techStack:
  ui: [Bootstrap, Blueprint JS]
  framework: [React]
  icons: [react-icons]
---

# UX Design Specification - Antiphon

**Author:** Mike
**Date:** 2026-03-15

---

## Executive Summary

### Project Vision

Antiphon is a spec-driven AI workflow orchestration platform that brings structure, visibility, and human governance to AI-assisted software development. Built as a React SPA (Bootstrap + Blueprint JS + react-icons) served by ASP.NET Core, it replaces ad-hoc AI coding sessions with structured, reviewable, version-controlled workflows. Single-tenant, self-hosted, desktop-only internal tool targeting developer teams.

The core UX thesis: AI does the work, humans steer through intelligent decision points. Neither operates in a vacuum.

### Target Users

- **Developers** — Primary users. Create workflows, watch AI agents execute in real-time, review generated artifacts (specs, architecture docs, test plans, code), provide feedback at approval gates, and iterate through course corrections. Tech-savvy, comfortable with git concepts, expect responsive tooling.
- **Team Leads** — Oversight users. Review pending approvals across the team, track workflow progress from the dashboard, drive adoption of spec-before-code culture. Need at-a-glance status without drilling into every workflow.
- **Admins** (post-MVP) — Platform engineers who deploy, configure LLM providers and model routing, manage projects, and set up integrations. Power users comfortable with YAML and infrastructure config.

### Key Design Challenges

1. **Information density vs. clarity** — Dashboard must surface workflow status, stage progression, pending approvals, and cost data without overwhelming. Progressive disclosure is critical.
2. **Real-time streaming UX** — Token-by-token agent output with activity status line must feel responsive and informative without being noisy. Users should always know what the agent is doing.
3. **Course correction flow complexity** — Diff-based cascade updates (update/regenerate/keep per affected stage) are the core differentiator but the most complex interaction. Must feel intuitive.
4. **Gate interaction model** — Approval gates are the primary human-AI collaboration point. Free-text prompting, approve/reject/go-back, and artifact review must feel conversational, not bureaucratic.

### Design Opportunities

1. **Progressive disclosure** — Workflow detail unfolds naturally: summary → stage view → artifact → diff → audit trail. Depth on demand.
2. **Live agent presence** — Real-time streaming + activity status line creates a compelling "AI working for you" experience that builds trust and differentiates from competitors.
3. **Git-backed versioning as UX feature** — Artifact version history and clean diff views make course correction feel powerful rather than complex.

## Core User Experience

### Defining Experience

Antiphon's core experience is a two-part loop:

1. **Dashboard triage** — Users scan their workflows (or the team's workflows) to identify what needs attention. Which workflows are blocked on me? Which stages just completed? What's the AI working on right now?
2. **Artifact review + action** — Users view the AI-generated output (rendered markdown), assess its quality, and take action: approve to advance, prompt the agent for changes, reject with feedback, or go back to a previous stage.

The dashboard also serves a **passive monitoring** mode — not every visit is action-driven. Sometimes users just want to glance: "is my thing still running?" "how far along is it?" The dashboard serves both glancing and deciding.

The dashboard answers "what needs me?" The gate interaction answers "what do I do about it?" Everything else in the product supports this loop.

### Platform Strategy

- **Desktop web only** — React SPA, modern evergreen browsers (Chrome, Edge, Firefox). No mobile, no offline, no SSR.
- **Mouse/keyboard primary** — Developer tooling; users expect keyboard shortcuts, text input, and precise interactions.
- **Always-connected** — Internal tool behind corporate network. SignalR for all real-time updates. No offline mode.
- **Single-tenant self-hosted** — One instance per org. No multi-tenant concerns, no public-facing pages, no SEO.
- **Component library discipline** — Bootstrap for layout, grid, and spacing utilities. Blueprint JS for complex interactive components (data tables, stage progression, artifact trees, diff panels, tabs). Do not mix visual languages — pick Bootstrap for structure, Blueprint for interaction. Consistent icon usage via react-icons.

### Effortless Interactions

- **Getting to what needs you** — Dashboard → pending item in 1-2 clicks. No hunting, no navigation maze. Role-based action surfacing: developers see their gates, team leads see team gates.
- **Understanding AI output** — Rendered markdown artifacts are immediately readable. Rich rendering: syntax highlighting for code blocks, Mermaid diagram support, clean typography. Content-first, minimal UI chrome. The artifact IS the page, not something embedded in a sidebar or modal.
- **Providing feedback** — Free-text prompt box feels like a chat message, not a form. Type, send, watch the agent respond. The feedback loop closes visibly — after sending, the user immediately sees the agent pick up the feedback and start working. No void.
- **Seeing agent activity** — Show the work at the right level. Dashboard shows summary status lines per workflow. Workflow detail page shows the full real-time stream with activity status line. No attempt to stream everything everywhere.
- **Knowing workflow status** — Progress indicators, stage state, and pending actions are visible at a glance on the dashboard. No drilling required to understand where things stand. Supports both quick glances and deep triage.

### Critical Success Moments

1. **"It's actually doing it"** — First time a user watches the agent stream output in real-time, sees tool calls happening, and realizes the AI is genuinely building their spec/architecture/code.
2. **"Course correction works"** — User rejects an artifact, types feedback, and gets a meaningfully better version back. The diff-based cascade updates downstream artifacts intelligently. This is the moment that proves the product's core thesis.
3. **"My team can see this"** — A developer has a reviewable artifact — a spec, architecture doc, or test plan — that their team can view, comment on, and approve before any code is written. The cultural shift from "surprise PR" to "reviewed spec" is the product's reason to exist.
4. **"I can see everything"** — Team lead opens the dashboard and knows exactly what's in flight, what's blocked, and what needs their review. No standup needed to get this picture.

### Experience Principles

1. **Show the work — at the right level** — AI execution is never a black box. But match the detail to the context: status lines on the dashboard, full streaming on the detail page. Transparency builds trust without overwhelming.
2. **Action over navigation** — Every screen makes the next action obvious. Pending approvals surface themselves. Role-aware: developers see their work, team leads see the team's work. The UI guides users toward what needs them.
3. **Content is king** — Artifacts (rendered markdown with syntax highlighting, diagrams, and clean typography) are the primary content. UI exists to frame and facilitate interaction with artifacts, not to compete with them for attention.
4. **Feedback is a conversation** — Gates are dialogue points, not checkboxes. Free-text prompting, reject-with-reason, and iterative refinement should feel natural. The loop always closes — send feedback, see the agent respond.
5. **Progressive depth** — Summary first, details on demand. Dashboard → workflow → stage → artifact → diff → audit trail. Each level adds depth without forcing it.

## Desired Emotional Response

### Primary Emotional Goals

- **In control** — AI is powerful but the user is steering. They can see what the agent is doing, course-correct at any point, and nothing ships without explicit human approval. The user is the decision-maker, not the AI.
- **Collaborative ownership** — Specs and artifacts are shared team objects. Reviewing them — whether in-app at a gate or via GitHub PR — feels like a natural team activity. The product enables the same collaborative review culture that exists for code (PRs) but extends it to requirements, architecture, and test plans.
- **Confident** — The user trusts the output because they can see how it was produced, review it thoroughly, and iterate before anything moves forward. Transparency breeds confidence.
- **Leverage** — The user isn't just "being efficient" — they're getting 10x more done because AI handles the grunt work and their team reviews the important stuff. Structure isn't overhead; it's a force multiplier.

### Emotional Journey Mapping

| Moment | Desired Feeling |
|--------|----------------|
| Opening the dashboard | Oriented — I know what's happening across my work and my team's work |
| Creating a new workflow | Anticipation — "let's see what the AI produces" |
| Watching agent stream | Engaged trust — "it's working, I can see exactly what it's doing" |
| Reviewing an artifact at a gate | Ownership — "this is my spec to evaluate and shape" |
| AI produces unexpectedly good output | Surprise — "this is actually better than what I would have written" |
| Sending feedback / prompting | Conversational — "I'm directing the AI, not filling out a form" |
| Seeing the agent respond to feedback | Responsive partnership — "it heard me and it's acting on it" |
| Team member reviews in-app at gate | Real-time participation — "I'm part of this as it happens" |
| Team member reviews via GitHub PR | Asynchronous contribution — "I can weigh in on my schedule and my feedback still matters" |
| Course correction (go back, cascade update) | Empowered — "I can fix this without starting over" |
| Workflow completes | Accomplishment — "we have a reviewed, approved artifact set" |
| Something goes wrong (agent error, bad output) | Empowered — "I can see what happened, I know exactly how to fix this" |

### Micro-Emotions

- **Confidence over confusion** — Every screen answers "what am I looking at?" and "what should I do next?" without cognitive load.
- **Trust over skepticism** — Real-time visibility into agent work, full audit trail, and version history build trust in AI output over time.
- **Accomplishment over frustration** — Course correction is empowering ("I can fix this"), not frustrating ("I have to start over").
- **Belonging over isolation** — Spec review is a team activity. In-app gates and GitHub PRs are two paths to the same collaborative review culture. No one builds alone.
- **Surprise over expectation** — The AI occasionally exceeds what the user thought possible. Don't suppress this — let the output speak for itself. These are the moments users tell their teammates about.

### Design Implications

- **In control** → Always-visible progress, clear stage states, explicit approve/reject/go-back actions. No auto-advancing without user consent.
- **Collaborative ownership** → Artifact review supports both in-app gate review AND GitHub PR review as first-class paths. In-app review leans into real-time participation; PR review leans into asynchronous contribution. Don't force them to feel the same — lean into what each does best.
- **Confident** → Rich markdown rendering so artifacts are immediately assessable. Diff views between versions. Full audit trail accessible but not intrusive.
- **Leverage** → Minimal clicks to create workflows, provide feedback, and approve gates. The UI should make users feel like they're commanding a team, not operating a machine.
- **Empowered when things break** → Clear error states with actionable options (retry from checkpoint, retry from gate, view error details). No dead ends. No passive "something went wrong" messages — always show what can be done next.

### Emotional Design Principles

1. **Control through visibility** — Users feel in control because they can see everything, not because the UI asks permission for everything. Show state, don't gate actions behind confirmations.
2. **Collaboration is the product** — Every design decision should ask: "does this make team review easier?" Meet collaborators where they are — in-app for those in Antiphon, GitHub PRs for those who live in GitHub. Don't force people into a new tool to participate.
3. **Trust is earned incrementally** — First use: user watches carefully. Tenth use: user trusts the output and reviews faster. Design for both — full detail available but not forced.
4. **Errors are recoverable, not catastrophic** — Agent failures, bad output, and wrong directions are expected. The UI treats them as normal workflow events with clear next actions, not exceptional errors.

## UX Pattern Analysis & Inspiration

### Inspiring Products Analysis

#### GitHub (Primary — Collaborative Review)

GitHub's pull request review flow is the benchmark for collaborative artifact review:
- **PR page as review surface** — Description, diff, conversation, checks, and merge status all live on one page. No tab-switching to understand the full picture.
- **Diff views** — Side-by-side or unified, syntax highlighted, collapsible by file. Immediately readable.
- **Review actions as distinct intents** — Approve, Request Changes, and Comment are three separate actions, not a single "submit" with a dropdown. Each carries clear meaning.
- **Inline conversation threads** — Comments anchored to specific lines. Discussions resolve without cluttering the main view.
- **Checks and status** — CI status, required reviewers, and merge readiness visible at a glance. The page tells you "what's blocking this" without investigation.

**Relevance to Antiphon:** Gate review is Antiphon's PR review. The artifact viewer + gate controls should feel like a PR page — everything on one surface, clear actions, conversation threaded alongside the content.

#### Claude Code (Primary — AI Agent Experience)

Claude Code defines what real-time AI agent interaction should feel like:
- **Streaming output** — Token-by-token rendering creates the feeling of watching the AI think. Responsive, alive.
- **Activity status line** — Constant awareness: current tool, token count, elapsed time. User never wonders "is it stuck?"
- **Tool call visibility** — File reads, writes, searches, and bash commands are visible. Users see WHAT the agent is doing, not just what it's producing. Builds trust.
- **Conversational feedback** — Type a message, the AI responds. No forms, no modals, no ceremony.

**Relevance to Antiphon:** The agent execution view should feel like Claude Code — streaming output, activity status, tool visibility. The feedback prompt at gates should feel like typing a message to Claude Code.

#### Linear (Secondary — Dashboard Design)

- **Minimal, fast dashboard** — Status at a glance without visual noise. Clean typography, restrained color use.
- **Keyboard-first** — Power users navigate entirely via keyboard. Cmd+K command palette.
- **Smart defaults** — Filters, views, and groupings that match how teams actually think about work.

**Relevance to Antiphon:** Dashboard should aspire to Linear's information density without clutter. Keyboard navigation for power users.

#### Vercel (Secondary — Pipeline Visibility)

- **Deployment as visual progression** — Build → Deploy → Ready as a clear stage pipeline with timing.
- **Streaming build logs** — Real-time log output during builds. Expandable sections for detail.
- **Clear success/failure states** — Green/red/yellow with immediate context on what happened.

**Relevance to Antiphon:** Workflow stage progression should feel like Vercel's deployment pipeline — clear visual stages with status indicators and timing.

#### Azure DevOps (Secondary — Approval Gates)

- **Multi-stage pipeline with gates** — Visual pipeline with approval checkpoints between stages.
- **Gate approval UI** — Approve/reject with comments. Required approvers visible.

**Relevance to Antiphon:** Gate approval flow can learn from Azure DevOps' multi-stage pipeline visualization, but must feel lighter and more conversational.

### Transferable UX Patterns

**Navigation Patterns:**
- **Single-surface review** (GitHub) — Artifact content, gate actions, and conversation all on one page. No tab-switching to complete a review.
- **Command palette** (Linear) — Cmd+K for fast navigation. Power users skip the mouse entirely.
- **Status-at-a-glance dashboard** (Linear) — Workflow cards with stage indicators, pending actions flagged visually.

**Interaction Patterns:**
- **Distinct review actions** (GitHub) — Approve, Reject with Feedback, and Go Back are visually distinct buttons with clear intent, not a generic "submit."
- **Conversational feedback** (Claude Code) — Free-text prompt box that feels like chat. Type → send → see response.
- **Streaming with activity status** (Claude Code) — Token stream + status line showing current tool, tokens, elapsed time.

**Visual Patterns:**
- **Stage pipeline visualization** (Vercel/Azure DevOps) — Horizontal or vertical stage progression with status colors and timing per stage.
- **Diff rendering** (GitHub) — Syntax-highlighted, side-by-side or unified, collapsible sections. For artifact version comparison.
- **Restrained color palette** (Linear) — Color used for status signaling (green/yellow/red/blue), not decoration. Clean backgrounds, strong typography.

### Anti-Patterns to Avoid

- **Jenkins-style information overload** — Wall of text logs with no hierarchy. Agent output needs structure: collapsible tool calls, highlighted artifacts, status summaries.
- **Jira-style form-heavy interactions** — Every action requiring a modal form with dropdowns and required fields. Gates should feel conversational, not bureaucratic.
- **Azure DevOps navigation maze** — Deep nested menus and breadcrumbs to reach the thing you need. Dashboard → workflow → gate should be 2 clicks maximum.
- **"Silent AI" pattern** — AI produces output with no visibility into process. Users wait at a spinner, then get a result. Antiphon must always show the work.
- **Forced-tool adoption** — Requiring all collaborators to use Antiphon's UI. GitHub PR review must be a first-class path for team members who live in GitHub.

### Design Inspiration Strategy

**What to Adopt:**
- GitHub's single-surface review pattern for gate interactions — artifact, actions, and conversation on one page
- Claude Code's streaming + activity status pattern for agent execution views
- Linear's dashboard density and keyboard-first navigation philosophy
- GitHub's diff rendering for artifact version comparison

**What to Adapt:**
- Vercel's deployment pipeline visualization → adapted for workflow stage progression with approval gates (not just pass/fail)
- GitHub's PR review actions (Approve/Request Changes/Comment) → adapted as gate actions (Approve/Reject with Feedback/Go Back/Prompt Agent)
- Claude Code's conversational input → adapted for gate feedback with awareness that responses may take minutes, not seconds

**What to Avoid:**
- Jenkins' log-dump approach to execution visibility
- Jira's form-heavy, modal-driven interaction model
- Azure DevOps' deep navigation hierarchies
- Any pattern where AI work is invisible or users must actively seek status

## Design System Foundation

### Design System Choice

**Approach:** Dual established system — Bootstrap + Blueprint JS, with react-icons for iconography.

This is a complementary pairing, not a competing one. Blueprint JS (by Palantir) was designed specifically for data-dense, keyboard-friendly developer tools — exactly Antiphon's profile. Bootstrap provides the layout and structural foundation that Blueprint doesn't prioritize.

### Rationale for Selection

- **Blueprint JS** was built for internal developer tools at Palantir. Its component library (tables, trees, tabs, panels, dialogs, menus) maps directly to Antiphon's needs: workflow dashboards, stage progression, artifact trees, diff panels, approval gate controls.
- **Bootstrap** provides the grid system, responsive layout utilities, and structural components (cards, navbars, badges, alerts) that Blueprint intentionally doesn't cover. Bootstrap's utility classes enable rapid layout iteration.
- **react-icons** provides unified access to multiple icon libraries (Feather, Material, Font Awesome, etc.) through a single import pattern, avoiding icon library lock-in.
- **No custom design system overhead** — Solo developer building with AI assistance. Established systems provide accessibility, keyboard handling, and consistent theming out of the box.

### Implementation Approach

**Component Ownership Boundaries:**

| Domain | Owner | Components |
|--------|-------|------------|
| Layout & structure | Bootstrap | Grid, containers, rows/cols, spacing utilities, responsive breakpoints |
| Navigation chrome | Bootstrap | Navbar, breadcrumbs, badges |
| Content containers | Bootstrap | Cards (as layout wrappers), alerts |
| Interactive controls | Blueprint | Buttons, form controls, switches, sliders |
| Data display | Blueprint | Tables, trees, tag inputs |
| Navigation panels | Blueprint | Tabs, panels, sidebar/drawer |
| Overlays | Blueprint | Dialogs, toasts, popovers, tooltips, menus |
| Iconography | react-icons | All icons across the application |

**Conflict Resolution Rule:** Where both libraries offer the same component (buttons, form inputs, dropdowns), **Blueprint wins**. Blueprint has superior keyboard handling, consistent theming, and is designed for dense UIs. Never mix Bootstrap and Blueprint versions of the same component on a single page.

### Customization Strategy

**Theme:**
- **Dark theme as default** — Developer tool convention. Aligns with Claude Code, VS Code, and most developer tooling. Easier on the eyes during extended use.
- Blueprint's dark theme as the base. Bootstrap variables aligned to match Blueprint's dark color tokens for visual consistency.
- Status colors standardized across both libraries: green (success/approved), yellow (pending/warning), red (error/rejected), blue (in-progress/info). Ensure sufficient contrast against dark backgrounds.

**Typography:**
- Blueprint's default font stack (system fonts). No custom web fonts — internal tool, performance over branding.
- Monospace font for code blocks, agent output streaming, and tool call displays.

**Spacing & Density:**
- Blueprint's default density (compact) for data-heavy views (dashboard, artifact tables, audit logs).
- Bootstrap's spacing utilities for page-level layout and content areas (artifact viewer, gate review page).

**Light Theme (Future):**
- Not in MVP. Blueprint supports light theme natively; Bootstrap can be themed to match. Add as user preference toggle post-MVP.

## Defining Core Experience

### Defining Experience

**"Point AI at a spec, watch it build artifacts, and review them with your team before any code is written."**

Antiphon's defining interaction is the shift from solo AI sessions to structured, reviewable, team-visible AI-assisted workflows. The user describes a feature, the AI drafts specs and artifacts through structured stages, and the team reviews and approves before implementation begins.

### User Mental Model

**The metaphor:** AI is the junior dev who does the drafting; you and your team are the reviewers who steer.

Users already understand this model from code review (PRs). Antiphon extends it upstream — instead of reviewing code after it's written, teams review specs, architecture, and test plans before any code exists.

**Current solutions and pain points:**
- **Claude Code / Cursor** — Great at generating, but solo activity. No team visibility, no review gates, no structure. When the AI goes wrong, start over or re-prompt.
- **Jira / Linear** — Great at tracking, but don't execute. Status updates, not work product.
- **The gap** — Nothing lets you say "AI, draft this spec" and then have your team review it through structured gates before implementation starts.

**User expectations:**
- "Creating a workflow should be as easy as opening a PR"
- "Reviewing an artifact should feel like reviewing a PR"
- "Giving feedback should feel like typing a message to Claude Code"
- "Seeing what the AI is doing should feel like watching Claude Code work"

### Success Criteria

- **"This just works"** — User creates a workflow, AI starts executing, output streams in real-time. No configuration ceremony, no setup wizards.
- **"I can see what it did"** — Artifact is immediately readable as rendered markdown. User can assess quality in seconds, not minutes.
- **"My team can review this"** — Artifact is reviewable in-app at the gate or via GitHub PR. Both paths lead to the same approval flow.
- **"I can fix this"** — Bad output → reject with feedback → better output. Course correction doesn't mean starting over.
- **Speed** — From workflow creation to first reviewable artifact: under 5 minutes for a standard feature spec.
- **Automatic context** — Agent loads project constitution, upstream artifacts, and stage instructions without user intervention. The AI knows the project.

### Novel UX Patterns

**Established patterns combined in a new context:**

| Pattern | Source | Antiphon Application |
|---------|--------|---------------------|
| Artifact review as PR-style review | GitHub | Gate review page: artifact content + actions + conversation on one surface |
| Real-time agent streaming | Claude Code | Agent execution view: token stream + activity status line + tool call visibility |
| Pipeline stage visualization | Vercel / Azure DevOps | Workflow detail: horizontal stage progression with status colors and timing |
| Conversational feedback | Claude Code | Gate prompt box: type feedback, watch agent respond |

**Genuinely novel pattern — Diff-based cascade correction:**

No existing product offers this. When a user corrects a stage artifact (e.g., rewrites architecture from microservice to monolith), the system:
1. Detects downstream stages that were built on the old version
2. Computes the git diff between old and new versions
3. Presents each affected stage with three choices: **Update based on diff** / **Regenerate from scratch** / **Keep as-is**
4. For "update," the AI receives the diff and intelligently patches the downstream artifact

This is the interaction most likely to confuse users on first encounter. UX mitigations:
- Clear visual indication of which stages are affected (highlighted in the stage progression)
- Simple, prominent choice buttons — not a complex dialog
- "Update based on diff" as the default/recommended option
- Preview of what will change before committing
- "Regenerate" as the safe fallback if patching produces poor results

### Experience Mechanics

**1. Initiation — Creating a Workflow:**
- User clicks "New Workflow" from dashboard
- Selects a workflow template (BMAD full, BMAD quick, custom)
- Points at a git repository and provides initial context (free-text description, pasted ticket details)
- System creates the workflow, sets up git branches, and immediately begins the first stage

**2. Interaction — Watching & Reviewing:**
- Agent streams output in real-time: token-by-token text, tool calls visible, activity status line showing current action + tokens + elapsed time
- When stage completes, artifact appears as rendered markdown on the gate review page
- User reads the artifact — content is the page, not embedded in a sidebar
- Gate actions are prominent: Approve (green), Reject with Feedback (yellow), Go Back (orange), Prompt Agent (blue)

**3. Feedback — Steering the AI:**
- User types free-text in the prompt box and sends
- Agent immediately picks up the feedback — streaming begins again, user sees the response forming
- Updated artifact replaces the previous version; diff available to see what changed
- Loop repeats until user is satisfied

**4. Completion — Artifact Approved:**
- User clicks Approve
- Stage branch merges to workflow master, git tag created for audit
- Workflow advances to next stage — agent begins executing
- Progress indicator updates; dashboard reflects new state for all connected users
- When all stages complete: reviewed, approved artifact set ready for implementation

## Visual Design Foundation

### Color System

**Base:** Blueprint JS dark theme defaults. No custom brand colors — leverage Blueprint's proven, accessible dark palette.

**Primary accent:** Blueprint blue (`#2d72d2`) — used for primary actions, active states, selected items, links, and focus indicators.

**Semantic colors (Blueprint defaults):**

| Role | Color | Usage |
|------|-------|-------|
| Primary | Blue `#2d72d2` | Primary actions, active states, links, focus |
| Success | Green `#238551` | Approved gates, completed stages, success states |
| Warning | Orange `#c87619` | Pending review, warnings, attention needed |
| Danger | Red `#cd4246` | Rejected gates, errors, failed stages |
| Intent None | Gray | Neutral actions, secondary buttons, disabled states |

**"AI active" visual treatment:** Do not use color alone to indicate "agent is working." Use motion — animated spinner, pulsing indicator, streaming text animation — to distinguish active execution from clickable elements. Blue means interactive; motion means busy. Keep the palette minimal, use motion for state.

**Background hierarchy (dark theme):**
- App background (darkest) → Panel/card background (slightly lighter) → Elevated surface (dialog, popover) → Input fields
- All following Blueprint's dark theme elevation model

**Text colors:**
- Primary text: white/light gray for high contrast against dark backgrounds
- Secondary text: muted gray for supporting information
- All contrast ratios meeting WCAG AA against their respective backgrounds

### Typography System

**Font stack:** Blueprint default system fonts — no custom web fonts.
- `-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif`
- Monospace: `"SF Mono", "Cascadia Code", "Fira Code", Consolas, monospace`

**Monospace usage:**
- Agent streaming output (token-by-token text)
- Tool call displays (file paths, commands, grep results)
- Code blocks in rendered markdown artifacts
- Git diff views
- Cost/token counts in activity status line

**Known inconsistency:** Monospace rendering will vary across OS (SF Mono on macOS, Cascadia Code on Windows if installed, Consolas fallback). Acceptable for internal tool — document but don't solve in MVP.

**Type scale:** Blueprint's default scale — no customization needed.
- Headings sized for clear hierarchy in artifact rendering
- Body text sized for comfortable reading of spec documents
- Dense text for dashboard data (workflow lists, stage indicators, cost figures)

### Spacing & Layout Foundation

**Spacing system:** Blueprint's 8px base grid supplemented by Bootstrap's spacing utilities.

**Layout approach:**
- Bootstrap grid (12-column) for page-level layout: sidebars, content areas, split views
- Blueprint spacing for component-level density: padding within panels, gaps between form elements, table cell spacing
- Compact density for data views (dashboard workflow list, audit logs, cost tables)
- Standard density for content views (artifact viewer, gate review page)

**Sidebar-ready grid:** MVP uses single content area (`col-12`), but layout should use Bootstrap grid in a way that anticipates a future sidebar. Structure pages so adding a `col-3` sidebar + `col-9` content split requires no re-layout of existing components.

**Key layout patterns:**
- **Dashboard:** Full-width workflow list with compact rows. Single content area in MVP — sidebar-ready grid for future.
- **Workflow detail:** Stage progression bar (horizontal) at top, main content area below. Content area shows either agent streaming or artifact + gate controls depending on stage state.
- **Gate review:** Artifact rendered at constrained width (~900px centered) for comfortable reading. Gate action buttons (Approve/Reject/Go Back) anchored at the end of artifact content — user reads first, then decides. Prompt input as a persistent bar at the bottom of the viewport — feedback accessible at any point during reading without scrolling to the bottom.
- **Content-width switching:** Artifact viewer component switches between constrained width (~900px for markdown reading) and full-width (for diff views and wide tables). Intentional layout requirement, not a CSS afterthought.

### Accessibility Considerations

- **Contrast:** All text meets WCAG AA contrast ratios against dark backgrounds. Status colors tested for sufficient contrast.
- **Keyboard navigation:** Blueprint components have built-in keyboard support. All gate actions, navigation, and workflow operations accessible via keyboard.
- **Focus indicators:** Blueprint's default focus rings — visible and consistent.
- **Motion sensitivity:** AI-active indicators use motion. Respect `prefers-reduced-motion` media query — fall back to static indicators for users who disable motion.
- **No formal WCAG target for MVP** — best-effort accessibility using Blueprint and Bootstrap's built-in support. No screen reader optimization or ARIA audit in v1.
