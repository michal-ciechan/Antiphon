import { Box, Loader, Text, Stack } from '@mantine/core'

export function ConversationView() {
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
        <Loader size="lg" color="active" />
        <Text size="lg" fw={500}>
          Agent executing...
        </Text>
        <Text size="sm" c="dimmed">
          Streaming output will appear here.
        </Text>
      </Stack>
    </Box>
  )
}
