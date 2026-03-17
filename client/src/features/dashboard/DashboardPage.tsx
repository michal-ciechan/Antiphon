import { useState } from 'react'
import { Container, Title, Table, Badge, Button, Group, Text, Anchor } from '@mantine/core'
import { Link } from 'react-router'
import { useWorkflows, type WorkflowStatus } from '../../api/workflows'
import { PipelineIndicator } from '../../shared/PipelineIndicator'
import { NewWorkflowDialog } from './NewWorkflowDialog'

const STATUS_COLORS: Record<WorkflowStatus, string> = {
  Created: 'gray',
  Running: 'blue',
  Paused: 'orange',
  GateWaiting: 'orange',
  Completed: 'green',
  Failed: 'red',
  Abandoned: 'gray',
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function DashboardPage() {
  const { data: workflows, isLoading } = useWorkflows()
  const [dialogOpened, setDialogOpened] = useState(false)

  return (
    <Container size="lg" py="xl">
      <NewWorkflowDialog opened={dialogOpened} onClose={() => setDialogOpened(false)} />
      <Group justify="space-between" mb="lg">
        <Title order={2}>Workflows</Title>
        <Button onClick={() => setDialogOpened(true)}>
          New Workflow
        </Button>
      </Group>

      {isLoading && <Text c="dimmed">Loading workflows...</Text>}

      {!isLoading && workflows && workflows.length === 0 && (
        <Text c="dimmed">No workflows yet. Create one to get started.</Text>
      )}

      {!isLoading && workflows && workflows.length > 0 && (
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>Current Stage</Table.Th>
              <Table.Th>Progress</Table.Th>
              <Table.Th>Created</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {workflows.map((wf) => (
              <Table.Tr key={wf.id}>
                <Table.Td>
                  <Anchor component={Link} to={`/workflow/${wf.id}`} fw={500}>
                    {wf.name}
                  </Anchor>
                </Table.Td>
                <Table.Td>
                  <Badge color={STATUS_COLORS[wf.status]} variant="light">
                    {wf.status}
                  </Badge>
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{wf.currentStageName ?? '—'}</Text>
                </Table.Td>
                <Table.Td>
                  <PipelineIndicator
                    completed={wf.completedStageCount}
                    total={wf.stageCount}
                  />
                </Table.Td>
                <Table.Td>
                  <Text size="sm">{formatDate(wf.createdAt)}</Text>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
    </Container>
  )
}
