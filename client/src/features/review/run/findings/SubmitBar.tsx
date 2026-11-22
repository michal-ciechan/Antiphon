import { Badge, Button, Group, Paper, Text, Tooltip } from '@mantine/core'
import { TbSend } from 'react-icons/tb'
import type { ReviewFindingDto } from '../../../../api/reviewEngine'
import { ACTION_META, REASON_META } from '../../reviewMeta'
import { draftProblem, type Draft, type Drafts } from './drafts'

/** Sticky bar: every drafted reply, and one button that posts them all as one review. */
export function SubmitBar({
  drafts,
  findings,
  actorName,
  hostLabel,
  submitting,
  onSubmit,
  onDiscard,
}: {
  drafts: Drafts
  findings: ReviewFindingDto[]
  actorName: string
  hostLabel: string
  submitting: boolean
  onSubmit: () => void
  onDiscard: () => void
}) {
  const list = Object.entries(drafts).filter((e): e is [string, Draft] => !!e[1])
  const keyOf = (id: string) => findings.find((f) => f.id === id)?.key ?? '?'
  const invalid = list.filter(([, d]) => draftProblem(d) !== null)
  const n = list.length
  return (
    <Paper withBorder p="xs" radius="md" style={{ position: 'sticky', bottom: 0, zIndex: 5, boxShadow: '0 -6px 16px rgba(0,0,0,0.5)' }}>
      <Group justify="space-between" wrap="wrap" gap="xs">
        <Group gap={6} wrap="wrap" style={{ minWidth: 0 }}>
          {list.map(([id, d]) => {
            const problem = draftProblem(d)
            return (
              <Tooltip key={id} label={problem ? `${keyOf(id)}: ${problem}` : `${keyOf(id)} ready`} disabled={!problem}>
                <Badge size="sm" variant={problem ? 'outline' : 'light'} color={problem ? 'gray' : ACTION_META[d.action].color}>
                  {keyOf(id)} · {ACTION_META[d.action].label}
                  {d.reasonCategory ? ` (${REASON_META[d.reasonCategory].label.toLowerCase()})` : ''}
                </Badge>
              </Tooltip>
            )
          })}
          {n === 0 && (
            <Text size="xs" c="dimmed">
              No replies drafted. Pick an answer on a finding; they post together as one review.
            </Text>
          )}
          {invalid.length > 0 && (
            <Text size="xs" c="warning.4">
              {invalid.map(([id]) => keyOf(id)).join(', ')} incomplete
            </Text>
          )}
        </Group>
        <Group gap={8} wrap="nowrap">
          {n > 0 && (
            <Button size="xs" variant="subtle" color="gray" onClick={onDiscard} disabled={submitting}>
              Discard
            </Button>
          )}
          <Tooltip
            multiline
            w={320}
            label={`One ${hostLabel} review (one notification). Each reply is saved in Antiphon first, then posted to its thread; a failed post stays pending and is retried. Signed with your name, not an @mention, so nobody is pinged.`}
          >
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              signed "— {actorName} via Antiphon"
            </Text>
          </Tooltip>
          <Button
            size="xs"
            leftSection={<TbSend size={14} />}
            disabled={n === 0 || invalid.length > 0}
            loading={submitting}
            onClick={onSubmit}
            data-testid="review-submit-replies"
          >
            Post {n} {n === 1 ? 'reply' : 'replies'} to {hostLabel} as {actorName} via Antiphon
          </Button>
        </Group>
      </Group>
    </Paper>
  )
}
