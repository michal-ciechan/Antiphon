import { useRef, useCallback } from 'react'
import { Box, Tabs, Text, Stack, Badge, Group, UnstyledButton, Loader, Table, Tooltip } from '@mantine/core'
import { VscFile, VscInfo, VscComment, VscDiff, VscHistory } from 'react-icons/vsc'
import { TbLayoutSidebarLeftCollapse, TbLayoutSidebarRightCollapse, TbColumns2 } from 'react-icons/tb'
import { WorkflowOutputsTab } from './WorkflowOutputsTab'
import { BranchDiffViewer } from './BranchDiffViewer'
import { ArtifactDiffViewer } from '../artifact'
import { useAuditQuery } from '../../api/audit'
import type { AuditQueryResult, CostByModelDto } from '../../api/audit'
import type { TimelineMessage } from './types'
import type { StageDto } from '../../api/workflows'

export type ContextTab = 'outputs' | 'stage-info' | 'conversation' | 'diff' | 'audit'

interface ContextPanelProps {
  activeTab: ContextTab
  onTabChange: (tab: ContextTab) => void
  /** Current stage info */
  currentStage?: StageDto | null
  /** All stages for the pipeline mini-view */
  stages?: StageDto[]
  /** Messages for the Conversation tab */
  messages?: TimelineMessage[]
  /** Current stage ID for timeline expansion */
  currentStageId?: string
  /** Workflow ID for audit queries */
  workflowId?: string
  /** Old artifact content for diff tab */
  diffOldContent?: string
  /** New artifact content for diff tab */
  diffNewContent?: string
  /** Label for old version in diff */
  diffOldLabel?: string
  /** Label for new version in diff */
  diffNewLabel?: string
  layoutMode?: 'split' | 'panel' | 'conversation'
  onLayoutModeChange?: (mode: 'split' | 'panel' | 'conversation') => void
}

const LAYOUT_BUTTONS = [
  { mode: 'panel' as const, icon: <TbLayoutSidebarLeftCollapse size={14} />, label: 'Panel view' },
  { mode: 'split' as const, icon: <TbColumns2 size={14} />, label: 'Split view' },
  { mode: 'conversation' as const, icon: <TbLayoutSidebarRightCollapse size={14} />, label: 'Conversation view' },
]

const TAB_CONFIG: { value: ContextTab; label: string; icon: React.ReactNode }[] = [
  { value: 'outputs', label: 'Outputs', icon: <VscFile size={14} /> },
  { value: 'stage-info', label: 'Stage Info', icon: <VscInfo size={14} /> },
  { value: 'conversation', label: 'Conversation', icon: <VscComment size={14} /> },
  { value: 'diff', label: 'Diff', icon: <VscDiff size={14} /> },
  { value: 'audit', label: 'Audit', icon: <VscHistory size={14} /> },
]

