import { Alert, Button, Group, Paper, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  agentTaskKeys,
  useCancelAgentTask,
  useCeilingDecision,
  type AgentTaskDetailDto,
  type CeilingAction,
  type CeilingHoldDto,
} from '../../api/agentTasks'
import { ApiError, getApiErrorMessage } from '../../api/client'

const NO_PHASE = 'No phase deadline covers this tail; the ceiling is its only deadline.'

/** Whole minutes 1..720, or null: a decimal, sign or blank never reaches the server. */
function extendMinutes(text: string): number | null {
  if (!/^\d{1,3}$/.test(text.trim())) return null
  const minutes = Number(text.trim())
  return minutes >= 1 && minutes <= 720 ? minutes : null
}

/**
 * CARD-1610 S4a: a Working task held at its ceiling. The delegate keeps running; this card only
 * records a typed Continue/Extend decision or calls the existing Cancel. Nothing is sent on render,
 * and there is no operator Extend here: that needs the owner-only token, so the CLI owns it.
 */
export function CeilingHoldCard({ detail }: { detail: AgentTaskDetailDto }) {
  const hold = detail.ceilingHold
  if (!hold) return null
  return <HoldCard taskId={detail.summary.id} hold={hold} />
}

function HoldCard({ taskId, hold }: { taskId: string; hold: CeilingHoldDto }) {
  const queryClient = useQueryClient()
  const decide = useCeilingDecision()
  const cancel = useCancelAgentTask()
  const [minutesText, setMinutesText] = useState('')
  const [error, setError] = useState<string | null>(null)
  const minutes = extendMinutes(minutesText)
  const busy = decide.isPending || cancel.isPending
  const final = hold.capReached

  const send = (ceilingAction: CeilingAction) =>
    decide.mutate(
      {
        id: taskId,
        ceilingAction,
        episodeId: hold.episodeId,
        ...(ceilingAction === 'Extend' && minutes !== null ? { extendMinutes: minutes } : {}),
      },
      {
        onSuccess: () => {
          setError(null)
          notifications.show({ color: 'green', message: 'Decision recorded; session continues' })
        },
        onError: (failure) => {
          const message = getApiErrorMessage(failure, 'Could not record the decision')
          setError(message)
          notifications.show({ color: 'red', message })
          // A stale episode means the hold moved on: show the server's current state.
          if (failure instanceof ApiError && failure.status === 409)
            void queryClient.invalidateQueries({ queryKey: agentTaskKeys.detail(taskId) })
        },
      },
    )

  const phase = hold.phaseKind
    ? `Phase ${hold.phaseKind}: ${hold.phaseLimitMinutes ?? '?'}-minute limit, deadline ${hold.phaseDeadlineAt ?? 'unknown'}; phase supervision continues.`
    : NO_PHASE

  return (
    <Paper withBorder p="md" data-testid="ceiling-hold-card">
      <Stack gap="xs">
        <Text fw={600}>Ceiling reached: held for a decision, still working</Text>
        <Text size="sm">{hold.question}</Text>
        <Text size="sm">
          Next bound {hold.nextCeilingMinutes} min; last transcript {hold.lastTranscriptKind ?? 'unknown'}.
        </Text>
        <Text size="sm">{phase}</Text>
        {final && (
          <Alert color="orange" title="Final hold at 720 minutes">
            Continue and Extend refuse here. An operator can extend with delegate.ps1 -Reply {taskId}{' '}
            -CeilingAction Extend -OperatorExtend -ExtendMinutes 1..240 -CeilingReason "...", or Cancel the
            task.
          </Alert>
        )}
        <Group align="flex-end" gap="sm">
          <Button onClick={() => send('Continue')} disabled={final || busy}>
            Continue
          </Button>
          <TextInput
            label="Extend by (minutes)"
            value={minutesText}
            onChange={(event) => setMinutesText(event.currentTarget.value)}
            disabled={final}
            inputMode="numeric"
            w={150}
          />
          <Button onClick={() => send('Extend')} disabled={final || busy || minutes === null}>
            Extend
          </Button>
          <Button color="red" variant="light" onClick={() => cancel.mutate(taskId)} disabled={busy}>
            Cancel
          </Button>
        </Group>
        {error && (
          <Alert color="red" data-testid="ceiling-hold-error">
            {error}
          </Alert>
        )}
      </Stack>
    </Paper>
  )
}
