import type { Meta, StoryObj } from '@storybook/react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactElement, type ReactNode } from 'react'
import {
  Alert,
  Badge,
  Box,
  Button,
  Code,
  Divider,
  Drawer,
  Group,
  Loader,
  MultiSelect,
  NumberInput,
  Paper,
  ScrollArea,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  Table,
  Tabs,
  Text,
  TextInput,
  Textarea,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import {
  BaseEdge,
  Background,
  Controls,
  EdgeLabelRenderer,
  Handle,
  MarkerType,
  Position,
  ReactFlow,
  getSmoothStepPath,
  type Edge,
  type EdgeProps,
  type EdgeTypes,
  type Node,
  type NodeProps,
  type NodeTypes,
  type ReactFlowInstance,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import {
  TbAdjustments,
  TbAlertCircle,
  TbAlertTriangle,
  TbArrowMerge,
  TbArrowsSplit2,
  TbBinaryTree,
  TbChevronDown,
  TbChevronRight,
  TbCode,
  TbFileText,
  TbGitPullRequest,
  TbHourglass,
  TbMessage2,
  TbMinus,
  TbPackage,
  TbPencil,
  TbPlus,
  TbRobot,
  TbSend,
  TbSparkles,
  TbTerminal2,
  TbTrash,
  TbUserCheck,
  TbWand,
  TbX,
} from 'react-icons/tb'
import {
  MODELS,
  NODE_KIND_META,
  NODE_NOT_RUN_NOTE,
  NODE_STATUS_META,
  RUN618_BASE_MOVES,
  RUN618_EDGES,
  RUN618_ITERATIONS,
  RUN618_NODES,
  RUNS,
  SEVERITIES,
  fmtTokens,
  nodePurpose,
  personName,
  type ModelKey,
  type NodeExec,
  type NodeKind,
  type NodeStatus,
  type WorkflowNodeDef,
} from './mockData'
import { BlastMini, CostText, ModelBadge, SectionLabel, StageChip, fmtCost } from './shared'
import { ArtifactDetail, InputCountBadge, NodeIOTabs } from './ArtifactViews'
import { instanceByKey } from './artifacts'

/**
 * PROPOSAL MOCK — not production code. One review run's workflow graph
 * (`docs/features/012-review-engine/design.md` §3 workflow graph, §3.3 yaml, §5 policy, §10
 * layers, §17 Run detail and Workflow editor, §20 replay evaluation). Nodes are laid out by hand
 * (no elkjs): the main path snakes left→right then right→left, and back edges loop round the
 * left-hand side with their iteration counts. Every step has a human description and a hover
 * tooltip built from its definition (kind, run, typed parameters with their layer, back edges).
 *   A — live run overlay (payments-api#618, iteration 3, reviewer fan-out 3/4). Nodes are grouped into
 *       collapsible sections (Intake, Review, Decide, Branch maintenance; xyflow parent nodes); a
 *       collapsed section is one summary node and edges into it attach to that node. The reviewer
 *       fan-out collapses to "reviewers ×4". A second state shows the same run after approval with a
 *       branch that was already behind at review (base-moved → base-drift → update-branch).
 *   B — editor: the graph is read-only and changes through AI prompts. Proposals land in a
 *       staging area (Current | Staged | Diff, cycle-rule validation, YAML diff, replay estimate).
 *       Typed, layered parameters and descriptions per step are edited in the inspector and
 *       staged the same way.
 *   C — the same run as a horizontal timeline of iterations and rounds instead of a graph
 */

const RUN = RUNS.find((r) => r.number === 618)!

// ---------------------------------------------------------------- layout (hand placed)

const W = 156
const H = 70
const HR = 52
const STEP = 196

const POS: Record<string, { x: number; y: number }> = {
  intake: { x: 0, y: 0 },
  delta: { x: STEP, y: 0 },
  categorise: { x: STEP * 2, y: 0 },
  select: { x: STEP * 3, y: 0 },
  group: { x: STEP * 4, y: 0 },
  packs: { x: STEP * 5, y: 0 },
  review: { x: STEP * 6, y: 0 },
  'reviewer:security': { x: STEP * 7 + 20, y: -88 },
  'reviewer:concurrency': { x: STEP * 7 + 20, y: -28 },
  'reviewer:data': { x: STEP * 7 + 20, y: 32 },
  'reviewer:general': { x: STEP * 7 + 20, y: 92 },
  collate: { x: STEP * 7 + 20, y: 300 },
  verify: { x: STEP * 6, y: 300 },
  publish: { x: STEP * 5, y: 300 },
  route: { x: STEP * 4, y: 300 },
  'needs-human-approval': { x: STEP * 3, y: 300 },
  'human-approve': { x: STEP * 2, y: 222 },
  'bot-approve': { x: STEP * 2, y: 370 },
  'base-moved': { x: STEP, y: 300 },
  'await-merge': { x: 0, y: 300 },
  'human-review': { x: STEP * 5, y: 470 },
  // row 3: what happens while the run is parked or after the base moved under an approval
  'await-fixes': { x: STEP * 4, y: 530 },
  'base-drift': { x: STEP, y: 530 },
  'update-branch': { x: 0, y: 530 },
}

const ROW2 = new Set(['collate', 'verify', 'publish', 'route', 'needs-human-approval', 'human-approve', 'bot-approve', 'base-moved', 'await-merge'])

/** Per-edge handle choice and optional lane (absolute flow coordinates) for hand-routed edges. */
const ROUTE: Record<string, { sh: string; th: string; laneX?: number; laneY?: number }> = {
  e12: { sh: 'b-out', th: 't-in' },
  e13: { sh: 'b-out', th: 't-in' },
  e19: { sh: 'l-out', th: 'r-in', laneY: 470 },
  // the square await-merge ← base-moved → base-drift → update-branch → await-merge, with
  // await-merge[base-moved] → base-drift as its diagonal through the gap between the columns;
  // the two labelled sides run just outside the square so their labels clear the nodes
  e25: { sh: 't-out', th: 't-in', laneY: 268 },
  e26: { sh: 'b-out', th: 't-in' },
  e20: { sh: 't-out', th: 'b-in' },
  e21: { sh: 'l-out', th: 'r-in' },
  e22: { sh: 'r-c', th: 'l-a', laneX: STEP - 22 },
  e23: { sh: 'l-out', th: 'b-in', laneY: 626 },
  e24: { sh: 'r-c', th: 'b-in', laneY: 640 },
  // back edges into intake, nested so they never cross: the lower a loop starts, the further out
  // it runs and the higher it enters intake
  b2: { sh: 't-out', th: 'l-88', laneX: -30, laneY: 170 },
  b4: { sh: 'l-out', th: 'l-70', laneX: -60 },
  b5: { sh: 'b-out', th: 'l', laneX: -90, laneY: 660 },
  b1: { sh: 'r', th: 'l-30', laneX: -120, laneY: 680 },
  b3: { sh: 'b-out', th: 'l-12', laneX: -150, laneY: 720 },
  // edges that AI proposals add in story B
  x1: { sh: 't-out', th: 'b-in', laneY: 200 },
  p3a: { sh: 't-out', th: 'b-in' },
  p3b: { sh: 'l-out', th: 'b-in', laneY: 205 },
  p3c: { sh: 'l-out-c', th: 't-in', laneY: 252 },
  p4a: { sh: 't-out', th: 'r-in' },
  p4b: { sh: 'l-out', th: 't-in' },
}

function routeFor(id: string, from: string, to: string) {
  if (ROUTE[id]) return ROUTE[id]
  if (from.startsWith('reviewer:') && to === 'collate') return { sh: 'r', th: 'r-in' }
  if (ROW2.has(from) && ROW2.has(to)) return { sh: 'l-out', th: 'r-in' }
  return { sh: 'r', th: 'l' }
}

// ---------------------------------------------------------------- step definitions: typed, layered parameters

type PV = string | number | boolean | string[]
type Layer = 'common' | 'team' | 'repo'
const LAYER_ORDER: Layer[] = ['repo', 'team', 'common']
const LAYER_META: Record<Layer, { label: string; color: string; hint: string }> = {
  common: { label: 'common@v14', color: 'gray', hint: 'the common prompt layer, lumen/antiphon-review-common @ v14' },
  team: { label: 'payments-core', color: 'grape', hint: 'the payments-core team layer' },
  repo: { label: 'repo', color: 'active', hint: "payments-api's own .antiphon/review/" },
}

interface Field {
  key: string
  label: string
  type: 'select' | 'multi' | 'number' | 'text' | 'bool'
  options?: string[]
  min?: number
  max?: number
  step?: number
  help?: string
  /** Where the value lives; default workflow.yaml. */
  file?: 'workflow.yaml' | 'policy.yaml' | 'groups'
  yamlKey?: string
  values: Partial<Record<Layer, PV>>
}

const fld = (key: string, label: string, type: Field['type'], values: Field['values'], extra: Partial<Field> = {}): Field => ({ key, label, type, values, ...extra })
const P01 = { min: 0, max: 1, step: 0.05 }
const POLICY = { file: 'policy.yaml' as const }

function gateFields(values: { queue?: PV; decisions?: PV; timeout?: PV; onTimeout?: PV }, timeoutTeam?: string): Field[] {
  return [
    fld('queue', 'Queue', 'select', values.queue === undefined ? {} : { common: values.queue }, { options: ['owning-team', 'repo-maintainers', 'security-guild'] }),
    fld('decisions', 'Decisions', 'multi', values.decisions === undefined ? {} : { common: values.decisions }, { options: ['approve', 'another-round', 'close', 're-review', 'drop', 'keep'] }),
    fld('timeout', 'Timeout (working time)', 'text', values.timeout === undefined ? {} : { common: values.timeout, ...(timeoutTeam ? { team: timeoutTeam } : {}) }),
    fld('onTimeout', 'On timeout', 'select', values.onTimeout === undefined ? {} : { common: values.onTimeout }, { options: ['reassign', 'escalate', 'close', 'drop'] }),
  ]
}

const SOURCES = ['impact-index', 'callers', 'co-change', 'di', 'check-hint', 'related.yaml', 'model']

/** Gate automatic passes (§3.2): a gate breaks a cycle only while its autoRetries is a whole number from 0 to this. */
const AUTO_RETRIES_MAX = 3

const NODE_FIELDS: Record<string, Field[]> = {
  intake: [
    fld('largePrLines', 'Large PR threshold (changed lines)', 'number', { common: 800, team: 600 }, { min: 100, step: 50 }),
    fld('generatedGlobs', 'Generated-file globs (skipped)', 'text', { common: '**/*.g.cs, **/*Snapshot.cs' }),
  ],
  delta: [
    fld('interdiff', 'Interdiff after force-push', 'bool', { common: true }),
    fld('reanchorWindow', 'Re-anchor search window (lines)', 'number', { common: 40 }, { min: 0, step: 10 }),
  ],
  categorise: [
    fld('chain', 'Decision chain', 'select', { common: 'decisions-default' }, { options: ['decisions-default', 'decisions-jev-only', 'decisions-seats'] }),
    fld('escalateBelow', 'Escalate a question below confidence', 'number', { common: 0.6, team: 0.65 }, { ...P01, help: 'Only that question moves up the chain (Jev → luna → haiku → grok).' }),
    fld('splitByArea', 'Split large PRs by area', 'bool', { common: true }),
  ],
  select: [
    fld('candidateCap', 'Candidate cap after prefilter', 'number', { common: 200, repo: 160 }, { min: 10, step: 10 }),
    fld('questionsPerCall', 'Jev questions per call', 'number', { common: 128 }, { min: 1, max: 128, help: 'The LiteLLM route allows at most 128.' }),
    fld('similarityFloor', 'Embedding similarity floor', 'number', { common: 0.32 }, { min: 0, max: 1, step: 0.01 }),
  ],
  group: [
    fld('suffixBudget', 'Group suffix token budget', 'number', { common: 8000 }, { step: 500 }),
    fld('bypassOwnGroup', 'Bypass categories get their own group', 'bool', { common: true }),
    fld('conditionalGroups', 'Conditional groups', 'text', { common: '' }),
  ],
  packs: [
    fld('prefixBudget', 'Shared prefix token budget', 'number', { common: 48000, team: 60000 }, { step: 1000 }),
    fld('wholeFileLines', 'Whole-file threshold (lines)', 'number', { common: 600 }, { step: 50 }),
    fld('sources', 'Related-file sources', 'multi', { common: SOURCES, repo: SOURCES.filter((s) => s !== 'model') }, { options: SOURCES }),
  ],
  review: [
    fld('policy', 'Failure policy', 'select', { common: 'all' }, { options: ['all', 'quorum:2', 'quorum:3', 'best-effort'] }),
    fld('childTimeout', 'Per-reviewer timeout', 'text', { common: '20m' }),
  ],
  collate: [
    fld('dedupeSimilarity', 'Dedupe similarity threshold', 'number', { common: 0.82 }, { min: 0, max: 1, step: 0.01 }),
    fld('dedupeAgainstHuman', 'Dedupe against human comments', 'bool', { common: true }),
  ],
  verify: [
    fld('chain', 'Completion chain', 'select', { common: 'completions-cheap' }, { options: ['completions-cheap', 'completions-strong'] }),
    fld('dropAt', 'Drop when disproved with confidence ≥', 'number', { common: 0.7 }, P01),
  ],
  publish: [
    fld('summaryMode', 'Summary comment', 'select', { common: 'edit-in-place' }, { options: ['edit-in-place', 'new-each-round'] }),
    fld('statusThrottleS', 'Status update throttle (s)', 'number', { common: 30 }, { min: 5, step: 5 }),
  ],
  route: [
    fld('humanGateAfterBotOnlyRounds', 'Human gate after bot-only rounds', 'number', { common: 3 }, { min: 1, max: 10, ...POLICY, yamlKey: 'rounds.humanGateAfterBotOnlyRounds' }),
  ],
  'await-fixes': [
    fld('on', 'Wake on', 'multi', { common: ['push', 'recheck', 'close', 'base-moved'] }, { options: ['push', 'recheck', 'close', 'base-moved', 'comment', 'timer'] }),
    fld('timeout', 'Timeout', 'text', { common: '14d' }),
  ],
  'base-drift': [
    fld('prefilterSources', 'Prefilter: overlap counts when base touches', 'multi', { common: ['pr-files', 'impact-index', 'callers', 'contract-globs'] }, {
      options: ['pr-files', 'impact-index', 'callers', 'co-change', 'contract-globs'],
      help: 'No overlap → unaffected without a model call.',
    }),
    fld('contractGlobs', 'Contract and schema globs (always overlap)', 'text', { common: '**/Contracts/**, **/*.proto, db/migrations/**', repo: '**/Contracts/**, db/migrations/**, openapi.yaml' }),
    fld('chain', 'Decision chain', 'select', { common: 'decisions-default' }, { options: ['decisions-default', 'decisions-jev-only', 'decisions-seats'] }),
    fld('escalateBelow', 'Escalate below confidence', 'number', { common: 0.6 }, { ...P01, help: 'Below this, base_drift moves up the chain (Jev → luna → haiku → grok).' }),
    fld('affectedAt', 'Affected when p(yes) ≥', 'number', { common: 0.35 }, { ...P01, help: 'Deliberately low: a false re-review is cheaper than a missed semantic conflict.' }),
    fld('steerTemplate', 'Steer on re-review', 'text', { common: 'Base moved {old}..{new}: review as if rebased onto the new base; check interactions with {commits}.' }),
  ],
  'await-merge': [
    fld('on', 'Wake on', 'multi', { common: ['merge', 'close', 'push', 'base-moved'] }, { options: ['merge', 'close', 'push', 'base-moved', 'timer'] }),
  ],
  'needs-human-approval': [fld('sticky', 'A yes is sticky for the rest of the PR', 'bool', { common: true }, { ...POLICY, yamlKey: 'approval.sticky', help: 'Thresholds are in the Workflow parameters tab.' })],
  'bot-approve': [
    fld('requires', 'Auto-approve requires', 'multi', { common: ['no-open-findings', 'all-checks-accounted', 'head-is-reviewed', 'ci-green', 'no-open-human-threads'] }, {
      options: ['no-open-findings', 'all-checks-accounted', 'head-is-reviewed', 'ci-green', 'no-open-human-threads'],
      ...POLICY,
      yamlKey: 'approval.autoApproveRequires',
    }),
  ],
  'human-approve': gateFields({ queue: 'owning-team', decisions: ['approve', 'another-round', 'close'], timeout: '3d', onTimeout: 'reassign' }, '2d'),
  'human-review': gateFields({ queue: 'owning-team', decisions: ['approve', 'another-round', 'close'], timeout: '3d', onTimeout: 'reassign' }, '2d'),
  'base-moved': [
    fld('fetchBase', 'Fetch the base ref before comparing', 'bool', { common: true }, {
      help: 'A late webhook cannot hide a move. Code only. Compares the merge-base (keepUpToDate on) or the reviewed base (keepUpToDate off) with the fetched tip: unchanged → await-merge, moved → base-drift.',
    }),
  ],
  'update-branch': [
    fld('afterApproval', 'After approval', 'select', { common: 'leave-to-author' }, { options: ['leave-to-author', 'enable-auto-merge', 'merge'], ...POLICY, yamlKey: 'afterApproval.mode' }),
    fld('keepUpToDate', 'Keep the branch up to date', 'bool', { common: true }, {
      ...POLICY,
      yamlKey: 'afterApproval.keepUpToDate',
      help: 'On: base-moved compares the branch’s merge-base with the base tip, so a branch that was already behind at review is caught and brought up to date too. Off: base-moved compares the reviewed base with the tip (drift only), and an unaffected move is not merged in: the step only checks for conflicts and resolves them; a clean merge records not-needed.',
    }),
    fld('updateMethod', 'Keep up to date by', 'select', { common: 'merge', team: 'rebase' }, { options: ['rebase', 'merge'] }),
    fld('resolveConflicts', 'Resolve conflicts', 'select', { common: 'ai' }, { options: ['ai', 'human', 'off'] }),
  ],
  // nodes that only exist once an AI proposal adds them: values arrive as staged items
  notify: [
    fld('when', 'Only when', 'text', {}),
    fld('channels', 'Channels', 'multi', {}, { options: ['attention-feed', 'consumer-register', 'telegram', 'email'] }),
  ],
  'disputed-findings': [
    ...gateFields({}),
    fld('autoRetries', 'Automatic passes before the gate opens', 'number', {}, {
      min: 0,
      max: AUTO_RETRIES_MAX,
      help: `Per finding, 0–${AUTO_RETRIES_MAX}. The first N disputes take the automatic decision without a human; the next one opens the gate. Must be set and bounded.`,
    }),
    fld('autoDecision', 'Automatic decision', 'select', {}, { options: ['re-review', 'drop', 'keep'] }),
  ],
}

const GROUPS = ['security', 'concurrency', 'data', 'general', 'migration']
const REVIEWER_FIELDS: Record<string, Field[]> = Object.fromEntries(
  GROUPS.map((g) => [
    g,
    [
      fld('tier', 'Tier / model', 'select', g === 'migration' ? { repo: 'opus' } : g === 'security' || g === 'concurrency' ? { common: 'sonnet', team: 'opus' } : { common: 'sonnet' }, {
        options: ['opus', 'sonnet', 'gpt-5.6-luna', 'haiku'],
        file: 'groups',
      }),
      fld('labels', 'Runner labels', 'multi', g === 'data' ? { common: ['review'], repo: ['review', 'postgres'] } : { common: ['review'] }, { options: ['review', 'dotnet9', 'node22', 'postgres'], file: 'groups' }),
      fld('parseRepairs', 'Parse repair attempts', 'number', { common: 1 }, { min: 0, max: 3, file: 'groups' }),
    ],
  ]),
)

const EDGE_FIELDS: Record<string, Field[]> = {
  b1: [fld('maxIterations', 'maxIterations', 'number', { common: 20 }, { min: 1, max: 50, help: 'Over the limit routes to the configured gate.' })],
  b2: [fld('maxIterations', 'maxIterations', 'number', { common: 5 }, { min: 1, max: 50 })],
  b3: [fld('maxIterations', 'maxIterations', 'number', { common: 5 }, { min: 1, max: 50 })],
  b4: [fld('maxIterations', 'maxIterations', 'number', { common: 20 }, { min: 1, max: 50 })],
  b5: [fld('maxIterations', 'maxIterations (re-reviews after base drift)', 'number', { common: 10 }, { min: 1, max: 50, help: 'Over the limit opens the human-review gate instead of another automatic re-review.' })],
}

const EDGE_DESC: Record<string, string> = {
  b1: 'Each push or /recheck while findings are open starts a new iteration; unchanged work is reused by inputs hash.',
  b2: 'A human sent the PR back for another round, optionally with a steer prompt that goes into every pack.',
  b3: 'After the bot-round limit a human can still ask for another round, with a steer.',
  b4: 'A push to an approved PR revokes the approval and starts a new review iteration.',
  b5: 'The base moved in a way that could affect this PR: delta re-review with an automatic steer to review as if rebased onto the new base.',
  e13: 'Findings above the bar were posted; park until the author pushes fixes.',
  e17: 'The bot approved: check whether the base moved since the reviewed base.',
  e18: 'A human approved: check whether the base moved while the gate was open.',
  e19: 'A human approved after the bot-round limit: check whether the base moved.',
  e25: 'The base has not moved since the approval: hold the approved PR as it is, with no update and no model call.',
  e26: 'The base moved since the approval: base-drift decides whether the move affects the PR.',
  e21: 'The base branch moved while waiting for fixes.',
  e22: 'The base branch moved under an approved PR.',
  e23: 'Base moved without affecting the approved PR: update the branch (per afterApproval) and carry the approval forward if patch ids are unchanged.',
  e24: 'Base moved without affecting the PR: keep waiting for fixes.',
  e14: 'Clean round: decide whether a human must approve.',
}

const WF_ROUNDS: { round: string; sev: Field; conf: Field; extra?: Field }[] = [
  {
    round: '1',
    sev: fld('rounds.1.minSeverity', 'Round 1 min severity', 'select', { common: 'nit' }, { options: SEVERITIES, ...POLICY }),
    conf: fld('rounds.1.minConfidence', 'Round 1 min confidence', 'number', { common: 0.5 }, { ...P01, ...POLICY }),
    extra: fld('rounds.1.maxNits', 'Round 1 max nits (summary only)', 'number', { common: 5, repo: 3 }, { min: 0, max: 20, ...POLICY }),
  },
  {
    round: '2',
    sev: fld('rounds.2.minSeverity', 'Round 2 min severity', 'select', { common: 'minor' }, { options: SEVERITIES, ...POLICY }),
    conf: fld('rounds.2.minConfidence', 'Round 2 min confidence', 'number', { common: 0.65 }, { ...P01, ...POLICY }),
  },
  {
    round: '3+',
    sev: fld('rounds.3.minSeverity', 'Round 3+ min severity', 'select', { common: 'major' }, { options: SEVERITIES, ...POLICY }),
    conf: fld('rounds.3.minConfidence', 'Round 3+ min confidence', 'number', { common: 0.8, team: 0.75 }, { ...P01, ...POLICY }),
  },
]

const CATEGORIES = ['migration', 'auth', 'security', 'public-api', 'infra', 'session-runtime', 'ledger']
const WF_SECTIONS: { title: string; fields: Field[] }[] = [
  {
    title: 'Bypass categories',
    fields: [fld('rounds.bypassCategories', 'Post in any round; the adjudicator cannot concede alone', 'multi', { common: ['security', 'data-loss'], team: ['security', 'data-loss', 'pci'] }, { options: ['security', 'data-loss', 'pci', 'privacy'], ...POLICY })],
  },
  {
    title: 'Approval: a human is required when',
    fields: [
      fld('approval.when.external_contract', 'external_contract ≥', 'number', { common: 0.6 }, { ...P01, ...POLICY }),
      fld('approval.when.reversal_after_months', 'reversal_after_months ≥', 'number', { common: 4 }, { min: 1, max: 5, ...POLICY }),
      fld('approval.when.categories', 'Categories', 'multi', { common: CATEGORIES.slice(0, 6), team: CATEGORIES }, { options: CATEGORIES, ...POLICY }),
      fld('approval.when.size', 'PR size', 'select', { common: 'large' }, { options: ['medium', 'large', 'never'], ...POLICY }),
      fld('approval.when.firstTimeAuthor', 'First-time author', 'bool', { common: true }, POLICY),
      fld('approval.when.concededDisputes', 'Conceded disputes ≥', 'number', { common: 2 }, { min: 1, max: 10, ...POLICY }),
      fld('approval.when.unverifiedHighSeverity', 'Unverified high-severity check', 'bool', { common: true }, POLICY),
    ],
  },
  { title: 'Dismissals', fields: [fld('dismiss.blocker', 'Who may dismiss a blocker', 'select', { common: 'author' }, { options: ['author', 'other-member', 'maintainer'], ...POLICY })] },
]
const WF_FIELDS: Field[] = [...WF_ROUNDS.flatMap((r) => [r.sev, r.conf, ...(r.extra ? [r.extra] : [])]), ...WF_SECTIONS.flatMap((s) => s.fields)]

function fieldsFor(target: string): Field[] {
  if (target === 'workflow') return WF_FIELDS
  if (target.startsWith('reviewer:')) return REVIEWER_FIELDS[target.slice('reviewer:'.length)] ?? []
  return NODE_FIELDS[target] ?? EDGE_FIELDS[target] ?? []
}

function baseValue(f: Field): { v?: PV; layer?: Layer } {
  for (const l of LAYER_ORDER) if (f.values[l] !== undefined) return { v: f.values[l], layer: l }
  return {}
}

function same(a?: PV, b?: PV): boolean {
  return JSON.stringify(a) === JSON.stringify(b)
}

function fmtPV(v?: PV): string {
  if (v === undefined) return '—'
  if (Array.isArray(v)) return v.length ? v.join(', ') : '[]'
  if (typeof v === 'boolean') return v ? 'on' : 'off'
  if (v === '') return '""'
  return String(v)
}

function yv(v?: PV): string {
  if (v === undefined) return '~'
  if (Array.isArray(v)) return `[${v.join(', ')}]`
  if (typeof v === 'string') return v === '' || /[:#≥\s,"'[\]{}]/.test(v) ? JSON.stringify(v) : v
  return String(v)
}

function fmtDur(e: NodeExec): string {
  if (e.status === 'running') return '4m12s…'
  if (e.durationS > 3600) return `${(e.durationS / 3600).toFixed(1)}h`
  if (e.durationS >= 60) return `${Math.round(e.durationS / 60)}m`
  return `${Math.round(e.durationS * 10) / 10}s`
}

// ---------------------------------------------------------------- staged graph model

interface EdgeSpec {
  id: string
  from: string
  to: string
  label?: string
  back?: { taken: number; max: number }
  taken?: boolean
  tone?: Tone
  description?: string
}

interface NewNode {
  id: string
  kind: NodeKind
  run?: string
  detail?: string
  pos: { x: number; y: number }
  description?: string
}

type ItemSource = 'ai' | 'you'
type ChangeItem =
  | { id: string; kind: 'add-node'; node: NewNode; why: string }
  | { id: string; kind: 'add-edge'; edge: EdgeSpec; why: string }
  | { id: string; kind: 'remove-edge'; edgeId: string; why: string }
  | { id: string; kind: 'param'; target: string; key: string; label: string; from?: PV; to: PV; source: ItemSource }
  | { id: string; kind: 'description'; target: string; from: string; to: string; source: ItemSource }

interface GNode {
  def: WorkflowNodeDef
  pos: { x: number; y: number }
  isNew: boolean
  description?: string
}
interface GEdge {
  spec: EdgeSpec
  isNew: boolean
  removed: boolean
}
interface GraphState {
  nodes: GNode[]
  edges: GEdge[]
}

function stageGraph(included: ChangeItem[]): GraphState {
  const removed = new Set(included.flatMap((i) => (i.kind === 'remove-edge' ? [i.edgeId] : [])))
  const maxOverride = new Map(included.flatMap((i) => (i.kind === 'param' && i.key === 'maxIterations' && typeof i.to === 'number' ? [[i.target, i.to] as const] : [])))
  const nodes: GNode[] = RUN618_NODES.map((def) => ({ def, pos: POS[def.id], isNew: false }))
  for (const i of included)
    if (i.kind === 'add-node')
      nodes.push({ def: { id: i.node.id, kind: i.node.kind, run: i.node.run, detail: i.node.detail, status: 'pending', execs: [] }, pos: i.node.pos, isNew: true, description: i.node.description })
  const edges: GEdge[] = RUN618_EDGES.map((spec) => ({ spec, isNew: false, removed: removed.has(spec.id) }))
  for (const i of included) if (i.kind === 'add-edge') edges.push({ spec: i.edge, isNew: true, removed: false })
  return {
    nodes,
    edges: edges.map((e) => {
      const m = maxOverride.get(e.spec.id)
      return m !== undefined && e.spec.back ? { ...e, spec: { ...e.spec, back: { ...e.spec.back, max: m } } } : e
    }),
  }
}

const BASE_GRAPH = stageGraph([])

function paramMap(included: ChangeItem[]): Map<string, PV> {
  const m = new Map<string, PV>()
  for (const i of included) if (i.kind === 'param') m.set(`${i.target}::${i.key}`, i.to)
  return m
}

function descMap(included: ChangeItem[]): Map<string, string> {
  const m = new Map<string, string>()
  for (const i of included) if (i.kind === 'description') m.set(i.target, i.to)
  return m
}

function nodeBaseDesc(g: GraphState, id: string): string {
  const n = g.nodes.find((x) => x.def.id === id)
  return n?.isNew ? (n.description ?? '') : nodePurpose(id)
}

function edgeBaseDesc(spec: EdgeSpec): string {
  return EDGE_DESC[spec.id] ?? spec.description ?? ''
}

// ---------------------------------------------------------------- step tooltip (built from the definition)

interface StepView {
  id: string
  kind: NodeKind
  run?: string
  detail?: string
  description: string
  params: { key: string; value?: PV; layer?: Layer | 'staged' }[]
  backEdges: { from: string; to: string; label?: string; max: number }[]
  isNew: boolean
}
type StepResolver = (id: string) => StepView | undefined

function makeStep(id: string, g: GraphState, sp: Map<string, PV>, sd: Map<string, string>): StepView | undefined {
  const n = g.nodes.find((x) => x.def.id === id)
  if (!n) return undefined
  const params = fieldsFor(id).map((f) => {
    const b = baseValue(f)
    const k = `${id}::${f.key}`
    return sp.has(k) && !same(sp.get(k), b.v) ? { key: f.yamlKey ?? f.key, value: sp.get(k), layer: 'staged' as const } : { key: f.yamlKey ?? f.key, value: b.v, layer: b.layer }
  })
  const backEdges = g.edges.filter((e) => !e.removed && e.spec.from === id && e.spec.back).map((e) => ({ from: e.spec.from, to: e.spec.to, label: e.spec.label, max: e.spec.back!.max }))
  return { id, kind: n.def.kind, run: n.def.run, detail: n.def.detail, description: sd.get(id) ?? nodeBaseDesc(g, id), params, backEdges, isNew: n.isNew }
}

const EMPTY_PARAMS = new Map<string, PV>()
const EMPTY_DESC = new Map<string, string>()
const baseResolve: StepResolver = (id) => makeStep(id, BASE_GRAPH, EMPTY_PARAMS, EMPTY_DESC)
const StepCtx = createContext<StepResolver>(baseResolve)

function StepTipBody({ s, exec }: { s: StepView; exec?: NodeExec | null }) {
  const head = `${s.id.startsWith('reviewer:') ? 'reviewer' : s.id}: { kind: ${s.kind}${s.run && !['fan-out', 'gate', 'wait'].includes(s.kind) ? `, run: ${s.run}` : ''}${s.detail && s.id.startsWith('reviewer:') ? `, group: ${s.detail}` : ''} }`
  return (
    <Stack gap={4}>
      <Group gap={6} wrap="nowrap">
        <Badge size="xs" color={NODE_KIND_META[s.kind].color} variant="light">
          {s.kind}
        </Badge>
        {s.isNew && (
          <Badge size="xs" color="success" variant="filled">
            staged
          </Badge>
        )}
      </Group>
      <Code block fz={10} p={4} style={{ whiteSpace: 'pre-wrap' }}>
        {head}
      </Code>
      <Text size="xs" c={s.description ? 'gray.3' : 'warning.4'}>
        {s.description || 'No description.'}
      </Text>
      {s.params.length > 0 && (
        <Box>
          {s.params.map((p) => (
            <Group key={p.key} gap={6} justify="space-between" wrap="nowrap">
              <Text fz={10} ff="monospace" c={p.layer === 'staged' ? 'warning.3' : 'gray.4'} truncate>
                {p.key}: {yv(p.value)}
              </Text>
              {p.layer && (
                <Text fz={9} c={p.layer === 'staged' ? 'warning.4' : `${LAYER_META[p.layer].color}.4`} style={{ whiteSpace: 'nowrap' }}>
                  {p.layer === 'staged' ? 'staged' : LAYER_META[p.layer].label}
                </Text>
              )}
            </Group>
          ))}
        </Box>
      )}
      {s.backEdges.map((b) => (
        <Text key={`${b.from}-${b.to}`} fz={10} ff="monospace" c="pink.3">
          ↺ {b.from}
          {b.label ? `[${b.label}]` : ''} → {b.to} · maxIterations: {b.max}
        </Text>
      ))}
      {exec && (
        <Box pt={4} style={{ borderTop: '1px solid var(--mantine-color-dark-4)' }}>
          <Text fz={10} c="dimmed" tt="uppercase" fw={700}>
            This run
          </Text>
          <Text fz={11}>
            it{exec.iteration} · <span style={{ color: STATUS_HEX[exec.status] }}>{NODE_STATUS_META[exec.status].label}</span> · {fmtDur(exec)}
            {exec.model ? ` · ${MODELS[exec.model].name}` : ''}
            {exec.tokensIn ? ` · ${fmtTokens(exec.tokensIn)} in` : ''}
            {exec.tokensOut ? ` / ${fmtTokens(exec.tokensOut)} out` : ''}
          </Text>
          {exec.output && (
            <Text fz={11} c="gray.4">
              {exec.output}
            </Text>
          )}
          {exec.note && (
            <Text fz={11} c="warning.4">
              {exec.note}
            </Text>
          )}
        </Box>
      )}
    </Stack>
  )
}

/** Hover card for one workflow step: its yaml head, description, parameters (with layer), back edges and, in live mode, this run's last execution. */
function StepTip({ id, exec, children }: { id: string; exec?: NodeExec | null; children: ReactElement }) {
  const resolve = useContext(StepCtx)
  const s = resolve(id)
  if (!s) return children
  return (
    <Tooltip
      label={<StepTipBody s={s} exec={exec} />}
      openDelay={300}
      withArrow
      multiline
      w={340}
      color="dark"
      styles={{ tooltip: { border: '1px solid var(--mantine-color-dark-4)', padding: 8 } }}
    >
      {children}
    </Tooltip>
  )
}

function StepName({ id }: { id: string }) {
  return (
    <StepTip id={id}>
      <Text span ff="monospace" size="xs" style={{ textDecoration: 'underline dotted', cursor: 'help' }}>
        {id}
      </Text>
    </StepTip>
  )
}

// ---------------------------------------------------------------- node view

const KIND_ICON: Record<NodeKind, ReactNode> = {
  code: <TbCode size={13} />,
  decision: <TbBinaryTree size={13} />,
  completion: <TbMessage2 size={13} />,
  agent: <TbRobot size={13} />,
  'fan-out': <TbArrowsSplit2 size={13} />,
  join: <TbArrowMerge size={13} />,
  gate: <TbUserCheck size={13} />,
  wait: <TbHourglass size={13} />,
}

const STATUS_HEX: Record<NodeStatus, string> = {
  done: '#33c96b',
  reused: '#20c997',
  running: '#3b7bf5',
  waiting: '#ffa92e',
  pending: '#5c5f66',
  skipped: '#373a40',
  failed: '#e93636',
}

const DIFF_HEX = { added: '#33c96b', removed: '#e93636', changed: '#ffa92e' }

type FlowNodeData = {
  def: WorkflowNodeDef
  mode: 'live' | 'edit'
  diff?: 'added' | 'removed'
  changed?: boolean
  description?: string
}
type FlowNode = Node<FlowNodeData, 'run'>

const hidden = { opacity: 0, width: 6, height: 6, minWidth: 0, minHeight: 0, border: 0 }

/** Every handle a hand-routed edge can use. Section and fan-out summary nodes carry the same set. */
function RunHandles() {
  return (
    <>
      <Handle type="target" position={Position.Left} id="l" style={hidden} />
      <Handle type="target" position={Position.Left} id="l-a" style={{ ...hidden, top: '25%' }} />
      <Handle type="target" position={Position.Left} id="l-c" style={{ ...hidden, top: '75%' }} />
      <Handle type="target" position={Position.Left} id="l-12" style={{ ...hidden, top: '12.5%' }} />
      <Handle type="target" position={Position.Left} id="l-30" style={{ ...hidden, top: '30%' }} />
      <Handle type="target" position={Position.Left} id="l-70" style={{ ...hidden, top: '70%' }} />
      <Handle type="target" position={Position.Left} id="l-88" style={{ ...hidden, top: '87.5%' }} />
      <Handle type="source" position={Position.Right} id="r-c" style={{ ...hidden, top: '75%' }} />
      <Handle type="source" position={Position.Right} id="r" style={hidden} />
      <Handle type="source" position={Position.Left} id="l-out" style={hidden} />
      <Handle type="source" position={Position.Left} id="l-out-c" style={{ ...hidden, top: '75%' }} />
      <Handle type="target" position={Position.Right} id="r-in" style={hidden} />
      <Handle type="source" position={Position.Bottom} id="b-out" style={hidden} />
      <Handle type="target" position={Position.Bottom} id="b-in" style={hidden} />
      <Handle type="target" position={Position.Top} id="t-in" style={hidden} />
      <Handle type="source" position={Position.Top} id="t-out" style={hidden} />
    </>
  )
}

function RunNodeView({ data, selected }: NodeProps<FlowNode>) {
  const { def, mode } = data
  const edit = mode === 'edit'
  const reviewer = def.id.startsWith('reviewer:')
  const st = edit ? 'pending' : def.status
  const last = def.execs[def.execs.length - 1]
  const lastCurrent = def.execs.find((e) => e.iteration === 3)
  const kindColor = NODE_KIND_META[def.kind].color
  const border = edit
    ? data.diff === 'added'
      ? DIFF_HEX.added
      : data.diff === 'removed'
        ? DIFF_HEX.removed
        : `var(--mantine-color-${kindColor}-7)`
    : STATUS_HEX[st]
  const dashed = edit ? !!data.diff : st === 'skipped' || st === 'pending'
  // per side, not `border` + `borderLeft`: React warns when a shorthand and its longhand both change on rerender
  const side = `${selected ? 2 : 1.5}px ${dashed ? 'dashed' : 'solid'} ${selected ? '#fff' : border}`
  return (
    <StepTip id={def.id} exec={edit ? undefined : (last ?? null)}>
      <Box
        w={W}
        h={reviewer ? HR : H}
        px={8}
        py={5}
        style={{
          background: st === 'skipped' && !edit ? 'transparent' : 'var(--mantine-color-dark-6)',
          borderTop: side,
          borderRight: side,
          borderBottom: side,
          borderLeft: `4px solid ${edit ? (data.diff ? border : `var(--mantine-color-${kindColor}-6)`) : border}`,
          borderRadius: def.kind === 'gate' || def.kind === 'wait' ? 14 : 6,
          opacity: !edit && st === 'skipped' ? 0.45 : 1,
          animation: !edit && st === 'running' ? 'rePulse 1.6s infinite' : undefined,
          overflow: 'hidden',
          cursor: 'pointer',
        }}
      >
        <RunHandles />
        <Group gap={4} wrap="nowrap" justify="space-between">
          <Group gap={4} wrap="nowrap" style={{ minWidth: 0 }}>
            <Box c={`${kindColor}.4`} style={{ display: 'flex' }}>
              {KIND_ICON[def.kind]}
            </Box>
            <Text size="12px" fw={700} truncate>
              {reviewer ? def.detail : def.id}
            </Text>
          </Group>
          {!edit && (
            <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
              <InputCountBadge nodeId={def.id} />
              {def.execs.length > 1 && (
                <Badge size="xs" variant="outline" color="gray" px={4}>
                  ×{def.execs.length}
                </Badge>
              )}
            </Group>
          )}
          {edit && (
            <Group gap={4} wrap="nowrap">
              <InputCountBadge nodeId={def.id} />
              {data.changed && <Box w={8} h={8} style={{ borderRadius: 4, background: DIFF_HEX.changed }} />}
              {data.diff === 'added' ? (
                <Badge size="xs" color="success" variant="filled" px={4}>
                  + new
                </Badge>
              ) : (
                <Text size="9px" c="dimmed" tt="uppercase">
                  {def.kind}
                </Text>
              )}
            </Group>
          )}
        </Group>
        {!reviewer && (
          <Text size="10px" c="dimmed" truncate>
            {def.detail ?? def.run}
          </Text>
        )}
        {edit && !data.description && (
          <Text size="10px" c="warning.4" truncate>
            no description
          </Text>
        )}
        {!edit && (
          <Group gap={4} wrap="nowrap" mt={2}>
            <Text size="10px" fw={600} c={st === 'pending' ? 'dimmed' : undefined} style={{ color: st === 'pending' ? undefined : STATUS_HEX[st] }}>
              {NODE_STATUS_META[st].label}
            </Text>
            {lastCurrent?.model && (
              <Text size="10px" ff="monospace" c={`${MODELS[lastCurrent.model].color}.4`} truncate>
                {MODELS[lastCurrent.model].short}
              </Text>
            )}
            {lastCurrent?.tokensIn !== undefined && (
              <Text size="10px" ff="monospace" c="dimmed">
                {fmtTokens(lastCurrent.tokensIn)}
              </Text>
            )}
            {!lastCurrent && last && st === 'pending' && (
              <Text size="10px" c="dimmed">
                it{last.iteration}: {last.output?.split(' ')[0] ?? 'done'}
              </Text>
            )}
          </Group>
        )}
      </Box>
    </StepTip>
  )
}

// ---------------------------------------------------------------- edge view

type Tone = 'forward' | 'back-wait' | 'back-gate' | 'invalid'
type FlowEdgeData = {
  tone: Tone
  label?: string
  laneX?: number
  laneY?: number
  back?: { taken: number; max: number }
  taken?: boolean
  diff?: 'added' | 'removed'
  description?: string
  /** Extra label text, e.g. a gate's automatic passes: "auto ×1, then human". */
  note?: string
}
type FlowEdge = Edge<FlowEdgeData, 'lane'>

const TONE_HEX: Record<Tone, string> = { forward: '#909296', 'back-wait': '#ffa92e', 'back-gate': '#f06595', invalid: '#e93636' }

type Pt = { x: number; y: number }

function dirOf(p: Position): Pt {
  if (p === Position.Left) return { x: -1, y: 0 }
  if (p === Position.Right) return { x: 1, y: 0 }
  if (p === Position.Top) return { x: 0, y: -1 }
  return { x: 0, y: 1 }
}

/** Orthogonal path through hand-picked lanes, with rounded corners. */
function lanePath(s: Pt, sp: Position, t: Pt, tp: Position, laneX?: number, laneY?: number): { path: string; lx: number; ly: number } {
  const STUB = 18
  const sd = dirOf(sp)
  const td = dirOf(tp)
  const s1 = { x: s.x + sd.x * STUB, y: s.y + sd.y * STUB }
  const t1 = { x: t.x + td.x * STUB, y: t.y + td.y * STUB }
  const raw: Pt[] = [s, s1]
  if (laneX !== undefined && laneY !== undefined) raw.push({ x: s1.x, y: laneY }, { x: laneX, y: laneY }, { x: laneX, y: t1.y })
  else if (laneY !== undefined) raw.push({ x: s1.x, y: laneY }, { x: t1.x, y: laneY })
  else if (laneX !== undefined) raw.push({ x: laneX, y: s1.y }, { x: laneX, y: t1.y })
  raw.push(t1, t)
  const pts = raw.filter((p, i) => i === 0 || Math.abs(p.x - raw[i - 1].x) + Math.abs(p.y - raw[i - 1].y) > 0.5)
  let d = `M ${pts[0].x} ${pts[0].y}`
  let best = { len: -1, x: 0, y: 0 }
  for (let i = 1; i < pts.length; i++) {
    const a = pts[i - 1]
    const b = pts[i]
    const len = Math.abs(b.x - a.x) + Math.abs(b.y - a.y)
    if (len > best.len) best = { len, x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 }
    if (i === pts.length - 1) {
      d += ` L ${b.x} ${b.y}`
      break
    }
    const c = pts[i + 1]
    const r = Math.min(10, len / 2, (Math.abs(c.x - b.x) + Math.abs(c.y - b.y)) / 2)
    const inDir = { x: Math.sign(b.x - a.x), y: Math.sign(b.y - a.y) }
    const outDir = { x: Math.sign(c.x - b.x), y: Math.sign(c.y - b.y) }
    d += ` L ${b.x - inDir.x * r} ${b.y - inDir.y * r} Q ${b.x} ${b.y} ${b.x + outDir.x * r} ${b.y + outDir.y * r}`
  }
  return { path: d, lx: best.x, ly: best.y }
}

function edgeHead(source: string, target: string, d?: FlowEdgeData): string {
  return `${source}${d?.label ? `[${d.label}]` : ''} → ${target}`
}

/** The smaller edge tooltip: from[label] → to, description, maxIterations for back edges. */
function EdgeTipBody({ source, target, d }: { source: string; target: string; d?: FlowEdgeData }) {
  return (
    <Stack gap={2}>
      <Text fz={11} ff="monospace" fw={700}>
        {edgeHead(source, target, d)}
      </Text>
      {d?.back && (
        <Text fz={10} ff="monospace" c="pink.3">
          back edge · maxIterations: {d.back.max} · taken {d.back.taken}
        </Text>
      )}
      {d?.note && (
        <Text fz={10} ff="monospace" c="pink.3">
          {d.note}
        </Text>
      )}
      {d?.description && <Text fz={11}>{d.description}</Text>}
      {d?.diff && (
        <Text fz={10} c={d.diff === 'added' ? 'success.4' : 'danger.4'}>
          {d.diff === 'added' ? 'staged: added' : 'staged: removed'}
        </Text>
      )}
    </Stack>
  )
}

function LaneEdgeView({ id, source, target, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data, markerEnd, selected }: EdgeProps<FlowEdge>) {
  const tone = data?.tone ?? 'forward'
  const hasLane = data?.laneX !== undefined || data?.laneY !== undefined
  let path: string
  let lx: number
  let ly: number
  if (hasLane) {
    const r = lanePath({ x: sourceX, y: sourceY }, sourcePosition, { x: targetX, y: targetY }, targetPosition, data?.laneX, data?.laneY)
    path = r.path
    lx = r.lx
    ly = r.ly
  } else {
    ;[path, lx, ly] = getSmoothStepPath({ sourceX, sourceY, sourcePosition, targetX, targetY, targetPosition, borderRadius: 8, offset: 14 })
  }
  const diff = data?.diff
  const color = tone === 'invalid' ? TONE_HEX.invalid : diff ? DIFF_HEX[diff] : TONE_HEX[tone]
  const back = tone === 'back-wait' || tone === 'back-gate'
  const strong = data?.taken || back || tone === 'invalid' || !!diff
  const tipText = [edgeHead(source, target, data), data?.back ? `maxIterations: ${data.back.max}` : '', data?.description ?? ''].filter(Boolean).join('\n')
  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerEnd={markerEnd}
        style={{
          stroke: selected ? '#fff' : color,
          strokeWidth: back || tone === 'invalid' || diff ? 2 : data?.taken ? 1.8 : 1.2,
          strokeDasharray: diff ? '5 4' : back ? '7 5' : tone === 'invalid' ? '3 3' : undefined,
          opacity: diff === 'removed' ? 0.75 : strong ? 1 : 0.45,
        }}
      />
      <path d={path} fill="none" stroke="transparent" strokeWidth={14}>
        <title>{tipText}</title>
      </path>
      {(data?.label || data?.back || diff) && (
        <EdgeLabelRenderer>
          <Tooltip label={<EdgeTipBody source={source} target={target} d={data} />} openDelay={300} withArrow multiline w={280} color="dark" styles={{ tooltip: { border: '1px solid var(--mantine-color-dark-4)' } }}>
            <Box
              className="nodrag nopan"
              style={{
                position: 'absolute',
                transform: `translate(-50%, -50%) translate(${lx}px, ${ly}px)`,
                pointerEvents: 'all',
                background: 'var(--mantine-color-dark-7)',
                border: `1px solid ${back || tone === 'invalid' || diff ? color : 'var(--mantine-color-dark-4)'}`,
                borderRadius: 10,
                padding: '1px 6px',
                fontSize: 10,
                whiteSpace: 'nowrap',
                color: back || tone === 'invalid' || diff ? color : 'var(--mantine-color-gray-5)',
                fontWeight: back || diff ? 700 : 500,
                textDecoration: diff === 'removed' ? 'line-through' : undefined,
                cursor: 'help',
              }}
            >
              {diff === 'added' ? '+ ' : diff === 'removed' ? '− ' : ''}
              {back && data?.back ? `↺ ${data.back.taken}/${data.back.max} · ` : ''}
              {data?.label ?? (diff === 'added' ? 'new' : diff === 'removed' ? 'removed' : '')}
              {data?.note && ` · ${data.note}`}
              {tone === 'invalid' && ' · no gate/wait/limit'}
            </Box>
          </Tooltip>
        </EdgeLabelRenderer>
      )}
    </>
  )
}

const nodeTypes: NodeTypes = { run: RunNodeView, section: SectionNodeView, fan: FanNodeView }
const edgeTypes: EdgeTypes = { lane: LaneEdgeView }

function toneOf(id: string, from: string): Tone {
  if (!id.startsWith('b')) return 'forward'
  return from === 'await-fixes' || from === 'await-merge' || from === 'base-drift' ? 'back-wait' : 'back-gate'
}

function toFlowEdge(e: EdgeSpec, extra: { taken?: boolean; diff?: 'added' | 'removed'; invalid?: boolean; description?: string; selected?: boolean; note?: string } = {}): FlowEdge {
  const r = routeFor(e.id, e.from, e.to)
  const tone: Tone = extra.invalid ? 'invalid' : (e.tone ?? toneOf(e.id, e.from))
  const data: FlowEdgeData = {
    tone,
    label: e.label,
    laneX: r.laneX,
    laneY: r.laneY,
    back: e.back,
    taken: extra.taken ?? e.taken,
    diff: extra.diff,
    description: extra.description ?? edgeBaseDesc(e),
    note: extra.note,
  }
  const color = tone === 'invalid' ? TONE_HEX.invalid : extra.diff ? DIFF_HEX[extra.diff] : TONE_HEX[tone]
  return {
    id: e.id,
    source: e.from,
    target: e.to,
    sourceHandle: r.sh,
    targetHandle: r.th,
    type: 'lane',
    selected: extra.selected,
    markerEnd: { type: MarkerType.ArrowClosed, color, width: 16, height: 16 },
    data,
  }
}

const isWait = (id: string) => RUN618_NODES.find((n) => n.id === id)?.kind === 'wait'

// ---------------------------------------------------------------- sections: collapsible xyflow parent (group) nodes

type SecId = 'intake' | 'review' | 'decide' | 'branch'
type SecKey = SecId | 'fan'
type Collapse = Record<SecKey, boolean>
type Rect = { x: number; y: number; width: number; height: number }

const SECTIONS: { id: SecId; label: string; blurb: string }[] = [
  { id: 'intake', label: 'Intake', blurb: 'pin · delta · categorise · select · group · packs' },
  { id: 'review', label: 'Review', blurb: 'fan-out · reviewers · collate · verify' },
  { id: 'decide', label: 'Decide', blurb: 'publish · route · approve, or wait for fixes' },
  { id: 'branch', label: 'Branch maintenance', blurb: 'base check · drift · update branch · await merge' },
]

const SECTION_OF: Record<string, SecId> = {
  intake: 'intake',
  delta: 'intake',
  categorise: 'intake',
  select: 'intake',
  group: 'intake',
  packs: 'intake',
  review: 'review',
  collate: 'review',
  verify: 'review',
  'disputed-findings': 'review',
  publish: 'decide',
  route: 'decide',
  'needs-human-approval': 'decide',
  'bot-approve': 'decide',
  'human-approve': 'decide',
  'human-review': 'decide',
  'await-fixes': 'decide',
  notify: 'decide',
  'base-moved': 'branch',
  'base-drift': 'branch',
  'update-branch': 'branch',
  'await-merge': 'branch',
}
const secOf = (id: string): SecId | undefined => (id.startsWith('reviewer:') ? 'review' : SECTION_OF[id])
const FAN_ID = 'fan:reviewers'

/**
 * Where a collapsed section's summary node sits: on its own row, spanning the nodes whose handles its
 * boundary edges used (intake's left side, packs' right side, publish's r-in, base-moved's r-in), so most
 * edges keep their hand-picked lanes when they re-attach to the summary.
 */
const SUMMARY: Record<SecKey, { x: number; y: number; width: number; height: number }> = {
  intake: { x: 0, y: 0, width: STEP * 5 + W, height: H },
  review: { x: STEP * 6, y: 0, width: STEP + 20 + W, height: H },
  decide: { x: STEP * 3, y: 300, width: STEP * 2 + W, height: H },
  branch: { x: 0, y: 300, width: STEP + W, height: H },
  fan: { x: STEP * 7 + 20, y: 0, width: W, height: H },
}

const LIVE_COLLAPSE: Collapse = { intake: true, review: false, decide: true, branch: true, fan: false }
const BEHIND_COLLAPSE: Collapse = { intake: true, review: true, decide: false, branch: false, fan: true }
const ALL_OPEN: Collapse = { intake: false, review: false, decide: false, branch: false, fan: false }

type SecData = {
  sec: SecId
  collapsed: boolean
  members: WorkflowNodeDef[]
  mode: 'live' | 'edit'
  added: number
  onToggle: () => void
  fan?: { collapsed: boolean; n: number; onToggle: () => void }
}
type SecNode = Node<SecData, 'section'>
type FanData = { members: WorkflowNodeDef[]; mode: 'live' | 'edit'; onToggle: () => void }
type FanNode = Node<FanData, 'fan'>
type AnyNode = FlowNode | SecNode | FanNode

function rollup(members: WorkflowNodeDef[]): { st: NodeStatus; text: string } {
  const running = members.filter((m) => m.status === 'running')
  if (running.length) return { st: 'running', text: `running: ${running.map((r) => (r.id.startsWith('reviewer:') ? r.detail : r.id)).join(', ')}` }
  const waiting = members.filter((m) => m.status === 'waiting')
  if (waiting.length) return { st: 'waiting', text: `parked on ${waiting.map((w) => w.id).join(', ')}` }
  const done = members.filter((m) => m.status === 'done' || m.status === 'reused').length
  if (done === members.length) return { st: 'done', text: 'all done this iteration' }
  if (members.every((m) => m.status === 'skipped')) return { st: 'skipped', text: 'not reached' }
  return { st: 'pending', text: `${done}/${members.length} done this iteration` }
}

function SectionNodeView({ data }: NodeProps<SecNode>) {
  const s = SECTIONS.find((x) => x.id === data.sec)!
  const live = data.mode === 'live'
  const r = rollup(data.members)
  if (!data.collapsed)
    return (
      <Box w="100%" h="100%" style={{ border: '1px dashed var(--mantine-color-dark-3)', borderRadius: 10, background: 'rgba(255,255,255,0.018)', pointerEvents: 'none' }}>
        <Group gap={8} wrap="nowrap" style={{ position: 'absolute', top: 4, left: 8, pointerEvents: 'all' }} className="nodrag nopan">
          <UnstyledButton
            onClick={(e) => {
              e.stopPropagation()
              data.onToggle()
            }}
          >
            <Group gap={4} wrap="nowrap">
              <TbChevronDown size={12} />
              <Text size="11px" fw={700} tt="uppercase" c="gray.4" style={{ letterSpacing: 0.6 }}>
                {s.label}
              </Text>
              <Text size="10px" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
                {data.members.length} steps · {s.blurb}
              </Text>
            </Group>
          </UnstyledButton>
          {data.fan && !data.fan.collapsed && (
            <UnstyledButton
              onClick={(e) => {
                e.stopPropagation()
                data.fan!.onToggle()
              }}
            >
              <Badge size="xs" variant="outline" color="grape" tt="none" style={{ cursor: 'pointer' }}>
                collapse reviewers ×{data.fan.n}
              </Badge>
            </UnstyledButton>
          )}
        </Group>
      </Box>
    )
  const border = live ? STATUS_HEX[r.st] : 'var(--mantine-color-dark-3)'
  return (
    <Box
      w="100%"
      h="100%"
      px={10}
      py={5}
      onClick={(e) => {
        e.stopPropagation()
        data.onToggle()
      }}
      style={{
        background: 'var(--mantine-color-dark-6)',
        borderTop: `1.5px solid ${border}`,
        borderRight: `1.5px solid ${border}`,
        borderBottom: `1.5px solid ${border}`,
        borderLeft: `4px solid ${border}`,
        borderRadius: 8,
        cursor: 'pointer',
        animation: live && r.st === 'running' ? 'rePulse 1.6s infinite' : undefined,
        overflow: 'hidden',
        boxShadow: '0 3px 0 -1px var(--mantine-color-dark-4)',
      }}
    >
      <RunHandles />
      <Group justify="space-between" wrap="nowrap" gap={8}>
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          <TbChevronRight size={13} />
          <Text size="12px" fw={700} style={{ whiteSpace: 'nowrap' }}>
            {s.label}
          </Text>
          <Text size="10px" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            {data.members.length} steps · collapsed
          </Text>
          {data.added > 0 && (
            <Badge size="xs" color="success" variant="filled">
              +{data.added} new
            </Badge>
          )}
        </Group>
        {live && (
          <Text size="10px" fw={600} style={{ color: STATUS_HEX[r.st] }} truncate>
            {r.text}
          </Text>
        )}
      </Group>
      <Group gap={3} mt={4} wrap="nowrap">
        {data.members.map((m) => (
          <Tooltip key={m.id} label={`${m.id} · ${live ? NODE_STATUS_META[m.status].label : m.kind}`} openDelay={150}>
            <Box w={live ? 14 : 10} h={8} style={{ borderRadius: 2, background: live ? STATUS_HEX[m.status] : `var(--mantine-color-${NODE_KIND_META[m.kind].color}-6)`, border: live && m.status === 'skipped' ? '1px dashed #5c5f66' : undefined }} />
          </Tooltip>
        ))}
      </Group>
      <Text size="10px" c="dimmed" truncate mt={3}>
        {s.blurb} · click to expand
      </Text>
    </Box>
  )
}

function FanNodeView({ data }: NodeProps<FanNode>) {
  const live = data.mode === 'live'
  const n = data.members.length
  const done = data.members.filter((m) => m.status === 'done').length
  const running = data.members.filter((m) => m.status === 'running')
  const st: NodeStatus = !live ? 'pending' : running.length ? 'running' : done === n ? 'done' : 'pending'
  const border = live ? STATUS_HEX[st] : 'var(--mantine-color-grape-7)'
  return (
    <Box
      w={W}
      h={H}
      px={8}
      py={5}
      onClick={(e) => {
        e.stopPropagation()
        data.onToggle()
      }}
      style={{
        background: 'var(--mantine-color-dark-6)',
        borderTop: `1.5px solid ${border}`,
        borderRight: `1.5px solid ${border}`,
        borderBottom: `1.5px solid ${border}`,
        borderLeft: `4px solid ${border}`,
        borderRadius: 6,
        cursor: 'pointer',
        boxShadow: '4px 4px 0 -1px var(--mantine-color-dark-5), 8px 8px 0 -2px var(--mantine-color-dark-4)',
        animation: live && st === 'running' ? 'rePulse 1.6s infinite' : undefined,
      }}
    >
      <RunHandles />
      <Group gap={4} wrap="nowrap" justify="space-between">
        <Group gap={4} wrap="nowrap">
          <Box c="grape.4" style={{ display: 'flex' }}>
            <TbRobot size={13} />
          </Box>
          <Text size="12px" fw={700}>
            reviewers ×{n}
          </Text>
        </Group>
        <Tooltip label="Expand the fan-out">
          <Box style={{ display: 'flex' }}>
            <TbChevronRight size={12} />
          </Box>
        </Tooltip>
      </Group>
      <Text size="10px" c="dimmed" truncate>
        {data.members.map((m) => m.detail).join(' · ')}
      </Text>
      {live && (
        <Text size="10px" fw={600} style={{ color: STATUS_HEX[st] }} truncate>
          {done}/{n} done{running.length ? ` · ${running.map((r) => r.detail).join(', ')} running` : ''}
        </Text>
      )}
    </Box>
  )
}

const BASE_EDGE_IDS = new Set(RUN618_EDGES.map((e) => e.id))

/** Handle and lane choice for an edge that re-attaches to a summary node; null keeps the original route. */
function remapRoute(vs: string, vt: string): { sh: string; th: string; laneX?: number; laneY?: number } | null {
  if (vs === 'sec:review' && (vt === 'publish' || vt === 'sec:decide')) return { sh: 'b-out', th: 'r-in' }
  if (vs === 'review' && vt === FAN_ID) return { sh: 'r', th: 'l' }
  if (vs === FAN_ID && vt === 'collate') return { sh: 'r', th: 'r-in' }
  if (vs === 'sec:decide') {
    if (vt === 'base-moved' || vt === 'sec:branch') return { sh: 'l-out', th: 'r-in' }
    if (vt === 'base-drift') return { sh: 'l-out-c', th: 'r-in' }
    if (vt === 'intake' || vt === 'sec:intake') return { sh: 't-out', th: 'l-88', laneX: -30, laneY: 170 }
  }
  if (vs === 'sec:branch') {
    if (vt === 'await-fixes') return { sh: 'r-c', th: 'b-in', laneY: 640 }
    if (vt === 'sec:decide') return { sh: 'b-out', th: 'b-in', laneY: 420 }
    if (vt === 'intake' || vt === 'sec:intake') return { sh: 'l-out', th: 'l-70', laneX: -60 }
  }
  if (vs === 'base-drift' && vt === 'sec:decide') return { sh: 'r-c', th: 'b-in', laneY: 640 }
  return null
}

/**
 * Wraps the graph's nodes in one parent (group) node per section. A collapsed section becomes a single
 * summary node; its children are hidden and every edge that crossed its boundary is re-attached to the
 * summary (several edges between the same two visible nodes merge into one, labels joined).
 */
function sectionize(nodes: FlowNode[], edges: FlowEdge[], c: Collapse, toggle: (k: SecKey) => void, mode: 'live' | 'edit'): { nodes: AnyNode[]; edges: FlowEdge[]; bounds: Rect } {
  const size = (id: string) => ({ width: W, height: id.startsWith('reviewer:') ? HR : H })
  const reviewers = nodes.filter((n) => n.id.startsWith('reviewer:'))
  const fanOn = c.fan && !c.review && reviewers.length > 1
  const visOf = (id: string) => {
    const sec = secOf(id)
    if (sec && c[sec]) return `sec:${sec}`
    if (fanOn && id.startsWith('reviewer:')) return FAN_ID
    return id
  }
  const frames = new Map<SecId, Rect>()
  for (const sec of SECTIONS) {
    const ms = nodes.filter((n) => secOf(n.id) === sec.id)
    if (ms.length === 0) continue
    const x0 = Math.min(...ms.map((n) => n.position.x))
    const y0 = Math.min(...ms.map((n) => n.position.y))
    const x1 = Math.max(...ms.map((n) => n.position.x + size(n.id).width))
    const y1 = Math.max(...ms.map((n) => n.position.y + size(n.id).height))
    frames.set(sec.id, { x: x0 - 12, y: y0 - 28, width: x1 - x0 + 24, height: y1 - y0 + 40 })
  }
  const out: AnyNode[] = []
  const rects: Rect[] = []
  for (const sec of SECTIONS) {
    const f = frames.get(sec.id)
    if (!f) continue
    const col = c[sec.id]
    const r = col ? SUMMARY[sec.id] : f
    rects.push(r)
    const ms = nodes.filter((n) => secOf(n.id) === sec.id)
    out.push({
      id: `sec:${sec.id}`,
      type: 'section',
      position: { x: r.x, y: r.y },
      width: r.width,
      height: r.height,
      draggable: false,
      selectable: false,
      style: { pointerEvents: col ? 'all' : 'none' },
      data: {
        sec: sec.id,
        collapsed: col,
        members: ms.map((n) => n.data.def),
        mode,
        added: ms.filter((n) => n.data.diff === 'added').length,
        onToggle: () => toggle(sec.id),
        fan: sec.id === 'review' && reviewers.length > 1 ? { collapsed: fanOn, n: reviewers.length, onToggle: () => toggle('fan') } : undefined,
      },
    })
  }
  const fr = frames.get('review')
  if (fanOn && fr) {
    const r = SUMMARY.fan
    rects.push(r)
    out.push({ id: FAN_ID, type: 'fan', parentId: 'sec:review', position: { x: r.x - fr.x, y: r.y - fr.y }, draggable: false, selectable: false, data: { members: reviewers.map((n) => n.data.def), mode, onToggle: () => toggle('fan') } })
  }
  for (const n of nodes) {
    const sec = secOf(n.id)
    const parent = sec ? (c[sec] ? SUMMARY[sec] : frames.get(sec)) : undefined
    const hide = (sec ? c[sec] : false) || (fanOn && n.id.startsWith('reviewer:'))
    if (!hide) rects.push({ x: n.position.x, y: n.position.y, ...size(n.id) })
    out.push(parent && sec ? { ...n, parentId: `sec:${sec}`, position: { x: n.position.x - parent.x, y: n.position.y - parent.y }, hidden: hide } : { ...n, hidden: hide })
  }

  const outEdges: FlowEdge[] = []
  const groups = new Map<string, FlowEdge[]>()
  for (const e of edges) {
    const vs = visOf(e.source)
    const vt = visOf(e.target)
    if (vs === e.source && vt === e.target) {
      outEdges.push(e)
      continue
    }
    if (vs === vt) continue
    const k = `${vs}>${vt}`
    groups.set(k, [...(groups.get(k) ?? []), e])
  }
  for (const [k, es] of groups) {
    const [vs, vt] = k.split('>')
    const first = es[0]
    const r =
      remapRoute(vs, vt) ??
      (BASE_EDGE_IDS.has(first.id)
        ? { sh: first.sourceHandle ?? 'r', th: first.targetHandle ?? 'l', laneX: first.data?.laneX, laneY: first.data?.laneY }
        : { sh: first.sourceHandle ?? 'r', th: first.targetHandle ?? 'l' })
    const labels = [...new Set(es.map((e) => e.data?.label).filter((l): l is string => !!l))]
    const backs = es.flatMap((e) => (e.data?.back ? [e.data.back] : []))
    outEdges.push({
      ...first,
      id: `m:${k}`,
      source: vs,
      target: vt,
      sourceHandle: r.sh,
      targetHandle: r.th,
      selected: es.some((e) => e.selected),
      data: {
        ...first.data!,
        label: labels.join(' | ') || undefined,
        laneX: r.laneX,
        laneY: r.laneY,
        taken: es.some((e) => e.data?.taken),
        back: backs.length ? { taken: backs.reduce((a, b) => a + b.taken, 0), max: backs.reduce((a, b) => a + b.max, 0) } : undefined,
        note: es.length > 1 ? `${es.length} edges` : first.data?.note,
        description: `Re-attached to the collapsed section: ${es.map((e) => edgeHead(e.source, e.target, e.data)).join(' · ')}`,
      },
    })
  }

  const lx = outEdges.flatMap((e) => (e.data?.laneX !== undefined ? [e.data.laneX] : []))
  const ly = outEdges.flatMap((e) => (e.data?.laneY !== undefined ? [e.data.laneY] : []))
  // lane labels are centred on the lane, so leave room for them left of the outermost lane
  const x0 = Math.min(...rects.map((r) => r.x), ...lx.map((v) => v - 70)) - 24
  const y0 = Math.min(...rects.map((r) => r.y), ...ly) - 16
  const x1 = Math.max(...rects.map((r) => r.x + r.width), ...lx) + 24
  const y1 = Math.max(...rects.map((r) => r.y + r.height), ...ly) + 16
  return { nodes: out, edges: outEdges, bounds: { x: x0, y: y0, width: x1 - x0, height: y1 - y0 } }
}

function useSections(initial: Collapse) {
  const [collapse, setCollapse] = useState<Collapse>(initial)
  const toggle = useCallback((k: SecKey) => setCollapse((c) => ({ ...c, [k]: !c[k] })), [])
  const setAll = useCallback((v: boolean) => setCollapse({ intake: v, review: v, decide: v, branch: v, fan: v }), [])
  return { collapse, toggle, setAll }
}

/** Refits the view whenever the visible graph's bounds change (a section collapsed or expanded, a node staged). */
function useFitOnChange(bounds: Rect) {
  const [rf, setRf] = useState<ReactFlowInstance<AnyNode, FlowEdge> | null>(null)
  const key = `${bounds.x}:${bounds.y}:${bounds.width}:${bounds.height}`
  useEffect(() => {
    if (!rf) return
    const [x, y, width, height] = key.split(':').map(Number)
    const id = window.requestAnimationFrame(() => rf.fitBounds({ x, y, width, height }, { padding: 0.02, duration: 250 }))
    return () => window.cancelAnimationFrame(id)
  }, [rf, key])
  return setRf
}

function SectionBar({ c, onToggle, onAll, fanN }: { c: Collapse; onToggle: (k: SecKey) => void; onAll: (v: boolean) => void; fanN: number }) {
  return (
    <Group gap={6} wrap="wrap">
      <Text size="xs" c="dimmed">
        Sections
      </Text>
      {SECTIONS.map((x) => (
        <Button
          key={x.id}
          size="compact-xs"
          variant={c[x.id] ? 'default' : 'light'}
          leftSection={c[x.id] ? <TbChevronRight size={12} /> : <TbChevronDown size={12} />}
          onClick={() => onToggle(x.id)}
        >
          {x.label}
        </Button>
      ))}
      <Button
        size="compact-xs"
        variant={c.fan ? 'default' : 'light'}
        color="grape"
        disabled={c.review}
        leftSection={c.fan ? <TbChevronRight size={12} /> : <TbChevronDown size={12} />}
        onClick={() => onToggle('fan')}
      >
        reviewers ×{fanN}
      </Button>
      <Button size="compact-xs" variant="subtle" color="gray" onClick={() => onAll(false)}>
        Expand all
      </Button>
      <Button size="compact-xs" variant="subtle" color="gray" onClick={() => onAll(true)}>
        Collapse all
      </Button>
    </Group>
  )
}

// ---------------------------------------------------------------- (A, C) base moves seen while parked

/** "Base moved (3 commits) · drift recorded · branch not updated: waiting on author", with the commits and the verdict. */
function BaseMoves({ behind = false }: { behind?: boolean }) {
  return (
    <Stack gap={6}>
      {RUN618_BASE_MOVES.map((m) => (
        <Paper key={m.at} withBorder p={6} radius="sm" style={{ borderLeft: '3px solid var(--mantine-color-cyan-6)' }}>
          <Text size="xs" fw={700}>
            Base moved ({m.commits.length} commits) · drift recorded · branch {m.branch}
          </Text>
          <Text size="xs" c="dimmed">
            it{m.iteration} · {m.at} · {m.atWh} wh into the await-fixes park · {m.from}..{m.to}
          </Text>
          <Text size="xs" c="cyan.4">
            <StepName id="base-drift" /> → {m.verdict}: {m.how}
          </Text>
          {m.commits.map((c) => (
            <Text key={c.sha} size="10px" ff="monospace" c="dimmed" truncate>
              {c.sha} {c.title} · {personName(c.author)}
            </Text>
          ))}
          <Text size="10px" c="dimmed" mt={2}>
            While a run waits on the author the PR branch is never updated; the next push (4a77b10) was reviewed against {m.to}, its merge-base stayed {m.from}.
          </Text>
        </Paper>
      ))}
      {behind && (
        <Paper withBorder p={6} radius="sm" style={{ borderLeft: '3px solid var(--mantine-color-warning-6)' }}>
          <Text size="xs" fw={700}>
            After approval: behind (merge-base 3 commits behind tip)
          </Text>
          <Text size="xs" c="dimmed">
            <StepName id="base-moved" /> · keepUpToDate on → compared merge-base 1f20c9e with tip 7b3e9d0 (reviewed base 7b3e9d0 = tip, so a reviewed-base check would have said unchanged)
          </Text>
          <Text size="xs" c="cyan.4">
            → <StepName id="base-drift" /> unaffected-approved (same range as the it1 drift record) → <StepName id="update-branch" /> rebased to 52c8e1a, approval carried
          </Text>
        </Paper>
      )}
    </Stack>
  )
}

// ---------------------------------------------------------------- (A') the same run after approval, with a branch already behind at review

type Patch = { status: NodeStatus; exec?: Omit<NodeExec, 'iteration' | 'status'> }
const BEHIND_PATCH: Record<string, Patch> = {
  'reviewer:concurrency': { status: 'done', exec: { started: 'Fri 16:06', durationS: 252, model: 'opus', tokensIn: 61_900, tokensOut: 2_900, cost: { api: 0, seatEq: 1.21 }, output: '0 findings · 3/3 checks pass' } },
  review: { status: 'done', exec: { started: 'Fri 16:06', durationS: 262, note: '4/4 children done' } },
  collate: { status: 'done', exec: { started: 'Fri 16:10', durationS: 2, output: '3 → 2 after dedupe' } },
  verify: { status: 'done', exec: { started: 'Fri 16:10', durationS: 31, model: 'haiku', tokensIn: 38_000, tokensOut: 1_900, cost: { api: 0, seatEq: 0.07 }, output: '2 dropped: already fixed in e41b07c' } },
  publish: { status: 'done', exec: { started: 'Fri 16:11', durationS: 3, output: 'no inline findings · summary edited · antiphon/review success' } },
  route: { status: 'done', exec: { started: 'Fri 16:11', durationS: 0.2, output: 'clean' } },
  'needs-human-approval': { status: 'done', exec: { started: 'Fri 16:11', durationS: 0.1, output: 'yes (sticky from iteration 2)' } },
  'human-approve': { status: 'done', exec: { started: 'Fri 16:11', durationS: 5_400, output: 'Hugo Martin → approve e41b07c' } },
  'base-moved': {
    status: 'done',
    exec: { started: 'Fri 17:42', durationS: 0.6, output: 'behind (merge-base 3 commits behind tip) · keepUpToDate on → compared merge-base 1f20c9e with tip 7b3e9d0; reviewed base 7b3e9d0 = tip' },
  },
  'base-drift': { status: 'done', exec: { started: 'Fri 17:42', durationS: 0.2, output: '1f20c9e..7b3e9d0 (3 commits) · same range as the it1 drift record → unaffected-approved (no model call)' } },
  'update-branch': { status: 'done', exec: { started: 'Fri 17:42', durationS: 38, output: 'rebased onto 7b3e9d0 → 52c8e1a · no conflicts · patch ids unchanged → approval carried forward' } },
  'await-merge': { status: 'waiting', exec: { started: 'Fri 17:43', durationS: 0, output: 'holding the approval at 52c8e1a until merge or close' } },
}
const BEHIND_NODES: WorkflowNodeDef[] = RUN618_NODES.map((d) => {
  const pt = BEHIND_PATCH[d.id]
  if (!pt) return d
  const execs = d.execs.filter((e) => e.iteration !== 3)
  return { ...d, status: pt.status, execs: pt.exec ? [...execs, { iteration: 3, status: pt.status, ...pt.exec }] : execs }
})
const BEHIND_TAKEN = new Set(['e8b', 'e9', 'e10', 'e11', 'e14', 'e16', 'e18', 'e26', 'e23', 'e20'])
const BEHIND_EDGES = RUN618_EDGES.map((e) => (BEHIND_TAKEN.has(e.id) ? { ...e, taken: true } : e))

const PULSE_CSS = `@keyframes rePulse { 0% { box-shadow: 0 0 0 0 rgba(59,123,245,.65) } 70% { box-shadow: 0 0 0 10px rgba(59,123,245,0) } 100% { box-shadow: 0 0 0 0 rgba(59,123,245,0) } }
.react-flow__attribution { background: transparent !important; }`

// ---------------------------------------------------------------- (A) live overlay

function Legend() {
  return (
    <Group gap="md" wrap="wrap">
      {(['done', 'reused', 'running', 'pending', 'skipped'] as NodeStatus[]).map((s) => (
        <Group key={s} gap={4} wrap="nowrap">
          <Box w={10} h={10} style={{ borderRadius: 2, border: `2px ${s === 'pending' || s === 'skipped' ? 'dashed' : 'solid'} ${STATUS_HEX[s]}` }} />
          <Text size="xs" c="dimmed">
            {NODE_STATUS_META[s].label}
          </Text>
        </Group>
      ))}
      <Group gap={4} wrap="nowrap">
        <svg width={26} height={8}>
          <line x1={0} y1={4} x2={26} y2={4} stroke="#909296" strokeWidth={1.8} />
        </svg>
        <Text size="xs" c="dimmed">
          forward
        </Text>
      </Group>
      <Group gap={4} wrap="nowrap">
        <svg width={26} height={8}>
          <line x1={0} y1={4} x2={26} y2={4} stroke="#ffa92e" strokeWidth={2} strokeDasharray="7 5" />
        </svg>
        <Text size="xs" c="dimmed">
          back edge via wait or base drift
        </Text>
      </Group>
      <Group gap={4} wrap="nowrap">
        <svg width={26} height={8}>
          <line x1={0} y1={4} x2={26} y2={4} stroke="#f06595" strokeWidth={2} strokeDasharray="7 5" />
        </svg>
        <Text size="xs" c="dimmed">
          back edge via gate · ↺ taken/max
        </Text>
      </Group>
      <Text size="xs" c="dimmed">
        · hover a step for its definition
      </Text>
    </Group>
  )
}

function NodeDetailPanel({ id, onArtifact, defs = RUN618_NODES }: { id: string; onArtifact: (artifactId: string, instanceKey?: string) => void; defs?: WorkflowNodeDef[] }) {
  const def = defs.find((n) => n.id === id)!
  const k = NODE_KIND_META[def.kind]
  const totalIn = def.execs.reduce((a, e) => a + (e.tokensIn ?? 0), 0)
  return (
    <Stack gap="xs">
      <Group justify="space-between" wrap="nowrap">
        <Group gap={6} wrap="nowrap">
          <Box c={`${k.color}.4`} style={{ display: 'flex' }}>
            {KIND_ICON[def.kind]}
          </Box>
          <StepTip id={def.id} exec={def.execs[def.execs.length - 1] ?? null}>
            <Text fw={700} ff="monospace" style={{ cursor: 'help' }}>
              {def.id}
            </Text>
          </StepTip>
        </Group>
        <Badge color={NODE_STATUS_META[def.status].color} variant="light" size="sm">
          {NODE_STATUS_META[def.status].label}
        </Badge>
      </Group>
      <Box>
        <Text size="10px" c="dimmed" tt="uppercase" fw={700} style={{ letterSpacing: 0.6 }}>
          Description
        </Text>
        <Text size="sm">{nodePurpose(def.id)}</Text>
      </Box>
      <Group gap={6} wrap="wrap">
        <Tooltip label={k.blurb}>
          <Badge size="xs" color={k.color} variant="outline">
            {k.label}
          </Badge>
        </Tooltip>
        {def.run && <Code fz={10}>{def.run}</Code>}
        {def.detail && <Code fz={10}>{def.detail}</Code>}
      </Group>
      <NodeIOTabs key={def.id} nodeId={def.id} onArtifact={onArtifact} />
      <Divider label={<Text size="xs">Executions by iteration</Text>} labelPosition="left" />
      {def.execs.length === 0 ? (
        <Text size="xs" c="dimmed" fs="italic">
          {NODE_NOT_RUN_NOTE[def.id] ?? 'Not reached in this run yet.'}
        </Text>
      ) : (
        <Table fz="xs" verticalSpacing={3} horizontalSpacing={4}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>it</Table.Th>
              <Table.Th>status</Table.Th>
              <Table.Th>started</Table.Th>
              <Table.Th ta="right">time</Table.Th>
              <Table.Th>model</Table.Th>
              <Table.Th ta="right">tokens</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {def.execs.map((e) => (
              <Table.Tr key={e.iteration}>
                <Table.Td>{e.iteration}</Table.Td>
                <Table.Td>
                  <Text size="xs" style={{ color: STATUS_HEX[e.status] }}>
                    {NODE_STATUS_META[e.status].label}
                  </Text>
                </Table.Td>
                <Table.Td>{e.started}</Table.Td>
                <Table.Td ta="right" ff="monospace">
                  {fmtDur(e)}
                </Table.Td>
                <Table.Td>{e.model ? <ModelBadge model={e.model} short /> : '—'}</Table.Td>
                <Table.Td ta="right" ff="monospace">
                  {e.tokensIn ? fmtTokens(e.tokensIn) : '—'}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      {def.execs.map((e) =>
        e.output || e.note ? (
          <Box key={e.iteration}>
            <Text size="xs" fw={600}>
              it{e.iteration}
            </Text>
            {e.output && <Text size="xs">{e.output}</Text>}
            {e.note && (
              <Text size="xs" c="warning.4">
                {e.note}
              </Text>
            )}
          </Box>
        ) : null,
      )}
      {(def.id === 'await-fixes' || def.id === 'base-drift') && (
        <>
          <Divider label={<Text size="xs">Base moves while parked</Text>} labelPosition="left" />
          <BaseMoves behind={defs !== RUN618_NODES} />
        </>
      )}
      {totalIn > 0 && (
        <Group gap={6}>
          <Text size="xs" c="dimmed">
            All iterations:
          </Text>
          <Text size="xs" ff="monospace">
            {fmtTokens(totalIn)} in
          </Text>
          <CostText
            cost={def.execs.reduce((a, e) => ({ api: a.api + (e.cost?.api ?? 0), seatEq: a.seatEq + (e.cost?.seatEq ?? 0) }), { api: 0, seatEq: 0 })}
            models={def.execs.flatMap((e) => (e.model ? [e.model] : []))}
          />
        </Group>
      )}
      <Text size="xs" c="dimmed" ff="monospace">
        inputs sha256:{(def.id.length * 2654435761 % 0xffffffff).toString(16).padStart(8, '0')}… · revision pr-review r7
      </Text>
      <Group gap={6}>
        {def.kind === 'agent' && (
          <>
            <Button size="compact-xs" variant="light" leftSection={<TbPackage size={12} />}>
              Open pack
            </Button>
            <Button size="compact-xs" variant="light" leftSection={<TbTerminal2 size={12} />}>
              Transcript
            </Button>
          </>
        )}
        {(def.kind === 'decision' || def.kind === 'completion') && (
          <Button size="compact-xs" variant="light" leftSection={<TbFileText size={12} />}>
            llm_call rows
          </Button>
        )}
      </Group>
    </Stack>
  )
}

function LiveOverlay({ scenario = 'live' }: { scenario?: 'live' | 'behind' }) {
  const behind = scenario === 'behind'
  const defs = behind ? BEHIND_NODES : RUN618_NODES
  const edgeDefs = behind ? BEHIND_EDGES : RUN618_EDGES
  const { collapse, toggle, setAll } = useSections(behind ? BEHIND_COLLAPSE : LIVE_COLLAPSE)
  const base = useMemo<FlowNode[]>(() => defs.map((def) => ({ id: def.id, type: 'run', position: POS[def.id], data: { def, mode: 'live' }, draggable: false })), [defs])
  const baseEdges = useMemo(() => edgeDefs.map((e) => toFlowEdge(e)), [edgeDefs])
  const g = useMemo(() => sectionize(base, baseEdges, collapse, toggle, 'live'), [base, baseEdges, collapse, toggle])
  const onInit = useFitOnChange(g.bounds)
  const [sel, setSel] = useState(behind ? 'base-moved' : 'reviewer:concurrency')
  const [art, setArt] = useState<{ id: string; key?: string } | null>(null)
  return (
    <Stack gap="xs">
      <style>{PULSE_CSS}</style>
      <Drawer
        opened={!!art}
        onClose={() => setArt(null)}
        position="right"
        size={520}
        title={<Text fw={700}>Artifact</Text>}
      >
        {art && (
          <ArtifactDetail
            artifactId={art.id}
            instance={art.key ? instanceByKey(art.key) : undefined}
            onNode={(id) => {
              if (defs.some((n) => n.id === id)) setSel(id)
              setArt(null)
            }}
          />
        )}
      </Drawer>
      <Group justify="space-between" wrap="nowrap">
        <Box>
          <Group gap={8}>
            <Text size="xs" ff="monospace" c="dimmed">
              lumen/payments-api#618 · {RUN.id}
            </Text>
            {behind ? (
              <StageChip stage="approved" size="xs" detail="iteration 3 · approved by Hugo Martin · branch was behind at review → updated, approval carried" />
            ) : (
              <StageChip stage="reviewing" size="xs" detail="iteration 3 · round 3 · steer from Hugo Martin" />
            )}
          </Group>
          <Text fw={700} size="lg">
            {RUN.title}
          </Text>
        </Box>
        <Group gap="md" wrap="nowrap">
          <BlastMini blast={RUN.blast} />
          <Box ta="right">
            <Text size="xs" c="dimmed">
              workflow pr-review · revision r7 · sha256:4be1c09…
            </Text>
            <Group gap={6} justify="flex-end">
              <Text size="xs" ff="monospace">
                {fmtTokens(RUN.tokensIn)} in · {fmtTokens(RUN.tokensOut)} out
              </Text>
              <CostText cost={RUN.cost} models={RUN.models} />
            </Group>
          </Box>
        </Group>
      </Group>
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Legend />
        <SectionBar c={collapse} onToggle={toggle} onAll={setAll} fanN={4} />
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 340px', gap: 12 }}>
        <Paper withBorder radius="md" h="calc(100vh - 230px)" mih={560} style={{ overflow: 'hidden' }}>
          <ReactFlow
            nodes={g.nodes}
            edges={g.edges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            colorMode="dark"
            onInit={onInit}
            minZoom={0.3}
            nodesDraggable={false}
            nodesConnectable={false}
            onNodeClick={(_, n) => {
              if (n.type === 'run') setSel(n.id)
            }}
          >
            <Background gap={24} size={1} color="#2c2e33" />
            <Controls showInteractive={false} />
          </ReactFlow>
        </Paper>
        <Paper withBorder radius="md" p="sm" h="calc(100vh - 230px)" mih={560}>
          <ScrollArea h="100%">
            <SectionLabel>Selected node</SectionLabel>
            <NodeDetailPanel key={`${scenario}:${sel}`} id={sel} defs={defs} onArtifact={(id, key) => setArt({ id, key })} />
            <Divider my="sm" />
            <SectionLabel>Base branch</SectionLabel>
            <BaseMoves behind={behind} />
            <Divider my="sm" />
            <SectionLabel>Statuses posted to {behind ? '52c8e1a' : 'e41b07c'}</SectionLabel>
            {behind ? (
              <Stack gap={2}>
                <Text size="xs">
                  <Text span ff="monospace">antiphon/review</Text> success · "Round 3 clean · 11 checks"
                </Text>
                <Text size="xs">
                  <Text span ff="monospace">antiphon/approval</Text> success · Hugo Martin, carried from e41b07c (patch ids unchanged)
                </Text>
              </Stack>
            ) : (
              <Stack gap={2}>
                <Text size="xs">
                  <Text span ff="monospace">antiphon/review</Text> pending · "Round 3 · 11 checks · 3/4 reviewers done"
                </Text>
                <Text size="xs" c="dimmed">
                  updated 12s ago · throttled to 1 per 30s
                </Text>
                <Text size="xs">
                  <Text span ff="monospace">antiphon/approval</Text> pending
                </Text>
              </Stack>
            )}
          </ScrollArea>
        </Paper>
      </Box>
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) editor: AI prompt → staging → revision

interface Proposal {
  id: string
  title: string
  model: ModelKey
  reply: string
  items: ChangeItem[]
  replay: string
}

const ROUTE_DESC_2 = 'Routes after a round: findings → wait for changes, clean → needs-human-approval, and after 2 bot-only rounds → human review (§5).'
const DISPUTED_DESC = nodePurpose('disputed-findings')

const PROPOSALS: Record<string, Proposal> = {
  p1: {
    id: 'p1',
    title: 'Migration-safety reviewer group',
    model: 'sonnet',
    reply:
      'Adds a migration-safety reviewer group that only runs when Categorise puts persistent data at p ≥ 0.6. It is group config plus one GroupChecks rule; on the graph it is a fifth fan-out lane, on the opus tier because its checks are blocker or data-loss.',
    items: [
      {
        id: 'p1-n',
        kind: 'add-node',
        node: { id: 'reviewer:migration', kind: 'agent', run: 'Reviewer', detail: 'migration', pos: { x: STEP * 7 + 20, y: 152 }, description: 'Reviews schema and data migrations: expand/contract order, backfills, Down steps and lock time. Runs only when persistent data p ≥ 0.6.' },
        why: 'fan-out lane for the new group',
      },
      { id: 'p1-e1', kind: 'add-edge', edge: { id: 'p1a', from: 'review', to: 'reviewer:migration' }, why: 'fan-out child' },
      { id: 'p1-e2', kind: 'add-edge', edge: { id: 'p1b', from: 'reviewer:migration', to: 'collate' }, why: 'joins at collate' },
      { id: 'p1-p', kind: 'param', target: 'group', key: 'conditionalGroups', label: 'Conditional groups', from: '', to: 'migration-safety when persistent_data ≥ 0.6', source: 'ai' },
    ],
    replay: `replay set: recall 18/20 → 19/20 (catches the 2025-11-03 backfill miss) · findings posted +4% · cost +${fmtCost(0.41)}/PR on the 31% of PRs it triggers`,
  },
  p2: {
    id: 'p2',
    title: 'Human gate after 2 bot-only rounds',
    model: 'haiku',
    reply:
      "This is a parameter, not a graph change: rounds.humanGateAfterBotOnlyRounds 3 → 2 in payments-api's policy.yaml, overriding common@v14. I also updated RouteAfterRound's description to match.",
    items: [
      { id: 'p2-p', kind: 'param', target: 'route', key: 'humanGateAfterBotOnlyRounds', label: 'Human gate after bot-only rounds', from: 3, to: 2, source: 'ai' },
      { id: 'p2-d', kind: 'description', target: 'route', from: nodePurpose('route'), to: ROUTE_DESC_2, source: 'ai' },
    ],
    replay: `replay set: human gates opened 4 → 7 of 20 · avg rounds 1.8 → 1.6 · cost ${fmtCost(-0.05)}/PR`,
  },
  p3: {
    id: 'p3',
    title: 'Re-review disputed findings',
    model: 'sonnet',
    reply: 'Adds verify[disputed] → packs so findings the verifier disputes get another review pass before publishing.',
    items: [{ id: 'p3-e', kind: 'add-edge', edge: { id: 'x1', from: 'verify', to: 'packs', label: 'disputed', description: 'Send disputed findings back for another review pass.' }, why: 'loop back for another pass' }],
    replay: 'not estimated: the staged graph is invalid',
  },
  // the two quick fixes for p3's cycle error (Insert gate, Add iteration limit); "Ask AI to fix" also lands on p3fix
  p3fix: {
    id: 'p3fix',
    title: 'Re-review disputed findings (through a gate)',
    model: 'sonnet',
    reply:
      'The loop now passes through a gate. verify[disputed] → disputed-findings (gate, owning team). The first dispute of a finding goes back to packs automatically (autoRetries 1), with the verifier’s objection added to the pack; any further dispute of that finding opens the gate: re-review, drop or keep. Times out after 2 wh to drop. The re-review edge keeps maxIterations 2.',
    items: [
      {
        id: 'p3f-n',
        kind: 'add-node',
        node: { id: 'disputed-findings', kind: 'gate', run: 'queue: owning-team', detail: 'auto ×1, then human · re-review · drop · keep', pos: { x: STEP * 6, y: 168 }, description: DISPUTED_DESC },
        why: 'gate that breaks the cycle',
      },
      { id: 'p3f-e1', kind: 'add-edge', edge: { id: 'p3a', from: 'verify', to: 'disputed-findings', label: 'disputed', description: 'The verifier disputes a finding: decide what happens to it.' }, why: 'only disputed findings' },
      {
        id: 'p3f-e2',
        kind: 'add-edge',
        edge: { id: 'p3b', from: 'disputed-findings', to: 'packs', label: 're-review', back: { taken: 0, max: 2 }, tone: 'back-gate', description: 'Re-reviews only the disputed findings, with the verifier’s objection added to the pack. The first dispute of a finding takes this edge automatically.' },
        why: 'back edge, maxIterations 2',
      },
      { id: 'p3f-e3', kind: 'add-edge', edge: { id: 'p3c', from: 'disputed-findings', to: 'publish', label: 'drop | keep', description: 'drop removes the finding; keep posts it as is.' }, why: 'out of the loop' },
      { id: 'p3f-p1', kind: 'param', target: 'disputed-findings', key: 'queue', label: 'Queue', to: 'owning-team', source: 'ai' },
      { id: 'p3f-p2', kind: 'param', target: 'disputed-findings', key: 'decisions', label: 'Decisions', to: ['re-review', 'drop', 'keep'], source: 'ai' },
      { id: 'p3f-p5', kind: 'param', target: 'disputed-findings', key: 'autoRetries', label: 'Automatic passes before the gate opens', to: 1, source: 'ai' },
      { id: 'p3f-p6', kind: 'param', target: 'disputed-findings', key: 'autoDecision', label: 'Automatic decision', to: 're-review', source: 'ai' },
      { id: 'p3f-p3', kind: 'param', target: 'disputed-findings', key: 'timeout', label: 'Timeout', to: '2wh', source: 'ai' },
      { id: 'p3f-p4', kind: 'param', target: 'disputed-findings', key: 'onTimeout', label: 'On timeout', to: 'drop', source: 'ai' },
    ],
    replay: `replay set: recall 18/20 → 18/20 · 3 of 41 disputed findings recovered as real · 0.9 automatic re-reviews/PR · +0.2 gates/PR · cost +${fmtCost(0.04)}/PR`,
  },
  p3limit: {
    id: 'p3limit',
    title: 'Re-review disputed findings (iteration limit)',
    model: 'sonnet',
    reply:
      'The loop is now bounded: verify[disputed] → packs carries maxIterations 2. Disputed findings get at most two more review passes per iteration; over the limit the configured gate (human-review) opens. No human is asked before that.',
    items: [
      {
        id: 'p3l-e',
        kind: 'add-edge',
        edge: { id: 'x1', from: 'verify', to: 'packs', label: 'disputed', back: { taken: 0, max: 2 }, tone: 'back-gate', description: 'Send disputed findings back for another review pass, at most 2 per iteration; over the limit human-review opens.' },
        why: 'back edge, maxIterations 2 → human-review',
      },
    ],
    replay: `replay set: recall 18/20 → 18/20 · 3 of 41 disputed findings recovered as real · 1.3 extra verify passes/PR · cost +${fmtCost(0.07)}/PR`,
  },
  p4: {
    id: 'p4',
    title: 'Notify consuming teams',
    model: 'sonnet',
    reply:
      'Inserts NotifyConsumers between publish and route. It only fires when external_contract ≥ 0.6, never blocks the run, and finds consumers from the contract → consumer register.',
    items: [
      {
        id: 'p4-n',
        kind: 'add-node',
        node: { id: 'notify', kind: 'code', run: 'NotifyConsumers', pos: { x: STEP * 4.5, y: 196 }, description: 'Tells teams that consume a changed contract (from the contract → consumer register) when external_contract ≥ 0.6. Never blocks the run.' },
        why: 'new code step',
      },
      { id: 'p4-r', kind: 'remove-edge', edgeId: 'e11', why: 'publish no longer goes straight to route' },
      { id: 'p4-e1', kind: 'add-edge', edge: { id: 'p4a', from: 'publish', to: 'notify' }, why: 'after publish' },
      { id: 'p4-e2', kind: 'add-edge', edge: { id: 'p4b', from: 'notify', to: 'route' }, why: 'then route as before' },
      { id: 'p4-p1', kind: 'param', target: 'notify', key: 'when', label: 'Only when', to: 'external_contract ≥ 0.6', source: 'ai' },
      { id: 'p4-p2', kind: 'param', target: 'notify', key: 'channels', label: 'Channels', to: ['attention-feed', 'consumer-register'], source: 'ai' },
    ],
    replay: `replay set: no recall change · 7/20 PRs would notify 2.1 consuming teams each · cost +${fmtCost(0.002)}/PR (one Jev call)`,
  },
}

const CHIPS: { text: string; id: string }[] = [
  { text: 'Add a migration-safety reviewer group when persistent data p ≥ 0.6', id: 'p1' },
  { text: 'Open the human gate after 2 bot-only rounds instead of 3', id: 'p2' },
  { text: 'Re-review disputed findings before publishing', id: 'p3' },
  { text: 'Notify consuming teams after publish when external_contract ≥ 0.6', id: 'p4' },
]

function matchProposal(text: string): Proposal | null {
  const chip = CHIPS.find((c) => c.text === text)
  if (chip) return PROPOSALS[chip.id]
  const t = text.toLowerCase()
  if (/disput|disprov|re-?review|retry/.test(t)) return PROPOSALS.p3
  if (/notif|consum|contract/.test(t)) return PROPOSALS.p4
  if (/migrat|schema|persist|backfill/.test(t)) return PROPOSALS.p1
  if (/gate|bot-only|rounds?/.test(t)) return PROPOSALS.p2
  return null
}

interface Turn {
  prompt: string
  reply: string
  model: ModelKey
  proposalId?: string
  canned: boolean
  at: string
  /** A one-click quick fix on a validation error: no model call. */
  fix?: QuickFix
}

type QuickFix = 'gate' | 'limit'
const QUICK_FIX_LABEL: Record<QuickFix, string> = { gate: 'Insert gate', limit: 'Add iteration limit' }
/** Quick fixes for a proposal's cycle error, as the fixed proposal each one stages. */
const QUICK_FIXES: Record<string, Record<QuickFix, string>> = { p3: { gate: 'p3fix', limit: 'p3limit' } }

type View = 'current' | 'staged' | 'diff'
type Sel = { type: 'node' | 'edge'; id: string } | null

interface Issue {
  level: 'error' | 'warning' | 'info'
  /** 'cycle' errors offer the Insert gate / Add iteration limit quick fixes. */
  kind?: 'cycle'
  text: string
  edgeId?: string
  nodeId?: string
}

/**
 * Cycle rule (D-2): with gate and wait nodes removed, and back edges that carry maxIterations
 * removed (over the limit they route to a gate), the graph must be acyclic. So
 * bot-approve → base-moved → base-drift[affected] → intake is valid through b5's maxIterations.
 * Plus back-edge limits and bounded gate autoRetries.
 */
function validate(nodes: GNode[], edges: GEdge[], sp: Map<string, PV> = new Map()): Issue[] {
  const issues: Issue[] = []
  const live = edges.filter((e) => !e.removed).map((e) => e.spec)
  const kind = new Map(nodes.map((n) => [n.def.id, n.def.kind]))
  const breaker = (id: string) => kind.get(id) === 'gate' || kind.get(id) === 'wait'
  const adj = new Map<string, { to: string; edge: string }[]>()
  for (const e of live) {
    if (breaker(e.from) || breaker(e.to) || e.back) continue
    adj.set(e.from, [...(adj.get(e.from) ?? []), { to: e.to, edge: e.id }])
  }
  const state = new Map<string, 0 | 1 | 2>()
  const stack: string[] = []
  const stackEdges: string[] = []
  let cycle: { path: string[]; edges: string[] } | null = null
  const dfs = (u: string) => {
    if (cycle) return
    state.set(u, 1)
    stack.push(u)
    for (const { to, edge } of adj.get(u) ?? []) {
      if (cycle) return
      if (state.get(to) === 1) {
        const i = stack.indexOf(to)
        cycle = { path: [...stack.slice(i), to], edges: [...stackEdges.slice(i), edge] }
        return
      }
      if (!state.get(to)) {
        stackEdges.push(edge)
        dfs(to)
        stackEdges.pop()
      }
    }
    stack.pop()
    state.set(u, 2)
  }
  for (const n of nodes) if (!state.get(n.def.id)) dfs(n.def.id)
  const found = cycle as { path: string[]; edges: string[] } | null
  if (found) {
    const p = found.path
    const short = p.length > 5 ? [p[0], p[1], '…', p[p.length - 2], p[p.length - 1]] : p
    issues.push({
      level: 'error',
      kind: 'cycle',
      text: `Cycle ${short.join(' → ')} has no gate, wait or iteration limit. Every cycle must pass through a gate or wait, or be closed by a back edge with maxIterations (D-2).`,
      edgeId: found.edges[found.edges.length - 1],
    })
  }
  for (const n of nodes) {
    if (n.def.kind !== 'gate') continue
    const f = fieldsFor(n.def.id).find((x) => x.key === 'autoRetries')
    if (!f) continue
    const k = `${n.def.id}::autoRetries`
    const v = sp.has(k) ? sp.get(k) : baseValue(f).v
    if (typeof v !== 'number' || !Number.isInteger(v) || v < 0 || v > AUTO_RETRIES_MAX)
      issues.push({
        level: 'error',
        text: `Gate ${n.def.id}: autoRetries is ${v === undefined ? 'missing' : `${fmtPV(v)}, not a whole number from 0 to ${AUTO_RETRIES_MAX}`}. Unbounded automatic passes would let the loop skip the human forever (§3.2).`,
        nodeId: n.def.id,
      })
  }
  // Back edges = DFS back edges from intake (they close a loop to an ancestor). Each needs
  // maxIterations, except a loop that only cycles between a wait node and its handlers (for
  // example await-fixes → base-drift → await-fixes): the wait's own timeout bounds it.
  const color = new Map<string, 1 | 2>()
  const path: string[] = []
  const backEdges: EdgeSpec[] = []
  const walk = (u: string) => {
    color.set(u, 1)
    path.push(u)
    for (const e of live.filter((x) => x.from === u)) {
      if (color.get(e.to) === 1) {
        const loop = path.slice(path.indexOf(e.to))
        const waitBounded = !loop.includes('intake') && loop.some((id) => kind.get(id) === 'wait')
        if (!waitBounded) backEdges.push(e)
      } else if (!color.get(e.to)) walk(e.to)
    }
    path.pop()
    color.set(u, 2)
  }
  walk('intake')
  for (const e of backEdges) if (!e.back) issues.push({ level: 'warning', text: `Back edge ${e.from} → ${e.to} has no maxIterations (it would route to a gate after the default 20).`, edgeId: e.id })
  for (const n of nodes) {
    if (n.def.id !== 'intake' && !live.some((e) => e.to === n.def.id)) issues.push({ level: 'warning', text: `Node ${n.def.id} has no incoming edge; it can never run.`, nodeId: n.def.id })
    if (!live.some((e) => e.from === n.def.id)) issues.push({ level: 'warning', text: `Node ${n.def.id} has no outgoing edge; the run would stop there.`, nodeId: n.def.id })
  }
  issues.push({ level: 'info', text: 'reviewer requires runner labels [review]: 3 of 4 enrolled runners match (server2-a, server2-b, desktop).' })
  return issues
}

// ---- YAML (resolved view; the PR writes only the repo-layer overrides)

const STRUCT: Record<string, Record<string, string>> = {
  review: { over: 'groups', node: 'reviewer' },
  reviewer: { role: 'Reviewer', output: 'review-findings/1' },
  collate: { from: 'review' },
}

function valueOf(target: string, f: Field, sp: Map<string, PV>): PV | undefined {
  const k = `${target}::${f.key}`
  return sp.has(k) ? sp.get(k) : baseValue(f).v
}

function workflowYaml(g: GraphState, sp: Map<string, PV>, sd: Map<string, string>): string[] {
  const lines = ['workflow: pr-review', 'version: 1', 'nodes:']
  let reviewerDone = false
  for (const n of g.nodes) {
    const isReviewer = n.def.id.startsWith('reviewer:')
    if (isReviewer && reviewerDone) continue
    const id = isReviewer ? 'reviewer' : n.def.id
    if (isReviewer) reviewerDone = true
    lines.push(`  ${id}:`)
    lines.push(`    kind: ${n.def.kind}`)
    if (n.def.run && !['fan-out', 'gate', 'wait'].includes(n.def.kind) && !isReviewer) lines.push(`    run: ${n.def.run}`)
    for (const [k, v] of Object.entries(STRUCT[id] ?? {})) lines.push(`    ${k}: ${v}`)
    if (!isReviewer)
      for (const f of fieldsFor(n.def.id)) if (!f.file || f.file === 'workflow.yaml') {
        const v = valueOf(n.def.id, f, sp)
        if (v !== undefined) lines.push(`    ${f.key}: ${yv(v)}`)
      }
    const desc = isReviewer ? nodePurpose('reviewer') : (sd.get(n.def.id) ?? nodeBaseDesc(g, n.def.id))
    lines.push(`    description: ${JSON.stringify(desc)}`)
  }
  lines.push('edges:')
  const seen = new Set<string>()
  for (const e of g.edges) {
    if (e.removed) continue
    const s = e.spec.from.startsWith('reviewer:') ? 'reviewer' : e.spec.from
    const t = e.spec.to.startsWith('reviewer:') ? 'reviewer' : e.spec.to
    const key = `${s}${e.spec.label ? `[${e.spec.label.replace(/ /g, '')}]` : ''} -> ${t}`
    if (seen.has(key)) continue
    seen.add(key)
    const desc = sd.get(e.spec.id) ?? edgeBaseDesc(e.spec)
    if (e.spec.back || desc) {
      lines.push(`  - ${key}:`)
      if (e.spec.back) lines.push(`      maxIterations: ${e.spec.back.max}`)
      if (desc) lines.push(`      description: ${JSON.stringify(desc)}`)
    } else lines.push(`  - ${key}`)
  }
  return lines
}

function policyYaml(sp: Map<string, PV>): string[] {
  const lines: string[] = []
  for (const [target, fields] of [...Object.entries(NODE_FIELDS), ['workflow', WF_FIELDS] as const]) {
    for (const f of fields) if (f.file === 'policy.yaml') {
      const v = valueOf(target, f, sp)
      if (v !== undefined) lines.push(`${f.yamlKey ?? f.key}: ${yv(v)}`)
    }
  }
  return lines
}

function groupsYaml(g: GraphState, sp: Map<string, PV>, sd: Map<string, string>): string[] {
  const lines: string[] = []
  for (const n of g.nodes.filter((x) => x.def.id.startsWith('reviewer:'))) {
    lines.push(`${n.def.detail}:`)
    for (const f of fieldsFor(n.def.id)) lines.push(`  ${f.key}: ${yv(valueOf(n.def.id, f, sp))}`)
    lines.push(`  description: ${JSON.stringify(sd.get(n.def.id) ?? nodeBaseDesc(g, n.def.id))}`)
  }
  return lines
}

type DiffLine = { t: ' ' | '+' | '-'; s: string }

function diffLines(a: string[], b: string[]): DiffLine[] {
  const n = a.length
  const m = b.length
  const dp = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0))
  for (let i = n - 1; i >= 0; i--) for (let j = m - 1; j >= 0; j--) dp[i][j] = a[i] === b[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1])
  const out: DiffLine[] = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (a[i] === b[j]) {
      out.push({ t: ' ', s: a[i] })
      i++
      j++
    } else if (dp[i + 1][j] >= dp[i][j + 1]) out.push({ t: '-', s: a[i++] })
    else out.push({ t: '+', s: b[j++] })
  }
  while (i < n) out.push({ t: '-', s: a[i++] })
  while (j < m) out.push({ t: '+', s: b[j++] })
  return out
}

function YamlDiff({ files }: { files: { path: string; a: string[]; b: string[] }[] }) {
  const changed = files.map((f) => ({ ...f, d: diffLines(f.a, f.b) })).filter((f) => f.d.some((l) => l.t !== ' '))
  if (changed.length === 0)
    return (
      <Text size="xs" c="dimmed">
        No YAML changes.
      </Text>
    )
  return (
    <Stack gap={6}>
      {changed.map((f) => {
        const keep = f.d.map((l, i) => l.t !== ' ' || f.d.slice(Math.max(0, i - 2), i + 3).some((x) => x.t !== ' '))
        return (
          <Box key={f.path} style={{ border: '1px solid var(--mantine-color-dark-4)', borderRadius: 4, overflow: 'hidden' }}>
            <Text size="10px" ff="monospace" px={6} py={2} bg="dark.6">
              {f.path}
            </Text>
            <Box ff="monospace" fz={10}>
              {f.d.map((l, i) =>
                keep[i] ? (
                  <Box
                    key={i}
                    px={6}
                    style={{
                      whiteSpace: 'pre-wrap',
                      wordBreak: 'break-all',
                      background: l.t === '+' ? 'rgba(46,160,67,0.18)' : l.t === '-' ? 'rgba(248,81,73,0.18)' : undefined,
                      color: l.t === ' ' ? 'var(--mantine-color-dark-1)' : undefined,
                    }}
                  >
                    {l.t} {l.s}
                  </Box>
                ) : i > 0 && keep[i - 1] ? (
                  <Box key={i} px={6} c="dimmed" bg="dark.7">
                    ⋯
                  </Box>
                ) : null,
              )}
            </Box>
          </Box>
        )
      })}
    </Stack>
  )
}

// ---- parameter inputs

function ParamInput({ f, value, onChange }: { f: Field; value?: PV; onChange: (v: PV) => void }) {
  if (f.type === 'select')
    return <Select size="xs" data={f.options ?? []} value={typeof value === 'string' ? value : null} allowDeselect={false} onChange={(v) => v !== null && onChange(v)} />
  if (f.type === 'multi') return <MultiSelect size="xs" data={f.options ?? []} value={Array.isArray(value) ? value : []} onChange={onChange} />
  if (f.type === 'number')
    return (
      <NumberInput
        size="xs"
        value={typeof value === 'number' ? value : ''}
        min={f.min}
        max={f.max}
        step={f.step}
        decimalScale={f.step !== undefined && f.step < 1 ? 2 : undefined}
        onChange={(v) => typeof v === 'number' && onChange(v)}
      />
    )
  if (f.type === 'bool') return <Switch size="xs" checked={value === true} onChange={(e) => onChange(e.currentTarget.checked)} />
  return <TextInput size="xs" value={typeof value === 'string' ? value : ''} onChange={(e) => onChange(e.currentTarget.value)} />
}

function LayerBadges({ f, staged }: { f: Field; staged?: PV }) {
  const b = baseValue(f)
  const parent = f.values.team ?? f.values.common
  const overrides = f.values.repo !== undefined && parent !== undefined && !same(f.values.repo, parent)
  const isStaged = staged !== undefined && !same(staged, b.v)
  return (
    <Group gap={4} wrap="nowrap">
      {b.layer && (
        <Tooltip label={`Value comes from ${LAYER_META[b.layer].hint}`}>
          <Badge size="xs" variant="light" color={LAYER_META[b.layer].color} tt="none">
            {LAYER_META[b.layer].label}
          </Badge>
        </Tooltip>
      )}
      {overrides && (
        <Tooltip label={`The repo overrides ${f.values.team !== undefined ? 'payments-core' : 'common@v14'} (${fmtPV(parent)})`}>
          <Badge size="xs" variant="outline" color="active" tt="none">
            overrides
          </Badge>
        </Tooltip>
      )}
      {isStaged && (
        <Tooltip label={`Staged for the repo layer. Was ${fmtPV(b.v)}${b.layer ? ` (${LAYER_META[b.layer].label})` : ''}.`}>
          <Badge size="xs" variant="filled" color="warning" tt="none">
            staged
          </Badge>
        </Tooltip>
      )}
    </Group>
  )
}

function ParamRow({ f, staged, onChange }: { f: Field; staged?: PV; onChange: (v: PV) => void }) {
  const b = baseValue(f)
  const isStaged = staged !== undefined && !same(staged, b.v)
  return (
    <Box py={3} pl={6} style={{ borderLeft: `2px solid ${isStaged ? 'var(--mantine-color-warning-5)' : 'transparent'}` }}>
      <Group justify="space-between" gap={4} wrap="nowrap" mb={2}>
        <Text size="xs" fw={500} truncate>
          {f.label}
        </Text>
        <LayerBadges f={f} staged={staged} />
      </Group>
      <ParamInput f={f} value={staged ?? b.v} onChange={onChange} />
      {(f.help || f.file === 'policy.yaml' || f.file === 'groups') && (
        <Text size="10px" c="dimmed" mt={1}>
          {f.file === 'policy.yaml' ? `policy.yaml ${f.yamlKey ?? f.key}. ` : f.file === 'groups' ? 'groups/<group>.yaml. ' : ''}
          {f.help}
        </Text>
      )}
    </Box>
  )
}

function DescriptionField({ base, staged, onChange, required = true }: { base: string; staged?: string; onChange: (v: string) => void; required?: boolean }) {
  const value = staged ?? base
  const isStaged = staged !== undefined && staged !== base
  return (
    <Box pl={6} style={{ borderLeft: `2px solid ${isStaged ? 'var(--mantine-color-warning-5)' : 'transparent'}` }}>
      <Group justify="space-between" gap={4} mb={2} wrap="nowrap">
        <Text size="xs" fw={600}>
          Description{' '}
          <Text span size="10px" c="dimmed" fw={400}>
            for humans · never sent to models
          </Text>
        </Text>
        {isStaged && (
          <Badge size="xs" color="warning" variant="filled" tt="none">
            staged
          </Badge>
        )}
      </Group>
      <Textarea size="xs" autosize minRows={2} maxRows={5} value={value} onChange={(e) => onChange(e.currentTarget.value)} placeholder={required ? 'Every step needs a description' : 'Optional: why this edge exists'} error={required && !value.trim() ? 'No description' : undefined} />
    </Box>
  )
}

// ---- change list

function ItemLine({ it, g }: { it: ChangeItem; g: GraphState }) {
  const edgeName = (id: string) => {
    const e = g.edges.find((x) => x.spec.id === id)?.spec
    return e ? (
      <>
        <StepName id={e.from} />
        {e.label ? `[${e.label}]` : ''} → <StepName id={e.to} />
      </>
    ) : (
      id
    )
  }
  const targetName = (id: string) => (g.nodes.some((n) => n.def.id === id) ? <StepName id={id} /> : id === 'workflow' ? 'policy' : edgeName(id))
  if (it.kind === 'add-node')
    return (
      <Text size="xs" component="div">
        <Badge size="xs" color="success" variant="light" mr={4} leftSection={<TbPlus size={9} />}>
          node
        </Badge>
        <StepName id={it.node.id} /> ({it.node.kind}) · <Text span c="dimmed" size="xs">{it.why}</Text>
      </Text>
    )
  if (it.kind === 'add-edge')
    return (
      <Text size="xs" component="div">
        <Badge size="xs" color="success" variant="light" mr={4} leftSection={<TbPlus size={9} />}>
          edge
        </Badge>
        <StepName id={it.edge.from} />
        {it.edge.label ? `[${it.edge.label}]` : ''} → <StepName id={it.edge.to} />
        {it.edge.back ? ` · maxIterations ${it.edge.back.max}` : ''}
      </Text>
    )
  if (it.kind === 'remove-edge')
    return (
      <Text size="xs" component="div">
        <Badge size="xs" color="danger" variant="light" mr={4} leftSection={<TbMinus size={9} />}>
          edge
        </Badge>
        {edgeName(it.edgeId)} · <Text span c="dimmed" size="xs">{it.why}</Text>
      </Text>
    )
  if (it.kind === 'param')
    return (
      <Text size="xs" component="div">
        <Badge size="xs" color="warning" variant="light" mr={4} leftSection={<TbAdjustments size={9} />}>
          param
        </Badge>
        {targetName(it.target)}.<Text span ff="monospace" size="xs">{it.key}</Text>: <Text span c="dimmed" size="xs">{fmtPV(it.from)}</Text> → <b>{fmtPV(it.to)}</b>
      </Text>
    )
  return (
    <Text size="xs" component="div">
      <Badge size="xs" color="warning" variant="light" mr={4} leftSection={<TbPencil size={9} />}>
        description
      </Badge>
      {targetName(it.target)}: <Text span c="gray.4" size="xs">"{it.to.length > 70 ? `${it.to.slice(0, 70)}…` : it.to || '(empty)'}"</Text>
    </Text>
  )
}

const INITIAL_TURNS: Turn[] = [{ prompt: CHIPS[2].text, reply: PROPOSALS.p3.reply, model: 'sonnet', proposalId: 'p3', canned: false, at: '16:42' }]
const quickFixTurn = (fix: QuickFix, at: string): Turn => {
  const p = PROPOSALS[QUICK_FIXES.p3[fix]]
  return { prompt: `Quick fix: ${QUICK_FIX_LABEL[fix]}`, reply: p.reply, model: p.model, proposalId: p.id, canned: false, at, fix }
}
const FIXED_TURNS: Turn[] = [...INITIAL_TURNS, quickFixTurn('gate', '16:43')]

function Editor({ start }: { start: 'invalid' | 'fresh' | 'fixed' }) {
  const staged = start !== 'fresh'
  const [view, setView] = useState<View>(staged ? 'diff' : 'current')
  const [proposal, setProposal] = useState<Proposal | null>(start === 'fixed' ? PROPOSALS.p3fix : start === 'invalid' ? PROPOSALS.p3 : null)
  const [turns, setTurns] = useState<Turn[]>(start === 'fixed' ? FIXED_TURNS : start === 'invalid' ? INITIAL_TURNS : [])
  const [manual, setManual] = useState<ChangeItem[]>([])
  const [excluded, setExcluded] = useState<string[]>([])
  const [busy, setBusy] = useState<string | null>(null)
  const [prompt, setPrompt] = useState('')
  const [refine, setRefine] = useState('')
  const [sel, setSel] = useState<Sel>(start === 'fixed' ? { type: 'node', id: 'disputed-findings' } : staged ? { type: 'node', id: 'verify' } : { type: 'node', id: 'route' })
  const [tab, setTab] = useState<string | null>(staged ? 'staging' : 'node')
  const [yamlMode, setYamlMode] = useState<'diff' | 'full'>('diff')

  const items = [...(proposal?.items ?? []), ...manual]
  const included = items.filter((i) => !excluded.includes(i.id))
  const sp = paramMap(included)
  const sd = descMap(included)
  const g = stageGraph(included)
  const shown = view === 'current' ? BASE_GRAPH : g
  const resolver: StepResolver = (id) => (view === 'current' ? baseResolve(id) : makeStep(id, g, sp, sd))

  /** A gate's automatic passes as shown on its node and on the edge its automatic decision takes. */
  const gateAuto = (id: string): { note: string; decision?: PV } | undefined => {
    const fs = fieldsFor(id)
    const fr = fs.find((f) => f.key === 'autoRetries')
    if (!fr) return undefined
    const fd = fs.find((f) => f.key === 'autoDecision')
    const n = valueOf(id, fr, sp)
    const note = typeof n === 'number' && Number.isInteger(n) && n >= 0 && n <= AUTO_RETRIES_MAX ? (n > 0 ? `auto ×${n}, then human` : 'human only') : 'auto × unbounded'
    return { note, decision: fd ? valueOf(id, fd, sp) : undefined }
  }
  const edgeNote = (e: EdgeSpec) => {
    const a = gateAuto(e.from)
    return a && a.decision === e.label ? a.note : undefined
  }

  const descOf = (id: string) => sd.get(id) ?? nodeBaseDesc(g, id)
  const issues = [
    ...validate(g.nodes, g.edges, sp),
    ...g.nodes.filter((n) => !descOf(n.def.id).trim()).map((n): Issue => ({ level: 'warning', text: `Node ${n.def.id} has no description.`, nodeId: n.def.id })),
  ]
  const errors = issues.filter((i) => i.level === 'error')
  const warnings = issues.filter((i) => i.level === 'warning')
  const invalidEdges = new Set(errors.flatMap((i) => (i.edgeId ? [i.edgeId] : [])))
  const changedNodes = new Set(included.flatMap((i) => (i.kind === 'param' || i.kind === 'description' ? [i.target] : [])))

  const flowNodes: FlowNode[] = shown.nodes.map((n) => ({
    id: n.def.id,
    type: 'run',
    position: n.pos,
    draggable: false,
    selected: sel?.type === 'node' && sel.id === n.def.id,
    data: {
      def: gateAuto(n.def.id) && n.def.detail ? { ...n.def, detail: n.def.detail.replace(/^auto ×\d+, then human|^human only/, gateAuto(n.def.id)!.note) } : n.def,
      mode: 'edit',
      diff: view === 'diff' && n.isNew ? 'added' : undefined,
      changed: view !== 'current' && changedNodes.has(n.def.id),
      description: view === 'current' ? nodePurpose(n.def.id) : descOf(n.def.id),
    },
  }))
  const flowEdges: FlowEdge[] = shown.edges
    .filter((e) => (view === 'staged' ? !e.removed : true))
    .map((e) =>
      toFlowEdge(e.spec, {
        taken: true,
        diff: view === 'diff' ? (e.isNew ? 'added' : e.removed ? 'removed' : undefined) : undefined,
        invalid: view !== 'current' && invalidEdges.has(e.spec.id),
        description: view === 'current' ? edgeBaseDesc(e.spec) : (sd.get(e.spec.id) ?? edgeBaseDesc(e.spec)),
        selected: sel?.type === 'edge' && sel.id === e.spec.id,
        note: view === 'current' ? undefined : edgeNote(e.spec),
      }),
    )
  const sections = useSections(ALL_OPEN)
  const sg = sectionize(flowNodes, flowEdges, sections.collapse, sections.toggle, 'edit')
  const onInit = useFitOnChange(sg.bounds)

  const ask = (text: string, fix = false) => {
    if (!text.trim() || busy) return
    const current = proposal
    setBusy(text)
    setTab('staging')
    setPrompt('')
    setRefine('')
    window.setTimeout(() => {
      const wantsLimit = !fix && current?.id === 'p3' && /limit|iteration|bound/i.test(text)
      const wantsFix = fix || (current?.id === 'p3' && /fix|valid|gate|wait|loop/i.test(text))
      const p = wantsLimit ? PROPOSALS.p3limit : wantsFix ? PROPOSALS.p3fix : matchProposal(text)
      const chip = CHIPS.some((c) => c.text === text)
      setTurns((t) => [
        ...t,
        {
          prompt: text,
          reply: p ? p.reply : 'No canned proposal matches that prompt in this mock. Try one of the example prompts.',
          model: p?.model ?? 'sonnet',
          proposalId: p?.id,
          canned: !chip && !fix,
          at: 'just now',
        },
      ])
      if (p) {
        setProposal(p)
        setExcluded([])
        setView('diff')
      }
      setBusy(null)
    }, 900)
  }

  const fixesFor = proposal ? QUICK_FIXES[proposal.id] : undefined
  const applyFix = (fix: QuickFix) => {
    if (!fixesFor || busy) return
    const p = PROPOSALS[fixesFor[fix]]
    setTurns((t) => [...t, quickFixTurn(fix, 'just now')])
    setProposal(p)
    setExcluded([])
    setView('diff')
    if (fix === 'gate') setSel({ type: 'node', id: 'disputed-findings' })
  }

  const stage = (it: ChangeItem, revert: boolean) => setManual((m) => (revert ? m.filter((x) => x.id !== it.id) : [...m.filter((x) => x.id !== it.id), it]))
  const aiTouches = (pred: (i: ChangeItem) => boolean) => (proposal?.items ?? []).some((i) => pred(i) && !excluded.includes(i.id))
  const editParam = (target: string, f: Field, v: PV) => {
    const b = baseValue(f).v
    const ai = aiTouches((i) => i.kind === 'param' && i.target === target && i.key === f.key)
    stage({ id: `you:${target}:${f.key}`, kind: 'param', target, key: f.key, label: f.label, from: b, to: v, source: 'you' }, same(v, b) && !ai)
  }
  const editDesc = (target: string, base: string, v: string) => {
    const ai = aiTouches((i) => i.kind === 'description' && i.target === target)
    stage({ id: `you:desc:${target}`, kind: 'description', target, from: base, to: v, source: 'you' }, v === base && !ai)
  }
  const discard = () => {
    setProposal(null)
    setManual([])
    setExcluded([])
    setView('current')
  }
  const save = () => {
    notifications.show({
      color: 'success',
      title: 'Revision r8 saved',
      message: 'PR #631 opened on payments-api: .antiphon/review/workflow.yaml, policy.yaml and groups/. The replay evaluation is attached to the PR.',
    })
    discard()
  }

  const selNode = sel?.type === 'node' ? g.nodes.find((n) => n.def.id === sel.id) : undefined
  const selEdge = sel?.type === 'edge' ? g.edges.find((e) => e.spec.id === sel.id) : undefined
  const yamlFiles = [
    { path: '.antiphon/review/workflow.yaml (resolved)', a: workflowYaml(BASE_GRAPH, EMPTY_PARAMS, EMPTY_DESC), b: workflowYaml(g, sp, sd) },
    { path: '.antiphon/review/policy.yaml (resolved)', a: policyYaml(EMPTY_PARAMS), b: policyYaml(sp) },
    { path: '.antiphon/review/groups/*.yaml (resolved)', a: groupsYaml(BASE_GRAPH, EMPTY_PARAMS, EMPTY_DESC), b: groupsYaml(g, sp, sd) },
  ]
  const replay = errors.length ? 'Not estimated: the staged graph is invalid.' : proposal ? proposal.replay : manual.length ? `Parameter edits only: replay runs on save (20 cases, ≈4 min, ≈${fmtCost(0.3)}).` : ''

  return (
    <StepCtx.Provider value={resolver}>
      <Stack gap="xs">
        <style>{PULSE_CSS}</style>
        <Group justify="space-between" wrap="nowrap">
          <Box>
            <Text fw={700} size="lg">
              Workflow editor · pr-review
            </Text>
            <Text size="xs" c="dimmed">
              payments-api repo layer over common@v14 → payments-core · revision r7 sha256:4be1c09… · the graph is read-only: change it with a prompt, tune steps in the inspector
            </Text>
          </Box>
          <Group gap={8} wrap="nowrap">
            <Text size="xs" c="dimmed">
              Graph shows
            </Text>
            <SegmentedControl
              size="xs"
              value={view}
              onChange={(v) => setView(v as View)}
              data={[
                { value: 'current', label: 'Current r7' },
                { value: 'staged', label: 'Staged' },
                { value: 'diff', label: `Diff${included.length ? ` (${included.length})` : ''}` },
              ]}
            />
          </Group>
        </Group>

        <Paper withBorder radius="md" p="xs">
          <Group gap={8} align="flex-start" wrap="nowrap">
            <Box pt={6} c="grape.4">
              <TbSparkles size={18} />
            </Box>
            <Textarea
              style={{ flex: 1 }}
              size="sm"
              autosize
              minRows={1}
              maxRows={3}
              placeholder="Describe the change to pr-review…  (Ctrl+Enter to propose)"
              value={prompt}
              onChange={(e) => setPrompt(e.currentTarget.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) ask(prompt)
              }}
            />
            <Button leftSection={<TbSend size={14} />} disabled={!prompt.trim() || !!busy} onClick={() => ask(prompt)}>
              Propose
            </Button>
          </Group>
          <Group gap={6} mt={6} wrap="wrap">
            <Text size="xs" c="dimmed">
              Try:
            </Text>
            {CHIPS.map((c) => (
              <UnstyledButton key={c.id} onClick={() => ask(c.text)} disabled={!!busy}>
                <Badge size="sm" variant="outline" color="grape" tt="none" style={{ cursor: 'pointer' }}>
                  {c.text}
                </Badge>
              </UnstyledButton>
            ))}
            <Group gap={4} ml="auto">
              <Text size="xs" c="dimmed">
                drafts with
              </Text>
              <ModelBadge model="sonnet" withProvider />
              <Text size="xs" c="dimmed">
                · proposals never apply until saved
              </Text>
            </Group>
          </Group>
        </Paper>

        <SectionBar c={sections.collapse} onToggle={sections.toggle} onAll={sections.setAll} fanN={shown.nodes.filter((n) => n.def.id.startsWith('reviewer:')).length} />
        <Box style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) 430px', gap: 12 }}>
          <Paper withBorder radius="md" h="calc(100vh - 360px)" mih={480} style={{ overflow: 'hidden', position: 'relative' }}>
            <ReactFlow
              nodes={sg.nodes}
              edges={sg.edges}
              nodeTypes={nodeTypes}
              edgeTypes={edgeTypes}
              colorMode="dark"
              onInit={onInit}
              minZoom={0.3}
              nodesDraggable={false}
              nodesConnectable={false}
              deleteKeyCode={null}
              onNodeClick={(_, n) => {
                if (n.type !== 'run') return
                setSel({ type: 'node', id: n.id })
                setTab('node')
              }}
              onEdgeClick={(_, e) => {
                if (e.id.startsWith('m:')) return
                setSel({ type: 'edge', id: e.id })
                setTab('node')
              }}
              onPaneClick={() => setSel(null)}
            >
              <Background gap={16} size={1} color="#2c2e33" />
              <Controls showInteractive={false} />
            </ReactFlow>
            <Group gap="md" px={8} py={4} style={{ position: 'absolute', top: 6, left: 8, background: 'rgba(26,27,30,0.85)', borderRadius: 6 }}>
              <Text size="xs" fw={700}>
                {view === 'current' ? 'Current r7' : view === 'staged' ? 'Staged (what r8 would be)' : 'Diff r7 → staged'}
              </Text>
              {view === 'diff' && (
                <>
                  <Text size="xs" c="success.4">
                    + added (green dashed)
                  </Text>
                  <Text size="xs" c="danger.4" td="line-through">
                    − removed
                  </Text>
                  <Group gap={4}>
                    <Box w={8} h={8} style={{ borderRadius: 4, background: DIFF_HEX.changed }} />
                    <Text size="xs" c="warning.4">
                      parameters or description staged
                    </Text>
                  </Group>
                </>
              )}
            </Group>
          </Paper>

          <Paper withBorder radius="md" p="xs" h="calc(100vh - 330px)" mih={480}>
            <Tabs value={tab} onChange={setTab} h="100%" style={{ display: 'flex', flexDirection: 'column' }}>
              <Tabs.List>
                <Tabs.Tab value="staging" rightSection={included.length ? <Badge size="xs" color={errors.length ? 'danger' : 'warning'}>{included.length}</Badge> : undefined}>
                  Staging
                </Tabs.Tab>
                <Tabs.Tab value="node">Step</Tabs.Tab>
                <Tabs.Tab value="workflow">Workflow parameters</Tabs.Tab>
              </Tabs.List>

              <Tabs.Panel value="staging" pt="xs" style={{ flex: 1, minHeight: 0 }}>
                <ScrollArea h="100%" offsetScrollbars>
                  <Stack gap="sm">
                    <Box>
                      <SectionLabel>Conversation</SectionLabel>
                      {turns.length === 0 && !busy && (
                        <Text size="xs" c="dimmed">
                          Nothing yet. Describe a change in the prompt bar, or edit a step's parameters; both land here before anything is saved.
                        </Text>
                      )}
                      <Stack gap={6}>
                        {turns.map((t, i) => (
                          <Box key={i}>
                            <Paper p={6} radius="sm" bg="dark.6">
                              <Text size="xs">
                                <b>You</b> <Text span c="dimmed" size="xs">· {t.at}</Text>
                              </Text>
                              <Text size="xs">{t.prompt}</Text>
                            </Paper>
                            <Paper p={6} radius="sm" mt={2} style={{ borderLeft: '3px solid var(--mantine-color-grape-6)' }}>
                              <Group gap={6} mb={2}>
                                {t.fix ? (
                                  <Badge size="xs" variant="outline" color="gray" tt="none" leftSection={<TbWand size={10} />}>
                                    quick fix · no model call
                                  </Badge>
                                ) : (
                                  <ModelBadge model={t.model} withProvider />
                                )}
                                {t.proposalId && (
                                  <Badge size="xs" variant="light" color={t.proposalId === proposal?.id ? 'grape' : 'gray'} tt="none">
                                    {PROPOSALS[t.proposalId].title}
                                    {t.proposalId === proposal?.id ? ' · staged' : ' · superseded'}
                                  </Badge>
                                )}
                                {t.canned && (
                                  <Text size="10px" c="dimmed">
                                    mock: canned response
                                  </Text>
                                )}
                              </Group>
                              <Text size="xs">{t.reply}</Text>
                            </Paper>
                          </Box>
                        ))}
                        {busy && (
                          <Group gap={6}>
                            <Loader size="xs" color="grape" />
                            <Text size="xs" c="dimmed">
                              sonnet seat is drafting a proposal for "{busy.length > 60 ? `${busy.slice(0, 60)}…` : busy}"
                            </Text>
                          </Group>
                        )}
                      </Stack>
                    </Box>

                    {items.length > 0 && (
                      <Box>
                        <SectionLabel right={<Text size="xs" c="dimmed">{included.length}/{items.length} included</Text>}>Staged changes</SectionLabel>
                        <Stack gap={4}>
                          {items.map((it) => (
                            <Group key={it.id} gap={6} wrap="nowrap" align="flex-start" style={{ opacity: excluded.includes(it.id) ? 0.45 : 1 }}>
                              <Switch
                                size="xs"
                                mt={2}
                                checked={!excluded.includes(it.id)}
                                onChange={(e) => {
                                  const on = e.currentTarget.checked
                                  setExcluded((x) => (on ? x.filter((y) => y !== it.id) : [...x, it.id]))
                                }}
                              />
                              <Box style={{ flex: 1, minWidth: 0 }}>
                                <ItemLine it={it} g={g} />
                                <Text size="10px" c="dimmed">
                                  {'source' in it && it.source === 'you' ? 'you · inspector' : `AI · ${proposal?.title ?? ''}`}
                                </Text>
                              </Box>
                              {'source' in it && it.source === 'you' && (
                                <UnstyledButton onClick={() => setManual((m) => m.filter((x) => x.id !== it.id))} aria-label="Remove change">
                                  <TbX size={12} />
                                </UnstyledButton>
                              )}
                            </Group>
                          ))}
                        </Stack>
                      </Box>
                    )}

                    {items.length > 0 && (
                      <Box>
                        <SectionLabel>Validation of the staged graph</SectionLabel>
                        {errors.length === 0 && warnings.length === 0 && (
                          <Text size="xs" c="success.4">
                            Valid: every cycle passes through a gate or wait or is closed by a back edge with maxIterations, every gate’s autoRetries is bounded, every step has a description.
                          </Text>
                        )}
                        <Stack gap={4}>
                          {[...errors, ...warnings].map((i, n) => (
                            <Group key={n} gap={6} wrap="nowrap" align="flex-start">
                              <Box c={i.level === 'error' ? 'danger.5' : 'warning.5'} style={{ display: 'flex', marginTop: 2 }}>
                                {i.level === 'error' ? <TbAlertCircle size={14} /> : <TbAlertTriangle size={14} />}
                              </Box>
                              <Box style={{ flex: 1, minWidth: 0 }}>
                                <Text size="xs">{i.text}</Text>
                                {i.kind === 'cycle' && fixesFor && (
                                  <Group gap={6} mt={4}>
                                    <Text size="10px" c="dimmed">
                                      Quick fix:
                                    </Text>
                                    {(['gate', 'limit'] as QuickFix[]).map((f) => (
                                      <Tooltip
                                        key={f}
                                        multiline
                                        w={260}
                                        label={
                                          f === 'gate'
                                            ? 'Route the edge through a disputed-findings gate: re-review → packs (auto ×1, then human), drop or keep → publish.'
                                            : 'Put maxIterations 2 on the edge; over the limit the human-review gate opens.'
                                        }
                                      >
                                        <Button size="compact-xs" variant="light" color="danger" onClick={() => applyFix(f)} disabled={!!busy}>
                                          {QUICK_FIX_LABEL[f]}
                                        </Button>
                                      </Tooltip>
                                    ))}
                                  </Group>
                                )}
                              </Box>
                            </Group>
                          ))}
                        </Stack>
                        {errors.length > 0 && (
                          <Button mt={6} size="xs" variant="light" color="grape" leftSection={<TbWand size={14} />} onClick={() => ask('Fix the validation error', true)} disabled={!!busy}>
                            Ask AI to fix
                          </Button>
                        )}
                      </Box>
                    )}

                    {replay && (
                      <Box>
                        <SectionLabel>Replay evaluation (before activation)</SectionLabel>
                        <Text size="xs" c={errors.length ? 'dimmed' : undefined}>
                          {replay}
                        </Text>
                      </Box>
                    )}

                    {items.length > 0 && (
                      <Box>
                        <SectionLabel
                          right={
                            <SegmentedControl
                              size="xs"
                              value={yamlMode}
                              onChange={(v) => setYamlMode(v as 'diff' | 'full')}
                              data={[
                                { value: 'diff', label: 'Diff' },
                                { value: 'full', label: 'Staged workflow.yaml' },
                              ]}
                            />
                          }
                        >
                          YAML
                        </SectionLabel>
                        {yamlMode === 'diff' ? (
                          <YamlDiff files={yamlFiles} />
                        ) : (
                          <Code block fz={10}>
                            {yamlFiles[0].b.join('\n')}
                          </Code>
                        )}
                        <Text size="10px" c="dimmed" mt={2}>
                          Resolved view. The PR writes only the repo-layer overrides.
                        </Text>
                      </Box>
                    )}

                    {proposal && (
                      <Box>
                        <SectionLabel>Refine</SectionLabel>
                        <Group gap={6} wrap="nowrap" align="flex-start">
                          <Textarea
                            style={{ flex: 1 }}
                            size="xs"
                            autosize
                            minRows={1}
                            maxRows={3}
                            placeholder={`Follow up on "${proposal.title}"…`}
                            value={refine}
                            onChange={(e) => setRefine(e.currentTarget.value)}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) ask(refine)
                            }}
                          />
                          <Button size="xs" variant="light" disabled={!refine.trim() || !!busy} onClick={() => ask(refine)}>
                            Refine
                          </Button>
                        </Group>
                      </Box>
                    )}
                  </Stack>
                </ScrollArea>
              </Tabs.Panel>

              <Tabs.Panel value="node" pt="xs" style={{ flex: 1, minHeight: 0 }}>
                <ScrollArea h="100%" offsetScrollbars>
                  {selNode && (
                    <Stack gap={6}>
                      <Group gap={6} wrap="nowrap">
                        <Box c={`${NODE_KIND_META[selNode.def.kind].color}.4`} style={{ display: 'flex' }}>
                          {KIND_ICON[selNode.def.kind]}
                        </Box>
                        <StepName id={selNode.def.id} />
                        <Badge size="xs" color={NODE_KIND_META[selNode.def.kind].color} variant="light">
                          {selNode.def.kind}
                        </Badge>
                        {selNode.def.run && <Code fz={10}>{selNode.def.run}</Code>}
                        {selNode.isNew && (
                          <Badge size="xs" color="success" variant="filled">
                            staged
                          </Badge>
                        )}
                      </Group>
                      <DescriptionField base={nodeBaseDesc(g, selNode.def.id)} staged={sd.get(selNode.def.id)} onChange={(v) => editDesc(selNode.def.id, nodeBaseDesc(g, selNode.def.id), v)} />
                      <Divider label={<Text size="xs">Parameters</Text>} labelPosition="left" />
                      {fieldsFor(selNode.def.id).length === 0 && (
                        <Text size="xs" c="dimmed">
                          This step takes no parameters.
                        </Text>
                      )}
                      {fieldsFor(selNode.def.id).map((f) => (
                        <ParamRow key={f.key} f={f} staged={sp.get(`${selNode.def.id}::${f.key}`)} onChange={(v) => editParam(selNode.def.id, f, v)} />
                      ))}
                      <Divider label={<Text size="xs">Structure</Text>} labelPosition="left" />
                      <Text size="xs">
                        In:{' '}
                        {g.edges
                          .filter((e) => !e.removed && e.spec.to === selNode.def.id)
                          .map((e, i) => (
                            <span key={e.spec.id}>
                              {i > 0 && ', '}
                              <StepName id={e.spec.from} />
                              {e.spec.label ? `[${e.spec.label}]` : ''}
                            </span>
                          ))}
                      </Text>
                      <Text size="xs">
                        Out:{' '}
                        {g.edges
                          .filter((e) => !e.removed && e.spec.from === selNode.def.id)
                          .map((e, i) => (
                            <span key={e.spec.id}>
                              {i > 0 && ', '}
                              {e.spec.label ? `[${e.spec.label}] ` : ''}
                              <StepName id={e.spec.to} />
                            </span>
                          ))}
                      </Text>
                      <Alert color="gray" variant="light" p={6} icon={<TbSparkles size={14} />}>
                        <Text size="xs">Nodes and edges change through the prompt bar, so every structural change comes with a validated proposal.</Text>
                        <Button size="compact-xs" variant="subtle" color="grape" mt={4} onClick={() => setPrompt(`For ${selNode.def.id}: `)}>
                          Ask AI about this step
                        </Button>
                      </Alert>
                    </Stack>
                  )}
                  {selEdge && (
                    <Stack gap={6}>
                      <Text size="sm">
                        <StepName id={selEdge.spec.from} />
                        {selEdge.spec.label ? `[${selEdge.spec.label}]` : ''} → <StepName id={selEdge.spec.to} />
                        {selEdge.spec.back && (
                          <Badge size="xs" color="pink" variant="light" ml={6}>
                            back edge
                          </Badge>
                        )}
                        {selEdge.isNew && (
                          <Badge size="xs" color="success" variant="filled" ml={6}>
                            staged
                          </Badge>
                        )}
                      </Text>
                      <DescriptionField
                        base={edgeBaseDesc(selEdge.spec)}
                        staged={sd.get(selEdge.spec.id)}
                        required={false}
                        onChange={(v) => editDesc(selEdge.spec.id, edgeBaseDesc(selEdge.spec), v)}
                      />
                      {fieldsFor(selEdge.spec.id).map((f) => (
                        <ParamRow key={f.key} f={f} staged={sp.get(`${selEdge.spec.id}::${f.key}`)} onChange={(v) => editParam(selEdge.spec.id, f, v)} />
                      ))}
                      {selEdge.isNew && selEdge.spec.back && (
                        <Text size="xs" c="dimmed">
                          maxIterations {selEdge.spec.back.max} (proposed). Refine the prompt to change it.
                        </Text>
                      )}
                      {invalidEdges.has(selEdge.spec.id) && (
                        <Text size="xs" c="danger.4">
                          This edge closes a cycle with no gate, wait or iteration limit. Use a quick fix in the Staging tab.
                        </Text>
                      )}
                    </Stack>
                  )}
                  {!sel && (
                    <Text size="xs" c="dimmed">
                      Select a step or an edge on the graph.
                    </Text>
                  )}
                </ScrollArea>
              </Tabs.Panel>

              <Tabs.Panel value="workflow" pt="xs" style={{ flex: 1, minHeight: 0 }}>
                <ScrollArea h="100%" offsetScrollbars>
                  <Stack gap="sm">
                    <Box>
                      <SectionLabel>Rounds: posting bar</SectionLabel>
                      <Table fz="xs" verticalSpacing={4} horizontalSpacing={4}>
                        <Table.Thead>
                          <Table.Tr>
                            <Table.Th w={40}>Round</Table.Th>
                            <Table.Th>Min severity</Table.Th>
                            <Table.Th>Min confidence</Table.Th>
                            <Table.Th>Max nits</Table.Th>
                          </Table.Tr>
                        </Table.Thead>
                        <Table.Tbody>
                          {WF_ROUNDS.map((r) => (
                            <Table.Tr key={r.round}>
                              <Table.Td>
                                <Text size="xs" fw={700}>
                                  {r.round}
                                </Text>
                              </Table.Td>
                              {[r.sev, r.conf, r.extra].map((f, i) =>
                                f ? (
                                  <Table.Td key={f.key}>
                                    <ParamInput f={f} value={sp.get(`workflow::${f.key}`) ?? baseValue(f).v} onChange={(v) => editParam('workflow', f, v)} />
                                    <Box mt={2}>
                                      <LayerBadges f={f} staged={sp.get(`workflow::${f.key}`)} />
                                    </Box>
                                  </Table.Td>
                                ) : (
                                  <Table.Td key={i}>
                                    <Text size="xs" c="dimmed">
                                      —
                                    </Text>
                                  </Table.Td>
                                ),
                              )}
                            </Table.Tr>
                          ))}
                        </Table.Tbody>
                      </Table>
                    </Box>
                    {WF_SECTIONS.map((s) => (
                      <Box key={s.title}>
                        <SectionLabel>{s.title}</SectionLabel>
                        {s.fields.map((f) => (
                          <ParamRow key={f.key} f={f} staged={sp.get(`workflow::${f.key}`)} onChange={(v) => editParam('workflow', f, v)} />
                        ))}
                      </Box>
                    ))}
                    <Text size="10px" c="dimmed">
                      All of these live in policy.yaml. Edits stage like any other change; the graph does not change.
                    </Text>
                  </Stack>
                </ScrollArea>
              </Tabs.Panel>
            </Tabs>
          </Paper>
        </Box>

        <Paper withBorder radius="md" px="sm" py={6}>
          <Group justify="space-between" wrap="nowrap">
            <Group gap="md" wrap="nowrap">
              <Text size="xs">
                <b>{included.length}</b> of {items.length} changes staged
              </Text>
              {items.length > 0 && (
                <Text size="xs" c={errors.length ? 'danger.4' : warnings.length ? 'warning.4' : 'success.4'}>
                  {errors.length ? `${errors.length} error` : warnings.length ? `${warnings.length} warning${warnings.length > 1 ? 's' : ''}` : 'valid'}
                </Text>
              )}
              {replay && !errors.length && (
                <Text size="xs" c="dimmed" truncate maw={520}>
                  {replay}
                </Text>
              )}
            </Group>
            <Group gap={6} wrap="nowrap">
              <Button size="xs" variant="default" leftSection={<TbTrash size={14} />} disabled={items.length === 0} onClick={discard}>
                Discard
              </Button>
              <Button size="xs" leftSection={<TbGitPullRequest size={14} />} disabled={errors.length > 0 || included.length === 0} onClick={save}>
                Save as revision r8 + open PR to .antiphon/review/workflow.yaml
              </Button>
            </Group>
          </Group>
        </Paper>
      </Stack>
    </StepCtx.Provider>
  )
}

