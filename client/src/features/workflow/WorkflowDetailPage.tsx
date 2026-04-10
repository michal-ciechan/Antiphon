import { useState, useMemo, useCallback, useEffect } from 'react'
import { Box, Text, Title, Badge, Group, Loader, Stack, Tooltip, UnstyledButton, Menu, ActionIcon } from '@mantine/core'
import { TbLayoutSidebarLeftCollapse, TbLayoutSidebarRightCollapse, TbColumns2, TbSettings, TbTrash } from 'react-icons/tb'
import { useParams, useNavigate } from 'react-router'
import { useWorkflow, type StageDto, type WorkflowStatus } from '../../api/workflows'
import { useWorkflowArtifacts } from '../../api/artifacts'
import { useBranchDiff } from '../../api/projects'
import { DeleteWorkflowModal } from './DeleteWorkflowModal'
import { useApproveGate, useRejectGate, usePromptAgent, useAddComment } from '../../api/gates'
import { useGoBack, useAffectedStages, useSubmitCascade, type AffectedStageDto, type CascadeDecision } from '../../api/cascade'
import { StagePipeline } from './StagePipeline'
import { ContextPanel, type ContextTab } from './ContextPanel'
import { ConversationView } from './ConversationView'
import { GateView } from './GateView'
import { CascadeDecisionCard } from './CascadeDecisionCard'
import { InlineTransitionCard } from './InlineTransitionCard'
import { GateActionBar } from '../gate/GateActionBar'
import { useStreamingStore } from '../../stores/streamingStore'
import type { TimelineMessage, ArtifactDto } from './types'
import { ArtifactModal, type ArtifactVersion } from '../artifact'

type PageMode = 'conversation' | 'gate' | 'cascade'

const STATUS_COLORS: Record<WorkflowStatus, string> = {
  Created: 'gray',
  Running: 'blue',
  Paused: 'orange',
  GateWaiting: 'orange',
  CascadeWaiting: 'yellow',
  Completed: 'green',
  Failed: 'red',
  Abandoned: 'gray',
}

function deriveMode(status: WorkflowStatus): PageMode {
  if (status === 'CascadeWaiting') return 'cascade'
  return status === 'GateWaiting' ? 'gate' : 'conversation'
}

function defaultTab(mode: PageMode): ContextTab {
  return mode === 'gate' ? 'outputs' : 'stage-info'
}

