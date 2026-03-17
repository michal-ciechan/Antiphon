import { Group, Box, Text, Tooltip, UnstyledButton } from '@mantine/core'
import type { StageDto, StageStatus } from '../../api/workflows'

interface StagePipelineProps {
  stages: StageDto[]
  onStageClick?: (stage: StageDto) => void
}

const STATUS_COLORS: Record<StageStatus, string> = {
  Completed: 'var(--mantine-color-success-6)',
  Running: 'var(--mantine-color-active-5)',
  Pending: 'var(--mantine-color-dark-4)',
  Failed: 'var(--mantine-color-danger-5)',
}

const STATUS_LABELS: Record<StageStatus, string> = {
  Completed: 'Done',
  Running: 'Active',
  Pending: 'Pending',
  Failed: 'Failed',
}

export function StagePipeline({ stages, onStageClick }: StagePipelineProps) {
  const sortedStages = [...stages].sort((a, b) => a.stageOrder - b.stageOrder)
  const completedCount = sortedStages.filter((s) => s.status === 'Completed').length

  return (
    <Box
      aria-label={`${completedCount} of ${sortedStages.length} stages complete`}
      role="navigation"
    >
      <Group gap={0} wrap="nowrap" align="center">
        {sortedStages.map((stage, index) => {
          const isClickable = stage.status === 'Completed' && !!onStageClick
          const isActive = stage.status === 'Running'

          const segment = (
            <Group key={stage.id} gap={0} wrap="nowrap" align="center">
              {index > 0 && (
                <Box
                  style={{
                    width: 24,
                    height: 2,
                    backgroundColor:
                      stage.status === 'Completed' || stage.status === 'Running'
                        ? 'var(--mantine-color-active-5)'
                        : 'var(--mantine-color-dark-4)',
                    flexShrink: 0,
                  }}
                />
              )}
              <Tooltip label={`${stage.name} — ${STATUS_LABELS[stage.status]}`}>
                {isClickable ? (
                  <UnstyledButton
                    onClick={() => onStageClick(stage)}
                    style={{ display: 'flex', alignItems: 'center', gap: 6 }}
                  >
                    <StageIndicator status={stage.status} isActive={isActive} />
                    <Text size="xs" fw={500} c={stage.status === 'Pending' ? 'dimmed' : undefined}>
                      {stage.name}
                    </Text>
                  </UnstyledButton>
                ) : (
                  <Group gap={6} wrap="nowrap" style={{ cursor: 'default' }}>
                    <StageIndicator status={stage.status} isActive={isActive} />
                    <Text size="xs" fw={500} c={stage.status === 'Pending' ? 'dimmed' : undefined}>
                      {stage.name}
                    </Text>
                  </Group>
                )}
              </Tooltip>
            </Group>
          )

          return segment
        })}
      </Group>
    </Box>
  )
}

function StageIndicator({ status, isActive }: { status: StageStatus; isActive: boolean }) {
  return (
    <Box
      style={{
        width: 12,
        height: 12,
        borderRadius: '50%',
        backgroundColor: STATUS_COLORS[status],
        flexShrink: 0,
        ...(isActive
          ? {
              animation: 'stage-pulse 2s ease-in-out infinite',
            }
          : {}),
      }}
    >
      {isActive && (
        <style>{`
          @keyframes stage-pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.5; }
          }
          @media (prefers-reduced-motion: reduce) {
            * { animation-duration: 0s !important; }
          }
        `}</style>
      )}
    </Box>
  )
}
