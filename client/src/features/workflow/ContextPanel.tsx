import { useRef, useCallback } from 'react'
import { Box, Tabs, Text, Stack, Badge, Group, UnstyledButton } from '@mantine/core'
import { VscFile, VscInfo, VscComment, VscDiff, VscHistory } from 'react-icons/vsc'
import { ConversationTimeline } from './ConversationTimeline'
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

export function ContextPanel({
  activeTab,
  onTabChange,
  artifacts,
  onArtifactClick,
  currentStage,
  stages,
  messages,
  currentStageId,
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
            padding: 'var(--mantine-spacing-sm)',
          }}
        >
          <Stack align="center" justify="center" style={{ height: '100%' }}>
            <VscDiff size={32} color="var(--mantine-color-dimmed)" />
            <Text c="dimmed" size="sm">
              No diff available yet.
            </Text>
          </Stack>
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
          <Stack align="center" justify="center" style={{ height: '100%' }}>
            <VscHistory size={32} color="var(--mantine-color-dimmed)" />
            <Text c="dimmed" size="sm">
              Audit trail coming soon.
            </Text>
          </Stack>
        </Tabs.Panel>
      </Tabs>
    </Box>
  )
}