export function WorkflowDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading, error } = useWorkflow(id)
  const { data: artifacts } = useWorkflowArtifacts(id)

  // Gate mutations
  const approveGate = useApproveGate(id)
  const rejectGate = useRejectGate(id)
  const promptAgent = usePromptAgent(id)
  const addComment = useAddComment(id)

  // Cascade mutations (Story 5.1 / 5.2)
  const goBack = useGoBack(id)
  const submitCascade = useSubmitCascade(id)
  const { data: affectedStages } = useAffectedStages(
    id,
    workflow?.status === 'CascadeWaiting',
  )

  // Local state for affected stages from go-back response
  const [goBackAffectedStages, setGoBackAffectedStages] = useState<AffectedStageDto[] | null>(null)

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

  // Branch diff — used to determine if there are outputs to default to
  const { data: branchDiff } = useBranchDiff(id)
  const hasOutputs = (branchDiff?.files?.length ?? 0) > 0

  // Track the active context panel tab; default to 'outputs' when outputs exist
  const [activeTab, setActiveTab] = useState<ContextTab | null>(null)
  const effectiveTab = activeTab ?? (hasOutputs ? 'outputs' : defaultTab(mode))

  // Selected completed stage artifact (for clicking pipeline stages)
  const [_selectedStage, setSelectedStage] = useState<StageDto | null>(null)

  // Version selection for artifact viewer
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null)

  // Layout mode: 'split' = default, 'panel' = right panel fills width, 'conversation' = left fills width
  const [layoutMode, setLayoutMode] = useState<'split' | 'panel' | 'conversation'>('split')

  // Artifact modal state — opened when user clicks an artifact in the Outputs tab
  const [modalArtifact, setModalArtifact] = useState<ArtifactDto | null>(null)

  // Delete confirmation modal
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false)

  // Track workflow page visits for audit trail
  useEffect(() => {
    if (!id) return
    fetch(`/api/workflows/${id}/visit`, { method: 'POST' }).catch(() => {})
    return () => {
      navigator.sendBeacon(`/api/workflows/${id}/close`)
    }
  }, [id])

  // Streaming store integration — messages from SignalR streaming events
  const streamingMessages = useStreamingStore((s) => s.messages)
  const storeIsStreaming = useStreamingStore((s) => s.isStreaming)

  // Combined messages: will include both persisted messages (future) and streaming messages
  const messages = useMemo<TimelineMessage[]>(() => {
    return streamingMessages
  }, [streamingMessages])

  const handleStageClick = (stage: StageDto) => {
    if (stage.status === 'Completed') {
      setSelectedStage(stage)
      setActiveTab('outputs')
    }
  }

  // Gate action handlers — no confirmation dialogs (UX-DR25, recoverable actions)
  const handleApprove = useCallback(() => {
    approveGate.mutate()
  }, [approveGate])

  const handleReject = useCallback(() => {
    rejectGate.mutate('')
  }, [rejectGate])

  const handleGoBack = useCallback(() => {
    // Go-back targets the earliest completed stage before the current gate stage
    if (!workflow) return
    const completedStages = workflow.stages
      .filter((s) => s.status === 'Completed')
      .sort((a, b) => a.stageOrder - b.stageOrder)
    const targetStage = completedStages[completedStages.length - 1]
    if (!targetStage) return

    goBack.mutate(targetStage.id, {
      onSuccess: (response) => {
        setGoBackAffectedStages(response.affectedStages)
      },
    })
  }, [workflow, goBack])

  const handleCascadeConfirm = useCallback(
    (decisions: CascadeDecision[]) => {
      submitCascade.mutate(decisions, {
        onSuccess: () => {
          setGoBackAffectedStages(null)
        },
      })
    },
    [submitCascade],
  )

  const handleCascadeCancel = useCallback(() => {
    // Cancel just dismisses the card — workflow remains in CascadeWaiting
    // User can re-fetch affected stages via the cascade/affected endpoint
    setGoBackAffectedStages(null)
  }, [])

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

  // Compute diff content for the ContextPanel Diff tab
  // Shows diff between previous version and current version of the current stage artifact
  const { diffOldContent, diffNewContent, diffOldLabel, diffNewLabel } = useMemo(() => {
    if (!artifacts || !currentStage || currentStage.currentVersion < 2) {
      return { diffOldContent: undefined, diffNewContent: undefined, diffOldLabel: undefined, diffNewLabel: undefined }
    }
    const stageArtifacts = artifacts.filter((a) => a.stageId === currentStage.id)
    const currentVersionArtifact = stageArtifacts.find((a) => a.version === currentStage.currentVersion)
    const previousVersionArtifact = stageArtifacts.find((a) => a.version === currentStage.currentVersion - 1)

    if (!currentVersionArtifact || !previousVersionArtifact) {
      return { diffOldContent: undefined, diffNewContent: undefined, diffOldLabel: undefined, diffNewLabel: undefined }
    }

    return {
      diffOldContent: previousVersionArtifact.content,
      diffNewContent: currentVersionArtifact.content,
      diffOldLabel: `v${previousVersionArtifact.version}`,
      diffNewLabel: `v${currentVersionArtifact.version}`,
    }
  }, [artifacts, currentStage])

  // Whether the InlineTransitionCard should show at the end of conversation
  const showTransitionCard =
    workflow?.status === 'GateWaiting' && mode === 'conversation'

  const isStreaming = workflow?.status === 'Running' || storeIsStreaming
  const isMutating =
    approveGate.isPending ||
    rejectGate.isPending ||
    promptAgent.isPending ||
    addComment.isPending ||
    goBack.isPending ||
    submitCascade.isPending

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
          <Group gap={4}>
            <Text size="sm" c="dimmed">
              {workflow.templateName} / {workflow.projectName}
            </Text>
            <Group gap={2} ml="xs">
              {(
                [
                  { mode: 'panel', icon: <TbLayoutSidebarLeftCollapse size={16} />, label: 'Panel view' },
                  { mode: 'split', icon: <TbColumns2 size={16} />, label: 'Split view' },
                  { mode: 'conversation', icon: <TbLayoutSidebarRightCollapse size={16} />, label: 'Conversation view' },
                ] as const
              ).map(({ mode, icon, label }) => (
                <Tooltip key={mode} label={label} withArrow>
                  <UnstyledButton
                    onClick={() => setLayoutMode(mode)}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      width: 26,
                      height: 26,
                      borderRadius: 'var(--mantine-radius-xs)',
                      color: layoutMode === mode ? 'var(--mantine-color-active-4)' : 'var(--mantine-color-dimmed)',
                      backgroundColor: layoutMode === mode ? 'var(--mantine-color-active-9)' : 'transparent',
                    }}
                  >
                    {icon}
                  </UnstyledButton>
                </Tooltip>
              ))}
            </Group>
            <Menu shadow="md" width={200} position="bottom-end">
              <Menu.Target>
                <ActionIcon variant="subtle" size="sm" aria-label="Workflow settings">
                  <TbSettings size={16} />
                </ActionIcon>
              </Menu.Target>
              <Menu.Dropdown>
                <Menu.Item
                  color="red"
                  leftSection={<TbTrash size={14} />}
                  onClick={() => setConfirmDeleteOpen(true)}
                >
                  Delete Workflow
                </Menu.Item>
              </Menu.Dropdown>
            </Menu>
          </Group>
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
            display: layoutMode === 'panel' ? 'none' : 'flex',
            flexDirection: 'column',
            overflow: 'auto',
          }}
        >
          {mode === 'cascade' ? (
            <CascadeDecisionCard
              affectedStages={goBackAffectedStages ?? affectedStages ?? []}
              onConfirm={handleCascadeConfirm}
              onCancel={handleCascadeCancel}
              isSubmitting={submitCascade.isPending}
            />
          ) : mode === 'conversation' ? (
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
          currentStage={currentStage}
          stages={workflow.stages}
          messages={messages}
          currentStageId={currentStage?.id}
          workflowId={id}
          branchDiff={branchDiff}
          diffOldContent={diffOldContent}
          diffNewContent={diffNewContent}
          diffOldLabel={diffOldLabel}
          diffNewLabel={diffNewLabel}
          layoutMode={layoutMode}
          onLayoutModeChange={setLayoutMode}
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

      {/* Delete workflow confirmation modal */}
      <DeleteWorkflowModal
        workflowId={id!}
        opened={confirmDeleteOpen}
        onClose={() => setConfirmDeleteOpen(false)}
        onDeleted={() => navigate('/')}
      />

      {/* Artifact viewer modal — opened when clicking an output in the Outputs tab */}
      <ArtifactModal
        artifact={modalArtifact}
        workflowId={id}
        onClose={() => setModalArtifact(null)}
      />
    </Box>
  )
}
