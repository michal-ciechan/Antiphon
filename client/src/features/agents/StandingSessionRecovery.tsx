import { useState } from 'react'
import { Alert, Button, Group, Modal, Stack, Text } from '@mantine/core'
import { useStandingSessionHistory, useStartAgent, type AgentSummaryDto, type StartAgentRequest } from '../../api/agents'
import { getApiErrorMessage } from '../../api/client'

export function StandingSessionRecovery({ agent }: { agent: AgentSummaryDto }) {
  const [open, setOpen] = useState(false)
  const [before, setBefore] = useState<string>()
  const [decision, setDecision] = useState<StartAgentRequest>()
  const [error, setError] = useState<string>()
  const [queued, setQueued] = useState(false)
  const history = useStandingSessionHistory(agent.id, open, before)
  const start = useStartAgent(agent.id)
  const held = agent.supervision?.continuityHeldAt
  const reason = agent.supervision?.continuityReason
  const decide = () => {
    if (!decision || start.isPending) return
    setError(undefined)
    start.mutate(decision, {
      onSuccess: () => { setQueued(true); setDecision(undefined) },
      onError: (failure) => setError(getApiErrorMessage(failure, 'Conversation recovery was refused')),
    })
  }
  return <Stack gap="xs">
    {held && <Alert color="orange" title="Conversation recovery needs a decision">
      <Text size="sm">{reason === 'NativeSessionMissing'
        ? 'The provider could not find this conversation.'
        : reason === 'OwnershipUnproven'
          ? 'Antiphon cannot prove ownership of this history. This does not mean the history was deleted.'
          : 'The existing conversation cannot currently be resumed. Inspect and repair its target or configuration.'}</Text>
      <Text size="sm">Target: {agent.supervision?.continuitySessionId ?? 'Unknown'}</Text>
    </Alert>}
    <Group>
      <Button variant="subtle" onClick={() => { setOpen(true); setQueued(false) }}>Resume previous conversation</Button>
      {!agent.liveSession && <Button variant="subtle" color="orange" onClick={() => { setOpen(true); setDecision({ fresh: true }) }}>Start fresh</Button>}
      {held && !agent.liveSession && <Button variant="light" onClick={() => { setOpen(true); setDecision({ retryContinuity: true }) }}>Retry after repair</Button>}
    </Group>
    <Modal opened={open} onClose={() => { if (!start.isPending) { setOpen(false); setDecision(undefined) } }} title={`Conversations for ${agent.name}`}>
      <Stack>
        {queued && <Alert color="blue">Launch queued. Recovery is confirmed only after the conversation finishes starting.</Alert>}
        {error && <Alert color="red">{error}</Alert>}
        {history.isLoading && <Text>Loading conversation history…</Text>}
        {history.error && <Alert color="red">Could not load conversation history.</Alert>}
        {history.data?.items.map(session => <Stack key={session.id} gap={4}>
          <Text size="sm">{session.id} · {session.kind} · {session.status}</Text>
          <Text size="xs">{session.cwd}</Text>
          <Button component="a" href={`/sessions/${session.id}`} variant="subtle">Open {session.id}</Button>
          <Button variant="light" disabled={!session.eligible || !!agent.liveSession || start.isPending}
            onClick={() => setDecision({ resumeSessionId: session.id })}>Select {session.id}</Button>
          {!session.eligible && <Text size="xs">Unavailable: {session.refusalCode}</Text>}
        </Stack>)}
        {history.data?.nextBefore && <Button variant="subtle" onClick={() => setBefore(history.data!.nextBefore!)}>Older conversations</Button>}
        {decision && <Alert color={decision.fresh ? 'orange' : 'blue'}>
          <Text size="sm">{decision.fresh
            ? `Start a new conversation and leave ${agent.persistentSessionId ?? 'the previous history'} behind. The histories will remain separate.`
            : `Resume ${decision.resumeSessionId ?? agent.supervision?.continuitySessionId ?? agent.persistentSessionId} with current agent settings.`}</Text>
          <Button mt="sm" loading={start.isPending} onClick={decide}>Confirm {decision.fresh ? 'fresh start' : 'resume'}</Button>
        </Alert>}
      </Stack>
    </Modal>
  </Stack>
}
