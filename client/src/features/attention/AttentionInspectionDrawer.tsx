import { Alert, Anchor, Code, Drawer, Loader, Paper, Stack, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import type { AttentionItemDto } from '../../api/attention'
import { getSessionTranscript } from '../../api/sessions'
import { SessionTranscriptPanel } from '../agents/SessionTranscriptPanel'

/** Read-only inspection for leak rows that have no task or agent drawer. */
export function AttentionInspectionDrawer({ items }: { items: AttentionItemDto[] }) {
  const [params, setParams] = useSearchParams()
  const sessionId = params.get('session')
  const censusKey = params.get('census')
  const selected = sessionId
    ? items.find((item) => item.sessionId === sessionId && item.kind !== 'RecentFailure')
    : items.find((item) => item.kind === 'ZombieCensusReport' && item.conditionKey === censusKey)

  const close = () => {
    const next = new URLSearchParams(params)
    next.delete('session')
    next.delete('census')
    setParams(next)
  }

  return (
    <Drawer opened={Boolean(sessionId || censusKey)} onClose={close} position="right" size="xl"
      title={sessionId ? 'Session inspection' : 'Census inspection'}
      closeButtonProps={{ 'aria-label': 'Close inspection' }}>
      <Stack gap="md">
        {selected && <>
          <Text fw={600}>{selected.title}</Text>
          <Text>{selected.headline}</Text>
          <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{selected.evidence}</Text>
        </>}
        {sessionId ? <>
          <Code>{sessionId}</Code>
          <SessionInspection key={sessionId} sessionId={sessionId} />
        </> : selected ? <>
          <Text size="sm">Snapshot taken {selected.sinceUtc}. This is census evidence at that time.</Text>
          {selected.censusCandidates?.map((candidate) => (
            <Paper withBorder p="sm" key={candidate.pid}>
              <Stack gap={4}>
                <Text fw={600}>{candidate.exe}</Text>
                <Text size="sm">PID {candidate.pid} · {candidate.agentName} · {candidate.class}</Text>
                <Text size="sm">Database: {candidate.dbStatus} · Runner claimed: {candidate.runnerClaimed ? 'yes' : 'no'}</Text>
                <Text size="sm">Started: {candidate.startUtc ?? 'unknown'} · Memory: {candidate.workingSetGb} GB · CPU: {candidate.cpuDeltaPercent ?? 'unknown'}%</Text>
                <Text size="sm">Identity: {candidate.identityMethod} · Candidate tree PID: {candidate.treeKillPid}</Text>
                {candidate.failedRules.length > 0 && <Text size="sm">Failed rules: {candidate.failedRules.join('; ')}</Text>}
                {candidate.sessionId ? (
                  <Anchor component={Link} to={`/attention?session=${encodeURIComponent(candidate.sessionId)}`}>
                    {candidate.sessionId}
                  </Anchor>
                ) : <Text size="sm" c="dimmed">No session identified.</Text>}
              </Stack>
            </Paper>
          ))}
          {!selected.censusCandidates?.length && <Text>No candidate details are available for this snapshot.</Text>}
        </> : censusKey ? <Text>This census condition is no longer on the attention feed.</Text> : null}
      </Stack>
    </Drawer>
  )
}

function SessionInspection({ sessionId }: { sessionId: string }) {
  const transcript = useQuery({
    queryKey: ['session', sessionId, 'transcript'],
    queryFn: () => getSessionTranscript(sessionId),
  })
  if (transcript.isPending) return <Loader aria-label="Loading session transcript" />
  if (transcript.error) return <Alert color="danger" title="Could not load session transcript">
    {transcript.error.message}
  </Alert>
  if (!transcript.data.entries.length) return <Text>No transcript entries are available for this session.</Text>
  return <SessionTranscriptPanel key={transcript.dataUpdatedAt} sessionId={sessionId} initialEntries={transcript.data.entries} />
}
