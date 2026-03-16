import { Container, Title, Text, Paper } from '@mantine/core'

export function DashboardPage() {
  return (
    <Container size="md" py="xl">
      <Paper p="xl" radius="md" withBorder>
        <Title order={2} mb="sm">
          Dashboard
        </Title>
        <Text c="dimmed">Dashboard — coming in Epic 4</Text>
      </Paper>
    </Container>
  )
}
