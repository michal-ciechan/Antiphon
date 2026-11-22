import { Alert, Anchor, Box, Button, Code, Collapse, Divider, Group, Paper, Stack, Text, Tooltip, UnstyledButton } from '@mantine/core'
import { useState } from 'react'
import { TbChevronDown, TbChevronRight, TbCloudOff, TbExternalLink, TbFileCode, TbRefresh } from 'react-icons/tb'
import type { FindingEventDto, ReviewFindingDto, ReviewSubjectDto } from '../../../../api/reviewEngine'
import { RenderedMarkdown } from '../../../../shared/RenderedMarkdown'
import { DiffHunk } from '../../components/DiffHunk'
import { PersonChip } from '../../components/PersonChip'
import { FindingStatusBadge, FlagBadges, SeverityBadge, SyncBadge } from '../../components/ReviewBadges'
import { ACTION_META, REASON_META, SCOPE_META } from '../../reviewMeta'
import { fmtAgo } from '../../reviewFormat'
import type { Draft } from './drafts'
import { ResponseComposer } from './ResponseComposer'

function lines(f: ReviewFindingDto): string {
  if (f.lineStart === null) return ''
  return f.lineEnd !== null && f.lineEnd !== f.lineStart ? `:${f.lineStart}-${f.lineEnd}` : `:${f.lineStart}`
}

function EventRow({ e, now }: { e: FindingEventDto; now: number }) {
  const m = ACTION_META[e.action]
  const detail = [
    e.reasonCategory ? REASON_META[e.reasonCategory].label.toLowerCase() : null,
    e.scope && e.scope !== 'Instance' ? `scope: ${SCOPE_META[e.scope].label.toLowerCase()}` : null,
  ].filter(Boolean)
  return (
    <Box py={4} style={{ borderBottom: '1px solid var(--mantine-color-dark-5)' }}>
      <Group gap={6} wrap="nowrap" justify="space-between">
        <Group gap={6} wrap="nowrap" style={{ minWidth: 0 }}>
          <PersonChip login={e.actorLogin} displayName={e.actorDisplayName} size="xs" />
          {e.action !== 'Reply' && (
            <Text size="xs" c={`${m.color}.4`} fw={600} style={{ whiteSpace: 'nowrap' }}>
              {m.label}
              {detail.length > 0 && ` (${detail.join(', ')})`}
            </Text>
          )}
        </Group>
        <Group gap={6} wrap="nowrap">
          <SyncBadge state={e.syncState} />
          <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            {fmtAgo(e.createdAt, now)}
          </Text>
        </Group>
      </Group>
      {e.text && (
        <Box fz="xs" pl={24} style={{ overflowWrap: 'anywhere' }}>
          <RenderedMarkdown>{e.text}</RenderedMarkdown>
        </Box>
      )}
    </Box>
  )
}

function WhyCheck({ f }: { f: ReviewFindingDto }) {
  const [open, setOpen] = useState(true)
  if (!f.checkWhy) return null
  return (
    <Paper p={6} radius="sm" bg="dark.6">
      <UnstyledButton onClick={() => setOpen((o) => !o)} w="100%">
        <Group gap={4}>
          {open ? <TbChevronDown size={12} /> : <TbChevronRight size={12} />}
          <Text size="xs" fw={600}>
            Why this check exists
          </Text>
          <Text size="xs" c="dimmed" ff="monospace">
            {f.checkId}
          </Text>
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Text size="xs" mt={4} c="gray.4">
          {f.checkWhy}
        </Text>
      </Collapse>
    </Paper>
  )
}

/**
 * The middle pane: the finding, its diff hunk at the reviewed head (or the bare anchor when the
 * anchor is in unchanged code), the suggestion, the check's @humans text, the thread and the composer.
 */
