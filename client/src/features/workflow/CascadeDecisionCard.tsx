import { useState, useCallback } from 'react'
import {
  Box,
  Text,
  Stack,
  Group,
  Button,
  Radio,
  Badge,
  Paper,
  ThemeIcon,
  Loader,
} from '@mantine/core'
import { VscWarning, VscCheck, VscClose } from 'react-icons/vsc'
import type {
  AffectedStageDto,
  CascadeAction,
  CascadeDecision,
} from '../../api/cascade'

interface CascadeDecisionCardProps {
  /** Affected stages detected by the go-back */
  affectedStages: AffectedStageDto[]
  /** Called when user confirms cascade decisions */
  onConfirm: (decisions: CascadeDecision[]) => void
  /** Called when user cancels the cascade */
  onCancel: () => void
  /** Whether the submit is in progress */
  isSubmitting?: boolean
}

const ACTION_LABELS: Record<CascadeAction, { label: string; description: string }> = {
  UpdateFromDiff: {
    label: 'Update from diff',
    description: 'Apply upstream changes via AI patch (recommended)',
  },
  Regenerate: {
    label: 'Regenerate',
    description: 'Re-execute this stage from scratch',
  },
  KeepAsIs: {
    label: 'Keep as-is',
    description: 'No changes to this stage',
  },
}

const ACTIONS: CascadeAction[] = ['UpdateFromDiff', 'Regenerate', 'KeepAsIs']

/**
 * Inline cascade decision card (UX-DR16).
 * Appears in the main content area when a go-back affects downstream stages.
 * Each affected stage shows its name, version, reason, and 3 radio options.
 * "Update from diff" is pre-selected as the default (FR26).
 */
export function CascadeDecisionCard({
  affectedStages,
  onConfirm,
  onCancel,
  isSubmitting = false,
}: CascadeDecisionCardProps) {
  // Initialize decisions with default action (UpdateFromDiff) for each stage
  const [decisions, setDecisions] = useState<Record<string, CascadeAction>>(() => {
    const initial: Record<string, CascadeAction> = {}
    for (const stage of affectedStages) {
      initial[stage.stageId] = stage.defaultAction
    }
    return initial
  })

  const handleActionChange = useCallback(
    (stageId: string, action: CascadeAction) => {
      setDecisions((prev) => ({ ...prev, [stageId]: action }))
    },
    [],
  )

  const handleConfirm = useCallback(() => {
    const result: CascadeDecision[] = affectedStages.map((stage) => ({
      stageId: stage.stageId,
      action: decisions[stage.stageId] ?? stage.defaultAction,
    }))
    onConfirm(result)
  }, [affectedStages, decisions, onConfirm])

  if (affectedStages.length === 0) {
    return null
  }

  return (
    <Paper
      p="lg"
      radius="md"
      style={{
        border: '1px solid var(--mantine-color-yellow-8)',
        backgroundColor: 'var(--mantine-color-dark-7)',
        margin: 'var(--mantine-spacing-md)',
      }}
    >
      <Stack gap="md">
        {/* Header */}
        <Group gap="sm">
          <ThemeIcon size="lg" radius="xl" color="yellow" variant="light">
            <VscWarning size={18} />
          </ThemeIcon>
          <Box>
            <Text size="lg" fw={600}>
              Course Correction: Affected Stages
            </Text>
            <Text size="sm" c="dimmed">
              The following downstream stages may be impacted by the changes.
              Choose how to handle each one.
            </Text>
          </Box>
        </Group>

        {/* Affected stages list */}
        <Stack gap="sm">
          {affectedStages.map((stage) => (
            <Paper
              key={stage.stageId}
              p="md"
              radius="sm"
              style={{
                border: '1px solid var(--mantine-color-dark-4)',
                backgroundColor: 'var(--mantine-color-dark-6)',
              }}
            >
              <Stack gap="xs">
                {/* Stage header */}
                <Group justify="space-between">
                  <Group gap="xs">
                    <Text fw={500}>{stage.stageName}</Text>
                    <Badge size="sm" variant="light" color="gray">
                      v{stage.currentVersion}
                    </Badge>
                  </Group>
                  <Text size="xs" c="dimmed">
                    Stage {stage.stageOrder + 1}
                  </Text>
                </Group>

                {/* Reason */}
                <Text size="sm" c="dimmed">
                  {stage.reason}
                </Text>

                {/* Radio options */}
                <Radio.Group
                  value={decisions[stage.stageId] ?? stage.defaultAction}
                  onChange={(value) =>
                    handleActionChange(stage.stageId, value as CascadeAction)
                  }
                >
                  <Stack gap={4} mt="xs">
                    {ACTIONS.map((action) => (
                      <Radio
                        key={action}
                        value={action}
                        label={
                          <Group gap="xs">
                            <Text size="sm" fw={500}>
                              {ACTION_LABELS[action].label}
                            </Text>
                            <Text size="xs" c="dimmed">
                              {ACTION_LABELS[action].description}
                            </Text>
                          </Group>
                        }
                        styles={{
                          radio: { cursor: 'pointer' },
                          label: { cursor: 'pointer' },
                        }}
                      />
                    ))}
                  </Stack>
                </Radio.Group>
              </Stack>
            </Paper>
          ))}
        </Stack>

        {/* Actions */}
        <Group justify="flex-end" gap="sm">
          <Button
            variant="default"
            leftSection={<VscClose size={14} />}
            onClick={onCancel}
            disabled={isSubmitting}
          >
            Cancel
          </Button>
          <Button
            color="blue"
            leftSection={isSubmitting ? <Loader size={14} /> : <VscCheck size={14} />}
            onClick={handleConfirm}
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Applying...' : 'Confirm Cascade'}
          </Button>
        </Group>
      </Stack>
    </Paper>
  )
}
