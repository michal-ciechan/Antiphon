import { useState, useMemo } from 'react'
import { Box, Text, Title, Badge, Group, Loader, Stack } from '@mantine/core'
import { useParams } from 'react-router'
import { useWorkflow, type StageDto, type WorkflowStatus } from '../../api/workflows'
import { StagePipeline } from './StagePipeline'
import { ContextPanel, type ContextTab } from './ContextPanel'
import { ConversationView } from './ConversationView'
import { GateView } from './GateView'

type PageMode = 'conversation' | 'gate'

const STATUS_COLORS: Record<WorkflowStatus, string> = {
  Created: 'gray',
  Running: 'blue',
  Paused: 'orange',
  GateWaiting: 'orange',
  Completed: 'green',
  Failed: 'red',
  Abandoned: 'gray',
}

function deriveMode(status: WorkflowStatus): PageMode {
  return status === 'GateWaiting' ? 'gate' : 'conversation'
}

function defaultTab(mode: PageMode): ContextTab {
  return mode === 'gate' ? 'outputs' : 'stage-info'
}

export function WorkflowDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { data: workflow, isLoading, error } = useWorkflow(id)

  // Mode is derived from workflow status, not URL
  const mode = useMemo<PageMode>(
    () => (workflow ? deriveMode(workflow.status) : 'conversation'),
    [workflow],
  )

  // Track the active context panel tab; default depends on mode
  const [activeTab, setActiveTab] = useState<ContextTab | null>(null)
  const effectiveTab = activeTab ?? defaultTab(mode)

  // Selected completed stage artifact (for clicking pipeline stages)
  const [_selectedStage, setSelectedStage] = useState<StageDto | null>(null)

  const handleStageClick = (stage: StageDto) => {
    if (stage.status === 'Completed') {
      setSelectedStage(stage)
      setActiveTab('outputs')
    }
  }

  if (isLoading) {
    return (
      <Box style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '60vh' }}>
        <Loader size="lg" />
      </Box>
    )
  }

  if (error || !workflow) {
    return (
      <Box style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '60vh' }}>
        <Stack align="center" gap="sm">
          <Text size="lg" fw={500} c="danger">
            Failed to load workflow
          </Text>
          <Text size="sm" c="dimmed">
            {error?.message ?? 'Workflow not found.'}
          </Text>
        </Stack>
      </Box>
    )
  }

  return (
    <Box style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 56px - 2 * var(--mantine-spacing-md))' }}>
      {/* Header: workflow info + StagePipeline */}
      <Box style={{ flexShrink: 0, paddingBottom: 'var(--mantine-spacing-sm)' }}>
        <Group justify="space-between" mb="xs">
          <Group gap="sm">
            <Title order={3}>{workflow.name}</Title>
            <Badge color={STATUS_COLORS[workflow.status]} variant="light">
              {workflow.status}
            </Badge>
          </Group>
          <Text size="sm" c="dimmed">
            {workflow.templateName} / {workflow.projectName}
          </Text>
        </Group>

        <StagePipeline stages={workflow.stages} onStageClick={handleStageClick} />
      </Box>

      {/* Main content area + ContextPanel */}
      <Box style={{ display: 'flex', flex: 1, minHeight: 0, gap: 0 }}>
        {/* Main area */}
        <Box
          style={{
            flex: 1,
            minWidth: 0,
            display: 'flex',
            flexDirection: 'column',
            overflow: 'auto',
          }}
        >
          {mode === 'conversation' ? (
            <ConversationView />
          ) : (
            <GateView stageName={workflow.currentStageName} />
          )}
        </Box>

        {/* Right panel — always visible */}
        <ContextPanel activeTab={effectiveTab} onTabChange={setActiveTab} />
      </Box>
    </Box>
  )
}
