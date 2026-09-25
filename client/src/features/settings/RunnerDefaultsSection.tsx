import { Alert, Button, Group, Loader, Paper, Select, Stack, Text, TextInput } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import { getApiErrorMessage } from '../../api/client'
import {
  usePutRunnerDefaults,
  useRunnerDefaultRevisions,
  useRunnerDefaults,
  type RunnerDefaultsDto,
} from '../../api/runnerDefaults'
import { useSessionRunners } from '../../api/sessionRunners'
import type { AgentKind } from '../../api/boards'

const INHERIT = '__inherit__'
const BUILTIN = '__builtin__'

function runnerLabel(id: string | null | undefined): string {
  if (!id || id === 'desktop' || id === 'local') return 'Desktop'
  return id
}

/**
 * CARD-0710. Fleet runner defaults. A failure here stays inside this paper so the
 * model-routing sections above keep working.
 */
export function RunnerDefaultsSection() {
  const defaults = useRunnerDefaults()
  const revisions = useRunnerDefaultRevisions()
  const runners = useSessionRunners()
  const save = usePutRunnerDefaults()

  if (defaults.isLoading || runners.isLoading) {
    return (
      <Paper withBorder p="md" data-testid="runner-defaults-section">
        <Text fw={600}>Runner defaults</Text>
        <Group justify="center" py="sm">
          <Loader size="sm" />
        </Group>
      </Paper>
    )
  }

  if (defaults.error || !defaults.data) {
    return (
      <Paper withBorder p="md" data-testid="runner-defaults-section">
        <Text fw={600}>Runner defaults</Text>
        <Text size="sm" c="dimmed">
          Could not load runner defaults. Model routing above is unchanged.
        </Text>
      </Paper>
    )
  }

  return (
    <RunnerDefaultsForm
      key={`${defaults.data.revision}:${defaults.data.updatedAt}`}
      current={defaults.data}
      catalogue={runners.data ?? []}
      history={revisions.data?.revisions ?? []}
      saving={save.isPending}
      onSave={async (body) => {
        try {
          await save.mutateAsync(body)
          notifications.show({ color: 'green', message: 'Runner defaults saved. The next fresh task uses them.' })
        } catch (error) {
          const message = getApiErrorMessage(error, 'Could not save runner defaults')
          notifications.show({ color: 'red', message })
          await defaults.refetch()
          throw error
        }
      }}
    />
  )
}

