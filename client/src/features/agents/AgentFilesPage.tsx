import { Badge, Box, Group, Paper, SegmentedControl, Text, Title, Tooltip } from '@mantine/core'
import { useLocalStorage } from '@mantine/hooks'
import { Link, useParams } from 'react-router'
import { TbArrowLeft, TbLayoutBottombar, TbLayoutSidebarRight } from 'react-icons/tb'
import { useAgent } from '../../api/agents'
import { FilesReviewPanel, type FilesPanelHeights } from './FilesReviewPanel'
import { SessionTranscriptPanel } from './SessionTranscriptPanel'

type DockSide = 'bottom' | 'right'

const DOCK_SIZE: Record<DockSide, number> = { bottom: 380, right: 520 }

// Viewport-derived editor heights: everything above/below the viewer (page header, the viewer's
// filename/mode row, comment row, paddings) plus the dock when it's at the bottom. The tree needs
// no height — the sidebar layout flex-fills it. max() keeps small windows usable — the content
// column scrolls as the fallback.
const HEIGHTS: Record<DockSide, FilesPanelHeights> = {
  bottom: { viewer: 'max(260px, calc(100vh - 545px))' },
  right: { viewer: 'max(260px, calc(100vh - 165px))' },
}

const statusColor: Record<string, string> = {
  Working: 'yellow',
  Ready: 'green',
  Idle: 'green',
  WaitingForHumanReview: 'orange',
  Stopped: 'gray',
  Disconnected: 'red',
  Failed: 'red',
}

/**
 * Full-screen files review for one agent (own browser tab, no app chrome): the files tree/viewer
 * takes the screen, with the agent conversation docked at the bottom or snapped to the right —
 * the same transcript component as the agent modal, plus a composer to reply from here.
 */
export function AgentFilesPage() {
  const { id } = useParams<{ id: string }>()
  const agent = useAgent(id ?? null)
  // Key bumped to -v2 when the default changed to 'right' — the old key had 'bottom' persisted
  // for existing users, which would silently pin the old default forever.
  const [dock, setDock] = useLocalStorage<DockSide>({
    key: 'antiphon-files-conversation-dock-v2',
    defaultValue: 'right',
  })

  if (!id) return null
  const sessionId = agent.data?.liveSession?.id ?? agent.data?.persistentSessionId ?? null

  return (
    <Box style={{ height: '100vh', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Group
        px="md"
        py={8}
        justify="space-between"
        style={{ borderBottom: '1px solid var(--mantine-color-default-border)', flexShrink: 0 }}
      >
        <Group gap="sm">
          <Tooltip label="Back to agents">
            <Link to="/agents" aria-label="Back to agents" style={{ display: 'flex', color: 'inherit' }}>
              <TbArrowLeft size={18} />
            </Link>
          </Tooltip>
          <Title order={4}>{agent.data?.name ?? 'Agent'} — files</Title>
          {agent.data && (
            <Badge size="sm" variant="light" color={statusColor[agent.data.status] ?? 'gray'}>
              {agent.data.status}
            </Badge>
          )}
          <Text size="xs" c="dimmed" truncate style={{ maxWidth: 420 }}>
            {agent.data?.workingDirectory}
          </Text>
        </Group>
        <SegmentedControl
          size="xs"
          value={dock}
          onChange={(v) => setDock(v as DockSide)}
          data={[
            {
              value: 'bottom',
              label: (
                <Tooltip label="Conversation at the bottom" withArrow>
                  <TbLayoutBottombar size={15} aria-label="Dock conversation at bottom" />
                </Tooltip>
              ),
            },
            {
              value: 'right',
              label: (
                <Tooltip label="Conversation on the right" withArrow>
                  <TbLayoutSidebarRight size={15} aria-label="Dock conversation on the right" />
                </Tooltip>
              ),
            },
          ]}
        />
      </Group>

      <Box
        style={{
          flexGrow: 1,
          minHeight: 0,
          display: 'flex',
          flexDirection: dock === 'bottom' ? 'column' : 'row',
        }}
      >
        <Box px="md" py="sm" style={{ flexGrow: 1, minWidth: 0, minHeight: 0, overflow: 'auto' }}>
          <FilesReviewPanel agentId={id} heights={HEIGHTS[dock]} layout="sidebar" />
        </Box>

        <Paper
          p="sm"
          radius={0}
          style={{
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            minHeight: 0,
            ...(dock === 'bottom'
              ? { height: DOCK_SIZE.bottom, borderTop: '1px solid var(--mantine-color-default-border)' }
              : { width: DOCK_SIZE.right, borderLeft: '1px solid var(--mantine-color-default-border)' }),
          }}
          data-testid="conversation-dock"
        >
          {sessionId ? (
            <SessionTranscriptPanel sessionId={sessionId} withComposer composerCollapsed fitHeight />
          ) : (
            <Text size="sm" c="dimmed" ta="center" py="xl">
              No session for this agent yet — start it from the Agents page to talk to it here.
            </Text>
          )}
        </Paper>
      </Box>
    </Box>
  )
}
