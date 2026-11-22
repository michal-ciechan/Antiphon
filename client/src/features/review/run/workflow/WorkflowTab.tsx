import { ActionIcon, Alert, Badge, Box, Button, Group, Modal, Paper, Popover, SegmentedControl, Skeleton, Stack, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { Background, Controls, MarkerType, ReactFlow, type Edge, type EdgeTypes, type Node, type NodeTypes } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useMemo, useState } from 'react'
import { TbArrowsMaximize, TbArrowsMinimize, TbChevronDown, TbChevronRight, TbInfoCircle, TbMaximize, TbMinimize, TbShieldCheck } from 'react-icons/tb'
import { useSearchParams } from 'react-router'
import { getApiErrorMessage } from '../../../../api/client'
import type { NodeKind, ReviewRunDetailDto, WorkflowGraphDto } from '../../../../api/reviewEngine'
import { QueryErrorAlert } from '../../components/ReviewProblem'
import { SectionLabel } from '../../components/SectionLabel'
import { NODE_STATUS_META } from '../../reviewMeta'
import { shaRange, triggerLabel } from '../../reviewFormat'
import { useReviewWorkflow, useValidateWorkflow } from '../../useReview'
import { useReviewUiStore } from '../../useReviewUiStore'
import { FloatingWindow } from './FloatingWindow'
import { useDocumentFullscreen } from './useDocumentFullscreen'
import { NodeSidePanel, ValidationPanel } from './NodeSidePanel'
import { backEdgeTaken, edgeTakenIn, execsIn, groupByItem, iterationInfo, iterationsOf, rollupStatus } from './overlay'
import { NODE_H, NODE_W, layoutWorkflow, sectionNodeId, type LaidEdge } from './workflowLayout'
import { LaneEdgeView, SectionNodeView, StepNodeView, type LaneEdge, type SectionNode, type StepNode } from './WorkflowNodes'

const nodeTypes: NodeTypes = { step: StepNodeView, section: SectionNodeView }
const edgeTypes: EdgeTypes = { lane: LaneEdgeView }

const EDGE_GREY = '#4a4d53'
/** the path the run took in the shown iteration */
const EDGE_TAKEN = '#4dabf7'
const BACK_COLOR: Partial<Record<NodeKind, string>> = { Wait: '#ffa92e', Gate: '#f06595' }

function edgeColor(e: LaidEdge, graph: WorkflowGraphDto, taken: boolean): string {
  if (taken) return EDGE_TAKEN
  if (e.isBackEdge) return BACK_COLOR[graph.nodes.find((n) => n.id === e.members[0].from)?.kind ?? 'Code'] ?? '#909296'
  return EDGE_GREY
}

