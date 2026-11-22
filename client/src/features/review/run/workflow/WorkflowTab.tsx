import { Alert, Badge, Box, Button, Group, Loader, Paper, SegmentedControl, Stack, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { Background, Controls, MarkerType, ReactFlow, type Edge, type EdgeTypes, type Node, type NodeTypes } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo, useState } from 'react'
import { TbChevronDown, TbChevronRight, TbShieldCheck } from 'react-icons/tb'
import { getApiErrorMessage } from '../../../../api/client'
import type { NodeKind, ReviewRunDetailDto, WorkflowGraphDto } from '../../../../api/reviewEngine'
import { SectionLabel } from '../../components/SectionLabel'
import { NODE_STATUS_META } from '../../reviewMeta'
import { useDefaultReviewWorkflow, useValidateWorkflow } from '../../useReview'
import { useReviewUiStore } from '../../useReviewUiStore'
import { NodeSidePanel, ValidationPanel } from './NodeSidePanel'
import { backEdgeTaken, execsIn, forwardEdgeTaken, iterationsOf, rollupStatus } from './overlay'
import { NODE_H, NODE_W, layoutWorkflow, sectionNodeId, type LaidEdge } from './workflowLayout'
import { LaneEdgeView, SectionNodeView, StepNodeView, type LaneEdge, type SectionNode, type StepNode } from './WorkflowNodes'

const nodeTypes: NodeTypes = { step: StepNodeView, section: SectionNodeView }
const edgeTypes: EdgeTypes = { lane: LaneEdgeView }

const EDGE_GREY = '#5c5f66'
const EDGE_TAKEN = '#a6a7ab'
const BACK_COLOR: Partial<Record<NodeKind, string>> = { Wait: '#ffa92e', Gate: '#f06595' }

function edgeColor(e: LaidEdge, graph: WorkflowGraphDto, taken: boolean): string {
  if (e.isBackEdge) return BACK_COLOR[graph.nodes.find((n) => n.id === e.members[0].from)?.kind ?? 'Code'] ?? '#909296'
  return taken ? EDGE_TAKEN : EDGE_GREY
}

