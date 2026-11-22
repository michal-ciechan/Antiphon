import { Badge, Box, Group, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { BaseEdge, EdgeLabelRenderer, Handle, Position, type Edge, type EdgeProps, type Node, type NodeProps } from '@xyflow/react'
import type { ReactNode } from 'react'
import {
  TbArrowMerge,
  TbArrowsSplit2,
  TbBinaryTree,
  TbChevronDown,
  TbChevronRight,
  TbCode,
  TbHourglass,
  TbMessage2,
  TbRobot,
  TbUserCheck,
} from 'react-icons/tb'
import type { NodeExecutionDto, NodeKind, NodeStatus, WorkflowNodeDto } from '../../../../api/reviewEngine'
import { NODE_KIND_META, NODE_STATUS_META, modelColor, modelShort } from '../../reviewMeta'
import { fmtTokens } from '../../reviewFormat'
import { NODE_H, NODE_W, type Dir } from './workflowLayout'

const KIND_ICON: Record<NodeKind, ReactNode> = {
  Code: <TbCode size={13} />,
  Decision: <TbBinaryTree size={13} />,
  Completion: <TbMessage2 size={13} />,
  Agent: <TbRobot size={13} />,
  FanOut: <TbArrowsSplit2 size={13} />,
  Join: <TbArrowMerge size={13} />,
  Gate: <TbUserCheck size={13} />,
  Wait: <TbHourglass size={13} />,
}

const hidden = { opacity: 0, width: 6, height: 6, minWidth: 0, minHeight: 0, border: 0 }

/** Every handle the layout routes through; side handles flip with the band's direction. */
function StepHandles({ dir }: { dir: Dir }) {
  return (
    <>
      <Handle type="target" id="in" position={dir === 'ltr' ? Position.Left : Position.Right} style={hidden} />
      <Handle type="source" id="out" position={dir === 'ltr' ? Position.Right : Position.Left} style={hidden} />
      <Handle type="target" id="top-in" position={Position.Top} style={hidden} />
      <Handle type="source" id="top-out" position={Position.Top} style={{ ...hidden, left: '65%' }} />
      <Handle type="target" id="bottom-in" position={Position.Bottom} style={{ ...hidden, left: '35%' }} />
      <Handle type="source" id="bottom-out" position={Position.Bottom} style={hidden} />
      <Handle type="target" id="back-in" position={Position.Left} style={{ ...hidden, top: '75%' }} />
    </>
  )
}

export type StepData = {
  def: WorkflowNodeDto
  status: NodeStatus
  execs: NodeExecutionDto[]
  /** a fan-out child's executions grouped by `itemKey` (e.g. reviewer groups); empty otherwise */
  items: { itemKey: string | null; execs: NodeExecutionDto[]; status: NodeStatus }[]
  /** how many iterations ran this node */
  iterationsRun: number
  dir: Dir
  selected: boolean
  /** one agent of an expanded fan-out step: its item (e.g. the reviewer group); null otherwise */
  item: string | null
  /** a fan-out step that can be shown as one node per agent */
  expandable: boolean
  expanded: boolean
  /** how many agents the step expands into */
  agentCount: number
  onToggleExpand?: () => void
}
export type StepNode = Node<StepData, 'step'>

/** The control that splits a fan-out step into its agents, or folds an agent back into its step. */
function ExpandToggle({ expanded, count, step, onToggle }: { expanded: boolean; count: number; step: string; onToggle: () => void }) {
  return (
    <Tooltip label={expanded ? `Fold the agents back into ${step}` : `Show each of the ${count} agents`} openDelay={200}>
      <UnstyledButton
        className="nodrag nopan"
        aria-label={expanded ? `Collapse ${step} agents` : `Expand ${step} into agents`}
        data-testid={expanded ? `review-graph-collapse-${step}` : `review-graph-expand-${step}`}
        onClick={(e) => {
          e.stopPropagation()
          onToggle()
        }}
        style={{ display: 'flex', alignItems: 'center', gap: 2, padding: '0 4px', borderRadius: 4, border: '1px solid var(--mantine-color-dark-3)', lineHeight: 1.4 }}
      >
        {expanded ? <TbArrowMerge size={11} /> : <TbArrowsSplit2 size={11} />}
        <Text size="9px" fw={600} c="gray.4">
          {expanded ? 'fold' : `${count} agents`}
        </Text>
      </UnstyledButton>
    </Tooltip>
  )
}

export function StepNodeView({ data }: NodeProps<StepNode>) {
  const { def, status, execs, items, dir, selected, iterationsRun, item, expandable, expanded, onToggleExpand } = data
  const meta = NODE_STATUS_META[status]
  const kind = NODE_KIND_META[def.kind]
  const dashed = status === 'NotReached' || status === 'Pending' || status === 'Skipped'
  const side = `${selected ? 2 : 1.5}px ${dashed ? 'dashed' : 'solid'} ${selected ? '#fff' : meta.hex}`
  const fan = items.length > 0 || execs.length > 1
  const units = items.length > 0 ? items : execs
  const done = units.filter((u) => u.status === 'Succeeded').length
  const model = execs.find((e) => e.model)?.model ?? null
  const tokens = execs.reduce((s, e) => s + e.inputTokens, 0)
  return (
    <Box
      w={NODE_W}
      h={NODE_H}
      px={8}
      py={5}
      style={{
        background: status === 'NotReached' ? 'transparent' : 'var(--mantine-color-dark-6)',
        borderTop: side,
        borderRight: side,
        borderBottom: side,
        borderLeft: `4px solid ${meta.hex}`,
        borderRadius: def.kind === 'Gate' || def.kind === 'Wait' ? 14 : 6,
        opacity: status === 'NotReached' ? 0.5 : 1,
        animation: status === 'Running' ? 'reviewPulse 1.6s infinite' : undefined,
        overflow: 'hidden',
        cursor: 'pointer',
      }}
    >
      <StepHandles dir={dir} />
      <Group gap={4} wrap="nowrap" justify="space-between">
        <Group gap={4} wrap="nowrap" style={{ minWidth: 0 }}>
          <Box c={`${kind.color}.4`} style={{ display: 'flex' }}>
            {KIND_ICON[def.kind]}
          </Box>
          <Text size="12px" fw={700} truncate>
            {item ?? def.id}
          </Text>
        </Group>
        <Group gap={4} wrap="nowrap" style={{ flexShrink: 0 }}>
          {onToggleExpand && (expandable || item) && (
            <ExpandToggle expanded={expanded} count={data.agentCount} step={def.id} onToggle={onToggleExpand} />
          )}
          {iterationsRun > 1 && (
            <Tooltip label={`Ran in ${iterationsRun} iterations`}>
              <Badge size="xs" variant="outline" color="gray" px={4}>
                ×{iterationsRun}
              </Badge>
            </Tooltip>
          )}
        </Group>
      </Group>
      {item ? (
        <Text size="10px" c="dimmed" truncate>
          agent of {def.id}
        </Text>
      ) : items.length > 0 ? (
        <Group gap={3} wrap="nowrap" mt={3} mb={1} style={{ overflow: 'hidden' }}>
          {items.map((it) => (
            <Tooltip key={it.itemKey ?? '-'} label={`${it.itemKey} · ${NODE_STATUS_META[it.status].label}${it.execs[0]?.model ? ` · ${modelShort(it.execs[0].model)}` : ''}`} openDelay={100}>
              <Box
                h={9}
                style={{
                  flex: '1 1 0',
                  maxWidth: 34,
                  borderRadius: 2,
                  background: it.status === 'NotReached' ? 'transparent' : NODE_STATUS_META[it.status].hex,
                  border: '1px solid #5c5f66',
                  animation: it.status === 'Running' ? 'reviewPulse 1.6s infinite' : undefined,
                }}
              />
            </Tooltip>
          ))}
        </Group>
      ) : (
        <Text size="10px" c="dimmed" truncate>
          {def.run ?? kind.label}
        </Text>
      )}
      <Group gap={4} wrap="nowrap" mt={2}>
        <Text size="10px" fw={600} style={{ color: status === 'NotReached' ? undefined : meta.hex }} c={status === 'NotReached' ? 'dimmed' : undefined}>
          {fan ? `×${units.length} · ${done}/${units.length} done` : meta.label}
        </Text>
        {model && (
          <Text size="10px" ff="monospace" c={`${modelColor(model)}.4`} truncate>
            {modelShort(model)}
          </Text>
        )}
        {tokens > 0 && (
          <Text size="10px" ff="monospace" c="dimmed">
            {fmtTokens(tokens)}
          </Text>
        )}
      </Group>
    </Box>
  )
}

export type SectionData = {
  title: string
  collapsed: boolean
  members: { id: string; status: NodeStatus }[]
  dir: Dir
  onToggle: () => void
}
export type SectionNode = Node<SectionData, 'section'>

function rollupText(members: SectionData['members']): { text: string; status: NodeStatus } {
  const count = (s: NodeStatus) => members.filter((m) => m.status === s).length
  if (count('Running') > 0) return { text: `${count('Running')}/${members.length} running`, status: 'Running' }
  if (count('Failed') > 0) return { text: `${count('Failed')} failed`, status: 'Failed' }
  if (count('Waiting') > 0) return { text: 'parked', status: 'Waiting' }
  const done = count('Succeeded')
  if (done === 0) return { text: 'not reached', status: 'NotReached' }
  return { text: `${done}/${members.length} done`, status: 'Succeeded' }
}

/** An expanded section is a dashed frame with a clickable title; a collapsed one is a summary box. */
export function SectionNodeView({ data }: NodeProps<SectionNode>) {
  const r = rollupText(data.members)
  if (!data.collapsed)
    return (
      <Box w="100%" h="100%" style={{ border: '1px dashed var(--mantine-color-dark-3)', borderRadius: 10, background: 'rgba(255,255,255,0.018)', pointerEvents: 'none' }}>
        <UnstyledButton
          className="nodrag nopan"
          onClick={(e) => {
            e.stopPropagation()
            data.onToggle()
          }}
          style={{ position: 'absolute', top: 5, left: 10, pointerEvents: 'all' }}
          aria-label={`Collapse ${data.title}`}
        >
          <Group gap={4} wrap="nowrap">
            <TbChevronDown size={12} />
            <Text size="11px" fw={700} tt="uppercase" c="gray.4" style={{ letterSpacing: 0.6 }}>
              {data.title}
            </Text>
            <Text size="10px" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              {data.members.length} steps · {r.text}
            </Text>
          </Group>
        </UnstyledButton>
      </Box>
    )
  const hex = NODE_STATUS_META[r.status].hex
  return (
    <Box
      w="100%"
      h="100%"
      px={10}
      py={6}
      onClick={(e) => {
        e.stopPropagation()
        data.onToggle()
      }}
      aria-label={`Expand ${data.title}`}
      style={{
        background: 'var(--mantine-color-dark-6)',
        borderTop: `1.5px solid ${hex}`,
        borderRight: `1.5px solid ${hex}`,
        borderBottom: `1.5px solid ${hex}`,
        borderLeft: `4px solid ${hex}`,
        borderRadius: 8,
        cursor: 'pointer',
        animation: r.status === 'Running' ? 'reviewPulse 1.6s infinite' : undefined,
        boxShadow: '0 3px 0 -1px var(--mantine-color-dark-4)',
      }}
    >
      <StepHandles dir={data.dir} />
      <Group justify="space-between" wrap="nowrap" gap={8}>
        <Group gap={6} wrap="nowrap">
          <TbChevronRight size={13} />
          <Text size="12px" fw={700} style={{ whiteSpace: 'nowrap' }}>
            {data.title}
          </Text>
          <Text size="10px" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            {data.members.length} steps · collapsed
          </Text>
        </Group>
        <Text size="10px" fw={600} style={{ color: r.status === 'NotReached' ? 'var(--mantine-color-dimmed)' : hex }} truncate>
          {r.text}
        </Text>
      </Group>
      <Group gap={3} mt={6} wrap="nowrap">
        {data.members.map((m) => (
          <Tooltip key={m.id} label={`${m.id} · ${NODE_STATUS_META[m.status].label}`} openDelay={150}>
            <Box w={16} h={8} style={{ borderRadius: 2, background: m.status === 'NotReached' ? 'transparent' : NODE_STATUS_META[m.status].hex, border: '1px solid #5c5f66' }} />
          </Tooltip>
        ))}
      </Group>
      <Text size="10px" c="dimmed" mt={4}>
        click to expand
      </Text>
    </Box>
  )
}

export type LaneEdgeData = { laneX: number; dropY: number; label: string; color: string }
export type LaneEdge = Edge<LaneEdgeData, 'lane'>

/** A back edge into an earlier band: down out of the source, along the left lane, into the target. */
export function LaneEdgeView({ id, sourceX, sourceY, targetX, targetY, data, markerEnd, style }: EdgeProps<LaneEdge>) {
  if (!data) return null
  const path = `M ${sourceX},${sourceY} V ${data.dropY} H ${data.laneX} V ${targetY} H ${targetX}`
  return (
    <>
      <BaseEdge id={id} path={path} markerEnd={markerEnd} style={style} />
      <EdgeLabelRenderer>
        <div
          className="nodrag nopan"
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${(data.laneX + sourceX) / 2}px, ${data.dropY}px)`,
            fontSize: 10,
            padding: '0 5px',
            borderRadius: 8,
            background: 'var(--mantine-color-dark-7)',
            border: `1px solid ${data.color}`,
            color: data.color,
            whiteSpace: 'nowrap',
            pointerEvents: 'all',
          }}
        >
          {data.label}
        </div>
      </EdgeLabelRenderer>
    </>
  )
}
