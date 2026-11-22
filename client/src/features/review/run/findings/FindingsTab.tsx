import { Badge, Box, Group, Paper, Stack, Text } from '@mantine/core'
import { useMediaQuery } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { useCallback, useMemo, useState } from 'react'
import type { ReviewFindingDto, ReviewRunDetailDto, ReviewViewerDto } from '../../../../api/reviewEngine'
import { BlastPanel } from '../../components/Blast'
import { SectionLabel } from '../../components/SectionLabel'
import { personLabel } from '../../reviewFormat'
import { useRefreshReviewRun, useRespondToFindings } from '../../useReview'
import { GatePanel } from '../GatePanel'
import { lastDecidedGate, openGate } from '../gateRules'
import { toResponses, type Draft, type Drafts } from './drafts'
import { FindingDetail } from './FindingDetail'
import { FindingList } from './FindingList'
import { groupFindings } from './findingGroups'
import { SubmitBar } from './SubmitBar'

/**
 * The PR review split view (design §11.2): findings list, the selected finding with its composer,
 * and a rail with the gate panel (the open gate, else the last decided one) and blast radius.
 * Drafts collect across findings and post together. The viewer comes from `GET /api/review-me`.
 */
export function FindingsTab({ run, findings, viewer, now }: { run: ReviewRunDetailDto; findings: ReviewFindingDto[]; viewer: ReviewViewerDto; now: number }) {
  const [drafts, setDrafts] = useState<Drafts>({})
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const wide = useMediaQuery('(min-width: 1200px)', true)
  const respond = useRespondToFindings(run.id)
  const refresh = useRefreshReviewRun(run.id)
  const groups = useMemo(() => groupFindings(findings), [findings])
  const selected = findings.find((f) => f.id === selectedId) ?? groups[0]?.items[0]
  const gate = openGate(run) ?? lastDecidedGate(run)
  const isAuthor = viewer.login === run.subject.authorLogin
  const viewerName = personLabel(viewer.login, viewer.displayName)
  const setDraft = useCallback((id: string, d: Draft | undefined) => setDrafts((prev) => ({ ...prev, [id]: d })), [])
  const hostLabel = run.subject.host === 'GitHub' ? 'GitHub' : 'the code host'

  const submit = () => {
    const responses = toResponses(drafts)
    respond.mutate(responses, {
      onSuccess: (result) => {
        setDrafts({})
        notifications.show({
          color: 'success',
          title: `Posted ${result.items.length} ${result.items.length === 1 ? 'reply' : 'replies'}`,
          message: `Saved in Antiphon; syncing to ${hostLabel} as one review signed by ${viewerName}.`,
        })
      },
    })
  }

  return (
    <Stack gap="sm">
      <Box
        style={{
          display: 'grid',
          gridTemplateColumns: wide ? 'minmax(260px, 320px) minmax(0, 1fr) minmax(260px, 300px)' : 'minmax(0, 1fr)',
          gap: 12,
          alignItems: 'start',
        }}
      >
        <FindingList groups={groups} selectedId={selected?.id ?? null} drafts={drafts} onSelect={setSelectedId} />
        <Paper withBorder radius="md" p="sm">
          {selected ? (
            <FindingDetail
              finding={selected}
              subject={run.subject}
              draft={drafts[selected.id]}
              onDraft={(d) => setDraft(selected.id, d)}
              actorName={viewerName}
              isAuthor={isAuthor}
              now={now}
              onRefresh={() => refresh.mutate()}
              refreshing={refresh.isPending}
            />
          ) : (
            <Text size="sm" c="dimmed">
              No findings yet. They appear here after the round publishes.
            </Text>
          )}
        </Paper>
        <Stack gap="sm">
          {gate ? (
            <GatePanel key={gate.id} run={run} gate={gate} findings={findings} viewer={viewer} now={now} />
          ) : (
            <Paper withBorder p="xs" radius="md">
              <SectionLabel>Gate</SectionLabel>
              <Text size="xs" c="dimmed">
                No gate is open on this run.
              </Text>
            </Paper>
          )}
          <Paper withBorder p="xs" radius="md">
            <SectionLabel>Blast radius</SectionLabel>
            {run.blastRadius ? (
              <BlastPanel blast={run.blastRadius} />
            ) : (
              <Text size="xs" c="dimmed">
                Not categorised yet.
              </Text>
            )}
            {run.categories.length > 0 && (
              <Group gap={4} mt={6}>
                {run.categories.map((c) => (
                  <Badge key={c} size="xs" variant="light" color="gray" tt="none">
                    {c}
                  </Badge>
                ))}
              </Group>
            )}
          </Paper>
        </Stack>
      </Box>
      <SubmitBar
        drafts={drafts}
        findings={findings}
        actorName={viewerName}
        hostLabel={hostLabel}
        submitting={respond.isPending}
        onSubmit={submit}
        onDiscard={() => setDrafts({})}
      />
    </Stack>
  )
}
