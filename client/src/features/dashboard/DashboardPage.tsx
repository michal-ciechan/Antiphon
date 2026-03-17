import { useState } from 'react'
import { Container, Title, Table, Badge, Button, Group, Text, Anchor, Modal, ActionIcon, Tooltip } from '@mantine/core'
import { Link } from 'react-router'
import {
  useWorkflows,
  usePauseWorkflow,
  useResumeWorkflow,
  useAbandonWorkflow,
  type WorkflowDto,
  type WorkflowStatus,
} from '../../api/workflows'
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

function canPause(wf: WorkflowDto): boolean {
  return wf.availableTransitions.includes('Paused')
}

function canResume(wf: WorkflowDto): boolean {
  return wf.availableTransitions.includes('Running') && wf.status === 'Paused'
}

function canAbandon(wf: WorkflowDto): boolean {
  return wf.availableTransitions.includes('Abandoned')
}

export function DashboardPage() {
  const { data: workflows, isLoading } = useWorkflows()
  const [dialogOpened, setDialogOpened] = useState(false)
  const [abandonTarget, setAbandonTarget] = useState<WorkflowDto | null>(null)
  const pauseMutation = usePauseWorkflow()
  const resumeMutation = useResumeWorkflow()
  const abandonMutation = useAbandonWorkflow()

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
              <Table.Th>Actions</Table.Th>
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
                <Table.Td>
                  <Group gap="xs">
                    {canPause(wf) && (
                      <Tooltip label="Pause">
                        <ActionIcon
                          variant="light"
                          color="orange"
                          size="sm"
                          onClick={() => pauseMutation.mutate(wf.id)}
                          loading={pauseMutation.isPending}
                        >
                          ⏸
                        </ActionIcon>
                      </Tooltip>
                    )}
                    {canResume(wf) && (
                      <Tooltip label="Resume">
                        <ActionIcon
                          variant="light"
                          color="blue"
                          size="sm"
                          onClick={() => resumeMutation.mutate(wf.id)}
                          loading={resumeMutation.isPending}
                        >
                          ▶
                        </ActionIcon>
                      </Tooltip>
                    )}
                    {canAbandon(wf) && (
                      <Tooltip label="Abandon">
                        <ActionIcon
                          variant="light"
                          color="red"
                          size="sm"
                          onClick={() => setAbandonTarget(wf)}
                        >
                          ✕
                        </ActionIcon>
                      </Tooltip>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      <Modal
        opened={abandonTarget !== null}
        onClose={() => setAbandonTarget(null)}
        title="Abandon Workflow"
        centered
      >
        <Text mb="lg">
          Are you sure you want to abandon <strong>{abandonTarget?.name}</strong>? This action is
          irreversible and the workflow cannot be resumed.
        </Text>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => setAbandonTarget(null)}>
            Cancel
          </Button>
          <Button
            color="red"
            loading={abandonMutation.isPending}
            onClick={() => {
              if (abandonTarget) {
                abandonMutation.mutate(abandonTarget.id, {
                  onSuccess: () => setAbandonTarget(null),
                })
              }
            }}
          >
            Abandon
          </Button>
        </Group>
      </Modal>
    </Container>
  )
}