function StageInfoTab({
  currentStage,
  stages,
}: {
  currentStage?: StageDto | null
  stages?: StageDto[]
}) {
  return (
    <Stack gap="sm">
      {/* Pipeline mini-view */}
      {stages && stages.length > 0 && (
        <Box>
          <Text size="xs" fw={600} mb={4}>
            Pipeline Progress
          </Text>
          <Stack gap={2}>
            {[...stages]
              .sort((a, b) => a.stageOrder - b.stageOrder)
              .map((stage) => {
                const isCurrent = currentStage?.id === stage.id
                return (
                  <Group
                    key={stage.id}
                    gap="xs"
                    style={{
                      padding: '2px 6px',
                      borderRadius: 'var(--mantine-radius-xs)',
                      backgroundColor: isCurrent
                        ? 'var(--mantine-color-active-9)'
                        : 'transparent',
                    }}
                  >
                    <Box
                      style={{
                        width: 8,
                        height: 8,
                        borderRadius: '50%',
                        backgroundColor:
                          stage.status === 'Completed'
                            ? 'var(--mantine-color-success-6)'
                            : stage.status === 'Running'
                              ? 'var(--mantine-color-active-5)'
                              : stage.status === 'Failed'
                                ? 'var(--mantine-color-danger-5)'
                                : 'var(--mantine-color-dark-4)',
                      }}
                    />
                    <Text size="xs" fw={isCurrent ? 600 : 400}>
                      {stage.name}
                    </Text>
                    <Text size="xs" c="dimmed" ml="auto">
                      v{stage.currentVersion}
                    </Text>
                  </Group>
                )
              })}
          </Stack>
        </Box>
      )}

      {/* Current stage details */}
      {currentStage ? (
        <Box>
          <Text size="xs" fw={600} mb={4}>
            Current Stage
          </Text>
          <Stack gap={2}>
            <Group gap="xs">
              <Text size="xs" c="dimmed">
                Name:
              </Text>
              <Text size="xs">{currentStage.name}</Text>
            </Group>
            <Group gap="xs">
              <Text size="xs" c="dimmed">
                Status:
              </Text>
              <Badge
                size="xs"
                variant="light"
                color={
                  currentStage.status === 'Completed'
                    ? 'success'
                    : currentStage.status === 'Running'
                      ? 'active'
                      : currentStage.status === 'Failed'
                        ? 'danger'
                        : 'gray'
                }
              >
                {currentStage.status}
              </Badge>
            </Group>
            <Group gap="xs">
              <Text size="xs" c="dimmed">
                Version:
              </Text>
              <Text size="xs">{currentStage.currentVersion}</Text>
            </Group>
            <Group gap="xs">
              <Text size="xs" c="dimmed">
                Gate Required:
              </Text>
              <Text size="xs">{currentStage.gateRequired ? 'Yes' : 'No'}</Text>
            </Group>
          </Stack>
        </Box>
      ) : (
        <Text c="dimmed" size="sm">
          No stage information available.
        </Text>
      )}
    </Stack>
  )
}

function formatCost(usd: number): string {
  if (usd < 0.01) return `$${usd.toFixed(6)}`
  if (usd < 1) return `$${usd.toFixed(4)}`
  return `$${usd.toFixed(2)}`
}

