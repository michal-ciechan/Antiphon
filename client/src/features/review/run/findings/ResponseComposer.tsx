import { Badge, Box, Button, Chip, Group, Paper, Radio, Stack, Text, Textarea, Tooltip } from '@mantine/core'
import { useState } from 'react'
import type { DismissScope, ReasonCategory, ReviewFindingDto, ReviewSubjectDto } from '../../../../api/reviewEngine'
import { ACTION_META, REASON_META, SCOPE_META } from '../../reviewMeta'
import { BLOCKER_CLEAR_REASONS, actionsFor, draftProblem, isWideScope, replyPreview, type Draft } from './drafts'
import { WideScopeModal } from './WideScopeModal'

const PLACEHOLDER: Partial<Record<Draft['action'], string>> = {
  Dispute: 'Why is this wrong? (required)',
  Question: 'Ask the bot (answered from the pack context)',
  NotABlocker: 'Why is this not a blocker? (required)',
}

/** Choose an answer for one finding: the action, reason and scope where needed, and free text. */
export function ResponseComposer({
  finding,
  subject,
  draft,
  onChange,
  actorName,
  isAuthor,
}: {
  finding: ReviewFindingDto
  subject: ReviewSubjectDto
  draft: Draft | undefined
  onChange: (draft: Draft | undefined) => void
  actorName: string
  isAuthor: boolean
}) {
  const [confirmOpen, setConfirmOpen] = useState(false)
  const d = draft
  const syncing = finding.syncState === 'Pending'
  const locked = finding.status === 'Outdated' || syncing
  const set = (patch: Partial<Draft>) => onChange({ action: d?.action ?? 'WillFix', text: d?.text ?? '', ...d, ...patch })
  const wide = d?.action === 'Dismiss' && isWideScope(d.scope)
  const problem = d ? draftProblem(d) : null
  return (
    <Stack gap={6}>
      {locked && (
        <Text size="xs" c="dimmed">
          {syncing ? 'Waiting for the previous reply to reach the code host.' : 'Outdated: the code this finding anchored to has changed.'}
        </Text>
      )}
      <Group gap={4}>
        {actionsFor(finding).map((a) => (
          <Button
            key={a}
            data-testid={`review-action-${a}`}
            size="compact-xs"
            disabled={locked}
            variant={d?.action === a ? 'filled' : 'default'}
            color={ACTION_META[a].color}
            onClick={() => (d?.action === a ? onChange(undefined) : set({ action: a, reasonCategory: undefined, scope: undefined, wideConfirmed: false }))}
          >
            {ACTION_META[a].label}
          </Button>
        ))}
      </Group>

      {d?.action === 'NotABlocker' && (
        <Paper p={6} radius="sm" bg="dark.6" style={{ borderLeft: '3px solid var(--mantine-color-orange-6)' }}>
          <Text size="xs" c="dimmed" mb={4}>
            Reason (required)
          </Text>
          <Chip.Group value={d.reasonCategory ?? ''} onChange={(v) => set({ reasonCategory: v as ReasonCategory })}>
            <Group gap={4}>
              {BLOCKER_CLEAR_REASONS.map((r) => (
                <Chip key={r} value={r} size="xs" variant="outline">
                  {REASON_META[r].label}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
          <Text size="xs" c="dimmed" mt={6}>
            The bot never approves while a blocker is open. A clear may need a second trusted reviewer (trust thresholds arrive with the trust API); the reason
            posts to the thread, and why-missed investigations search cleared blockers.
          </Text>
        </Paper>
      )}

      {d?.action === 'Dismiss' && (
        <Paper p={6} radius="sm" bg="dark.6">
          <Text size="xs" c="dimmed" mb={4}>
            Reason
          </Text>
          <Chip.Group value={d.reasonCategory ?? ''} onChange={(v) => set({ reasonCategory: v as ReasonCategory, wideConfirmed: false })}>
            <Group gap={4}>
              {(Object.keys(REASON_META) as ReasonCategory[]).map((r) => (
                <Chip key={r} value={r} size="xs" variant="outline">
                  {REASON_META[r].label}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
          <Text size="xs" c="dimmed" mt={6} mb={4}>
            Scope
          </Text>
          <Radio.Group
            value={d.scope ?? ''}
            onChange={(v) => {
              const scope = v as DismissScope
              set({ scope, wideConfirmed: false })
              if (isWideScope(scope) && d.reasonCategory) setConfirmOpen(true)
            }}
          >
            <Group gap="md">
              {(Object.keys(SCOPE_META) as DismissScope[]).map((s) => (
                <Tooltip key={s} label={SCOPE_META[s].hint}>
                  <Radio value={s} size="xs" label={SCOPE_META[s].label} />
                </Tooltip>
              ))}
            </Group>
          </Radio.Group>
          {wide && (
            <Group gap={6} mt={6} wrap="wrap">
              {d.wideConfirmed ? (
                <Badge size="xs" color="cyan" variant="light" tt="none">
                  rule confirmed
                </Badge>
              ) : (
                <Text size="xs" c="warning.4">
                  {d.reasonCategory ? 'Wider than this finding: confirm the rule it creates before posting.' : 'Pick a reason: the confirmation previews the rule it creates.'}
                </Text>
              )}
              <Button size="compact-xs" variant="light" color="cyan" disabled={!d.reasonCategory} onClick={() => setConfirmOpen(true)}>
                {d.wideConfirmed ? 'Review' : 'Review and confirm'}
              </Button>
            </Group>
          )}
          {wide && d.reasonCategory && d.scope && (
            <WideScopeModal
              key={`${finding.id}:${d.scope}:${d.reasonCategory}`}
              opened={confirmOpen}
              finding={finding}
              subject={subject}
              reason={d.reasonCategory}
              scope={d.scope}
              text={d.text}
              actorName={actorName}
              initialSteer={d.steer}
              onClose={() => setConfirmOpen(false)}
              onConfirm={(steer) => {
                set({ wideConfirmed: true, steer })
                setConfirmOpen(false)
              }}
            />
          )}
        </Paper>
      )}

      {d?.action === 'Dispute' && finding.bypassesRoundBar && (
        <Text size="xs" c="red.4">
          {finding.flags.join(', ')} is a bypass category: the adjudicator can hold but can't concede alone, so a dispute escalates to the owning team.
        </Text>
      )}

      {d && (
        <Textarea
          size="xs"
          autosize
          minRows={2}
          aria-label="Reply text"
          placeholder={PLACEHOLDER[d.action] ?? 'Optional note'}
          value={d.text}
          onChange={(e) => set({ text: e.currentTarget.value })}
        />
      )}
      {d && (
        <Box>
          <Group justify="space-between" gap={4}>
            <Text size="xs" c="dimmed">
              {finding.posted ? 'Posts to the thread as' : 'Saved in Antiphon (not posted inline) as'}
            </Text>
            {problem && (
              <Text size="xs" c="warning.4">
                {problem}
              </Text>
            )}
          </Group>
          <Paper p={6} radius="sm" withBorder>
            <Text size="xs" style={{ whiteSpace: 'pre-wrap' }} c={problem ? 'dimmed' : undefined}>
              {replyPreview(d, actorName)}
            </Text>
          </Paper>
          {isAuthor && d.action === 'NotABlocker' && (
            <Text size="xs" c="dimmed" mt={4}>
              You're the author: policy decides whether an author may clear a blocker (dismiss.blocker, default author).
            </Text>
          )}
        </Box>
      )}
    </Stack>
  )
}
