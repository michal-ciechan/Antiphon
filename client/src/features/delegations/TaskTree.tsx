import { ActionIcon, Badge, Box, Group, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { TbChevronDown, TbChevronRight, TbSitemap } from 'react-icons/tb'
import type { AgentTaskSummaryDto } from '../../api/agentTasks'
import { TierBadge } from './TaskChip'
import { STATUS_COLOR, countSubtree, formatCost, shortId, type TaskNode } from './taskVisuals'

/**
 * The shape of the fan-out. The lanes answer "what is happening right now"; this answers "who asked
 * for what" — which is the question that matters once nesting is normal, because a sub-orchestrator
 * collapsed to one row with its subtree's count and spend is how you read a run without drowning
 * in leaves.
 */
export function TaskTree({
  forest,
  expanded,
  selectedId,
  onToggle,
  onSelect,
}: {
  forest: TaskNode[]
  expanded: Set<string>
  selectedId: string | null
  onToggle: (id: string) => void
  onSelect: (task: AgentTaskSummaryDto) => void
}) {
  if (forest.length === 0) {
    return (
      <Text size="sm" c="dimmed" p="sm">
        No delegated tasks yet. Start one with “New task”, or let an orchestrator delegate with the
        antiphon-delegate skill.
      </Text>
    )
  }

  return (
    <Box>
      {forest.map((node) => (
        <TaskTreeRow
          key={node.task.id}
          node={node}
          depth={0}
          expanded={expanded}
          selectedId={selectedId}
          onToggle={onToggle}
          onSelect={onSelect}
        />
      ))}
    </Box>
  )
}

function TaskTreeRow({
  node,
  depth,
  expanded,
  selectedId,
  onToggle,
  onSelect,
}: {
  node: TaskNode
  depth: number
  expanded: Set<string>
  selectedId: string | null
  onToggle: (id: string) => void
  onSelect: (task: AgentTaskSummaryDto) => void
}) {
  const { task, children } = node
  const hasChildren = children.length > 0
  const isOpen = expanded.has(task.id)
  const selected = selectedId === task.id
  const subtreeCount = hasChildren ? countSubtree(node) : 1

  return (
    <>
      <Group gap={4} wrap="nowrap" pl={depth * 14} align="center" style={{ minWidth: 0 }}>
        {/* Indent guide: the vertical rule is what makes depth readable past two levels. */}
        {depth > 0 && (
          <Box
            aria-hidden
            style={{
              width: 1,
              alignSelf: 'stretch',
              backgroundColor: 'var(--mantine-color-dark-4)',
              marginRight: 4,
            }}
          />
        )}
        {hasChildren ? (
          <ActionIcon
            size="xs"
            variant="subtle"
            color="gray"
            onClick={() => onToggle(task.id)}
            aria-label={isOpen ? `Collapse ${task.title}` : `Expand ${task.title}`}
            aria-expanded={isOpen}
          >
            {isOpen ? <TbChevronDown size={14} /> : <TbChevronRight size={14} />}
          </ActionIcon>
        ) : (
          <Box w={22} />
        )}

        <UnstyledButton
          onClick={() => onSelect(task)}
          data-testid={`task-tree-row-${shortId(task.id)}`}
          aria-pressed={selected}
          style={{
            flex: 1,
            minWidth: 0,
            padding: '4px 6px',
            borderRadius: 4,
            backgroundColor: selected ? 'var(--mantine-color-dark-6)' : undefined,
            outline: selected ? '1px solid var(--mantine-color-active-5)' : undefined,
          }}
        >
          <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
            <Tooltip label={task.status} withArrow>
              <Box
                aria-hidden
                w={7}
                h={7}
                style={{
                  borderRadius: '50%',
                  flexShrink: 0,
                  backgroundColor: `var(--mantine-color-${STATUS_COLOR[task.status]}-6)`,
                }}
              />
            </Tooltip>
            {task.kind === 'Orchestrator' && <TbSitemap size={12} aria-hidden style={{ flexShrink: 0 }} />}
            <Text size="sm" truncate style={{ flex: 1, minWidth: 0 }}>
              {task.title}
            </Text>
            <Box style={{ flexShrink: 0 }}>
              <TierBadge level={task.modelLevel} />
            </Box>
          </Group>
        </UnstyledButton>

        {/* A collapsed parent must still account for its subtree, or the run reads as cheaper
            than it was. */}
        {hasChildren && !isOpen && (
          <Badge
            size="xs"
            variant="default"
            style={{ flexShrink: 0, fontVariantNumeric: 'tabular-nums' }}
          >
            {subtreeCount} · {formatCost(task.subtreeCostUsd)}
          </Badge>
        )}
      </Group>

      {isOpen &&
        children.map((child) => (
          <TaskTreeRow
            key={child.task.id}
            node={child}
            depth={depth + 1}
            expanded={expanded}
            selectedId={selectedId}
            onToggle={onToggle}
            onSelect={onSelect}
          />
        ))}
    </>
  )
}
