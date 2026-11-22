import type { WorkflowEdgeDto, WorkflowGraphDto, WorkflowNodeDto } from '../../../../api/reviewEngine'

/**
 * Lays the workflow out without a layout engine: each section is a horizontal band, bands stack
 * top to bottom and alternate direction (a snake, so the main path flows on), and nodes inside a
 * band are layered by longest path over the band's forward edges. Back edges into an earlier band
 * are routed round a lane on the left, as in the proposal. A collapsed section is one summary box
 * and the edges touching it re-attach to the box (duplicates merged).
 */

export const NODE_W = 172
export const NODE_H = 66
const STEP_X = NODE_W + 52
const STEP_Y = NODE_H + 22
const BAND_PAD = 14
const BAND_HEADER = 28
const BAND_GAP = 54
const SUMMARY_W = 320
const SUMMARY_H = 76
const LANE_X0 = -40
const LANE_STEP = 16

export type Dir = 'ltr' | 'rtl'

export interface LaidNode {
  /** the step id, or `<stepId>::<itemKey>` for one agent of an expanded fan-out step */
  id: string
  def: WorkflowNodeDto
  x: number
  y: number
  dir: Dir
  sectionId: string | null
  /** the fan-out item (e.g. reviewer group) this node shows; null for an ordinary step */
  item: string | null
  /** the workflow step this node belongs to (its own id for an ordinary step) */
  stepId: string
}

export interface LaidSection {
  id: string
  title: string
  nodeIds: string[]
  collapsed: boolean
  x: number
  y: number
  width: number
  height: number
  dir: Dir
}

export interface LaidEdge {
  id: string
  /** visible endpoints: a node id or `sec:<sectionId>` */
  source: string
  target: string
  sourceHandle: string
  targetHandle: string
  /** the graph edges this visible edge stands for */
  members: WorkflowEdgeDto[]
  label: string | null
  isBackEdge: boolean
  maxIterations: number | null
  /** lane routing for a back edge into an earlier band */
  lane: { x: number; dropY: number } | null
}

export interface WorkflowLayout {
  nodes: LaidNode[]
  sections: LaidSection[]
  edges: LaidEdge[]
}

export const sectionNodeId = (id: string) => `sec:${id}`
/** One agent of an expanded fan-out step. */
export const agentNodeId = (stepId: string, item: string) => `${stepId}::${item}`
/** Splits a node id into its step and (for an agent node) item. */
export function parseNodeId(id: string): { stepId: string; item: string | null } {
  const at = id.indexOf('::')
  return at < 0 ? { stepId: id, item: null } : { stepId: id.slice(0, at), item: id.slice(at + 2) }
}

/** Back edges for layout: the graph's own flags, plus any DFS back edge that still closes a cycle. */
function layoutBackEdges(graph: WorkflowGraphDto): Set<WorkflowEdgeDto> {
  const back = new Set(graph.edges.filter((e) => e.isBackEdge))
  const adj = new Map<string, WorkflowEdgeDto[]>()
  for (const e of graph.edges) if (!back.has(e)) adj.set(e.from, [...(adj.get(e.from) ?? []), e])
  const state = new Map<string, 'open' | 'done'>()
  const visit = (v: string) => {
    state.set(v, 'open')
    for (const e of adj.get(v) ?? []) {
      const s = state.get(e.to)
      if (s === 'open') back.add(e)
      else if (!s) visit(e.to)
    }
    state.set(v, 'done')
  }
  for (const n of graph.nodes) if (!state.has(n.id)) visit(n.id)
  return back
}

/**
 * `expanded` maps a fan-out step to the items it is shown expanded into (one node per agent,
 * stacked in the step's column; the band grows to fit, and its edges fan out to each agent).
 */
