import { Anchor, Badge, Box, Group, Loader, Stack, Text } from '@mantine/core'
import { useInterval } from '@mantine/hooks'
import { useMemo, useState, type CSSProperties } from 'react'
import { Link } from 'react-router'
import { useAgentList, type AgentSummaryDto } from '../../../api/agents'
import { usePipeline } from '../../../api/agentTasks'
import { useAttention } from '../../../api/attention'
import { useHomeTasks, type HomeTaskGroup, type HomeTaskItemDto } from '../../../api/homeTasks'
import { HomeTaskModal } from './HomeTaskModal'
import { TaskCard } from './TaskCard'
import {
  GROUP_LABEL,
  GROUP_ORDER,
  filterByProject,
  groupItems,
  livenessFor,
  pipelineRowFor,
  questionFor,
} from './homeTasksModel'

const ALWAYS_VISIBLE: ReadonlySet<HomeTaskGroup> = new Set(['NeedsHuman', 'Running'])

const EMPTY_LINE: Record<'NeedsHuman' | 'Running', string> = {
  NeedsHuman: 'Nothing needs you.',
  Running: 'Nothing running.',
}

/**
 * The home-rail Tasks section: one list of Cards and unbound delegations, grouped, filtered to
 * the selected project's directories. Modal routing lives in `HomeTaskModal`.
 */
export function TasksSection({
  dirKeys,
  workspaceAgents = [],
  onSelectAgent,
  style,
}: {
  dirKeys: string[]
  workspaceAgents?: AgentSummaryDto[]
  onSelectAgent?: (agentId: string) => void
  style?: CSSProperties
}) {
  const homeTasks = useHomeTasks()
  const attention = useAttention()
  const agents = useAgentList()
  // A failed pipeline fetch is "no enrichment" — no second error line. The projection owns the one.
  const pipeline = usePipeline()
  const [now, setNow] = useState(() => Date.now())
  useInterval(() => setNow(Date.now()), 60_000, { autoInvoke: true })
  const [openItem, setOpenItem] = useState<HomeTaskItemDto | null>(null)

  const filtered = useMemo(
    () => filterByProject(homeTasks.data?.items ?? [], dirKeys),
    [homeTasks.data, dirKeys],
  )
  const grouped = useMemo(() => groupItems(filtered), [filtered])
  const workspaceIds = useMemo(
    () => new Set(workspaceAgents.map((agent) => agent.id)),
    [workspaceAgents],
  )

  const handleSelectAgent = onSelectAgent
    ? (agentId: string) => {
        if (workspaceIds.has(agentId)) onSelectAgent(agentId)
      }
    : undefined

  const layout: CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    ...style,
  }

  if (homeTasks.error) {
    return (
      <Box style={layout}>
        <SectionHeader />
        <Text size="sm" c="dimmed">
          Tasks are unavailable — the server did not answer for them.
        </Text>
      </Box>
    )
  }

  if (!homeTasks.data) {
    return (
      <Box style={layout}>
        <SectionHeader />
        <Loader size="sm" />
      </Box>
    )
  }

  const attentionItems = attention.data?.items ?? []
  const agentList = agents.data ?? []

  return (
    <Box style={layout}>
      <SectionHeader />
      <Stack gap="sm" style={{ minHeight: 0, flex: 1, overflowY: 'auto' }}>
        {GROUP_ORDER.map((group) => {
          const visible = grouped[group]
          const hidden = group === 'NeedsHuman' || group === 'Running' ? 0 : grouped.hidden[group]
          const always = ALWAYS_VISIBLE.has(group)
          if (!always && visible.length === 0) {
            return group === 'Done' ? <Box key={group} id="home-tasks-done" /> : null
          }

          return (
            <Stack
              key={group}
              gap={6}
              id={group === 'Done' ? 'home-tasks-done' : undefined}
            >
              <Group gap="xs" data-testid={`home-tasks-group-${group}`}>
                <Text size="xs" tt="uppercase" fw={700} c="dimmed">
                  {GROUP_LABEL[group]}
                </Text>
                <Badge size="xs" variant="default">
                  {visible.length + hidden}
                </Badge>
              </Group>
              {visible.length === 0 ? (
                <Text size="xs" c="dimmed">
                  {EMPTY_LINE[group as 'NeedsHuman' | 'Running']}
                </Text>
              ) : (
                visible.map((item) => (
                  <TaskCard
                    key={item.key}
                    item={item}
                    question={questionFor(item, attentionItems)}
                    agents={agentList}
                    liveness={livenessFor(item, attentionItems)}
                    pipelineRow={pipelineRowFor(item, pipeline.data)}
                    pipeline={pipeline.data ?? null}
                    now={now}
                    onOpen={() => setOpenItem(item)}
                    onOpenTask={(taskId) =>
                      setOpenItem({
                        ...item,
                        source: 'Delegation',
                        id: taskId,
                        key: `task:${taskId}`,
                      })
                    }
                    onSelectAgent={handleSelectAgent}
                  />
                ))
              )}
              <MoreLink hidden={hidden} visible={visible} group={group} />
            </Stack>
          )
        })}
        <Group gap="md">
          <Anchor component={Link} to="/boards" size="xs" c="dimmed">
            Open the board →
          </Anchor>
          <Anchor component={Link} to="/orchestrator?tab=delegations" size="xs" c="dimmed">
            Open delegations →
          </Anchor>
        </Group>
      </Stack>
      <HomeTaskModal item={openItem} onClose={() => setOpenItem(null)} />
    </Box>
  )
}

function SectionHeader() {
  return (
    <Group justify="space-between" pb={6} style={{ flexShrink: 0 }}>
      <Text size="xs" tt="uppercase" fw={700} c="dimmed">
        Tasks
      </Text>
    </Group>
  )
}

function MoreLink({
  hidden,
  visible,
  group,
}: {
  hidden: number
  visible: HomeTaskItemDto[]
  group: HomeTaskGroup
}) {
  if (hidden <= 0) return null
  if (group === 'Done') {
    return (
      <Anchor component={Link} to="/orchestrator?tab=history" size="xs" c="dimmed">
        +{hidden} more → open history
      </Anchor>
    )
  }
  const card = visible.find((row) => row.source === 'Card' && row.boardId)
  const to = card?.boardId ? `/boards/${card.boardId}` : '/orchestrator?tab=delegations'
  const label = card
    ? `+${hidden} more → open board`
    : `+${hidden} more → open delegations`
  return (
    <Anchor component={Link} to={to} size="xs" c="dimmed">
      {label}
    </Anchor>
  )
}
