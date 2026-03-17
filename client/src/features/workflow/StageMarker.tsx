import { useState } from 'react'
import { Box, Text, Group, Badge, UnstyledButton, Collapse } from '@mantine/core'
import { VscChevronDown, VscChevronRight, VscCheck, VscClose, VscArrowLeft } from 'react-icons/vsc'
import type { StageGroup } from './types'

interface StageMarkerProps {
  stage: StageGroup
  /** Whether this stage should be expanded by default (current stage = true) */
  defaultExpanded: boolean
  children: React.ReactNode
}

const GATE_BADGE: Record<string, { color: string; label: string; icon: React.ReactNode }> = {
  approved: {
    color: 'success',
    label: 'Approved',
    icon: <VscCheck size={10} />,
  },
  rejected: {
    color: 'warning',
    label: 'Rejected',
    icon: <VscClose size={10} />,
  },
  'go-back': {
    color: 'gray',
    label: 'Go Back',
    icon: <VscArrowLeft size={10} />,
  },
}

export function StageMarker({ stage, defaultExpanded, children }: StageMarkerProps) {
  const [expanded, setExpanded] = useState(defaultExpanded)

  const gateBadge = stage.gateDecision ? GATE_BADGE[stage.gateDecision] : null

  return (
    <Box mb="xs">
      {/* Stage divider header */}
      <UnstyledButton
        onClick={() => setExpanded((v) => !v)}
        style={{
          width: '100%',
          padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
          backgroundColor: 'var(--mantine-color-dark-6)',
          borderRadius: 'var(--mantine-radius-sm)',
          borderLeft: '3px solid var(--mantine-color-active-5)',
        }}
      >
        <Group gap="sm" wrap="nowrap">
          {expanded ? (
            <VscChevronDown size={14} />
          ) : (
            <VscChevronRight size={14} />
          )}

          <Text size="sm" fw={700} style={{ flex: 1 }}>
            {stage.stageName}
          </Text>

          <Badge size="xs" variant="light" color="gray">
            v{stage.version}
          </Badge>

          <Text size="xs" c="dimmed">
            {stage.messageCount} message{stage.messageCount !== 1 ? 's' : ''}
          </Text>

          {gateBadge && (
            <Badge
              size="xs"
              variant="light"
              color={gateBadge.color}
              leftSection={gateBadge.icon}
            >
              {gateBadge.label}
            </Badge>
          )}

          <Text size="xs" c="dimmed">
            {new Date(stage.firstTimestamp).toLocaleTimeString()}
          </Text>
        </Group>
      </UnstyledButton>

      {/* Stage messages (collapsible) */}
      <Collapse in={expanded}>
        <Box pl="sm" pt="xs">
          {children}
        </Box>
      </Collapse>
    </Box>
  )
}
