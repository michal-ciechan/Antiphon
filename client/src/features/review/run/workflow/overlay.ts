import type { NodeExecutionDto, NodeStatus, ReviewIterationDto, ReviewRunDetailDto, WorkflowEdgeDto } from '../../../../api/reviewEngine'

/** The run's node executions, read against the workflow graph for one iteration. */

/** Iteration numbers, oldest first: the run's `iterations[]`, plus any execution's iteration it lacks. */
export function iterationsOf(run: ReviewRunDetailDto): number[] {
  return [...new Set([...run.iterations.map((i) => i.iteration), ...run.nodes.map((n) => n.iteration)])].sort((a, b) => a - b)
}

/** The `iterations[]` row for an iteration number, if the server sent one. */
export function iterationInfo(run: ReviewRunDetailDto, iteration: number): ReviewIterationDto | undefined {
  return run.iterations.find((i) => i.iteration === iteration)
}

export function execsIn(run: ReviewRunDetailDto, iteration: number): Map<string, NodeExecutionDto[]> {
  const map = new Map<string, NodeExecutionDto[]>()
  for (const n of run.nodes) {
    if (n.iteration !== iteration) continue
    map.set(n.nodeId, [...(map.get(n.nodeId) ?? []), n])
  }
  return map
}

const PRECEDENCE: NodeStatus[] = ['Running', 'Failed', 'Waiting', 'Pending', 'Succeeded', 'Skipped']

/** One status for a node's executions in an iteration (a fan-out child has several). */
export function rollupStatus(execs: NodeExecutionDto[] | undefined): NodeStatus {
  if (!execs || execs.length === 0) return 'NotReached'
  for (const s of PRECEDENCE) if (execs.some((e) => e.status === s)) return s
  return 'NotReached'
}

/** Fan-out executions grouped by `itemKey` (e.g. the reviewer group), in first-seen order. */
export function groupByItem(execs: NodeExecutionDto[]): { itemKey: string | null; execs: NodeExecutionDto[]; status: NodeStatus }[] {
  const groups = new Map<string | null, NodeExecutionDto[]>()
  for (const e of execs) groups.set(e.itemKey, [...(groups.get(e.itemKey) ?? []), e])
  return [...groups].map(([itemKey, xs]) => ({ itemKey, execs: xs, status: rollupStatus(xs) }))
}

/** An edge label can name several outcomes: "push | recheck". */
export function edgeOutcomes(label: string | null): string[] {
  return label ? label.split('|').map((s) => s.trim()).filter(Boolean) : []
}

/** Whether an execution left through this edge, from its recorded `outcome`; null when not recorded. */
function leftBy(e: NodeExecutionDto, edge: WorkflowEdgeDto): boolean | null {
  if (!e.outcome || !edge.label) return null
  return edgeOutcomes(edge.label).includes(e.outcome)
}

const startOf = (e: NodeExecutionDto) => Date.parse(e.startedAt ?? '')
const endOf = (e: NodeExecutionDto) => Date.parse(e.completedAt ?? e.startedAt ?? '')

/** Timing fallback for a back edge: a target execution, in this or the next iteration, starting once the source finished. */
function followedBy(run: ReviewRunDetailDto, s: NodeExecutionDto, edge: WorkflowEdgeDto): boolean {
  if (s.status !== 'Succeeded') return false
  return run.nodes.some((t) => t.nodeId === edge.to && t.id !== s.id && (t.iteration === s.iteration || t.iteration === s.iteration + 1) && startOf(t) >= endOf(s))
}

/**
 * Whether the run took this edge in an iteration. A labelled edge is taken when an execution of
 * its source recorded a matching `outcome` (a back edge's outcome ends the iteration). Without a
 * recorded outcome, fall back to timing: a forward edge needs both ends in the iteration, a back
 * edge a target execution that starts after the source finished.
 */
export function edgeTakenIn(run: ReviewRunDetailDto, execs: Map<string, NodeExecutionDto[]>, edge: WorkflowEdgeDto): boolean {
  const from = execs.get(edge.from) ?? []
  if (from.length === 0) return false
  const verdicts = from.map((e) => leftBy(e, edge)).filter((v): v is boolean => v !== null)
  if (verdicts.length > 0) return verdicts.includes(true)
  if (!edge.isBackEdge) return (execs.get(edge.to)?.length ?? 0) > 0
  return from.some((s) => followedBy(run, s, edge))
}

/**
 * How many times a back edge was taken across the run: executions of its source whose `outcome`
 * matches the edge label, or (no outcome recorded) that a later target execution followed.
 */
export function backEdgeTaken(run: ReviewRunDetailDto, edge: WorkflowEdgeDto): number {
  let taken = 0
  for (const s of run.nodes) {
    if (s.nodeId !== edge.from) continue
    const verdict = leftBy(s, edge)
    if (verdict !== null) {
      if (verdict) taken++
      continue
    }
    if (followedBy(run, s, edge)) taken++
  }
  return taken
}

/**
 * The execution a failed run stopped on: the latest Failed one. Its `summary` is the failure reason
 * until the contract serves a run-level `failureReason` (planned in v0.2).
 */
export function failedExecution(run: ReviewRunDetailDto): NodeExecutionDto | undefined {
  return run.nodes.filter((n) => n.status === 'Failed').sort((a, b) => b.iteration - a.iteration || endOf(b) - endOf(a))[0]
}
