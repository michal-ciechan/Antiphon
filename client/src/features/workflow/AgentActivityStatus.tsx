import { Box, Text, Group } from '@mantine/core'
import { useStreamingStore, type ActivityStatus } from '../../stores/streamingStore'

function formatElapsed(ms: number): string {
  const seconds = Math.floor(ms / 1000)
  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  if (minutes > 0) {
    return `${minutes}m ${remainingSeconds}s`
  }
  return `${seconds}s`
}

function formatTokens(count: number): string {
  if (count >= 1000) {
    return `${(count / 1000).toFixed(1)}k`
  }
  return count.toString()
}

interface AgentActivityStatusProps {
  /** Override activity data (if not using store) */
  activity?: ActivityStatus | null
}

/**
 * Displays the current agent activity status line (FR18, UX-DR19).
 * Shows: pulsing dot, current action (tool name + target), tokens in/out,
 * tool call count, and elapsed time. Uses aria-live for accessibility.
 */
export function AgentActivityStatus({ activity: activityProp }: AgentActivityStatusProps) {
  const storeActivity = useStreamingStore((s) => s.activity)
  const isStreaming = useStreamingStore((s) => s.isStreaming)

  const activity = activityProp ?? storeActivity

  if (!activity && !isStreaming) {
    return null
  }

  return (
    <Box
      aria-live="polite"
      aria-atomic="true"
      role="status"
      style={{
        padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
        borderTop: '1px solid var(--mantine-color-dark-4)',
        backgroundColor: 'var(--mantine-color-dark-8)',
        flexShrink: 0,
      }}
    >
      <Group gap="md" wrap="nowrap">
        {/* Pulsing dot indicator */}
        <Box
          style={{
            width: 8,
            height: 8,
            borderRadius: '50%',
            backgroundColor: 'var(--mantine-color-active-5)',
            animation: isStreaming ? 'pulse 1.5s ease-in-out infinite' : 'none',
            flexShrink: 0,
          }}
        />

        {/* Current action */}
        <Text size="xs" c="dimmed" style={{ flex: 1, minWidth: 0 }} lineClamp={1}>
          {activity?.currentAction ?? 'Initializing...'}
        </Text>

        {/* Token counts */}
        {activity && (
          <Group gap="xs" wrap="nowrap" style={{ flexShrink: 0 }}>
            <Text size="xs" c="dimmed" title="Tokens in">
              {formatTokens(activity.tokensIn)} in
            </Text>
            <Text size="xs" c="dimmed">
              /
            </Text>
            <Text size="xs" c="dimmed" title="Tokens out">
              {formatTokens(activity.tokensOut)} out
            </Text>
          </Group>
        )}

        {/* Tool call count */}
        {activity && activity.toolCallCount > 0 && (
          <Text size="xs" c="dimmed" title="Tool calls" style={{ flexShrink: 0 }}>
            {activity.toolCallCount} tool{activity.toolCallCount !== 1 ? 's' : ''}
          </Text>
        )}

        {/* Elapsed time */}
        {activity && (
          <Text size="xs" c="dimmed" style={{ flexShrink: 0 }}>
            {formatElapsed(activity.elapsedMs)}
          </Text>
        )}
      </Group>

      {/* CSS animation for the pulsing dot */}
      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 1; }
          50% { opacity: 0.3; }
        }
      `}</style>
    </Box>
  )
}
