import { Box, Group, Button } from '@mantine/core'
import { VscCheck, VscClose, VscArrowLeft } from 'react-icons/vsc'
import { PromptBar } from './PromptBar'

interface GateActionBarProps {
  /** Whether gate action buttons should be visible (only in gate mode) */
  showGateActions: boolean
  onApprove: () => void
  onReject: () => void
  onGoBack: () => void
  onSendToAgent: (text: string) => void
  onAddComment: (text: string) => void
  disabled?: boolean
}

/**
 * Persistent bottom action bar (UX-DR12).
 * Left side: gate action buttons (Approve, Reject, Go Back) - only visible during gate.
 * Right side: prompt input with Send to Agent + Add Comment buttons - always visible.
 *
 * This component is the SAME instance across mode transitions (never re-mounted).
 */
export function GateActionBar({
  showGateActions,
  onApprove,
  onReject,
  onGoBack,
  onSendToAgent,
  onAddComment,
  disabled = false,
}: GateActionBarProps) {
  return (
    <Box
      style={{
        flexShrink: 0,
        borderTop: '1px solid var(--mantine-color-dark-4)',
        padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
        backgroundColor: 'var(--mantine-color-dark-7)',
      }}
    >
      <Group gap="md" wrap="nowrap">
        {/* Gate action buttons — left side */}
        {showGateActions && (
          <Group gap="xs" style={{ flexShrink: 0 }}>
            <Button
              size="sm"
              color="success"
              leftSection={<VscCheck size={14} />}
              onClick={onApprove}
              disabled={disabled}
            >
              Approve
            </Button>
            <Button
              size="sm"
              color="warning"
              leftSection={<VscClose size={14} />}
              onClick={onReject}
              disabled={disabled}
            >
              Reject
            </Button>
            <Button
              size="sm"
              variant="default"
              leftSection={<VscArrowLeft size={14} />}
              onClick={onGoBack}
              disabled={disabled}
            >
              Go Back
            </Button>
          </Group>
        )}

        {/* Prompt bar — right side, always visible */}
        <PromptBar
          onSendToAgent={onSendToAgent}
          onAddComment={onAddComment}
          disabled={disabled}
        />
      </Group>
    </Box>
  )
}
