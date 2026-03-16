import { Container, Title, Text, Paper } from '@mantine/core'

export function SettingsPage() {
  return (
    <Container size="md" py="xl">
      <Paper p="xl" radius="md" withBorder>
        <Title order={2} mb="sm">
          Settings
        </Title>
        <Text c="dimmed">Settings — coming in Story 1.8</Text>
      </Paper>
    </Container>
  )
}
