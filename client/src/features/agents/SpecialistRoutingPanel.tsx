import { Alert, Button, Group, Stack, Switch, Text, TextInput } from '@mantine/core'
import { useEffect, useState } from 'react'
import { getApiErrorMessage } from '../../api/client'
import { useRevalidateSpecialistRouting, useSaveSpecialistRouting, useSpecialistRouting, type SpecialistPair } from '../../api/specialistRouting'

export function SpecialistRoutingPanel({ agentId }: { agentId: string }) {
  const query = useSpecialistRouting(agentId)
  const save = useSaveSpecialistRouting(agentId)
  const revalidate = useRevalidateSpecialistRouting(agentId)
  const [pairs, setPairs] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [token, setToken] = useState<string | null>(null)
  const [dirty, setDirty] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!query.data || dirty) return
    const data = query.data
    setPairs((data.candidates.length ? data.candidates : [{ agentKind: data.primaryKind, modelLevel: data.primaryLevel }]).map(p => `${p.agentKind}/${p.modelLevel}`).join(', '))
    setEnabled(data.enabled ?? true)
    setToken(data.concurrencyToken)
  }, [query.data, dirty])
  async function persist() {
    const values = pairs.split(',').map(p => p.trim())
    if (values.length < 1 || values.length > 3 || values.some(p => !/^(ClaudeCode|Codex)\/(Frontier|High|Medium|Low)$/.test(p))) {
      setError('Enter one to three Kind/Level pairs, for example ClaudeCode/Low, Codex/Low.')
      return
    }
    const candidates = values.map(p => { const [agentKind, modelLevel] = p.split('/'); return { agentKind, modelLevel } as SpecialistPair })
    try { await save.mutateAsync({ concurrencyToken: token, enabled, candidates }); setDirty(false); setError(null) }
    catch (e) { setError(getApiErrorMessage(e, 'Routing could not be saved. Reload before resolving concurrent changes.')) }
  }
  if (query.isPending) return <Text size="sm">Loading Check routing…</Text>
  if (query.error) return <Alert color="red">{getApiErrorMessage(query.error, 'Check routing could not be loaded.')}</Alert>
  return <Stack gap="xs">
    <Text fw={600}>Check interpreter routing</Text>
    <Text size="sm">Primary: {query.data?.primaryKind}/{query.data?.primaryLevel} ({query.data?.primaryModelAlias}). Keep this pair first.</Text>
    {query.data?.health && <Text size="sm">{query.data.health.status}: {query.data.health.reason}</Text>}
    <Switch label="Enable declared routing" checked={enabled} onChange={e => { setEnabled(e.currentTarget.checked); setDirty(true) }} />
    <TextInput label="Ordered candidates" value={pairs} onChange={e => { setPairs(e.currentTarget.value); setDirty(true) }} />
    <Text size="xs" c="dimmed">Saving authorizes bounded qualification on the listed models. A candidate can serve Checks only after it qualifies.</Text>
    {query.data?.candidateStates.map(c => <Stack gap={0} key={c.id}>
      <Text size="sm">{c.agentKind}/{c.modelLevel}: {c.status} · transient failures {c.transientFailures}</Text>
      <Text size="xs">{c.reason}</Text>
      <Text size="xs" c="dimmed">Unprovisioned since {c.unprovisionedAt ?? '—'}; last refusal {c.lastAdmissionRefusedAt ?? '—'}; next eligibility {c.nextEligibleAt ?? 'unknown'}.</Text>
    </Stack>)}
    {error && <Alert color="red">{error}</Alert>}
    <Group>
      <Button size="xs" onClick={persist} loading={save.isPending}>Save routing</Button>
      <Button size="xs" variant="light" disabled={dirty || !token || !enabled} loading={revalidate.isPending} onClick={async () => {
        if (!token) return
        try { await revalidate.mutateAsync(token); setError(null) }
        catch (e) { setError(getApiErrorMessage(e, 'Revalidation could not be requested.')) }
      }}>Revalidate</Button>
      <Button size="xs" variant="subtle" onClick={async () => { await query.refetch(); setDirty(false); setError(null) }}>Reload routing</Button>
    </Group>
  </Stack>
}
