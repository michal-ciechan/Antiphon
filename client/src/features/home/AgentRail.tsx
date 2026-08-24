import { Anchor, Badge, Box, Group, Loader, Stack, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { TbTerminal2 } from 'react-icons/tb'
import { Link } from 'react-router'
import type { AgentSummaryDto } from '../../api/agents'
import { SessionContextBadge } from '../agents/SessionContextBadge'

/**
 * The project's agents, one compact row each — pool delegates included, they are ordinary agent
 * rows. Selecting a row drives both the files pane and the chat dock. Liveness lives in the
 * terminal icon's colour (green live / yellow starting-stopping / gray none), matching AgentsPage;
 * the badge only appears when it says something real.
 */
export function AgentRail({
  agents,
  selectedId,
  onSelect,
}: {
  agents: AgentSummaryDto[]
  selectedId: string | null
  onSelect: (id: string) => void
}) {
  return (
    <Stack gap={4} style={{ minHeight: 0, flexGrow: 1, overflowY: 'auto' }}>
      {agents.length === 0 && (
        <Text size="sm" c="dimmed" p="sm">
          No agents in this directory yet. Delegating work spawns one, or{' '}
          <Anchor component={Link} to="/agents" size="sm">
            create an agent
          </Anchor>
          .
        </Text>
      )}
      {agents.map((agent) => (
        <UnstyledButton
          key={agent.id}
          onClick={() => onSelect(agent.id)}
          aria-label={`Select agent ${agent.name}`}
          aria-pressed={selectedId === agent.id}
          px="xs"
          py={6}
          style={{
            borderRadius: 6,
            background:
              selectedId === agent.id ? 'var(--mantine-color-default-hover)' : undefined,
            outline:
              selectedId === agent.id ? '1px solid var(--mantine-color-active-5)' : undefined,
          }}
        >
          <Group gap={8} wrap="nowrap">
            <Tooltip
              label={
                agent.liveSession?.status === 'Running'
                  ? 'Terminal — live now'
                  : agent.liveSession
                    ? `Terminal ${agent.liveSession.status.toLowerCase()}…`
                    : 'No live session'
              }
              openDelay={400}
              withArrow
            >
              <Box
                component="span"
                style={{ display: 'inline-flex', flexShrink: 0 }}
                c={
                  agent.liveSession?.status === 'Running'
                    ? 'green'
                    : agent.liveSession
                      ? 'yellow'
                      : 'dimmed'
                }
              >
                <TbTerminal2 size={15} />
              </Box>
            </Tooltip>
            <Text size="sm" fw={500} truncate style={{ flexGrow: 1, minWidth: 0 }}>
              {agent.name}
            </Text>
            {agent.liveSession?.agentKind === 'ClaudeCode' && (
              <SessionContextBadge
                fullness={agent.liveSession.contextFullness}
                state={agent.liveSession.contextFullnessState}
                size="xs"
              />
            )}
            <ActivityBadge agent={agent} />
            {agent.queueLength > 0 && (
              <Tooltip label={`${agent.queueLength} queued card${agent.queueLength === 1 ? '' : 's'}`}>
                <Badge size="xs" variant="default" style={{ flexShrink: 0 }}>
                  {agent.queueLength}
                </Badge>
              </Tooltip>
            )}
          </Group>
        </UnstyledButton>
      ))}
    </Stack>
  )
}

function ActivityBadge({ agent }: { agent: AgentSummaryDto }) {
  if (agent.working) {
    return (
      <Badge
        size="xs"
        color="yellow"
        variant="light"
        leftSection={<Loader size={8} color="yellow" type="dots" />}
        style={{ flexShrink: 0 }}
        data-testid={`rail-working-${agent.id}`}
      >
        Working
      </Badge>
    )
  }
  if (agent.status === 'WaitingForHumanReview') {
    return (
      <Badge size="xs" color="orange" variant="light" style={{ flexShrink: 0 }}>
        Review
      </Badge>
    )
  }
  if (agent.status === 'Failed' || agent.status === 'Disconnected') {
    return (
      <Badge size="xs" color="red" variant="light" style={{ flexShrink: 0 }}>
        {agent.status}
      </Badge>
    )
  }
  return null
}
