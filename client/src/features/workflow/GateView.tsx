import { Box, Text, Stack, ThemeIcon } from '@mantine/core'

interface GateViewProps {
  stageName: string | null
}

export function GateView({ stageName }: GateViewProps) {
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