function RunnerDefaultsForm({
  current,
  catalogue,
  history,
  saving,
  onSave,
}: {
  current: RunnerDefaultsDto
  catalogue: { runnerId: string; displayName: string; platform: string | null; available: boolean }[]
  history: {
    revision: number
    globalRunnerId: string | null
    kindDefaults: { agentKind: AgentKind; runnerId: string }[]
    createdAt: string
    reason: string
    provenance: string
  }[]
  saving: boolean
  onSave: (body: {
    expectedRevision: number
    globalRunnerId: string | null
    kindDefaults: { agentKind: AgentKind; runnerId: string }[]
    reason: string
    provenance: 'Human'
  }) => Promise<void>
}) {
  const [globalChoice, setGlobalChoice] = useState(current.globalRunnerId ?? BUILTIN)
  const [kinds, setKinds] = useState<Record<string, string>>(() => {
    const next: Record<string, string> = {}
    for (const kind of current.supportedKinds) next[kind] = INHERIT
    for (const row of current.kindDefaults) next[row.agentKind] = row.runnerId
    return next
  })
  const [reason, setReason] = useState('')
  const [conflict, setConflict] = useState<string | null>(null)

  useEffect(() => {
    setConflict(null)
  }, [current.revision])

  const options = useMemo(() => {
    const rows = [
      { value: 'desktop', label: 'Desktop' },
      ...catalogue
        .filter((row) => row.runnerId !== 'desktop')
        .map((row) => ({
          value: row.runnerId,
          label: `${row.displayName} · ${row.platform ?? 'platform unknown'}${row.available ? '' : ' · offline'}`,
        })),
    ]
    const known = new Set(rows.map((row) => row.value))
    if (current.globalRunnerId && !known.has(current.globalRunnerId) && current.globalRunnerId !== 'desktop') {
      rows.push({ value: current.globalRunnerId, label: `${current.globalRunnerId} · missing from catalogue` })
    }
    for (const row of current.kindDefaults) {
      if (!known.has(row.runnerId) && row.runnerId !== 'desktop') {
        rows.push({ value: row.runnerId, label: `${row.runnerId} · missing from catalogue` })
        known.add(row.runnerId)
      }
    }
    return rows
  }, [catalogue, current])

  const inherited = globalChoice === BUILTIN ? 'the built-in fallback' : runnerLabel(globalChoice)

  const submit = async (snapshot?: { globalRunnerId: string | null; kindDefaults: { agentKind: AgentKind; runnerId: string }[] }) => {
    if (!reason.trim()) return
    const kindDefaults = snapshot
      ? snapshot.kindDefaults
      : current.supportedKinds
          .filter((kind) => kinds[kind] && kinds[kind] !== INHERIT)
          .map((kind) => ({ agentKind: kind as AgentKind, runnerId: kinds[kind] }))
    try {
      await onSave({
        expectedRevision: current.revision,
        globalRunnerId: snapshot ? snapshot.globalRunnerId : globalChoice === BUILTIN ? null : globalChoice,
        kindDefaults,
        reason: reason.trim(),
        provenance: 'Human',
      })
      setReason('')
      setConflict(null)
    } catch {
      setConflict('The saved revision changed. Your draft is still here; review the current values and save again.')
    }
  }

  return (
    <Paper withBorder p="md" data-testid="runner-defaults-section">
      <Stack gap="sm">
        <div>
          <Text fw={600}>Runner defaults</Text>
          <Text size="sm" c="dimmed">
            Saved for the next fresh task. A platform requirement and an explicit runner on that task take precedence,
            so a Windows task can bypass a Linux preference. Revision {current.revision}.
          </Text>
        </div>
        {current.unresolvedReferences.length > 0 && (
          <Alert color="yellow" data-testid="runner-defaults-unresolved">
            Unresolved runner {current.unresolvedReferences.join(', ')}. It stays until you choose another.
          </Alert>
        )}
        {conflict && (
          <Alert color="red" data-testid="runner-defaults-conflict">
            {conflict}
          </Alert>
        )}
        <Select
          label="Global runner"
          description="Built-in fallback defers to the desktop when no kind override matches."
          value={globalChoice}
          onChange={(value) => setGlobalChoice(value ?? BUILTIN)}
          data={[{ value: BUILTIN, label: 'Built-in fallback' }, ...options]}
          data-testid="runner-defaults-global"
        />
        {current.supportedKinds.map((kind) => (
          <Select
            key={kind}
            label={kind}
            description={`Use global default (${inherited}).`}
            value={kinds[kind] ?? INHERIT}
            onChange={(value) => setKinds((prev) => ({ ...prev, [kind]: value ?? INHERIT }))}
            data={[{ value: INHERIT, label: 'Use global default' }, ...options]}
            data-testid={`runner-defaults-kind-${kind}`}
          />
        ))}
        <TextInput
          label="Reason"
          value={reason}
          onChange={(event) => setReason(event.currentTarget.value)}
          data-testid="runner-defaults-reason"
        />
        <Group>
          <Button data-testid="runner-defaults-save" loading={saving} disabled={!reason.trim()} onClick={() => void submit()}>
            Save runner defaults
          </Button>
        </Group>
        <Stack gap={4} data-testid="runner-defaults-history">
          <Text size="sm" fw={600}>History</Text>
          {history.length === 0 && <Text size="sm" c="dimmed">No revisions yet.</Text>}
          {history.map((row) => (
            <Group key={row.revision} justify="space-between">
              <Text size="xs">
                r{row.revision} {runnerLabel(row.globalRunnerId)} · {row.provenance} · {row.reason}
              </Text>
              <Button
                size="compact-xs"
                variant="subtle"
                data-testid={`runner-defaults-restore-${row.revision}`}
                disabled={!reason.trim()}
                onClick={() => void submit({
                  globalRunnerId: row.globalRunnerId,
                  kindDefaults: row.kindDefaults.map((kind) => ({ agentKind: kind.agentKind, runnerId: kind.runnerId })),
                })}
              >
                Restore as new revision
              </Button>
            </Group>
          ))}
        </Stack>
      </Stack>
    </Paper>
  )
}
