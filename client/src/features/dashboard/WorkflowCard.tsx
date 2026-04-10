import { useCallback, useRef, useEffect, useState } from 'react'
import { Card, Badge, Group, Text, Box, ActionIcon, Tooltip } from '@mantine/core'
import { TbTrash } from 'react-icons/tb'
import { useNavigate } from 'react-router'
import type { WorkflowDto, WorkflowStatus } from '../../api/workflows'
import { MiniPipeline, type MiniPipelineStage } from './MiniPipeline'
import { DeleteWorkflowModal } from '../workflow/DeleteWorkflowModal'

interface WorkflowCardProps {
  workflow: WorkflowDto
  /** Set true when this card was just updated via SignalR */
  highlight?: boolean
  /** Set true when this card just appeared (new workflow) */
  fadeIn?: boolean
}

const BORDER_COLORS: Record<WorkflowStatus, string> = {
  Created: 'var(--mantine-color-gray-6)',
  Running: 'var(--mantine-color-blue-6)',
  Paused: 'var(--mantine-color-orange-6)',
  GateWaiting: 'var(--mantine-color-orange-6)',
  CascadeWaiting: 'var(--mantine-color-yellow-6)',
  Completed: 'var(--mantine-color-green-6)',
  Failed: 'var(--mantine-color-red-6)',
  Abandoned: 'var(--mantine-color-gray-6)',
}

const STATUS_COLORS: Record<WorkflowStatus, string> = {
  Created: 'gray',
  Running: 'blue',
  Paused: 'orange',
  GateWaiting: 'orange',
  CascadeWaiting: 'yellow',
  Completed: 'green',
  Failed: 'red',
  Abandoned: 'gray',
}

const STATUS_LABELS: Record<WorkflowStatus, string> = {
  Created: 'Created',
  Running: 'Active',
  Paused: 'Paused',
  GateWaiting: 'Pending Review',
  CascadeWaiting: 'Cascade Pending',
  Completed: 'Complete',
  Failed: 'Failed',
  Abandoned: 'Abandoned',
}

function formatRelativeTime(iso: string): string {
  const now = Date.now()
  const then = new Date(iso).getTime()
  const diffMs = now - then
  const diffMin = Math.floor(diffMs / 60000)
  if (diffMin < 1) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  const diffHrs = Math.floor(diffMin / 60)
  if (diffHrs < 24) return `${diffHrs}h ago`
  const diffDays = Math.floor(diffHrs / 24)
  if (diffDays < 7) return `${diffDays}d ago`
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}

/**
 * Build MiniPipeline stages from the summary counts on WorkflowDto.
 * We don't have individual stage data on the list DTO, so we synthesize
 * the stage indicators from completedStageCount, stageCount, and status.
 */
function buildMiniStages(wf: WorkflowDto): MiniPipelineStage[] {
  const stages: MiniPipelineStage[] = []
  for (let i = 0; i < wf.stageCount; i++) {
    if (i < wf.completedStageCount) {
      stages.push({ name: `Stage ${i + 1}`, status: 'Completed' })
    } else if (i === wf.completedStageCount) {
      // Current stage
      if (wf.status === 'Failed') {
        stages.push({ name: wf.currentStageName ?? `Stage ${i + 1}`, status: 'Failed' })
      } else if (wf.status === 'Running' || wf.status === 'GateWaiting' || wf.status === 'Paused') {
        stages.push({ name: wf.currentStageName ?? `Stage ${i + 1}`, status: 'Running' })
      } else {
        stages.push({ name: `Stage ${i + 1}`, status: 'Pending' })
      }
    } else {
      stages.push({ name: `Stage ${i + 1}`, status: 'Pending' })
    }
  }
  return stages
}

/**
 * WorkflowCard displays a single workflow in the dashboard grid.
 * Shows title, status badge, MiniPipeline, current stage, cost, last updated, template name.
 * Left border color indicates status. Hover shows subtle elevation. Click navigates to detail.
 * Keyboard accessible: focusable with Enter to navigate.
 */
export function WorkflowCard({ workflow, highlight, fadeIn }: WorkflowCardProps) {
  const navigate = useNavigate()
  const [isHighlighted, setIsHighlighted] = useState(!!highlight)
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false)
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // When highlight prop becomes true, start the glow animation
  useEffect(() => {
    if (highlight) {
      setIsHighlighted(true)
      timeoutRef.current = setTimeout(() => setIsHighlighted(false), 1500)
    }
    return () => {
      if (timeoutRef.current) clearTimeout(timeoutRef.current)
    }
  }, [highlight])

  const handleClick = useCallback(() => {
    navigate(`/workflow/${workflow.id}`)
  }, [navigate, workflow.id])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        handleClick()
      }
    },
    [handleClick],
  )

  const miniStages = buildMiniStages(workflow)

  return (
    <Card
      className={[
        'workflow-card',
        isHighlighted ? 'workflow-card-highlight' : '',
        fadeIn ? 'workflow-card-fade-in' : '',
      ]
        .filter(Boolean)
        .join(' ')}
      shadow="sm"
      padding="md"
      radius="md"
      withBorder
      tabIndex={0}
      role="article"
      aria-label={`Workflow: ${workflow.name}, status: ${STATUS_LABELS[workflow.status]}`}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      style={{
        borderLeftWidth: 4,
        borderLeftColor: BORDER_COLORS[workflow.status],
        cursor: 'pointer',
        transition: 'box-shadow 200ms ease, transform 100ms ease',
      }}
    >
      {/* Row 1: Title + Status Badge + Delete */}
      <Group justify="space-between" mb="xs" wrap="nowrap">
        <Text fw={600} size="md" lineClamp={1} style={{ flex: 1 }}>
          {workflow.name}
        </Text>
        <Group gap={4} style={{ flexShrink: 0 }}>
          <Badge color={STATUS_COLORS[workflow.status]} variant="light" size="sm">
            {STATUS_LABELS[workflow.status]}
          </Badge>
          {workflow.status !== 'Running' && (
            <Tooltip label="Delete workflow" withArrow>
              <ActionIcon
                variant="subtle"
                color="red"
                size="sm"
                aria-label="Delete workflow"
                onClick={(e) => {
                  e.stopPropagation()
                  setDeleteConfirmOpen(true)
                }}
              >
                <TbTrash size={14} />
              </ActionIcon>
            </Tooltip>
          )}
        </Group>
      </Group>

      {/* Row 2: MiniPipeline */}
      <Box mb="xs">
        <MiniPipeline stages={miniStages} />
      </Box>

      {/* Row 3: Current stage + Template */}
      <Group justify="space-between" mb={4}>
        <Text size="xs" c="dimmed" lineClamp={1}>
          {workflow.currentStageName ?? 'Not started'}
        </Text>
        <Text size="xs" c="dimmed" lineClamp={1}>
          {workflow.templateName}
        </Text>
      </Group>

      {/* Row 4: Project + Last updated */}
      <Group justify="space-between">
        <Text size="xs" c="dimmed" lineClamp={1}>
          {workflow.projectName}
        </Text>
        <Text size="xs" c="dimmed">
          {formatRelativeTime(workflow.updatedAt)}
        </Text>
      </Group>

      {/* Stop propagation so clicks inside the modal (Cancel, X, overlay) don't navigate */}
      <div onClick={(e) => e.stopPropagation()}>
        <DeleteWorkflowModal
          workflowId={workflow.id}
          opened={deleteConfirmOpen}
          onClose={() => setDeleteConfirmOpen(false)}
        />
      </div>
    </Card>
  )
}
