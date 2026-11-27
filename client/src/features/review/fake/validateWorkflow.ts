import type { DiagnosticSeverity, NodeKind, WorkflowDiagnosticDto, WorkflowGraphDto, WorkflowNodeDto, WorkflowNodePolicyDto, WorkflowValidationDto } from '../../../api/reviewEngine'

const STEPS: Record<string, NodeKind> = {
  Intake: 'Code', Delta: 'Code', Categorise: 'Decision', SelectChecks: 'Decision', GroupChecks: 'Code',
  BuildPacks: 'Code', CollateFindings: 'Join', VerifyFindings: 'Completion', Publish: 'Code', RouteAfterRound: 'Code',
  NeedsHumanApproval: 'Code', BotApprove: 'Code', RecordBaseDrift: 'Code', CheckBaseMoved: 'Code',
  BaseDriftCheck: 'Decision', UpdateBranch: 'Code', Merge: 'Code',
}
const STEP_OUTPUTS: Readonly<Record<string, readonly string[]>> = {
  Delta: ['incremental', 'full'],
  Categorise: ['feature', 'bugfix', 'refactor', 'test', 'docs', 'infra', 'dependency'],
  RouteAfterRound: ['bot-round-limit', 'findings', 'clean'],
  NeedsHumanApproval: ['yes', 'no'],
  CheckBaseMoved: ['unchanged', 'moved', 'behind'],
  BaseDriftCheck: ['affected', 'unaffected'],
  UpdateBranch: ['approved', 'open'],
}
const DECISION_STEPS = new Set(['Delta', 'Categorise', 'SelectChecks', 'BaseDriftCheck'])
const CONTROL_KINDS = new Set<NodeKind>(['Gate', 'Wait', 'FanOut'])
const KINDS = new Set<NodeKind>([...Object.values(STEPS), ...CONTROL_KINDS, 'Agent'])

export function fakeWorkflowNodePolicy(node: WorkflowNodeDto): WorkflowNodePolicyDto {
  const step = node.run && STEPS[node.run] === node.kind ? node.run : null
  const chainDefault = step === 'VerifyFindings' ? 'completions-cheap' : step && DECISION_STEPS.has(step) ? 'decisions-default' : null
  const authored = typeof node.params.chain === 'string' ? node.params.chain : null
  const chain = authored === '' && (step === 'Categorise' || step === 'SelectChecks') ? null : authored
  return {
    nodeId: node.id,
    onFailure: step ? {
      value: node.params.onFailure === 'continue' ? 'continue' : 'stop',
      source: typeof node.params.onFailure === 'string' ? 'WorkflowRevision' : 'StepDefault',
    } : null,
    gatesOff: node.kind === 'Gate' ? { value: false, source: 'StepDefault' } : null,
    chain: chainDefault ? { value: chain ?? chainDefault, source: chain === null ? 'StepDefault' : 'WorkflowRevision' } : null,
  }
}

/**
 * The fake's POST /api/review-workflows/validate. It applies the design's cycle rule (§3.2): every
 * cycle must contain a Gate or Wait node, or be closed by an edge with `maxIterations`. A missing
 * description or an empty section is a warning. Instance-specific execution policy comes from the HTTP API.
 */
export function validateWorkflowGraph(graph: WorkflowGraphDto): WorkflowValidationDto {
  const diagnostics: WorkflowDiagnosticDto[] = []
  const add = (severity: DiagnosticSeverity, code: string, message: string, nodeIds: string[], edge: WorkflowDiagnosticDto['edge'] = null) =>
    diagnostics.push({ severity, code, message, nodeIds, edge })
  const ids = new Set<string>()
  const children = new Set(graph.nodes.flatMap((node) =>
    node.kind === 'FanOut' && typeof node.params.node === 'string' ? [node.params.node] : [],
  ))
  for (const n of graph.nodes) {
    if (ids.has(n.id)) add('Error', 'duplicate-node', `Node "${n.id}" is defined twice.`, [n.id])
    ids.add(n.id)
    if (!KINDS.has(n.kind)) add('Error', 'invalid-kind', 'Unknown kind for ' + n.id + '.', [n.id])
    else if (CONTROL_KINDS.has(n.kind)) {
      if (n.run !== null) add('Error', 'kind-mismatch', 'This kind handles its own execution.', [n.id])
    } else if (!(n.kind === 'Agent' && n.run === null && children.has(n.id))) {
      const kind = n.run && Object.hasOwn(STEPS, n.run) ? STEPS[n.run] : null
      if (!kind) add('Error', 'unknown-step', 'Unknown step for ' + n.id + '.', [n.id])
      else if (kind !== n.kind) add('Error', 'kind-mismatch', 'This step requires kind ' + kind + '.', [n.id])
    }
    if (n.run && Object.hasOwn(STEPS, n.run)) {
      const outputs = Array.isArray(n.params.outputs) ? n.params.outputs : n.params.outputs === undefined ? [] : [n.params.outputs]
      for (const output of new Set(outputs)) {
        if (typeof output !== 'string' || (!(STEP_OUTPUTS[n.run] ?? []).includes(output) && output !== n.params.fallback))
          add('Error', 'unsupported-output', 'The registered step cannot emit ' + String(output) + '.', [n.id])
      }
    }
    if ('inputs' in n.params) {
      if (!Array.isArray(n.params.inputs) || n.params.inputs.some((id) => typeof id !== 'string' || !/^[A-Za-z][A-Za-z0-9_-]*$/.test(id)))
        add('Error', 'invalid-input', 'Inputs must be an array of node IDs.', [n.id])
      else add('Error', 'unsupported-input', 'This step does not support declared inputs.', [n.id])
    }
    if (!n.description?.trim()) add('Warning', 'missing-description', `Node "${n.id}" has no description.`, [n.id])
  }
  const routes = new Map<string, Set<string>>()
  for (const [index, e] of graph.edges.entries()) {
    const edge = { from: e.from, to: e.to, index }
    const missing = [e.from, e.to].filter((id) => !ids.has(id))
    if (missing.length > 0) add('Error', 'unknown-node', 'Unknown edge endpoint ' + missing.join(', ') + '.', missing, edge)
    if (children.has(e.from) || children.has(e.to) || (e.overflow && children.has(e.overflow)))
      add('Error', 'child-only', 'Fan-out children can only run through their parent fan-out.', [e.from, e.to], edge)
    const used = routes.get(e.from) ?? new Set<string>()
    routes.set(e.from, used)
    const labels = e.label?.split('|').map((label) => label.trim()).filter(Boolean)
    for (const label of new Set(labels?.length ? labels : [''])) {
      if (used.has(label)) add('Error', 'ambiguous-route', 'More than one route for ' + (label || 'the default outcome') + '.', [e.from, e.to], edge)
      used.add(label)
    }
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
      add('Error', 'unbounded-cycle', `Cycle through ${scc.join(' → ')} has no gate or wait node and no maxIterations on its closing edge.`, scc)
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
