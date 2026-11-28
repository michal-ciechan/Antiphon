import { ActionIcon, Alert, Badge, Box, Button, Group, Modal, Paper, Popover, SegmentedControl, Skeleton, Stack, Text, Tooltip } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { Background, Controls, MarkerType, ReactFlow, type Edge, type EdgeTypes, type Node, type NodeTypes, type ReactFlowInstance } from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { useEffect, useMemo, useRef, useState } from 'react'
import { TbArrowsMaximize, TbArrowsMinimize, TbChevronDown, TbChevronRight, TbInfoCircle, TbMaximize, TbMinimize, TbShieldCheck } from 'react-icons/tb'
import { useSearchParams } from 'react-router'
import { getApiErrorMessage } from '../../../../api/client'
import type { NodeKind, ReviewRunDetailDto, WorkflowDiagnosticDto, WorkflowEdgeDto, WorkflowGraphDto } from '../../../../api/reviewEngine'
import { QueryErrorAlert } from '../../components/ReviewProblem'
import { SectionLabel } from '../../components/SectionLabel'
import { NODE_STATUS_META } from '../../reviewMeta'
import { shaRange, triggerLabel } from '../../reviewFormat'
import { useReviewWorkflow, useReviewWorkflowPolicy, useValidateWorkflow } from '../../useReview'
import { useReviewUiStore } from '../../useReviewUiStore'
import { FloatingWindow } from './FloatingWindow'
import { useDocumentFullscreen } from './useDocumentFullscreen'
import { NodeSidePanel, ValidationPanel } from './NodeSidePanel'
import { backEdgeTaken, edgeTakenIn, execsIn, groupByItem, iterationInfo, iterationsOf, rollupStatus } from './overlay'
import { NODE_H, NODE_W, layoutWorkflow, parseNodeId, sectionNodeId, type LaidEdge } from './workflowLayout'
import { diagnosticCounts } from './workflowDiagnostics'
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
      {(['Succeeded', 'Running', 'Waiting', 'Failed', 'FailedContinued', 'Pending', 'NotReached'] as const).map((s) => (
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
  const policy = useReviewWorkflowPolicy(run.id, run.workflowRevisionId)
  const [inlineFlow, setInlineFlow] = useState<ReactFlowInstance | null>(null)
  const [fullscreenFlow, setFullscreenFlow] = useState<ReactFlowInstance | null>(null)
  const inspectorHeading = useRef<HTMLHeadingElement>(null)
  const [diagnosticFocus, setDiagnosticFocus] = useState<{ target: string; endpoints: string[] } | null>(null)
  const knownNodes = useMemo(() => new Set(graph.nodes.map((node) => node.id)), [graph.nodes])
  const { nodeDiagnostics, edgeDiagnostics } = useMemo(() => {
    const nodeDiagnostics = new Map<string, WorkflowDiagnosticDto[]>()
    const edgeDiagnostics = new Map<WorkflowEdgeDto, WorkflowDiagnosticDto[]>()
    for (const diagnostic of validate.data?.diagnostics ?? []) {
      for (const id of new Set(diagnostic.nodeIds)) {
        const items = nodeDiagnostics.get(id) ?? []
        items.push(diagnostic)
        nodeDiagnostics.set(id, items)
      }
      const edge = diagnostic.edge
      if (edge) {
        const matches = graph.edges.filter((candidate, index) =>
          candidate.from === edge.from && candidate.to === edge.to && (edge.index === undefined || edge.index === index),
        )
        for (const match of matches) {
          const items = edgeDiagnostics.get(match) ?? []
          items.push(diagnostic)
          edgeDiagnostics.set(match, items)
        }
      }
    }
    return { nodeDiagnostics, edgeDiagnostics }
  }, [graph.edges, validate.data])
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
  const execs = useMemo(() => execsIn(run, iteration), [run, iteration])

  // Fan-out steps (executions with an itemKey) can be shown as one node per agent.
  const [expandedSteps, setExpandedSteps] = useState<ReadonlySet<string>>(() => new Set())
  const agentsByStep = useMemo(() => {
    const m = new Map<string, string[]>()
    for (const n of run.nodes) {
      if (!n.itemKey) continue
      const items = m.get(n.nodeId) ?? []
      if (!items.includes(n.itemKey)) items.push(n.itemKey)
      m.set(n.nodeId, items)
    }
    return m
  }, [run.nodes])
  const expandedItems = useMemo(() => {
    const m = new Map<string, string[]>()
    for (const id of expandedSteps) {
      // the agents of the shown iteration; a step that did not run in it shows every agent it ever had
      const shownNow = groupByItem(execs.get(id) ?? []).map((g) => g.itemKey).filter((k): k is string => !!k)
      const items = shownNow.length > 0 ? shownNow : (agentsByStep.get(id) ?? [])
      if (items.length > 0) m.set(id, items)
    }
    return m
  }, [expandedSteps, execs, agentsByStep])
  const expandKey = [...expandedItems].map(([k, v]) => `${k}:${v.join('+')}`).sort().join(',')
  const toggleExpand = (stepId: string) =>
    setExpandedSteps((prev) => {
      const next = new Set(prev)
      if (next.has(stepId)) next.delete(stepId)
      else next.add(stepId)
      return next
    })
  const fanSteps = [...agentsByStep.keys()]

  const layout = useMemo(() => layoutWorkflow(graph, collapsed, expandedItems), [graph, collapsed, expandedItems])

  function focusDiagnostic(diagnostic: WorkflowDiagnosticDto, target: string) {
    const endpoints = [...new Set([target, ...(diagnostic.edge ? [diagnostic.edge.from, diagnostic.edge.to] : [])])]
    for (const section of graph.sections) {
      if (collapsed.has(section.id) && section.nodeIds.some((id) => endpoints.includes(id)))
        setCollapsed(graph.name, section.id, false)
    }
    setSelectedId(layout.nodes.find((node) => node.stepId === target)?.id ?? target)
    setDiagnosticFocus({ target, endpoints })
  }

  const activeFlow = fullscreen ? fullscreenFlow : inlineFlow
  useEffect(() => {
    if (!diagnosticFocus || !selectedId || parseNodeId(selectedId).stepId !== diagnosticFocus.target) return
    const nodes = layout.nodes
      .filter((node) => diagnosticFocus.endpoints.includes(node.stepId))
      .map((node) => ({ id: node.id }))
    const frame = requestAnimationFrame(() => {
      inspectorHeading.current?.focus()
      if (nodes.length > 0) void activeFlow?.fitView({ nodes, maxZoom: 1.3, padding: 0.3 })
      setDiagnosticFocus(null)
    })
    return () => cancelAnimationFrame(frame)
  }, [activeFlow, diagnosticFocus, layout.nodes, selectedId])

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
          diagnostics: [...new Set(s.nodeIds.flatMap((id) => nodeDiagnostics.get(id) ?? []))],
          onToggle: () => setCollapsed(graph.name, s.id, !s.collapsed),
        },
      }
      out.push(section)
    }
    for (const n of layout.nodes) {
      const stepExecs = execs.get(n.stepId) ?? []
      const mine = n.item ? stepExecs.filter((x) => x.itemKey === n.item) : stepExecs
      const agents = agentsByStep.get(n.stepId) ?? []
      const step: StepNode = {
        id: n.id,
        type: 'step',
        position: { x: n.x, y: n.y },
        width: NODE_W,
        height: NODE_H,
        draggable: false,
        data: {
          def: n.def,
          diagnostics: nodeDiagnostics.get(n.stepId) ?? [],
          status: rollupStatus(mine),
          execs: mine,
          items: n.item ? [] : groupByItem(mine).filter((g) => g.itemKey !== null),
          iterationsRun: new Set(run.nodes.filter((x) => x.nodeId === n.stepId && (!n.item || x.itemKey === n.item)).map((x) => x.iteration)).size,
          dir: n.dir,
          selected: selectedId === n.id,
          item: n.item,
          expandable: agents.length > 0,
          expanded: expandedSteps.has(n.stepId),
          agentCount: agents.length,
          onToggleExpand: agents.length > 0 ? () => toggleExpand(n.stepId) : undefined,
        },
      }
      out.push(step)
    }
    return out
  }, [layout, execs, run.nodes, selectedId, graph.name, setCollapsed, agentsByStep, expandedSteps, nodeDiagnostics])

  const flowEdges = useMemo(() => {
    // an edge animates while its visible target runs; for an agent node, only that agent's executions count
    const targetRunning = (target: string) => {
      const t = parseNodeId(target)
      return (execs.get(t.stepId) ?? []).some((x) => (!t.item || x.itemKey === t.item) && x.status === 'Running')
    }
    const out: Edge[] = []
    for (const e of layout.edges) {
      const taken = e.members.some((m) => edgeTakenIn(run, execs, m))
      const diagnostics = diagnosticCounts([...new Set(e.members.flatMap((edge) => edgeDiagnostics.get(edge) ?? []))])
      const color = diagnostics.label ? (diagnostics.errors ? '#ff8787' : '#ffd43b') : edgeColor(e, graph, taken)
      const marker = { type: MarkerType.ArrowClosed, color, width: 14, height: 14 }
      const backLabel = e.isBackEdge
        ? `↺ ${e.members.reduce((s, m) => s + backEdgeTaken(run, m), 0)}/${e.maxIterations ?? '∞'}${e.label ? ` ${e.label}` : ''}`
        : null
      const label = [backLabel ?? e.label, diagnostics.label].filter(Boolean).join(' · ')
      if (e.lane) {
        const lane: LaneEdge = {
          id: e.id,
          type: 'lane',
          source: e.source,
          target: e.target,
          sourceHandle: e.sourceHandle,
          targetHandle: e.targetHandle,
          markerEnd: marker,
          style: { stroke: color, strokeWidth: diagnostics.label ? 2.6 : taken ? 2 : 1.4, strokeDasharray: '5 4' },
          data: { laneX: e.lane.x, dropY: e.lane.dropY, label, color },
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
        animated: taken && !e.isBackEdge && targetRunning(e.target),
        style: { stroke: color, strokeWidth: diagnostics.label ? 2.6 : taken ? 2.2 : e.isBackEdge ? 1.4 : 1.1, strokeDasharray: e.isBackEdge ? '5 4' : undefined },
        label: label || undefined,
        labelStyle: { fill: diagnostics.label || e.isBackEdge || taken ? color : '#909296', fontSize: 10, fontWeight: taken ? 700 : 400 },
        labelBgStyle: { fill: '#1a1b1e' },
        labelBgPadding: [4, 2],
        labelBgBorderRadius: 4,
      })
    }
    return out
  }, [layout, execs, graph, run, edgeDiagnostics])
  const shown = iterationInfo(run, iteration)

  // a selected agent node shows its step's description with only that agent's executions
  const selection = selectedId ? parseNodeId(selectedId) : null
  const selectedDef = selection ? graph.nodes.find((n) => n.id === selection.stepId) : undefined
  const policyMatchesGraph = policy.data?.runId === run.id && policy.data.revisionId === run.workflowRevisionId && policy.data.hash === graph.hash
  const selectedPolicy = policyMatchesGraph ? policy.data?.nodes.find((node) => node.nodeId === selectedDef?.id) : undefined
  const selectedTitle = selection ? (selection.item ? `${selection.stepId} · ${selection.item}` : selection.stepId) : ''
  const selectedExecs = run.nodes
    .filter((n) => selection && n.nodeId === selection.stepId && (!selection.item || n.itemKey === selection.item))
    .sort((a, b) => a.iteration - b.iteration || Date.parse(a.startedAt ?? '') - Date.parse(b.startedAt ?? ''))

  // One canvas definition, rendered inline and (when open) in the fullscreen view.
  const canvas = (variant: 'inline' | 'fullscreen') => (
    <ReactFlow
      key={`${variant}:${collapseKey}|${expandKey}`}
      nodes={flowNodes}
      edges={flowEdges}
      nodeTypes={nodeTypes}
      edgeTypes={edgeTypes}
      onInit={variant === 'inline' ? setInlineFlow : setFullscreenFlow}
      fitView
      fitViewOptions={{ padding: variant === 'fullscreen' ? 0.04 : 0.06 }}
      minZoom={0.2}
      maxZoom={variant === 'fullscreen' ? 2.4 : 1.6}
      nodesDraggable={false}
      nodesConnectable={false}
      proOptions={{ hideAttribution: true }}
      colorMode="dark"
      onNodeClick={(_, n) => {
        if (n.type !== 'step') return
        setDiagnosticFocus(null)
        setSelectedId(n.id)
      }}
      onPaneClick={() => {
        setDiagnosticFocus(null)
        setSelectedId(null)
      }}
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
          {fanSteps.length > 0 && (
            <Tooltip label={`Show each agent of ${fanSteps.join(', ')} as its own node (or click "agents" on a step)`}>
              <Button
                size="compact-xs"
                variant={expandedSteps.size > 0 ? 'light' : 'subtle'}
                color="grape"
                data-testid="review-graph-toggle-agents"
                onClick={() => setExpandedSteps(expandedSteps.size > 0 ? new Set() : new Set(fanSteps))}
              >
                {expandedSteps.size > 0 ? 'Fold agents' : 'Show agents'}
              </Button>
            </Tooltip>
          )}
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
          <Tooltip label="Check this workflow against the server’s registered steps and graph rules">
            <Button size="compact-xs" variant="light" color="teal" leftSection={<TbShieldCheck size={12} />} loading={validate.isPending} onClick={() => validate.mutate(graph)}>
              Validate
            </Button>
          </Tooltip>
          <Text size="xs" ff="monospace" c="dimmed" title={graph.hash}>
            {graph.name} · {graph.hash.replace(/^sha256:/, '').slice(0, 12)}
          </Text>
        </Group>
    </>
  )

  const validationBody = (
    <>
      {validate.data && <ValidationPanel result={validate.data} nodeIds={knownNodes} onFocus={focusDiagnostic} />}
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
              <NodeSidePanel
                def={selectedDef}
                execs={selectedExecs}
                iteration={iteration}
                now={now}
                policy={selectedPolicy}
                policyPending={policy.isPending}
                policyUnavailable={policy.isError || (!policy.isPending && !policyMatchesGraph)}
                headingRef={!fullscreen ? inspectorHeading : undefined}
              />
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
                title={selectedTitle}
                badge={
                  <Badge size="xs" variant="light" color="gray" tt="none">
                    {selectedDef.kind}
                  </Badge>
                }
                onClose={() => setSelectedId(null)}
                testId="review-workflow-node-window"
              >
                <NodeSidePanel
                  def={selectedDef}
                  execs={selectedExecs}
                  iteration={iteration}
                  now={now}
                  policy={selectedPolicy}
                  policyPending={policy.isPending}
                  policyUnavailable={policy.isError || (!policy.isPending && !policyMatchesGraph)}
                  headingRef={fullscreen ? inspectorHeading : undefined}
                />
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
  return <WorkflowGraph key={run.id + ':' + (run.workflowRevisionId ?? workflow.data.hash)} run={run} graph={workflow.data} now={now} />
}