function WorkflowLegend({ hint }: { hint: string }) {
  return (
    <Stack gap={6}>
      <SectionLabel>Legend</SectionLabel>
      {(['Succeeded', 'Running', 'Waiting', 'Failed', 'Pending', 'NotReached'] as const).map((s) => (
        <Group key={s} gap={6}>
          <Box w={14} h={10} style={{ borderRadius: 2, border: `2px ${s === 'NotReached' || s === 'Pending' ? 'dashed' : 'solid'} ${NODE_STATUS_META[s].hex}` }} />
          <Text size="xs">{NODE_STATUS_META[s].label}</Text>
        </Group>
      ))}
      <Group gap={6}>
        <Box w={18} h={3} style={{ background: EDGE_TAKEN, borderRadius: 2 }} />
        <Text size="xs">edge taken in this iteration (from each step's outcome)</Text>
      </Group>
      <Group gap={6}>
        <Badge size="xs" variant="outline" color="orange" tt="none">
          ↺ 1/20
        </Badge>
        <Text size="xs">back edge: times taken / iteration limit</Text>
      </Group>
      <Text size="xs" c="dimmed">
        {hint}
      </Text>
    </Stack>
  )
}

/** `?graph=fullscreen` opens the graph full screen; it is a deep link, and Back leaves it. */
const FULLSCREEN_PARAM = 'graph'

/**
 * The run overlaid on its workflow graph: status colours, fan-out items by `itemKey`, the edges the
 * run took (from each execution's `outcome`) highlighted, and iteration counts on back edges.
 * Fullscreen shows the same graph filling the window, with step details in a floating window.
 */
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
  const [params, setParams] = useSearchParams()
  const fullscreen = params.get(FULLSCREEN_PARAM) === 'fullscreen'
  const browser = useDocumentFullscreen()
  const openFullscreen = () => {
    setParams((p) => {
      p.set(FULLSCREEN_PARAM, 'fullscreen')
      return p
    })
    browser.enter()
  }
  const closeFullscreen = () => {
    browser.exit()
    setParams(
      (p) => {
        p.delete(FULLSCREEN_PARAM)
        return p
      },
      { replace: true },
    )
  }

  // the workflow's default (`collapsed`) until the viewer toggles a section
  const collapsed = useMemo(() => {
    const set = new Set<string>()
    for (const s of graph.sections) if (stored?.[s.id] ?? s.collapsed) set.add(s.id)
    return set
  }, [graph.sections, stored])
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
          items: groupByItem(execs.get(n.id) ?? []).filter((g) => g.itemKey !== null),
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
      const taken = e.members.some((m) => edgeTakenIn(run, execs, m))
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
          style: { stroke: color, strokeWidth: taken ? 2 : 1.4, strokeDasharray: '5 4' },
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
        style: { stroke: color, strokeWidth: taken ? 2.2 : e.isBackEdge ? 1.4 : 1.1, strokeDasharray: e.isBackEdge ? '5 4' : undefined },
        label: backLabel ?? e.label ?? undefined,
        labelStyle: { fill: e.isBackEdge || taken ? color : '#909296', fontSize: 10, fontWeight: taken ? 700 : 400 },
        labelBgStyle: { fill: '#1a1b1e' },
        labelBgPadding: [4, 2],
        labelBgBorderRadius: 4,
      })
    }
    return out
  }, [layout, execs, graph, run])
  const shown = iterationInfo(run, iteration)

  const selectedDef = graph.nodes.find((n) => n.id === selectedId)
  const selectedExecs = run.nodes
    .filter((n) => n.nodeId === selectedId)
    .sort((a, b) => a.iteration - b.iteration || Date.parse(a.startedAt ?? '') - Date.parse(b.startedAt ?? ''))

  // One canvas definition, rendered inline and (when open) in the fullscreen view.
  const canvas = (variant: 'inline' | 'fullscreen') => (
    <ReactFlow
      key={`${variant}:${collapseKey}`}
      nodes={flowNodes}
      edges={flowEdges}
      nodeTypes={nodeTypes}
      edgeTypes={edgeTypes}
      fitView
      fitViewOptions={{ padding: variant === 'fullscreen' ? 0.04 : 0.06 }}
      minZoom={0.2}
      maxZoom={variant === 'fullscreen' ? 2.4 : 1.6}
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
  )

  const subjectLabel = `${run.subject.repo}${run.subject.prNumber ? `#${run.subject.prNumber}` : ''}`

  const controls = (
    <>
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
          {shown && (
            <Text size="xs" c="dimmed">
              R{shown.round} · {triggerLabel(shown.trigger)} ·{' '}
              <Text span size="xs" ff="monospace">
                {shaRange(shown.baseSha, shown.headSha)}
              </Text>
            </Text>
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
    </>
  )

  const validationBody = (
    <>
      {validate.data && <ValidationPanel result={validate.data} />}
      {validate.error && (
        <Alert color="danger" variant="light" p={6}>
          <Text size="xs">{getApiErrorMessage(validate.error, 'Validation failed.')}</Text>
        </Alert>
      )}
    </>
  )

  return (
    <Stack gap="xs">
      <Group justify="space-between" wrap="wrap" gap="xs">
        {controls}
        <Tooltip label="Open the graph full screen; click a step to see its details in a floating window">
          <Button size="compact-xs" variant="default" leftSection={<TbArrowsMaximize size={12} />} onClick={openFullscreen} data-testid="review-workflow-fullscreen-open">
            Fullscreen
          </Button>
        </Tooltip>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: wide ? 'minmax(0, 1fr) 360px' : 'minmax(0, 1fr)', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" h={wide ? 680 : 520} style={{ overflow: 'hidden' }}>
          {canvas('inline')}
        </Paper>
        <Stack gap="sm">
          {validationBody}
          <Paper withBorder radius="md" p="sm">
            {selectedDef ? (
              <NodeSidePanel def={selectedDef} execs={selectedExecs} iteration={iteration} now={now} />
            ) : (
              <WorkflowLegend hint="Click a step for its description, parameters and every execution. Click a collapsed section to expand it." />
            )}
          </Paper>
        </Stack>
      </Box>

      <Modal
        opened={fullscreen}
        onClose={closeFullscreen}
        fullScreen
        withCloseButton={false}
        padding={0}
        radius={0}
        transitionProps={{ transition: 'fade', duration: 120 }}
        styles={{ body: { height: '100dvh', padding: 0 } }}
      >
        <Box data-testid="review-workflow-fullscreen" style={{ height: '100dvh', display: 'flex', flexDirection: 'column', background: '#141517' }}>
          <Group justify="space-between" wrap="wrap" gap="xs" px="sm" py={6} style={{ borderBottom: '1px solid var(--mantine-color-dark-4)' }}>
            <Group gap="sm" wrap="wrap" style={{ minWidth: 0 }}>
              <Text size="sm" fw={600}>
                {subjectLabel} · workflow
              </Text>
              {controls}
            </Group>
            <Group gap={6} wrap="nowrap" style={{ marginLeft: 'auto' }}>
              <Popover position="bottom-end" withArrow shadow="md" width={320}>
                <Popover.Target>
                  <Button size="compact-xs" variant="subtle" color="gray" leftSection={<TbInfoCircle size={12} />}>
                    Legend
                  </Button>
                </Popover.Target>
                <Popover.Dropdown>
                  <WorkflowLegend hint="Click a step to open its details in a floating window; drag the window by its title bar." />
                </Popover.Dropdown>
              </Popover>
              <Tooltip label={browser.active ? 'Leave browser fullscreen (keeps this view)' : 'Browser fullscreen (hides the browser chrome)'}>
                <ActionIcon variant="subtle" color="gray" onClick={browser.active ? browser.exit : browser.enter} aria-label="Toggle browser fullscreen">
                  {browser.active ? <TbArrowsMinimize size={16} /> : <TbMaximize size={16} />}
                </ActionIcon>
              </Tooltip>
              <Button size="compact-xs" variant="light" leftSection={<TbMinimize size={12} />} onClick={closeFullscreen} data-testid="review-workflow-fullscreen-exit">
                Exit fullscreen
              </Button>
            </Group>
          </Group>
          <Box style={{ flex: 1, minHeight: 0, position: 'relative' }}>
            {fullscreen && canvas('fullscreen')}
            {selectedDef && (
              <FloatingWindow
                key="node"
                title={selectedDef.id}
                badge={
                  <Badge size="xs" variant="light" color="gray" tt="none">
                    {selectedDef.kind}
                  </Badge>
                }
                onClose={() => setSelectedId(null)}
                testId="review-workflow-node-window"
              >
                <NodeSidePanel def={selectedDef} execs={selectedExecs} iteration={iteration} now={now} />
              </FloatingWindow>
            )}
            {(validate.data || validate.error) && (
              <FloatingWindow key="validation" title="Validation" anchor="bottom-left" width={420} onClose={() => validate.reset()}>
                {validationBody}
              </FloatingWindow>
            )}
          </Box>
        </Box>
      </Modal>
    </Stack>
  )
}

/**
 * The Workflow tab: the graph the run snapshotted (`workflowRevisionId`, fetched by revision), or
 * the default workflow for a run without one.
 */
export function WorkflowTab({ run, now }: { run: ReviewRunDetailDto; now: number }) {
  const workflow = useReviewWorkflow(run.workflowRevisionId)
  if (workflow.isLoading) return <Skeleton h={520} radius="md" />
  if (!workflow.data)
    return (
      <QueryErrorAlert
        title="Could not load the workflow"
        error={workflow.error}
        fallback={run.workflowRevisionId ? `The review API did not return workflow revision ${run.workflowRevisionId}.` : 'The review API did not return the default workflow.'}
        onRetry={() => void workflow.refetch()}
        retrying={workflow.isFetching}
      />
    )
  return <WorkflowGraph run={run} graph={workflow.data} now={now} />
}
