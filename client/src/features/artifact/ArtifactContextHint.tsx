import { Group, Text, Anchor } from '@mantine/core'

interface ArtifactContextHintProps {
  /** Number of revisions for this artifact */
  revisions: number
  /** ISO timestamp of when the artifact was created/last updated */
  createdAt: string
  /** Callback to navigate to conversation view for the originating stage */
  onViewConversation?: () => void
}

/**
 * Small breadcrumb/context line above the artifact (UX-DR8).
 * Shows: "Generated . {revisions} revisions . {time ago} . [View conversation ->]"
 */
export function ArtifactContextHint({
  revisions,
  createdAt,
  onViewConversation,
}: ArtifactContextHintProps) {
  const timeAgo = formatTimeAgo(createdAt)

  return (
    <Group
      gap="xs"
      style={{
        padding: 'var(--mantine-spacing-xs) 0',
        borderBottom: '1px solid var(--mantine-color-dark-5)',
        marginBottom: 'var(--mantine-spacing-sm)',
      }}
    >
      <Text size="xs" c="dimmed">
        Generated
      </Text>
      <Text size="xs" c="dimmed">
        &middot;
      </Text>
      <Text size="xs" c="dimmed">
        {revisions} {revisions === 1 ? 'revision' : 'revisions'}
      </Text>
      <Text size="xs" c="dimmed">
        &middot;
      </Text>
      <Text size="xs" c="dimmed">
        {timeAgo}
      </Text>
      {onViewConversation && (
        <>
          <Text size="xs" c="dimmed">
            &middot;
          </Text>
          <Anchor size="xs" onClick={onViewConversation} component="button" type="button">
            View conversation &rarr;
          </Anchor>
        </>
      )}
    </Group>
  )
}

function formatTimeAgo(isoDate: string): string {
  const date = new Date(isoDate)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHr = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHr / 24)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin}m ago`
  if (diffHr < 24) return `${diffHr}h ago`
  if (diffDay < 30) return `${diffDay}d ago`
  return date.toLocaleDateString()
}
