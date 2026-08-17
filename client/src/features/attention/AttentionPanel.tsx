import {
  ActionIcon,
  Alert,
  Badge,
  Box,
  Center,
  Collapse,
  Group,
  Loader,
  Paper,
  Stack,
  Text,
  Title,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import { useMemo, useState } from 'react'
import { TbAlertCircle, TbChecks, TbChevronDown, TbChevronRight, TbRefresh } from 'react-icons/tb'
import { useNavigate } from 'react-router'
import { useAttention, type AttentionItemDto } from '../../api/attention'
import { formatCost, formatDuration } from '../delegations/taskVisuals'
import {
  ATTENTION_GROUPS,
  ATTENTION_VISUALS,
  ageSeconds,
  groupOf,
  keyOf,
  targetOf,
  type AttentionGroupKey,
} from './attentionVisuals'

/**
 * "Across everything, what is stuck — and why." The diagnostic tab (CARD-0035 §D2).
 *
 * <p><b>Empty is the design target, not a failure.</b> On a healthy day this panel says "Nothing is
 * stuck." and that has to read as reassurance: a blank box or an error-shaped placeholder would
 * teach the operator that the view is broken, and a view nobody believes is worse than no view. The
 * rows it does show are all server-computed — nothing here decides that something is stuck, it only
 * decides how a decision already made reads.</p>
 *
 * <p>Every row's click goes to the thing that explains it — the task drawer on the sibling tab, or
 * the agent's incident drawer. That is the whole reason this lives on the Orchestrator page.</p>
 */
export function AttentionPanel() {
  const attention = useAttention()
  const navigate = useNavigate()
  const [collapsed, setCollapsed] = useState<Set<AttentionGroupKey>>(
    () => new Set(ATTENTION_GROUPS.filter((g) => g.collapsed).map((g) => g.key)),
  )

  const items = useMemo(() => attention.data?.items ?? [], [attention.data])

  const grouped = useMemo(() => {
    const byGroup = new Map<AttentionGroupKey, AttentionItemDto[]>()
    for (const group of ATTENTION_GROUPS) byGroup.set(group.key, [])
    // Order is the SERVER's — severity desc, then oldest-stuck first — and the groups are read in
    // the same order, so re-sorting here could only ever disagree with the rank it already assigned.
    for (const item of items) byGroup.get(groupOf(item))?.push(item)
    return byGroup
  }, [items])

  // Failures are context; they must not make a healthy fleet look busy, and the badge on the tab
  // counts the same set for the same reason.
  const openCount = items.filter((item) => item.kind !== 'RecentFailure').length

  if (attention.isLoading) {
    return (
      <Group justify="center" py="xl">
        <Loader size="md" />
      </Group>
    )
  }

  if (attention.error) {
    return (
      <Alert color="danger" icon={<TbAlertCircle />} title="Could not load the attention list">
        {attention.error instanceof Error ? attention.error.message : 'No response from the server.'}
      </Alert>
    )
  }

  const toggle = (key: AttentionGroupKey) =>
    setCollapsed((previous) => {
      const next = new Set(previous)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Group gap="xs">
          <Title order={4}>Needs attention</Title>
          <Badge variant="light" color={openCount === 0 ? 'success' : 'danger'}>
            {openCount} open
          </Badge>
          {/* False is not "nothing disagrees" — it is "nobody asked". Collapsing the two would let a
              runner that is down read as a clean bill of health, which is the one lie this view
              cannot afford to tell. */}
          {attention.data && !attention.data.runnerConsulted && (
            <Tooltip
              multiline
              w={280}
              label="The session runner did not answer, so leaked and unclaimed sessions are not in this list. It is not a claim that there are none."
            >
              <Badge variant="light" color="warning">
                Runner not consulted
              </Badge>
            </Tooltip>
          )}
        </Group>
        <Tooltip label="Refresh">
          <ActionIcon
            variant="subtle"
            aria-label="Refresh"
            onClick={() => attention.refetch()}
            loading={attention.isFetching}
          >
            <TbRefresh />
          </ActionIcon>
        </Tooltip>
      </Group>

      {openCount === 0 && <NothingIsStuck />}

      {ATTENTION_GROUPS.map((group) => {
        const rows = grouped.get(group.key) ?? []
        if (rows.length === 0) return null
        const isCollapsed = collapsed.has(group.key)

        return (
          <Stack key={group.key} gap={6}>
            <UnstyledButton onClick={() => toggle(group.key)} aria-expanded={!isCollapsed}>
              <Group gap="xs" wrap="nowrap">
                {isCollapsed ? <TbChevronRight size={14} /> : <TbChevronDown size={14} />}
                <Text size="xs" tt="uppercase" fw={700}>
                  {group.title}
                </Text>
                <Badge size="xs" variant="default">
                  {rows.length}
                </Badge>
                <Text size="xs" c="dimmed">
                  {group.hint}
                </Text>
              </Group>
            </UnstyledButton>
            <Collapse in={!isCollapsed}>
              <Stack gap={6}>
                {rows.map((item) => (
                  <AttentionRow
                    key={keyOf(item)}
                    item={item}
                    onOpen={(target) => navigate(target)}
                  />
                ))}
              </Stack>
            </Collapse>
          </Stack>
        )
      })}
    </Stack>
  )
}

/**
 * The common case. It says what is true rather than showing nothing — an empty panel is
 * indistinguishable from a broken one, and this view only works if a quiet day is legible AS a quiet
 * day. The second line names the exclusion, because "nothing is stuck" while an agent is visibly
 * grinding away would otherwise look like the view had missed it.
 */
function NothingIsStuck() {
  return (
    <Paper withBorder p="xl" data-testid="attention-empty">
      <Center>
        <Stack gap={4} align="center">
          <TbChecks size={28} color="var(--mantine-color-success-6)" />
          <Text fw={600}>Nothing is stuck.</Text>
          <Text size="sm" c="dimmed" ta="center" maw={420}>
            Work that is merely slow is deliberately not listed — a session that is mid-turn is
            working, not stuck.
          </Text>
        </Stack>
      </Center>
    </Paper>
  )
}

function AttentionRow({
  item,
  onOpen,
}: {
  item: AttentionItemDto
  onOpen: (target: string) => void
}) {
  const visual = ATTENTION_VISUALS[item.kind]
  const Icon = visual.icon
  const target = targetOf(item)
  const seconds = ageSeconds(item)
  const age = seconds === null ? null : formatDuration(seconds)

  const body = (
    <Stack gap={4}>
      <Group gap="xs" wrap="nowrap" justify="space-between">
        <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
          <Tooltip label={visual.hint} multiline w={280}>
            <Badge
              size="sm"
              variant="light"
              color={visual.color}
              leftSection={<Icon size={12} />}
              style={{ flexShrink: 0 }}
            >
              {visual.label}
            </Badge>
          </Tooltip>
          <Text size="sm" fw={600} truncate>
            {item.title}
          </Text>
        </Group>
        <Group gap={6} wrap="nowrap" style={{ flexShrink: 0 }}>
          {item.subtreeCostUsd != null && item.subtreeCostUsd > 0 && (
            <Tooltip label="Spend on this task and everything under it">
              <Badge size="xs" variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {formatCost(item.subtreeCostUsd)}
              </Badge>
            </Tooltip>
          )}
          {age && (
            <Tooltip label="How long this has been true">
              <Badge size="xs" variant="default" style={{ fontVariantNumeric: 'tabular-nums' }}>
                {age}
              </Badge>
            </Tooltip>
          )}
        </Group>
      </Group>

      <Text size="sm">{item.headline}</Text>

      {item.evidence && (
        // Pre-wrapped, not reflowed: the evidence is often the tail of a check digest, and its line
        // breaks are the structure a human reads it by.
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'pre-wrap' }} lineClamp={4}>
          {item.evidence}
        </Text>
      )}
    </Stack>
  )

  if (!target) {
    return (
      <Paper withBorder p="xs" data-testid={`attention-row-${item.kind}`}>
        {body}
      </Paper>
    )
  }

  return (
    <Paper withBorder p={0} data-testid={`attention-row-${item.kind}`}>
      <UnstyledButton
        onClick={() => onOpen(target)}
        w="100%"
        p="xs"
        aria-label={`Open ${item.title}`}
      >
        <Box>{body}</Box>
      </UnstyledButton>
    </Paper>
  )
}