export function layoutWorkflow(
  graph: WorkflowGraphDto,
  collapsed: ReadonlySet<string>,
  expanded: ReadonlyMap<string, string[]> = new Map(),
): WorkflowLayout {
  const byId = new Map(graph.nodes.map((n) => [n.id, n]))
  const itemsOf = (id: string) => {
    const items = expanded.get(id)
    return items && items.length > 0 ? items : null
  }
  const span = (id: string) => itemsOf(id)?.length ?? 1
  const back = layoutBackEdges(graph)
  const sectionOf = new Map<string, string>()
  const bands = graph.sections.map((s) => ({ id: s.id, title: s.title, nodeIds: s.nodeIds.filter((id) => byId.has(id)) }))
  for (const b of bands) for (const id of b.nodeIds) if (!sectionOf.has(id)) sectionOf.set(id, b.id)
  const loose = graph.nodes.filter((n) => !sectionOf.has(n.id)).map((n) => n.id)
  if (loose.length > 0) bands.push({ id: '', title: 'Other steps', nodeIds: loose })
  const bandOf = (id: string) => sectionOf.get(id) ?? ''

  // local layering inside each band
  const local = bands.map((b) => {
    const members = new Set(b.nodeIds)
    const preds = new Map<string, string[]>()
    for (const e of graph.edges) {
      if (back.has(e) || !members.has(e.from) || !members.has(e.to) || e.from === e.to) continue
      preds.set(e.to, [...(preds.get(e.to) ?? []), e.from])
    }
    const layer = new Map<string, number>()
    const depth = (id: string, seen: Set<string>): number => {
      const known = layer.get(id)
      if (known !== undefined) return known
      if (seen.has(id)) return 0
      seen.add(id)
      const d = Math.max(-1, ...(preds.get(id) ?? []).map((p) => depth(p, seen))) + 1
      layer.set(id, d)
      return d
    }
    for (const id of b.nodeIds) depth(id, new Set())
    const cols = Math.max(1, ...[...layer.values()].map((l) => l + 1))
    const rowsByLayer: string[][] = Array.from({ length: cols }, () => [])
    for (const id of b.nodeIds) rowsByLayer[layer.get(id) ?? 0].push(id)
    // keep children near their parents: order each layer by the mean row of its predecessors
    const rowOf = new Map<string, number>()
    rowsByLayer.forEach((ids, l) => {
      if (l > 0) {
        const score = (id: string) => {
          const ps = (preds.get(id) ?? []).filter((p) => rowOf.has(p))
          return ps.length ? ps.reduce((s, p) => s + rowOf.get(p)!, 0) / ps.length : 0
        }
        ids.sort((a, z) => score(a) - score(z))
      }
      // an expanded fan-out step takes one row per agent
      let row = 0
      for (const id of ids) {
        rowOf.set(id, row)
        row += span(id)
      }
    })
    const rows = Math.max(1, ...rowsByLayer.map((ids) => ids.reduce((s, id) => s + span(id), 0)))
    return { band: b, layer, rowOf, cols, rows }
  })

  const width = (l: (typeof local)[number]) => (collapsed.has(l.band.id) ? SUMMARY_W : l.cols * STEP_X - (STEP_X - NODE_W) + BAND_PAD * 2)
  const height = (l: (typeof local)[number]) => (collapsed.has(l.band.id) ? SUMMARY_H : BAND_HEADER + l.rows * STEP_Y - (STEP_Y - NODE_H) + BAND_PAD * 2)
  const maxWidth = Math.max(...local.map(width))

  const nodes: LaidNode[] = []
  const sections: LaidSection[] = []
  let y = 0
  local.forEach((l, i) => {
    const dir: Dir = i % 2 === 0 ? 'ltr' : 'rtl'
    const w = width(l)
    const h = height(l)
    const x0 = dir === 'ltr' ? 0 : maxWidth - w
    const isCollapsed = collapsed.has(l.band.id)
    if (l.band.id) sections.push({ id: l.band.id, title: l.band.title, nodeIds: l.band.nodeIds, collapsed: isCollapsed, x: x0, y, width: w, height: h, dir })
    if (!isCollapsed) {
      for (const id of l.band.nodeIds) {
        const col = l.layer.get(id) ?? 0
        const c = dir === 'ltr' ? col : l.cols - 1 - col
        const x = x0 + BAND_PAD + c * STEP_X
        const top = y + BAND_HEADER + BAND_PAD + (l.rowOf.get(id) ?? 0) * STEP_Y
        const base = { def: byId.get(id)!, x, dir, sectionId: l.band.id || null, stepId: id }
        const items = itemsOf(id)
        if (items) items.forEach((item, k) => nodes.push({ ...base, id: agentNodeId(id, item), y: top + k * STEP_Y, item }))
        else nodes.push({ ...base, id, y: top, item: null })
      }
    }
    y += h + BAND_GAP
  })

  const bandIndex = new Map(local.map((l, i) => [l.band.id, i]))
  const sectionRect = new Map(sections.map((s) => [s.id, s]))
  /** The visible nodes a step's edges attach to: its section box, its agents, or itself. */
  const endpoints = (id: string): string[] => {
    const s = bandOf(id)
    if (s && collapsed.has(s)) return [sectionNodeId(s)]
    return itemsOf(id)?.map((item) => agentNodeId(id, item)) ?? [id]
  }

  // merge edges that collapse onto the same visible pair; an expanded step fans out to its agents
  const merged = new Map<string, WorkflowEdgeDto[]>()
  for (const e of graph.edges) {
    if (!byId.has(e.from) || !byId.has(e.to)) continue
    const sources = endpoints(e.from)
    const targets = endpoints(e.to)
    // a loop on an expanded step stays on each agent rather than crossing between them
    const pairs = e.from === e.to && itemsOf(e.from) && sources[0] !== sectionNodeId(bandOf(e.from)) ? sources.map((s) => [s, s]) : sources.flatMap((s) => targets.map((t) => [s, t]))
    for (const [vs, vt] of pairs) {
      if (vs === vt && vs.startsWith('sec:')) continue
      const key = `${vs}>${vt}>${back.has(e) ? 'b' : 'f'}`
      merged.set(key, [...(merged.get(key) ?? []), e])
    }
  }

  const edges: LaidEdge[] = []
  let lane = 0
  for (const [key, members] of merged) {
    const [vs, vt, kind] = key.split('>')
    const isBack = kind === 'b'
    const bs = bandIndex.get(bandOf(members[0].from)) ?? 0
    const bt = bandIndex.get(bandOf(members[0].to)) ?? 0
    const labels = [...new Set(members.map((m) => m.label).filter((l): l is string => !!l))]
    const bounded = members.map((m) => m.maxIterations).filter((m): m is number => m !== null)
    let sourceHandle = 'out'
    let targetHandle = 'in'
    let laneRoute: LaidEdge['lane'] = null
    if (isBack && bt < bs) {
      const sourceBand = local[bs]
      const rect = sourceBand.band.id ? sectionRect.get(sourceBand.band.id) : undefined
      sourceHandle = 'bottom-out'
      targetHandle = 'back-in'
      laneRoute = { x: LANE_X0 - lane * LANE_STEP, dropY: (rect ? rect.y + rect.height : 0) + 10 + lane * 5 }
      lane++
    } else if (isBack) {
      sourceHandle = 'bottom-out'
      targetHandle = 'bottom-in'
    } else if (bt > bs) {
      sourceHandle = 'bottom-out'
      targetHandle = 'top-in'
    } else if (bt < bs) {
      sourceHandle = 'top-out'
      targetHandle = 'bottom-in'
    }
    edges.push({
      id: `e:${key}`,
      source: vs,
      target: vt,
      sourceHandle,
      targetHandle,
      members,
      label: labels.length ? labels.join(' | ') : null,
      isBackEdge: isBack,
      maxIterations: bounded.length ? bounded.reduce((a, b) => a + b, 0) : null,
      lane: laneRoute,
    })
  }
  return { nodes, sections, edges }
}
