import { useRef, useCallback } from 'react'
import { Box, Tabs, Text, Stack, Badge, Group, UnstyledButton, Loader, Table } from '@mantine/core'
import { VscFile, VscInfo, VscComment, VscDiff, VscHistory } from 'react-icons/vsc'
import { ConversationTimeline } from './ConversationTimeline'
import { ArtifactDiffViewer } from '../artifact'
import { useAuditQuery } from '../../api/audit'
import type { AuditQueryResult, CostByModelDto } from '../../api/audit'
import type { TimelineMessage, ArtifactDto } from './types'
import type { StageDto } from '../../api/workflows'

export type ContextTab = 'outputs' | 'stage-info' | 'conversation' | 'diff' | 'audit'

interface ContextPanelProps {
  activeTab: ContextTab
  onTabChange: (tab: ContextTab) => void
  /** Artifacts for the Outputs tab */
  artifacts?: ArtifactDto[]
  /** Called when an artifact is clicked in the Outputs tab */
  onArtifactClick?: (artifact: ArtifactDto) => void
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
}

const TAB_CONFIG: { value: ContextTab; label: string; icon: React.ReactNode }[] = [
  { value: 'outputs', label: 'Outputs', icon: <VscFile size={14} /> },
  { value: 'stage-info', label: 'Stage Info', icon: <VscInfo size={14} /> },
  { value: 'conversation', label: 'Conversation', icon: <VscComment size={14} /> },
  { value: 'diff', label: 'Diff', icon: <VscDiff size={14} /> },
  { value: 'audit', label: 'Audit', icon: <VscHistory size={14} /> },
]

function OutputsTab({
  artifacts,
  onArtifactClick,
}: {
  artifacts?: ArtifactDto[]
  onArtifactClick?: (artifact: ArtifactDto) => void
}) {
  if (!artifacts || artifacts.length === 0) {
    return (
      <Text c="dimmed" size="sm">
        No outputs yet.
      </Text>
    )
  }

  // Sort: primary first, then by stage name
  const sorted = [...artifacts].sort((a, b) => {
    if (a.isPrimary && !b.isPrimary) return -1
    if (!a.isPrimary && b.isPrimary) return 1
    return a.stageName.localeCompare(b.stageName)
  })

  return (
    <Stack gap="xs">
      {sorted.map((artifact) => (
        <UnstyledButton
          key={artifact.id}
          onClick={() => onArtifactClick?.(artifact)}
          style={{
            padding: 'var(--mantine-spacing-xs)',
            borderRadius: 'var(--mantine-radius-sm)',
            border: '1px solid var(--mantine-color-dark-4)',
            backgroundColor: 'var(--mantine-color-dark-7)',
          }}
        >
          <Group gap="xs" wrap="nowrap">
            <VscFile size={16} />
            <Box style={{ flex: 1 }}>
              <Group gap="xs">
                <Text size="sm" fw={500}>
                  {artifact.fileName}
                </Text>
                {artifact.isPrimary && (
                  <Badge size="xs" color="active" variant="light">
                    Primary
                  </Badge>
                )}
              </Group>
              <Text size="xs" c="dimmed">
                {artifact.stageName} - v{artifact.version}
              </Text>
            </Box>
          </Group>
        </UnstyledButton>
      ))}
    </Stack>
  )
}

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
                      ? 'active'
                      : record.eventType === 'ToolInvocation'
                        ? 'grape'
                        : record.eventType === 'GoBack' || record.eventType === 'UpdateBasedOnDiff'
                          ? 'warning'
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

export function ContextPanel({
  activeTab,
  onTabChange,
  artifacts,
  onArtifactClick,
  currentStage,
  stages,
  messages,
  currentStageId,
  workflowId,
  diffOldContent,
  diffNewContent,
  diffOldLabel,
  diffNewLabel,
}: ContextPanelProps) {
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

  return (
    <Box
      style={{
        width: 360,
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
        <Tabs.List>
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
        </Tabs.List>

        {/* Outputs Tab */}
        <Tabs.Panel
          value="outputs"
          data-context-panel-content
          style={{
            flex: 1,
            overflow: 'auto',
            padding: 'var(--mantine-spacing-sm)',
          }}
        >
          <OutputsTab artifacts={artifacts} onArtifactClick={onArtifactClick} />
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

        {/* Conversation Tab -- renders the shared ConversationTimeline */}
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
          <ConversationTimeline
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
          {diffOldContent != null && diffNewContent != null ? (
            <ArtifactDiffViewer
              oldContent={diffOldContent}
              newContent={diffNewContent}
              oldLabel={diffOldLabel}
              newLabel={diffNewLabel}
            />
          ) : (
            <Stack align="center" justify="center" style={{ height: '100%', padding: 'var(--mantine-spacing-sm)' }}>
              <VscDiff size={32} color="var(--mantine-color-dimmed)" />
              <Text c="dimmed" size="sm">
                No diff available yet.
              </Text>
            </Stack>
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