function formatTokens(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}k`
  return String(count)
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

function CostByModelRow({ model }: { model: CostByModelDto }) {
  return (
    <Table.Tr>
      <Table.Td>
        <Text size="xs" fw={500}>{model.modelName}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="xs">{formatCost(model.costUsd)}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="xs">{formatTokens(model.tokensIn + model.tokensOut)}</Text>
      </Table.Td>
      <Table.Td>
        <Text size="xs">{model.callCount}</Text>
      </Table.Td>
    </Table.Tr>
  )
}

function AuditTab({ workflowId, stageId }: { workflowId?: string; stageId?: string }) {
  const { data, isLoading, error } = useAuditQuery({
    workflowId,
    stageId,
    take: 50,
  })

  if (!workflowId && !stageId) {
    return (
      <Stack align="center" justify="center" style={{ height: '100%' }}>
        <VscHistory size={32} color="var(--mantine-color-dimmed)" />
        <Text c="dimmed" size="sm">
          Select a workflow to view audit trail.
        </Text>
      </Stack>
    )
  }

  if (isLoading) {
    return (
      <Stack align="center" justify="center" style={{ height: '100%' }}>
        <Loader size="sm" />
        <Text c="dimmed" size="sm">Loading audit data...</Text>
      </Stack>
    )
  }

  if (error) {
    return (
      <Text c="danger" size="sm">
        Failed to load audit data.
      </Text>
    )
  }

  if (!data || data.totalCount === 0) {
    return (
      <Stack align="center" justify="center" style={{ height: '100%' }}>
        <VscHistory size={32} color="var(--mantine-color-dimmed)" />
        <Text c="dimmed" size="sm">
          No audit records yet.
        </Text>
      </Stack>
    )
  }

  const { costSummary, records } = data as AuditQueryResult

  return (
    <Stack gap="sm">
      {/* Cost Summary */}
      <Box>
        <Text size="xs" fw={600} mb={4}>
          Cost Summary
        </Text>
        <Stack gap={2}>
          <Group gap="xs">
            <Text size="xs" c="dimmed">Total Cost:</Text>
            <Text size="xs" fw={600}>{formatCost(costSummary.totalCostUsd)}</Text>
          </Group>
          <Group gap="xs">
            <Text size="xs" c="dimmed">Tokens In:</Text>
            <Text size="xs">{formatTokens(costSummary.totalTokensIn)}</Text>
            <Text size="xs" c="dimmed" ml="xs">Out:</Text>
            <Text size="xs">{formatTokens(costSummary.totalTokensOut)}</Text>
          </Group>
          <Group gap="xs">
            <Text size="xs" c="dimmed">LLM Calls:</Text>
            <Text size="xs">{costSummary.totalLlmCalls}</Text>
            <Text size="xs" c="dimmed" ml="xs">Tool Calls:</Text>
            <Text size="xs">{costSummary.totalToolCalls}</Text>
          </Group>
        </Stack>
      </Box>

      {/* Cost by Model */}
      {costSummary.byModel.length > 0 && (
        <Box>
          <Text size="xs" fw={600} mb={4}>
            Cost by Model
          </Text>
          <Table striped highlightOnHover withTableBorder style={{ fontSize: '0.7rem' }}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th><Text size="xs">Model</Text></Table.Th>
                <Table.Th><Text size="xs">Cost</Text></Table.Th>
                <Table.Th><Text size="xs">Tokens</Text></Table.Th>
                <Table.Th><Text size="xs">Calls</Text></Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {costSummary.byModel.map((model) => (
                <CostByModelRow key={model.modelName} model={model} />
              ))}
            </Table.Tbody>
          </Table>
        </Box>
      )}

      {/* Execution Timeline */}
      <Box>
        <Text size="xs" fw={600} mb={4}>
          Execution Timeline ({data.totalCount} events)
        </Text>
        <Stack gap={2}>
          {records.map((record) => (
            <Box
              key={record.id}
              style={{
                padding: '4px 8px',
                borderRadius: 'var(--mantine-radius-xs)',
                border: '1px solid var(--mantine-color-dark-5)',
                backgroundColor: 'var(--mantine-color-dark-7)',
              }}
            >
              <Group gap="xs" justify="space-between">
                <Badge
                  size="xs"
                  variant="light"
                  color={
                    record.eventType === 'LlmCall'
                      ? 'blue'
                      : record.eventType === 'ToolInvocation'
                        ? 'grape'
                        : record.eventType === 'GoBack' || record.eventType === 'UpdateBasedOnDiff'
                          ? 'yellow'
                        : record.eventType === 'WorkflowOpened'
                          ? 'teal'
                        : record.eventType === 'WorkflowClosed'
                          ? 'pink'
                        : record.eventType === 'SessionDisconnected'
                          ? 'red'
                        : record.eventType === 'WorkflowCreated'
                          ? 'green'
                        : record.eventType === 'GateApproved'
                          ? 'green'
                        : record.eventType === 'GateRejected'
                          ? 'red'
                        : record.eventType === 'WorkflowCompleted'
                          ? 'teal'
                        : record.eventType === 'WorkflowAbandoned' || record.eventType === 'WorkflowPaused'
                          ? 'orange'
                        : 'gray'
                  }
                >
                  {record.eventType}
                </Badge>
                <Text size="xs" c="dimmed">
                  {new Date(record.createdAt).toLocaleTimeString()}
                </Text>
              </Group>
              <Text size="xs" mt={2}>{record.summary}</Text>
              {record.tokensIn > 0 && (
                <Group gap="xs" mt={2}>
                  <Text size="xs" c="dimmed">
                    {formatTokens(record.tokensIn)} in / {formatTokens(record.tokensOut)} out
                  </Text>
                  {record.costUsd > 0 && (
                    <Text size="xs" c="dimmed">
                      {formatCost(record.costUsd)}
                    </Text>
                  )}
                  {record.durationMs > 0 && (
                    <Text size="xs" c="dimmed">
                      {formatDuration(record.durationMs)}
                    </Text>
                  )}
                </Group>
              )}
            </Box>
          ))}
        </Stack>
      </Box>
    </Stack>
  )
}

const MESSAGE_TYPE_CONFIG: Record<string, { label: string; color: string }> = {
  'agent':        { label: 'Agent',   color: 'blue' },
  'tool-call':    { label: 'Tool',    color: 'grape' },
  'user-prompt':  { label: 'Prompt',  color: 'green' },
  'user-comment': { label: 'Comment', color: 'orange' },
  'system-event': { label: 'System',  color: 'gray' },
}

function ConversationCompactList({
  messages,
  currentStageId,
}: {
  messages: TimelineMessage[]
  currentStageId?: string
}) {
  if (messages.length === 0) {
    return (
      <Stack align="center" justify="center" style={{ height: '100%' }}>
        <VscComment size={32} color="var(--mantine-color-dimmed)" />
        <Text c="dimmed" size="sm">No conversation yet.</Text>
      </Stack>
    )
  }

  // Group by stage maintaining order
  const stageOrder: string[] = []
  const stageMap = new Map<string, { stageName: string; messages: TimelineMessage[] }>()
  for (const msg of messages) {
    if (!stageMap.has(msg.stageId)) {
      stageMap.set(msg.stageId, { stageName: msg.stageName, messages: [] })
      stageOrder.push(msg.stageId)
    }
    stageMap.get(msg.stageId)!.messages.push(msg)
  }

  return (
    <Stack gap={2} p="sm">
      {stageOrder.map((stageId) => {
        const group = stageMap.get(stageId)!
        const isCurrent = stageId === currentStageId
        return (
          <Box key={stageId}>
            {/* Stage header */}
            <Text
              size="xs"
              fw={600}
              c={isCurrent ? 'active' : 'dimmed'}
              mb={4}
              mt={stageOrder.indexOf(stageId) > 0 ? 'xs' : 0}
            >
              {group.stageName}
            </Text>
            <Stack gap={2}>
              {group.messages.map((msg) => {
                const typeConfig = MESSAGE_TYPE_CONFIG[msg.type] ?? { label: msg.type, color: 'gray' }
                const label = msg.type === 'tool-call' && msg.toolName
                  ? `Tool: ${msg.toolName}`
                  : typeConfig.label
                const content = msg.type === 'tool-call'
                  ? (msg.toolOutput ?? msg.toolInput ?? '')
                  : msg.content

                return (
                  <Box
                    key={msg.id}
                    style={{
                      padding: '4px 8px',
                      borderRadius: 'var(--mantine-radius-xs)',
                      border: '1px solid var(--mantine-color-dark-5)',
                      backgroundColor: 'var(--mantine-color-dark-7)',
                    }}
                  >
                    <Group gap="xs" justify="space-between">
                      <Badge size="xs" variant="light" color={typeConfig.color}>
                        {label}
                      </Badge>
                      <Text size="xs" c="dimmed">
                        {new Date(msg.timestamp).toLocaleTimeString()}
                      </Text>
                    </Group>
                    {content && (
                      <Text size="xs" mt={2} lineClamp={3} style={{ wordBreak: 'break-word' }}>
                        {content}
                      </Text>
                    )}
                  </Box>
                )
              })}
            </Stack>
          </Box>
        )
      })}
    </Stack>
  )
}

export function ContextPanel({
  activeTab,
  onTabChange,
  currentStage,
  stages,
  messages,
  currentStageId,
  workflowId,
  diffOldContent,
  diffNewContent,
  diffOldLabel,
  diffNewLabel,
  layoutMode = 'split',
  onLayoutModeChange,
}: ContextPanelProps) {
  const collapsed = layoutMode === 'conversation'
  const expanded = layoutMode === 'panel'
  // Per-tab scroll preservation
  const scrollPositions = useRef<Record<string, number>>({})

  const handleTabChange = useCallback(
    (value: string | null) => {
      if (!value) return
      // Save current scroll position
      const currentPanel = document.querySelector('[data-context-panel-content]')
      if (currentPanel) {
        scrollPositions.current[activeTab] = currentPanel.scrollTop
      }
      onTabChange(value as ContextTab)
      // Restore scroll position for the new tab after render
      requestAnimationFrame(() => {
        const panel = document.querySelector('[data-context-panel-content]')
        if (panel) {
          panel.scrollTop = scrollPositions.current[value] ?? 0
        }
      })
    },
    [activeTab, onTabChange],
  )

  // Collapsed: show only a narrow strip with tab icons + expand button
  if (collapsed) {
    return (
      <Box
        style={{
          width: 32,
          flexShrink: 0,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          height: '100%',
          borderLeft: '1px solid var(--mantine-color-dark-4)',
          backgroundColor: 'var(--mantine-color-dark-7)',
          paddingTop: 4,
          gap: 2,
        }}
      >
        {/* Layout mode icons */}
        {LAYOUT_BUTTONS.map(({ mode, icon, label }) => (
          <Tooltip key={mode} label={label} position="left" withArrow>
            <UnstyledButton
              onClick={() => onLayoutModeChange?.(mode)}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 24,
                height: 24,
                borderRadius: 'var(--mantine-radius-xs)',
                color: layoutMode === mode ? 'var(--mantine-color-active-4)' : 'var(--mantine-color-dimmed)',
                backgroundColor: layoutMode === mode ? 'var(--mantine-color-active-9)' : 'transparent',
              }}
            >
              {icon}
            </UnstyledButton>
          </Tooltip>
        ))}

        {/* Tab icon shortcuts */}
        {TAB_CONFIG.map((tab) => (
          <Tooltip key={tab.value} label={tab.label} position="left" withArrow>
            <UnstyledButton
              onClick={() => {
                onLayoutModeChange?.('split')
                onTabChange(tab.value)
              }}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 24,
                height: 24,
                borderRadius: 'var(--mantine-radius-xs)',
                color:
                  activeTab === tab.value
                    ? 'var(--mantine-color-active-4)'
                    : 'var(--mantine-color-dimmed)',
                backgroundColor:
                  activeTab === tab.value
                    ? 'var(--mantine-color-active-9)'
                    : 'transparent',
              }}
            >
              {tab.icon}
            </UnstyledButton>
          </Tooltip>
        ))}
      </Box>
    )
  }

  return (
    <Box
      style={{
        width: expanded ? undefined : 360,
        flex: expanded ? 1 : undefined,
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        borderLeft: '1px solid var(--mantine-color-dark-4)',
      }}
    >
      <Tabs
        value={activeTab}
        onChange={handleTabChange}
        style={{ display: 'flex', flexDirection: 'column', height: '100%' }}
      >
        <Tabs.List style={{ position: 'relative' }}>
          {TAB_CONFIG.map((tab) => (
            <Tabs.Tab
              key={tab.value}
              value={tab.value}
              leftSection={tab.icon}
              style={{ fontSize: '0.75rem' }}
            >
              {tab.label}
            </Tabs.Tab>
          ))}
          {/* Layout mode buttons — same 3 icons as conversation header, in sync */}
          <Group gap={2} ml="auto" pr={4} style={{ flexShrink: 0 }}>
            {LAYOUT_BUTTONS.map(({ mode, icon, label }) => (
              <Tooltip key={mode} label={label} position="left" withArrow>
                <UnstyledButton
                  onClick={() => onLayoutModeChange?.(mode)}
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
        </Tabs.List>

        {/* Outputs Tab */}
        <Tabs.Panel
          value="outputs"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
          }}
        >
          <WorkflowOutputsTab workflowId={workflowId} />
        </Tabs.Panel>

        {/* Stage Info Tab */}
        <Tabs.Panel
          value="stage-info"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
            padding: 'var(--mantine-spacing-sm)',
          }}
        >
          <StageInfoTab currentStage={currentStage} stages={stages} />
        </Tabs.Panel>

        {/* Conversation Tab -- compact audit-style list */}
        <Tabs.Panel
          value="conversation"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
            display: 'flex',
            flexDirection: 'column',
          }}
        >
          <ConversationCompactList
            messages={messages ?? []}
            currentStageId={currentStageId}
          />
        </Tabs.Panel>

        {/* Diff Tab */}
        <Tabs.Panel
          value="diff"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
          }}
        >
          {/* Branch diff (base branch vs workflow branch) */}
          <BranchDiffViewer workflowId={workflowId} />

          {/* Artifact version diff (shown when two versions are selected) */}
          {diffOldContent != null && diffNewContent != null && (
            <Box mt="xs">
              <ArtifactDiffViewer
                oldContent={diffOldContent}
                newContent={diffNewContent}
                oldLabel={diffOldLabel}
                newLabel={diffNewLabel}
              />
            </Box>
          )}
        </Tabs.Panel>

        {/* Audit Tab */}
        <Tabs.Panel
          value="audit"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
            padding: 'var(--mantine-spacing-sm)',
          }}
        >
          <AuditTab workflowId={workflowId} stageId={currentStage?.id} />
        </Tabs.Panel>
      </Tabs>
    </Box>
  )
}
