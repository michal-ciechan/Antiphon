import { Badge, Box, Group, Paper, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { SectionLabel } from '../../components/SectionLabel'
import { SyncBadge } from '../../components/ReviewBadges'
import { ACTION_META, FINDING_STATUS_META, SEVERITY_META } from '../../reviewMeta'
import type { Drafts } from './drafts'
import type { FindingGroup } from './findingGroups'

export function FindingList({
  groups,
  selectedId,
  drafts,
  onSelect,
}: {
  groups: FindingGroup[]
  selectedId: string | null
  drafts: Drafts
  onSelect: (id: string) => void
}) {
  return (
    <Paper withBorder radius="md" p={6}>
      {groups.map((g) => (
        <Box key={g.title} mb={6}>
          <SectionLabel right={<Text size="xs" c="dimmed">{g.items.length}</Text>}>{g.title}</SectionLabel>
          {g.items.map((f) => {
            const d = drafts[f.id]
            return (
              <UnstyledButton
                key={f.id}
                w="100%"
                onClick={() => onSelect(f.id)}
                p={6}
                aria-current={selectedId === f.id}
                style={{
                  borderRadius: 4,
                  background: selectedId === f.id ? 'var(--mantine-color-dark-5)' : undefined,
                  borderLeft: `3px solid var(--mantine-color-${SEVERITY_META[f.severity].color}-6)`,
                  marginBottom: 2,
                  opacity: g.title === 'Closed' ? 0.65 : 1,
                }}
              >
                <Group gap={6} wrap="nowrap" align="flex-start">
                  <Text size="xs" ff="monospace" c="dimmed" w={24}>
                    {f.key}
                  </Text>
                  <Box style={{ flex: 1, minWidth: 0 }}>
                    <Text size="xs" fw={500} lineClamp={2}>
                      {f.title}
                    </Text>
                    <Group gap={6} mt={2} wrap="nowrap">
                      <Text size="10px" c={`${SEVERITY_META[f.severity].color}.4`} fw={700} tt="uppercase">
                        {SEVERITY_META[f.severity].label}
                      </Text>
                      {f.flags.length > 0 && (
                        <Tooltip label={f.bypassesRoundBar ? 'Bypass category: posts regardless of the round bar' : 'flag'}>
                          <Text size="10px" c="red.4">
                            {f.flags.join(' ')}
                          </Text>
                        </Tooltip>
                      )}
                      <Text size="10px" c="dimmed" ff="monospace" truncate>
                        {f.filePath.split('/').pop()}
                        {f.lineStart !== null ? `:${f.lineStart}` : ''}
                      </Text>
                    </Group>
                  </Box>
                  <Box style={{ flexShrink: 0 }}>
                    {d ? (
                      <Badge size="xs" variant="light" color={ACTION_META[d.action].color}>
                        {ACTION_META[d.action].label}
                      </Badge>
                    ) : f.syncState === 'Pending' || f.syncState === 'Failed' ? (
                      <SyncBadge state={f.syncState} />
                    ) : f.status !== 'Open' ? (
                      <Badge size="xs" variant="dot" color={FINDING_STATUS_META[f.status].color}>
                        {FINDING_STATUS_META[f.status].label}
                      </Badge>
                    ) : null}
                  </Box>
                </Group>
              </UnstyledButton>
            )
          })}
        </Box>
      ))}
      {groups.length === 0 && (
        <Text size="sm" c="dimmed" ta="center" py="md">
          No findings on this run.
        </Text>
      )}
    </Paper>
  )
}
