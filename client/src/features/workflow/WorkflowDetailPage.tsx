import { useState, useMemo, useCallback } from 'react'
import { Box, Text, Title, Badge, Group, Loader, Stack } from '@mantine/core'
import { useParams } from 'react-router'
import { useWorkflow, type StageDto, type WorkflowStatus } from '../../api/workflows'
import { useWorkflowArtifacts } from '../../api/artifacts'
import { useApproveGate, useRejectGate, usePromptAgent, useAddComment } from '../../api/gates'
import { StagePipeline } from './StagePipeline'
import { ContextPanel, type ContextTab } from './ContextPanel'
import { ConversationView } from './ConversationView'
import { GateView } from './GateView'
import { InlineTransitionCard } from './InlineTransitionCard'
import { GateActionBar } from '../gate/GateActionBar'
import type { TimelineMessage, ArtifactDto } from './types'
import type { ArtifactVersion } from '../artifact'

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
  const { data: artifacts } = useWorkflowArtifacts(id)

  // Gate mutations
  const approveGate = useApproveGate(id)
  const rejectGate = useRejectGate(id)
  const promptAgent = usePromptAgent(id)
  const addComment = useAddComment(id)

  // Mode is derived from workflow status, but can be manually overridden
  // to support InlineTransitionCard switching to gate mode
  const [modeOverride, setModeOverride] = useState<PageMode | null>(null)
  const derivedMode = useMemo<PageMode>(
    () => (workflow ? deriveMode(workflow.status) : 'conversation'),
    [workflow],
  )
  const mode = modeOverride ?? derivedMode

  // Reset override when derived mode changes (e.g., after approve transitions back to running)
  const [prevDerived, setPrevDerived] = useState(derivedMode)
  if (derivedMode !== prevDerived) {
    setPrevDerived(derivedMode)
    setModeOverride(null)
  }

  // Track the active context panel tab; default depends on mode
  const [activeTab, setActiveTab] = useState<ContextTab | null>(null)
  const effectiveTab = activeTab ?? defaultTab(mode)

  // Selected completed stage artifact (for clicking pipeline stages)
  const [_selectedStage, setSelectedStage] = useState<StageDto | null>(null)

  // Version selection for artifact viewer
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null)

  // Conversation messages - will be populated by SignalR streaming in future stories.
  // For now, provide an empty array so the timeline renders properly.
  const [messages] = useState<TimelineMessage[]>([])

  const handleStageClick = (stage: StageDto) => {
    if (stage.status === 'Completed') {
      setSelectedStage(stage)
      setActiveTab('outputs')
    }
  }

  const handleArtifactClick = useCallback((_artifact: ArtifactDto) => {
    // Future: load artifact content in main area
    setActiveTab('outputs')
  }, [])

  // Gate action handlers — no confirmation dialogs (UX-DR25, recoverable actions)
  const handleApprove = useCallback(() => {
    approveGate.mutate()
  }, [approveGate])

  const handleReject = useCallback(() => {
    rejectGate.mutate('')
  }, [rejectGate])

  const handleGoBack = useCallback(() => {
    rejectGate.mutate('go-back')
  }, [rejectGate])

  const handleSendToAgent = useCallback(
    (text: string) => {
      promptAgent.mutate(text)
    },
    [promptAgent],
  )

  const handleAddComment = useCallback(
    (text: string) => {
      addComment.mutate(text)
    },
    [addComment],
  )

  // Switch to gate mode when InlineTransitionCard is clicked
  const handleSwitchToGate = useCallback(() => {
    setModeOverride('gate')
  }, [])

  // Switch back to conversation mode (from gate view "View conversation" link)
  const handleViewConversation = useCallback(() => {
    setModeOverride('conversation')
  }, [])

  // Find the current stage object
  const currentStage = useMemo(() => {
    if (!workflow) return null
    return workflow.stages.find((s) => s.name === workflow.currentStageName) ?? null
  }, [workflow])

  // Find the primary artifact for the current stage
  const currentArtifact = useMemo(() => {
    if (!artifacts || !currentStage) return null
    // Find primary artifact for current stage, at selected version or latest
    const stageArtifacts = artifacts.filter((a) => a.stageId === currentStage.id)
    if (selectedVersion != null) {
      return stageArtifacts.find((a) => a.version === selectedVersion) ?? null
    }
    // Get the primary artifact (or first available)
    return (
      stageArtifacts.find((a) => a.isPrimary) ??
      stageArtifacts[stageArtifacts.length - 1] ??
      null
    )
  }, [artifacts, currentStage, selectedVersion])

  // Build version history from artifacts
  const artifactVersions = useMemo<ArtifactVersion[]>(() => {
    if (!artifacts || !currentStage) return []
    const stageArtifacts = artifacts.filter((a) => a.stageId === currentStage.id)
    const maxVersion = Math.max(...stageArtifacts.map((a) => a.version), 0)
    return stageArtifacts.map((a) => ({
      version: a.version,
      createdAt: a.createdAt,
      isCurrent: a.version === maxVersion,
    }))
  }, [artifacts, currentStage])

  // Whether the InlineTransitionCard should show at the end of conversation
  const showTransitionCard =
    workflow?.status === 'GateWaiting' && mode === 'conversation'

  const isStreaming = workflow?.status === 'Running'
  const isMutating =
    approveGate.isPending ||
    rejectGate.isPending ||
    promptAgent.isPending ||
    addComment.isPending

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
            <>
              <ConversationView
                messages={messages}
                currentStageId={currentStage?.id}
                isStreaming={isStreaming}
              />
              {/* InlineTransitionCard at end of conversation when gate is ready (UX-DR15) */}
              {showTransitionCard && (
                <InlineTransitionCard
                  stageName={workflow.currentStageName ?? 'Unknown'}
                  onSwitchToGate={handleSwitchToGate}
                />
              )}
            </>
          ) : (
            <GateView
              stageName={workflow.currentStageName}
              artifact={currentArtifact}
              versions={artifactVersions}
              onViewConversation={handleViewConversation}
              onSelectVersion={setSelectedVersion}
            />
          )}
        </Box>

        {/* Right panel -- always visible */}
        <ContextPanel
          activeTab={effectiveTab}
          onTabChange={setActiveTab}
          artifacts={artifacts}
          onArtifactClick={handleArtifactClick}
          currentStage={currentStage}
          stages={workflow.stages}
          messages={messages}
          currentStageId={currentStage?.id}
        />
      </Box>

      {/* Persistent bottom action bar (UX-DR12) -- same instance across mode transitions */}
      <GateActionBar
        showGateActions={mode === 'gate'}
        onApprove={handleApprove}
        onReject={handleReject}
        onGoBack={handleGoBack}
        onSendToAgent={handleSendToAgent}
        onAddComment={handleAddComment}
        disabled={isMutating}
      />
    </Box>
  )
}
