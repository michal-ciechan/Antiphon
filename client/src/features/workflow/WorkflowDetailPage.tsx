import { Container, Title, Text, Paper } from '@mantine/core'
import { useParams } from 'react-router'

export function WorkflowDetailPage() {
  const { id } = useParams<{ id: string }>()

  return (
    <Container size="md" py="xl">
      <Paper p="xl" radius="md" withBorder>
        <Title order={2} mb="sm">
          Workflow Detail
        </Title>
        <Text c="dimmed">
          Workflow Detail (ID: {id}) — coming in Epic 2
        </Text>
      </Paper>
    </Container>
  )
}
