import { Alert, Badge, Button, Center, Group, Loader, Paper, Stack, Text, Title } from '@mantine/core'
import { TbAlertCircle, TbChecks, TbHelpCircle } from 'react-icons/tb'
import { useMemo } from 'react'
import { useNavigate } from 'react-router'
import { useAllBoardDetails, useBoards } from '../../api/boards'
import { useAttention, type AttentionItemDto } from '../../api/attention'
import { formatDuration } from '../delegations/taskVisuals'
import { ageSeconds, targetOf } from './attentionVisuals'
import { MoveMenu } from '../board/MoveMenu'

/**
 * The attention feed's dedicated decision altitude: it does not derive another signal, it gives
 * the existing CardNeedsDecision rows their board context, whole question, and closing verb.
 */
export function DecisionsPanel() {
  const attention = useAttention()
  const boards = useBoards()
  const boardIds = useMemo(() => (boards.data ?? []).map((board) => board.id), [boards.data])
  const details = useAllBoardDetails(boardIds, boards.isSuccess)
  const navigate = useNavigate()
  const items = useMemo(
    () => (attention.data?.items ?? []).filter((item) => item.kind === 'CardNeedsDecision'),
    [attention.data],
  )
  const boardById = useMemo(() => new Map((boards.data ?? []).map((board) => [board.id, board])), [boards.data])
  const detailById = useMemo(
    () => new Map((details.data ?? []).map((board) => [board.id, board])),
    [details.data],
  )

  if (attention.isLoading || boards.isLoading) {
    return <Center py="xl"><Loader size="md" /></Center>
  }
  if (attention.error || boards.error) {
    const error = attention.error ?? boards.error
    return (
      <Alert color="danger" icon={<TbAlertCircle />} title="Could not load decisions">
        {error instanceof Error ? error.message : 'No response from the server.'}
      </Alert>
    )
  }
  if (items.length === 0) {
    return (
      <Paper withBorder p="xl" data-testid="decisions-empty">
        <Center><Stack gap={4} align="center"><TbChecks size={28} color="var(--mantine-color-success-6)" /><Text fw={600}>No decisions waiting.</Text></Stack></Center>
      </Paper>
    )
  }

  const groups = new Map<string, AttentionItemDto[]>()
  for (const item of items) {
    const board = item.boardId ? boardById.get(item.boardId) : undefined
    const name = board ? `${board.projectName} / ${board.name}` : 'Unknown board'
    groups.set(name, [...(groups.get(name) ?? []), item])
  }

  return (
    <Stack gap="md">
      <Group gap="xs"><Title order={4}>Decisions</Title><Badge variant="light" color="danger">{items.length} waiting</Badge></Group>
      {[...groups].map(([boardName, rows]) => (
        <Stack key={boardName} gap={6}>
          <Text size="xs" tt="uppercase" fw={700} c="dimmed">{boardName}</Text>
          {rows.map((item) => {
            const detail = item.boardId ? detailById.get(item.boardId) : undefined
            const card = detail?.columns.flatMap((column) => column.cards).find((candidate) => candidate.id === item.cardId)
            const age = ageSeconds(item)
            const target = targetOf(item)
            return (
              <Paper key={`${item.cardId}-${item.sinceUtc}`} withBorder p="sm" data-testid="decision-row">
                <Stack gap={8}>
                  <Group justify="space-between" wrap="nowrap">
                    <Group gap="xs" wrap="nowrap" style={{ minWidth: 0 }}>
                      <Badge size="sm" variant="light" color="danger" leftSection={<TbHelpCircle size={12} />}>Needs decision</Badge>
                      <Text size="sm" fw={600}>{item.title}</Text>
                    </Group>
                    {age !== null && <Badge size="xs" variant="default">{formatDuration(age)}</Badge>}
                  </Group>
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{item.evidence}</Text>
                  <Group gap="xs">
                    {card && detail && <MoveMenu boardId={detail.id} card={card} columns={detail.columns} variant="decide" />}
                    <Button size="xs" variant="subtle" onClick={() => target && navigate(target)}>Open card</Button>
                  </Group>
                </Stack>
              </Paper>
            )
          })}
        </Stack>
      ))}
    </Stack>
  )
}