export function FindingDetail({
  finding: f,
  subject,
  draft,
  onDraft,
  actorName,
  isAuthor,
  now,
  onRefresh,
  refreshing,
}: {
  finding: ReviewFindingDto
  subject: ReviewSubjectDto
  draft: Draft | undefined
  onDraft: (draft: Draft | undefined) => void
  actorName: string
  isAuthor: boolean
  now: number
  onRefresh: () => void
  refreshing: boolean
}) {
  return (
    <Stack gap="sm">
      <Box>
        <Group gap={6} wrap="wrap">
          <SeverityBadge severity={f.severity} />
          <FlagBadges flags={f.flags} bypassesRoundBar={f.bypassesRoundBar} />
          <Tooltip label="Reviewer confidence; the posting bar compares it with the round's minimum">
            <Text size="xs" ff="monospace" c="gray.4">
              p {f.confidence.toFixed(2)}
            </Text>
          </Tooltip>
          <Text size="xs" c="dimmed">
            · raised in R{f.roundRaised}
          </Text>
          <FindingStatusBadge status={f.status} />
          <SyncBadge state={f.syncState} />
          {!f.posted && (
            <Tooltip label="Below the round's bar or summary-only: stored in Antiphon, not posted inline">
              <Text size="xs" c="dimmed" fs="italic">
                not posted inline
              </Text>
            </Tooltip>
          )}
        </Group>
        <Text fw={700} size="md" mt={4}>
          {f.key} · {f.title}
        </Text>
        <Group gap={6} mt={2} wrap="nowrap">
          <TbFileCode size={12} />
          <Text size="xs" ff="monospace" c="dimmed" truncate>
            {f.filePath}
            {lines(f)}
          </Text>
          {f.externalThreadUrl && (
            <Anchor href={f.externalThreadUrl} target="_blank" rel="noreferrer" size="xs">
              <Group gap={2} wrap="nowrap">
                thread <TbExternalLink size={11} />
              </Group>
            </Anchor>
          )}
        </Group>
      </Box>

      {f.syncState === 'Failed' && (
        <Alert color="warning" variant="light" p={8} icon={<TbCloudOff size={16} />} title={<Text size="xs" fw={700}>Saved in Antiphon, not yet on the code host</Text>}>
          <Text size="xs">
            The last reply failed to post. The finding is never resolved in Antiphon alone; Refresh retries the post.
          </Text>
          <Button mt={6} size="compact-xs" variant="light" color="warning" leftSection={<TbRefresh size={12} />} loading={refreshing} onClick={onRefresh}>
            Retry now
          </Button>
        </Alert>
      )}

      {f.hunk ? (
        <DiffHunk hunk={f.hunk} highlight={{ start: f.lineStart, end: f.lineEnd }} />
      ) : (
        f.anchor && (
          <Code block fz={12}>
            {f.lineStart !== null ? `${f.lineStart}  ` : ''}
            {f.anchor}
          </Code>
        )
      )}
      <Box fz="sm">
        <RenderedMarkdown>{f.body}</RenderedMarkdown>
      </Box>
      {f.suggestion && (
        <Box>
          <Text size="xs" c="dimmed" mb={2}>
            Suggested change
          </Text>
          <Code block fz={12} style={{ borderLeft: '3px solid var(--mantine-color-success-7)' }}>
            {f.suggestion}
          </Code>
        </Box>
      )}
      <WhyCheck f={f} />

      <Divider label={<Text size="xs">Thread</Text>} labelPosition="left" />
      {f.events.length === 0 ? (
        <Text size="xs" c="dimmed">
          No replies yet.
        </Text>
      ) : (
        <Box>
          {f.events.map((e) => (
            <EventRow key={e.id} e={e} now={now} />
          ))}
        </Box>
      )}

      <Divider label={<Text size="xs">Your response</Text>} labelPosition="left" />
      <ResponseComposer key={f.id} finding={f} subject={subject} draft={draft} onChange={onDraft} actorName={actorName} isAuthor={isAuthor} />
    </Stack>
  )
}
