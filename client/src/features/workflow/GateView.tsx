import { Box, Text, Stack, ThemeIcon, Loader } from '@mantine/core'
import { ArtifactViewer } from '../artifact'
import type { ArtifactVersion } from '../artifact'
import type { ArtifactDto } from './types'

interface GateViewProps {
  stageName: string | null
  /** The primary artifact to render in gate mode */
  artifact?: ArtifactDto | null
  /** Whether the artifact is still loading */
  isLoading?: boolean
  /** All versions of this artifact for version history */
  versions?: ArtifactVersion[]
  /** Callback to navigate to conversation view */
  onViewConversation?: () => void
  /** Callback when user selects a different version */
  onSelectVersion?: (version: number) => void
}

/**
 * Gate Mode main area (UX-DR8).
 * Shows the primary artifact rendered at constrained width (~900px) with
 * ArtifactContextHint above it. Falls back to a placeholder when no
 * artifact is available yet.
 */
export function GateView({
  stageName,
  artifact,
  isLoading = false,
  versions,
  onViewConversation,
  onSelectVersion,
}: GateViewProps) {
  if (isLoading) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: 0,
        }}
      >
        <Loader size="lg" />
      </Box>
    )
  }

  // If no artifact content, show placeholder
  if (!artifact?.content) {
    return (
      <Box
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: 0,
        }}
      >
        <Stack align="center" gap="md">
          <ThemeIcon size="xl" radius="xl" color="warning" variant="light">
            !
          </ThemeIcon>
          <Text size="lg" fw={500}>
            Artifact ready for review
          </Text>
          {stageName && (
            <Text size="sm" c="dimmed">
              Stage: {stageName}
            </Text>
          )}
          <Text size="sm" c="dimmed">
            Review the artifact and approve, reject, or provide feedback.
          </Text>
        </Stack>
      </Box>
    )
  }

  return (
    <Box
      style={{
        flex: 1,
        overflow: 'auto',
        minHeight: 0,
      }}
    >
      <ArtifactViewer
        content={artifact.content}
        revisions={artifact.version}
        createdAt={artifact.createdAt}
        constrainedWidth={true}
        versions={versions}
        currentVersion={artifact.version}
        onSelectVersion={onSelectVersion}
        onViewConversation={onViewConversation}
      />
    </Box>
  )
}