/** The run overlaid on its workflow graph: status colours, fan-out counts, iteration badges on back edges. */
function WorkflowGraph({ run, graph, now }: { run: ReviewRunDetailDto; graph: WorkflowGraphDto; now: number }) {
  const iterations = useMemo(() => iterationsOf(run), [run])
  const latest = iterations[iterations.length - 1] ?? 1
  const [picked, setPicked] = useState<number | null>(null)
  const iteration = picked ?? latest
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const stored = useReviewUiStore((s) => s.sectionCollapse[graph.name])
  const setCollapsed = useReviewUiStore((s) => s.setSectionCollapsed)
  const setAllCollapsed = useReviewUiStore((s) => s.setAllSectionsCollapsed)
  const validate = useValidateWorkflow()
  const wide = useMediaQuery('(min-width: 1100px)', true)

  // a section starts collapsed when nothing in it has run (the contract has no `collapsed` flag yet)
  const collapsed = useMemo(() => {
    const set = new Set<string>()
    for (const s of graph.sections) {
      const fallback = !run.nodes.some((n) => s.nodeIds.includes(n.nodeId))
      if (stored?.[s.id] ?? fallback) set.add(s.id)
    }
    return set
  }, [graph.sections, run.nodes, stored])
  const collapseKey = [...collapsed].sort().join(',')
  const layout = useMemo(() => layoutWorkflow(graph, collapsed), [graph, collapsed])
  const execs = useMemo(() => execsIn(run, iteration), [run, iteration])

  const flowNodes = useMemo(() => {
    const out: Node[] = []
    for (const s of layout.sections) {
      const section: SectionNode = {
        id: sectionNodeId(s.id),
        type: 'section',
        position: { x: s.x, y: s.y },
        width: s.width,
        height: s.height,
        draggable: false,
        selectable: false,
        zIndex: s.collapsed ? 1 : -1,
        style: { pointerEvents: s.collapsed ? 'all' : 'none' },
        data: {
          title: s.title,
          collapsed: s.collapsed,
          dir: s.dir,
          members: s.nodeIds.map((id) => ({ id, status: rollupStatus(execs.get(id)) })),
          onToggle: () => setCollapsed(graph.name, s.id, !s.collapsed),
        },
      }
      out.push(section)
    }
    for (const n of layout.nodes) {
      const step: StepNode = {
        id: n.id,
        type: 'step',
        position: { x: n.x, y: n.y },
        width: NODE_W,
        height: NODE_H,
        draggable: false,
        data: {
          def: n.def,
          status: rollupStatus(execs.get(n.id)),
          execs: execs.get(n.id) ?? [],
          iterationsRun: new Set(run.nodes.filter((x) => x.nodeId === n.id).map((x) => x.iteration)).size,
          dir: n.dir,
          selected: selectedId === n.id,
        },
      }
      out.push(step)
    }
    return out
  }, [layout, execs, run.nodes, selectedId, graph.name, setCollapsed])

  const flowEdges = useMemo(() => {
    const out: Edge[] = []
    for (const e of layout.edges) {
      const taken = e.members.some((m) => forwardEdgeTaken(execs, m))
      const color = edgeColor(e, graph, taken)
      const marker = { type: MarkerType.ArrowClosed, color, width: 14, height: 14 }
      const backLabel = e.isBackEdge
        ? `↺ ${e.members.reduce((s, m) => s + backEdgeTaken(run, m), 0)}/${e.maxIterations ?? '∞'}${e.label ? ` ${e.label}` : ''}`
        : null
      if (e.lane) {
        const lane: LaneEdge = {
          id: e.id,
          type: 'lane',
          source: e.source,
          target: e.target,
          sourceHandle: e.sourceHandle,
          targetHandle: e.targetHandle,
          markerEnd: marker,
          style: { stroke: color, strokeWidth: 1.4, strokeDasharray: '5 4' },
          data: { laneX: e.lane.x, dropY: e.lane.dropY, label: backLabel ?? '', color },
        }
        out.push(lane)
        continue
      }
      out.push({
        id: e.id,
        type: 'smoothstep',
        source: e.source,
        target: e.target,
        sourceHandle: e.sourceHandle,
        targetHandle: e.targetHandle,
        markerEnd: marker,
        animated: taken && !e.isBackEdge && (execs.get(e.members[0].to) ?? []).some((x) => x.status === 'Running'),
        style: { stroke: color, strokeWidth: taken || e.isBackEdge ? 1.6 : 1.1, strokeDasharray: e.isBackEdge ? '5 4' : undefined },
        label: backLabel ?? e.label ?? undefined,
        labelStyle: { fill: e.isBackEdge ? color : '#c1c2c5', fontSize: 10 },
        labelBgStyle: { fill: '#1a1b1e' },
        labelBgPadding: [4, 2],
        labelBgBorderRadius: 4,
      })
    }
    return out
  }, [layout, execs, graph, run])

  const selectedDef = graph.nodes.find((n) => n.id === selectedId)
  const selectedExecs = run.nodes
    .filter((n) => n.nodeId === selectedId)
    .sort((a, b) => a.iteration - b.iteration || Date.parse(a.startedAt ?? '') - Date.parse(b.startedAt ?? ''))

  return (
    <Stack gap="xs">
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap={6} wrap="wrap">
          <Text size="xs" c="dimmed">
            Sections
          </Text>
          {layout.sections.map((s) => (
            <Button
              key={s.id}
              size="compact-xs"
              variant={s.collapsed ? 'default' : 'light'}
              leftSection={s.collapsed ? <TbChevronRight size={12} /> : <TbChevronDown size={12} />}
              onClick={() => setCollapsed(graph.name, s.id, !s.collapsed)}
            >
              {s.title}
            </Button>
          ))}
          <Button size="compact-xs" variant="subtle" color="gray" onClick={() => setAllCollapsed(graph.name, graph.sections.map((s) => s.id), false)}>
            Expand all
          </Button>
          <Button size="compact-xs" variant="subtle" color="gray" onClick={() => setAllCollapsed(graph.name, graph.sections.map((s) => s.id), true)}>
            Collapse all
          </Button>
        </Group>
        <Group gap={6} wrap="wrap">
          {iterations.length > 1 && (
            <>
              <Text size="xs" c="dimmed">
                Iteration
              </Text>
              <SegmentedControl
                size="xs"
                value={String(iteration)}
                onChange={(v) => setPicked(Number(v))}
                data={iterations.map((i) => ({ value: String(i), label: `it${i}` }))}
              />
            </>
          )}
          <Tooltip label="POST /api/review-workflows/validate: the cycle rule and section checks">
            <Button size="compact-xs" variant="light" color="teal" leftSection={<TbShieldCheck size={12} />} loading={validate.isPending} onClick={() => validate.mutate(graph)}>
              Validate
            </Button>
          </Tooltip>
          <Text size="xs" ff="monospace" c="dimmed">
            {graph.name} · {graph.hash.slice(7, 19)}
          </Text>
        </Group>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: wide ? 'minmax(0, 1fr) 360px' : 'minmax(0, 1fr)', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" h={wide ? 680 : 520} style={{ overflow: 'hidden' }}>
          <ReactFlow
            key={collapseKey}
            nodes={flowNodes}
            edges={flowEdges}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
            fitViewOptions={{ padding: 0.06 }}
            minZoom={0.2}
            maxZoom={1.6}
            nodesDraggable={false}
            nodesConnectable={false}
            proOptions={{ hideAttribution: true }}
            colorMode="dark"
            onNodeClick={(_, n) => {
              if (n.type === 'step') setSelectedId(n.id)
            }}
            onPaneClick={() => setSelectedId(null)}
          >
            <Background gap={24} size={1} color="#2c2e33" />
            <Controls showInteractive={false} />
          </ReactFlow>
        </Paper>
        <Stack gap="sm">
          {validate.data && <ValidationPanel result={validate.data} />}
          {validate.error && (
            <Alert color="danger" variant="light" p={6}>
              <Text size="xs">{getApiErrorMessage(validate.error, 'Validation failed.')}</Text>
            </Alert>
          )}
          <Paper withBorder radius="md" p="sm">
            {selectedDef ? (
              <NodeSidePanel def={selectedDef} execs={selectedExecs} iteration={iteration} now={now} />
            ) : (
              <Stack gap={6}>
                <SectionLabel>Legend</SectionLabel>
                {(['Succeeded', 'Running', 'Waiting', 'Failed', 'Pending', 'NotReached'] as const).map((s) => (
                  <Group key={s} gap={6}>
                    <Box w={14} h={10} style={{ borderRadius: 2, border: `2px ${s === 'NotReached' || s === 'Pending' ? 'dashed' : 'solid'} ${NODE_STATUS_META[s].hex}` }} />
                    <Text size="xs">{NODE_STATUS_META[s].label}</Text>
                  </Group>
                ))}
                <Group gap={6}>
                  <Badge size="xs" variant="outline" color="orange" tt="none">
                    ↺ 1/20
                  </Badge>
                  <Text size="xs">back edge: times taken / iteration limit</Text>
                </Group>
                <Text size="xs" c="dimmed">
                  Click a step for its description, parameters and every execution. Click a collapsed section to expand it.
                </Text>
              </Stack>
            )}
          </Paper>
        </Stack>
      </Box>
    </Stack>
  )
}

/**
 * The Workflow tab. The contract has no GET for a workflow revision yet, so the graph is the
 * default workflow even when the run snapshotted another revision (`workflowRevisionId`).
 */
export function WorkflowTab({ run, now }: { run: ReviewRunDetailDto; now: number }) {
  const workflow = useDefaultReviewWorkflow()
  if (workflow.isLoading)
    return (
      <Group justify="center" py="xl">
        <Loader size="sm" />
      </Group>
    )
  if (!workflow.data)
    return (
      <Alert color="danger" variant="light" title="Could not load the workflow">
        {getApiErrorMessage(workflow.error, 'The review API did not return the default workflow.')}
      </Alert>
    )
  return (
    <Stack gap="xs">
      {run.workflowRevisionId && workflow.data.revisionId && run.workflowRevisionId !== workflow.data.revisionId && (
        <Alert color="warning" variant="light" p={6}>
          <Text size="xs">This run started on revision {run.workflowRevisionId}; showing the current default workflow.</Text>
        </Alert>
      )}
      <WorkflowGraph run={run} graph={workflow.data} now={now} />
    </Stack>
  )
}
