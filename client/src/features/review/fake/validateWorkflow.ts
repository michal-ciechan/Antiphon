import type { WorkflowDiagnosticDto, WorkflowGraphDto, WorkflowValidationDto } from '../../../api/reviewEngine'

/**
 * The fake's POST /api/review-workflows/validate. It applies the design's cycle rule (§3.2): every
 * cycle must contain a Gate or Wait node, or be closed by an edge with `maxIterations`. A missing
 * description is a warning (code prefix `warn.`), which does not make the graph invalid.
 */
export function validateWorkflowGraph(graph: WorkflowGraphDto): WorkflowValidationDto {
  const diagnostics: WorkflowDiagnosticDto[] = []
  const ids = new Set<string>()
  for (const n of graph.nodes) {
    if (ids.has(n.id)) diagnostics.push({ code: 'duplicate-node', message: `Node "${n.id}" is defined twice.`, nodeIds: [n.id], edge: null })
    ids.add(n.id)
    if (!n.description?.trim()) diagnostics.push({ code: 'warn.missing-description', message: `Node "${n.id}" has no description.`, nodeIds: [n.id], edge: null })
  }
  for (const e of graph.edges) {
    const missing = [e.from, e.to].filter((id) => !ids.has(id))
    if (missing.length > 0) diagnostics.push({ code: 'unknown-node', message: `Edge ${e.from} → ${e.to} names unknown node ${missing.join(', ')}.`, nodeIds: missing, edge: { from: e.from, to: e.to } })
  }
  const sectionOf = new Map<string, string>()
  for (const s of graph.sections) {
    for (const id of s.nodeIds) {
      if (!ids.has(id)) diagnostics.push({ code: 'section-unknown-node', message: `Section "${s.title}" lists unknown node "${id}".`, nodeIds: [id], edge: null })
      const prior = sectionOf.get(id)
      if (prior && prior !== s.id) diagnostics.push({ code: 'node-in-two-sections', message: `Node "${id}" is in sections "${prior}" and "${s.id}".`, nodeIds: [id], edge: null })
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
    if (scc.length > 1 || selfLoop) {
      diagnostics.push({
        code: 'cycle-unbounded',
        message: `Cycle through ${scc.join(' → ')} has no gate or wait node and no maxIterations on its closing edge.`,
        nodeIds: scc,
        edge: null,
      })
    }
  }

  return { valid: diagnostics.every((d) => d.code.startsWith('warn.')), diagnostics }
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
