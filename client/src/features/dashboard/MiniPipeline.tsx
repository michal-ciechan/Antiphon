import { Group, Box, Tooltip } from '@mantine/core'
import type { StageStatus } from '../../api/workflows'

export interface MiniPipelineStage {
  name: string
  status: StageStatus
}

interface MiniPipelineProps {
  stages: MiniPipelineStage[]
}

const STATUS_COLORS: Record<StageStatus, string> = {
  Completed: 'var(--mantine-color-green-6)',
  Running: 'var(--mantine-color-blue-6)',
  Pending: 'var(--mantine-color-dark-4)',
  Failed: 'var(--mantine-color-red-6)',
}

/**
 * Compact horizontal stage indicator dots.
 * - Done = green filled
 * - Active = blue with pulse animation
 * - Pending = gray
 * - Failed = red
 *
 * Adapts dot size based on stage count (UX-DR20).
 */
export function MiniPipeline({ stages }: MiniPipelineProps) {
  if (stages.length === 0) return null

  // Adapt dot size to stage count so it fits in a card
  const dotSize = stages.length > 8 ? 8 : stages.length > 5 ? 10 : 12

  return (
    <Group gap={4} wrap="nowrap" align="center" role="img" aria-label={buildAriaLabel(stages)}>
      {stages.map((stage, i) => (
        <Tooltip key={i} label={`${stage.name}: ${stage.status}`} withArrow>
          <Box
            className={stage.status === 'Running' ? 'mini-pipeline-pulse' : undefined}
            style={{
              width: dotSize,
              height: dotSize,
              borderRadius: '50%',
              backgroundColor: STATUS_COLORS[stage.status],
              transition: 'background-color 300ms ease',
              flexShrink: 0,
            }}
          />
        </Tooltip>
      ))}
    </Group>
  )
}

function buildAriaLabel(stages: MiniPipelineStage[]): string {
  const completed = stages.filter((s) => s.status === 'Completed').length
  const active = stages.find((s) => s.status === 'Running')
  const failed = stages.find((s) => s.status === 'Failed')
  let label = `Pipeline: ${completed} of ${stages.length} stages completed`
  if (active) label += `, currently running ${active.name}`
  if (failed) label += `, ${failed.name} failed`
  return label
}
