import { Box, Group, Text, Button, ThemeIcon } from '@mantine/core'
import { VscPassFilled, VscArrowRight } from 'react-icons/vsc'

interface InlineTransitionCardProps {
  /** The stage name that just completed */
  stageName: string
  /** Callback when user clicks to switch to gate view */
  onSwitchToGate: () => void
}

/**
 * Inline card that appears at the end of the conversation stream when a stage
 * completes and an artifact is ready for review (UX-DR15).
 *
 * Clicking it transitions the page to Gate Mode. This is NOT a toast or overlay;
 * it renders inline within the ConversationTimeline.
 */
export function InlineTransitionCard({ stageName, onSwitchToGate }: InlineTransitionCardProps) {
  return (
    <Box
      style={{
        margin: 'var(--mantine-spacing-sm) 0',
        padding: 'var(--mantine-spacing-md)',
        borderRadius: 'var(--mantine-radius-md)',
        border: '1px solid var(--mantine-color-green-8)',
        backgroundColor: 'var(--mantine-color-dark-6)',
      }}
    >
      <Group justify="space-between" wrap="nowrap">
        <Group gap="sm">
          <ThemeIcon size="md" radius="xl" color="green" variant="light">
            <VscPassFilled size={14} />
          </ThemeIcon>
          <Box>
            <Text size="sm" fw={500}>
              Stage complete
            </Text>
            <Text size="xs" c="dimmed">
              {stageName} artifact ready for review
            </Text>
          </Box>
        </Group>
        <Button
          size="sm"
          variant="light"
          color="green"
          rightSection={<VscArrowRight size={14} />}
          onClick={onSwitchToGate}
        >
          Switch to gate view
        </Button>
      </Group>
    </Box>
  )
}
