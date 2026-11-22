import type { DiagnosticSeverity, WorkflowDiagnosticDto, WorkflowGraphDto, WorkflowValidationDto } from '../../../api/reviewEngine'

/**
 * The fake's POST /api/review-workflows/validate. It applies the design's cycle rule (§3.2): every
 * cycle must contain a Gate or Wait node, or be closed by an edge with `maxIterations`. A missing
 * description, an empty section and an unlabelled edge out of a branching node are warnings,
 * which do not make the graph invalid; every other diagnostic is an error.
 */
export function validateWorkflowGraph(graph: WorkflowGraphDto): WorkflowValidationDto {
  const diagnostics: WorkflowDiagnosticDto[] = []
  const add = (severity: DiagnosticSeverity, code: string, message: string, nodeIds: string[], edge: WorkflowDiagnosticDto['edge'] = null) =>
    diagnostics.push({ severity, code, message, nodeIds, edge })
  const ids = new Set<string>()
  for (const n of graph.nodes) {
    if (ids.has(n.id)) add('Error', 'duplicate-node', `Node "${n.id}" is defined twice.`, [n.id])
    ids.add(n.id)
    if (!n.description?.trim()) add('Warning', 'missing-description', `Node "${n.id}" has no description.`, [n.id])
  }
  for (const e of graph.edges) {
    const missing = [e.from, e.to].filter((id) => !ids.has(id))
    if (missing.length > 0) add('Error', 'unknown-node', `Edge ${e.from} → ${e.to} names unknown node ${missing.join(', ')}.`, missing, { from: e.from, to: e.to })
  }
  const outgoing = new Map<string, number>()
  for (const e of graph.edges) outgoing.set(e.from, (outgoing.get(e.from) ?? 0) + 1)
  for (const e of graph.edges) {
    if ((outgoing.get(e.from) ?? 0) > 1 && !e.label && !e.isBackEdge)
      add('Warning', 'unlabelled-branch', `Edge ${e.from} → ${e.to} leaves a branching node without a label, so no outcome can select it.`, [e.from], { from: e.from, to: e.to })
  }
  const sectionOf = new Map<string, string>()
  for (const s of graph.sections) {
    if (s.nodeIds.length === 0) add('Warning', 'empty-section', `Section "${s.title}" has no steps.`, [])
    for (const id of s.nodeIds) {
      if (!ids.has(id)) add('Error', 'section-unknown-node', `Section "${s.title}" lists unknown node "${id}".`, [id])
      const prior = sectionOf.get(id)
      if (prior && prior !== s.id) add('Error', 'node-in-two-sections', `Node "${id}" is in sections "${prior}" and "${s.id}".`, [id])
      sectionOf.set(id, s.id)
    }
  }

  // A cycle breaks the rule only if it survives removing every Gate/Wait node and every bounded edge.
  const breaker = new Set(graph.nodes.filter((n) => n.kind === 'Gate' || n.kind === 'Wait').map((n) => n.id))
  const adj = new Map<string, string[]>()
  for (const e of graph.edges) {
    if (e.maxIterations !== null || breaker.has(e.from) || breaker.has(e.to) || !ids.has(e.from) || !ids.has(e.to)) continue
    adj.set(e.from, [...(adj.get(e.from) ?? []), e.to])
  }
  for (const scc of stronglyConnected([...ids].filter((id) => !breaker.has(id)), adj)) {
    const selfLoop = scc.length === 1 && (adj.get(scc[0]) ?? []).includes(scc[0])
    if (scc.length > 1 || selfLoop)
      add('Error', 'cycle-unbounded', `Cycle through ${scc.join(' → ')} has no gate or wait node and no maxIterations on its closing edge.`, scc)
  }

  return { valid: diagnostics.every((d) => d.severity !== 'Error'), diagnostics }
}

/** Tarjan's algorithm. */
function stronglyConnected(nodes: string[], adj: Map<string, string[]>): string[][] {
  let index = 0
  const idx = new Map<string, number>()
  const low = new Map<string, number>()
  const onStack = new Set<string>()
  const stack: string[] = []
  const out: string[][] = []
  const visit = (v: string) => {
    idx.set(v, index)
    low.set(v, index)
    index++
    stack.push(v)
    onStack.add(v)
    for (const w of adj.get(v) ?? []) {
      if (!idx.has(w)) {
        visit(w)
        low.set(v, Math.min(low.get(v)!, low.get(w)!))
      } else if (onStack.has(w)) {
        low.set(v, Math.min(low.get(v)!, idx.get(w)!))
      }
    }
    if (low.get(v) === idx.get(v)) {
      const scc: string[] = []
      let w: string
      do {
        w = stack.pop()!
        onStack.delete(w)
        scc.push(w)
      } while (w !== v)
      out.push(scc.reverse())
    }
  }
  for (const v of nodes) if (!idx.has(v)) visit(v)
  return out
}
