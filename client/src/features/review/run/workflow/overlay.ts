import type { NodeExecutionDto, NodeStatus, ReviewRunDetailDto, WorkflowEdgeDto } from '../../../../api/reviewEngine'

/** The run's node executions, read against the workflow graph for one iteration. */

export function iterationsOf(run: ReviewRunDetailDto): number[] {
  return [...new Set(run.nodes.map((n) => n.iteration))].sort((a, b) => a - b)
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

/**
 * How many times a back edge was taken: an iteration k in which the edge's source ran and
 * finished, followed by an iteration k + 1 that ran the edge's target. A heuristic until the
 * contract records the outcome each execution took.
 */
export function backEdgeTaken(run: ReviewRunDetailDto, edge: WorkflowEdgeDto): number {
  const iterations = iterationsOf(run)
  let taken = 0
  for (const k of iterations) {
    const sourceDone = run.nodes.some((n) => n.iteration === k && n.nodeId === edge.from && n.status === 'Succeeded')
    const targetNext = run.nodes.some((n) => n.iteration === k + 1 && n.nodeId === edge.to)
    if (sourceDone && targetNext) taken++
  }
  return taken
}

/** A forward edge counts as taken in an iteration when both ends ran in it. */
export function forwardEdgeTaken(execs: Map<string, NodeExecutionDto[]>, edge: WorkflowEdgeDto): boolean {
  return (execs.get(edge.from)?.length ?? 0) > 0 && (execs.get(edge.to)?.length ?? 0) > 0
}
