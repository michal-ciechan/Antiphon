import { useQuery } from '@tanstack/react-query'
import { apiGet } from './client'
import type { AgentKind, CardDto } from './boards'
import type { AgentModelLevel } from './agents'
import type { AgentTaskKind, AgentTaskStatus } from './agentTasks'
import type { PlanSummaryDto } from './plans'

/**
 * The card thread (mobile-thread spec §D2, slice T2): one card's work assembled from the four
 * places it is actually recorded — the card row, the plan files in git, the delegated tasks, and
 * the commits. There is no foreign key behind any of it; the join is the identifier-as-citation
 * convention, which is why every piece says how it was matched rather than presenting the
 * correlation as if a key had guaranteed it.
 */

/**
 * Which field carried the citation. A `title` match is a much stronger claim than a `goal` match —
 * a goal often names OTHER cards as context — so the row can say which it was.
 */
export type ThreadTaskMatchedOn = 'title' | 'goal' | 'both'

export interface CardThreadCheckDto {
  text: string
  /**
   * True when the text is the check interpreter's READING of the bundle (CARD-0035 slice 5),
   * false when it is the tail of the deterministic digest. The UI says which it is showing — a
   * reading and a digest tail are not the same kind of claim.
   */
  fromInterpreter: boolean
  at: string
}

export interface CardThreadTaskDto {
  id: string
  title: string
  status: AgentTaskStatus
  kind: AgentTaskKind
  /**
   * Which agent program ran (or will run) it — `ClaudeCode` unless the caller chose Grok. The tier
   * alone does not name a model: the same `Frontier` rung is `fable` on Claude and `grok-4.6` on
   * Grok (CARD-0084 S4), so the thread reads its tier through `tierAlias(modelLevel, agentKind)`.
   */
  agentKind: AgentKind
  modelLevel: AgentModelLevel
  agentName: string | null
  agentSessionId: string | null
  createdAt: string
  dispatchedAt: string | null
  completedAt: string | null
  nextCheckAt: string | null
  checkCount: number
  costUsd: number
  subtreeCostUsd: number
  matchedOn: ThreadTaskMatchedOn
  latestCheck: CardThreadCheckDto | null
  result: string | null
  resultFilePath: string | null
  deliverablePath?: string | null
  deliverableRef?: string | null
  failureReason: string | null
}

/**
 * `subject: true` means the plan is ABOUT this card (filename, title or `Card(s):` header says
 * so); false means it merely cites it in passing. Both arrive ranked, subjects first — collapsing
 * the two would put every plan on every neighbour's thread.
 */
export interface CardThreadPlanDto {
  plan: PlanSummaryDto
  subject: boolean
}

export interface CardThreadCommitDto {
  sha: string
  shortSha: string
  author: string
  date: string
  subject: string
}

export interface CardThreadDto {
  card: CardDto
  identifier: string
  repoRoot: string | null
  /**
   * False means `commits` (and the git-derived plan list) is ABSENT, not empty — nobody could ask
   * a repository, which is a different answer from "nothing cites this card". `tasks` comes from
   * the database and is always complete regardless.
   */
  reposConsulted: boolean
  generatedAt: string
  plans: CardThreadPlanDto[]
  tasks: CardThreadTaskDto[]
  commits: CardThreadCommitDto[]
}

export const cardThreadKeys = {
  all: ['cards', 'thread'] as const,
  byId: (identifier: string) => ['cards', 'thread', identifier] as const,
}

/**
 * `identifier` takes every form the card routes take since CARD-0051 — `CARD-0067`, `card-67`,
 * `#67`, `67`, or the guid. It MUST be percent-encoded in the path: a bare `#` is a URL fragment
 * and never reaches the server. The 15s interval matches `useAgentTasks` and exists for the same
 * reason: SignalR's `CardChanged`/`AgentTaskChanged` already invalidate this key, so the poll only
 * covers a dropped connection.
 */
export function useCardThread(identifier: string | null) {
  return useQuery({
    queryKey: cardThreadKeys.byId(identifier ?? ''),
    queryFn: () => apiGet<CardThreadDto>(`/cards/${encodeURIComponent(identifier!)}/thread`),
    enabled: identifier !== null,
    refetchInterval: 15_000,
  })
}