// ---------------------------------------------------------------- (C) iteration timeline

const SCALE = 80 // px per minute
const MAX_MIN = 11.5
const LANE_H = 18

function Timeline() {
  const [sel, setSel] = useState<string | null>('3:reviewer:concurrency')
  const selected = sel ? RUN618_ITERATIONS.find((t) => t.iteration === Number(sel.split(':')[0]))?.spans.find((s) => `${RUN618_ITERATIONS.find((t) => t.spans.includes(s))!.iteration}:${s.node}` === sel) : undefined
  const activeMin = RUN618_ITERATIONS.reduce((a, t) => a + Math.max(...t.spans.map((s) => s.end)), 0)
  const parkedWh = RUN618_ITERATIONS.reduce((a, t) => a + (t.park?.wh ?? 0), 0)
  return (
    <Stack gap="sm">
      <Group justify="space-between" wrap="nowrap">
        <Box>
          <Group gap={8}>
            <Text size="xs" ff="monospace" c="dimmed">
              lumen/payments-api#618 · {RUN.id}
            </Text>
            <StageChip stage="reviewing" size="xs" />
          </Group>
          <Text fw={700} size="lg">
            {RUN.title} · timeline
          </Text>
        </Box>
        <Text size="xs" c="dimmed" ta="right">
          3 iterations · 3 rounds · {activeMin.toFixed(1)} min active compute · {parkedWh.toFixed(1)} wh parked (no seat held)
          <br />
          <CostText cost={RUN.cost} />
        </Text>
      </Group>

      <Paper withBorder radius="md" p="xs">
        <SectionLabel>Rounds ribbon</SectionLabel>
        <Group gap={0} wrap="nowrap">
          {RUN618_ITERATIONS.map((t, i) => (
            <Group key={t.iteration} gap={0} wrap="nowrap" style={{ flex: t.park ? 3 : 2 }}>
              <Paper
                p={6}
                radius="sm"
                style={{ flex: 1, border: `1px solid ${i === 2 ? '#3b7bf5' : 'var(--mantine-color-dark-4)'}`, background: i === 2 ? 'rgba(59,123,245,0.12)' : undefined }}
              >
                <Text size="xs" fw={700}>
                  it{t.iteration} · R{t.round}{' '}
                  <Text span size="xs" c="dimmed" fw={400}>
                    {t.startedAt} · {t.trigger}
                  </Text>
                </Text>
                <Text size="xs" c="dimmed">
                  {t.events.map((e) => e.label).join(' · ') || 'running'}
                </Text>
              </Paper>
              {t.park && (
                <Box px={8} style={{ flex: 1, minWidth: 140 }}>
                  <Box h={2} style={{ background: isWait(t.park.node) ? 'repeating-linear-gradient(90deg,#ffa92e 0 6px,transparent 6px 10px)' : 'repeating-linear-gradient(90deg,#f06595 0 6px,transparent 6px 10px)' }} />
                  <Text size="10px" ta="center" c={isWait(t.park.node) ? 'warning.4' : 'pink.4'} mt={2}>
                    parked {t.park.wh} wh on <StepName id={t.park.node} />
                  </Text>
                  {t.park.during?.map((d) => (
                    <Text key={d.node + d.atWh} size="10px" ta="center" c="cyan.4">
                      at {d.atWh} wh: <StepName id={d.node} />: {d.label}
                    </Text>
                  ))}
                  <Text size="10px" ta="center" c="dimmed">
                    → {t.park.outcome}
                  </Text>
                </Box>
              )}
            </Group>
          ))}
        </Group>
      </Paper>

      <Paper withBorder radius="md" p="xs">
        <Group justify="space-between" mb={4}>
          <SectionLabel>Iterations (minutes from iteration start, same scale) · hover a bar for the step's definition</SectionLabel>
          <Group gap={8}>
            {(['jev', 'luna', 'haiku', 'sonnet', 'opus'] as const).map((m) => (
              <Group key={m} gap={3}>
                <Box w={10} h={8} bg={`${MODELS[m].color}.7`} style={{ borderRadius: 2 }} />
                <Text size="10px" c="dimmed">
                  {MODELS[m].short}
                </Text>
              </Group>
            ))}
            <Group gap={3}>
              <Box w={10} h={8} bg="gray.7" style={{ borderRadius: 2 }} />
              <Text size="10px" c="dimmed">
                code
              </Text>
            </Group>
          </Group>
        </Group>
        <ScrollArea type="auto">
          <Box pl={150} pos="relative" h={16} miw={150 + MAX_MIN * SCALE + 230}>
            {Array.from({ length: Math.ceil(MAX_MIN) + 1 }, (_, m) => (
              <Text key={m} size="10px" c="dimmed" pos="absolute" left={150 + m * SCALE} top={0}>
                {m}m
              </Text>
            ))}
          </Box>
          {RUN618_ITERATIONS.map((t) => {
            const lanes = Math.max(...t.spans.map((s) => s.lane ?? -1)) + 2
            return (
              <Box key={t.iteration} pos="relative" miw={150 + MAX_MIN * SCALE + 230} h={lanes * LANE_H + 10} style={{ borderTop: '1px solid var(--mantine-color-dark-5)' }}>
                <Box pos="absolute" left={0} top={4} w={140}>
                  <Text size="xs" fw={700}>
                    it{t.iteration} · round {t.round}
                  </Text>
                  <Text size="10px" c="dimmed">
                    {t.trigger}
                  </Text>
                  <CostText cost={t.cost} />
                </Box>
                {Array.from({ length: Math.ceil(MAX_MIN) + 1 }, (_, m) => (
                  <Box key={m} pos="absolute" left={150 + m * SCALE} top={0} bottom={0} w={1} bg="dark.6" />
                ))}
                {t.spans.map((s) => {
                  const row = s.lane === undefined ? 0 : s.lane + 1
                  const key = `${t.iteration}:${s.node}`
                  const color = s.model ? `var(--mantine-color-${MODELS[s.model].color}-7)` : 'var(--mantine-color-gray-7)'
                  const w = Math.max(6, (s.end - s.start) * SCALE)
                  const label = s.node.replace('reviewer:', '')
                  const exec: NodeExec = RUN618_NODES.find((n) => n.id === s.node)?.execs.find((e) => e.iteration === t.iteration) ?? {
                    iteration: t.iteration,
                    status: s.status,
                    started: t.startedAt,
                    durationS: Math.round((s.end - s.start) * 60),
                    model: s.model,
                  }
                  return (
                    <StepTip key={key} id={s.node} exec={exec}>
                      <UnstyledButton
                        onClick={() => setSel(key)}
                        pos="absolute"
                        left={150 + s.start * SCALE}
                        top={4 + row * LANE_H}
                        w={s.status === 'reused' ? 14 : w}
                        h={LANE_H - 3}
                        style={{
                          background: s.status === 'reused' ? 'repeating-linear-gradient(45deg,#20c997 0 3px,transparent 3px 6px)' : color,
                          borderRadius: 3,
                          outline: sel === key ? '2px solid white' : undefined,
                          borderRight: s.status === 'running' ? '3px dashed #3b7bf5' : undefined,
                          animation: s.status === 'running' ? 'rePulse 1.6s infinite' : undefined,
                          overflow: 'hidden',
                          paddingLeft: 4,
                        }}
                      >
                        {w > 46 && (
                          <Text size="10px" c="white" truncate fw={600}>
                            {label}
                            {s.status === 'running' ? ' …' : ''}
                          </Text>
                        )}
                      </UnstyledButton>
                    </StepTip>
                  )
                })}
                {t.park && (
                  <Box pos="absolute" left={150 + (Math.max(...t.spans.map((s) => s.end)) + 0.3) * SCALE} top={4} h={LANE_H * 2}>
                    <Group gap={4} wrap="nowrap">
                      <Text c={isWait(t.park.node) ? 'warning.5' : 'pink.5'} fw={700}>
                        ⫽
                      </Text>
                      <Paper px={6} py={2} radius="xl" style={{ border: `1px dashed ${isWait(t.park.node) ? '#ffa92e' : '#f06595'}` }}>
                        <Text size="10px" style={{ whiteSpace: 'nowrap' }}>
                          parked {t.park.wh} wh on <StepName id={t.park.node} /> → {t.park.outcome}
                        </Text>
                      </Paper>
                    </Group>
                    {t.park.during?.map((d) => {
                      const exec = RUN618_NODES.find((n) => n.id === d.node)?.execs.find((e) => e.iteration === t.iteration) ?? null
                      return (
                        <Group key={d.node + d.atWh} gap={4} wrap="nowrap" mt={4} pl={14}>
                          <Text size="10px" c="dimmed">
                            ↳ {d.atWh} wh in
                          </Text>
                          <StepTip id={d.node} exec={exec}>
                            <Paper px={6} py={2} radius="xl" style={{ border: '1px solid var(--mantine-color-cyan-7)', cursor: 'help' }}>
                              <Text size="10px" style={{ whiteSpace: 'nowrap' }}>
                                <b>{d.node}</b>: {d.label}
                              </Text>
                            </Paper>
                          </StepTip>
                        </Group>
                      )
                    })}
                  </Box>
                )}
              </Box>
            )
          })}
        </ScrollArea>
        <Text size="10px" c="dimmed" mt={4}>
          Hatched = reused from an earlier iteration (inputs hash unchanged). Parks are compressed: working hours, no seat held. Steps that ran during a park (base-drift) are shown under it.
        </Text>
      </Paper>

      {selected && (
        <Paper withBorder radius="md" p="xs">
          <Group gap="md">
            <StepName id={selected.node} />
            <Text size="xs">
              it{sel!.split(':')[0]} · {selected.start.toFixed(1)}–{selected.end.toFixed(1)} min · {NODE_STATUS_META[selected.status].label}
            </Text>
            {selected.model && <ModelBadge model={selected.model} withProvider />}
            <Text size="xs" c="dimmed">
              {RUN618_NODES.find((n) => n.id === selected.node)?.execs.find((e) => e.iteration === Number(sel!.split(':')[0]))?.output ??
                RUN618_NODES.find((n) => n.id === selected.node)?.execs.find((e) => e.iteration === Number(sel!.split(':')[0]))?.note ??
                ''}
            </Text>
            {(() => {
              const c = RUN618_NODES.find((n) => n.id === selected.node)?.execs.find((e) => e.iteration === Number(sel!.split(':')[0]))?.cost
              return c ? <CostText cost={c} models={selected.model ? [selected.model] : undefined} /> : <Text size="xs" c="dimmed">no model cost</Text>
            })()}
          </Group>
        </Paper>
      )}
      <style>{PULSE_CSS}</style>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/Run Detail',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Live overlay: status colours, 4-reviewer fan-out (3 done, concurrency running), ↺ taken/max on back edges, node detail panel. Hover any step for its definition and last execution. */
export const LiveRunOverlay: Story = { name: 'A · Live run overlay', render: () => <LiveOverlay /> }

/**
 * (A) The same run after Hugo approves iteration 3, with keepUpToDate on. The branch was already behind at review: its
 * merge-base (1f20c9e) never moved when the base moved during the it1 park. base-moved compares the merge-base with the
 * tip and reports "behind (merge-base 3 commits behind tip)" → base-drift (unaffected-approved) → update-branch (rebased,
 * approval carried) → await-merge. Intake and Review start collapsed.
 */
export const LiveRunBehindAtReview: Story = { name: 'A · Live run overlay (approved, branch behind at review)', render: () => <LiveOverlay scenario="behind" /> }

/**
 * (B) Editor driven by AI prompts. The graph is read-only; a prompt produces a proposal in the staging area (include/exclude per
 * change, Current | Staged | Diff, validation, YAML diff, replay estimate). Opens on a proposal that breaks the cycle rule
 * (verify[disputed] → packs): use a quick fix on the error (Insert gate or Add iteration limit) or press "Ask AI to fix".
 * Step parameters and descriptions are edited in the inspector and staged the same way.
 */
export const WorkflowEditor: Story = { name: 'B · Editor (AI prompt + staging)', render: () => <Editor start="invalid" /> }

/**
 * (B) The same proposal after the "Insert gate" quick fix: verify[disputed] → disputed-findings (gate, autoRetries 1:
 * the first dispute re-reviews automatically, then a human decides re-review, drop or keep). Validation passes. Turn
 * off the autoRetries change in Staged changes to see the unbounded-autoRetries error.
 */
export const WorkflowEditorFixed: Story = { name: 'B · Editor (disputed loop, gate inserted)', render: () => <Editor start="fixed" /> }

/** (B) The same editor with nothing staged, opened on a step's parameters. Try an example prompt or edit a parameter. */
export const WorkflowEditorFresh: Story = { name: 'B · Editor (fresh, parameters)', render: () => <Editor start="fresh" /> }

/** (C) The same run as a timeline: rounds ribbon, then one swimlane per iteration with parks compressed. Bars carry the step tooltip. */
export const IterationTimeline: Story = { name: 'C · Iteration timeline', render: () => <Timeline /> }
