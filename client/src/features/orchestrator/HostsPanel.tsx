import { useState } from 'react'
import { Badge, Button, Group, Loader, NumberInput, Paper, Stack, Text, TextInput, Title } from '@mantine/core'
import { ApiError, getApiErrorMessage } from '../../api/client'
import { type HostBudgetDto, useHosts, usePutHostBudget, usePutRunnerCapacity } from '../../api/hosts'

function problemCode(error: unknown, fallback: string): string {
  if (error instanceof ApiError && error.body && typeof error.body === 'object'
    && 'code' in error.body && typeof error.body.code === 'string') {
    return error.body.code
  }
  return getApiErrorMessage(error, fallback)
}

function HostRow({ host }: { host: HostBudgetDto }) {
  const budget = usePutHostBudget()
  const capacity = usePutRunnerCapacity()
  const [budgetValue, setBudgetValue] = useState<number | string>(host.configuredMaxInFlight ?? host.effectiveLimit ?? '')
  const [budgetReason, setBudgetReason] = useState('')
  const [capacityValue, setCapacityValue] = useState<number | string>(host.declaredCapacity ?? '')
  const [capacityReason, setCapacityReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const overBudget = host.effectiveLimit !== null && host.inFlight > host.effectiveLimit

  return (
    <Paper withBorder p="sm" data-testid={`host-${host.hostId}`}>
      <Stack gap="xs">
        <Group justify="space-between">
          <Group gap="xs">
            <Text fw={600}>{host.hostId}</Text>
            <Badge size="sm" color={host.dispatchEligible ? 'green' : 'gray'}>
              {host.dispatchEligible ? 'Eligible' : host.available ? 'Recovering' : 'Offline'}
            </Badge>
            {overBudget && <Badge size="sm" color="orange">Over budget</Badge>}
          </Group>
          <Text size="sm">{host.inFlight} / {host.effectiveLimit ?? '—'}</Text>
        </Group>
        <Text size="xs" c="dimmed">
          {host.kind} · source: {host.source}
          {host.kind === 'runner' ? ` · runner declares ${host.declaredCapacity ?? 'unknown'}` : ''}
        </Text>
        <Group align="flex-end" gap="xs">
          <NumberInput
            label={`Budget for ${host.hostId}`}
            aria-label={`Budget for ${host.hostId}`}
            value={budgetValue}
            onChange={setBudgetValue}
            min={0}
            max={512}
            w={125}
          />
          <TextInput
            label={`Reason for ${host.hostId}`}
            aria-label={`Reason for ${host.hostId}`}
            value={budgetReason}
            onChange={(event) => setBudgetReason(event.currentTarget.value)}
            w={210}
          />
          <Button
            size="sm"
            aria-label={`Save budget for ${host.hostId}`}
            loading={budget.isPending}
            disabled={!budgetReason.trim()}
            onClick={() => {
              setError(null)
              budget.mutate({
                hostId: host.hostId,
                maxInFlight: budgetValue === '' ? null : Number(budgetValue),
                reason: budgetReason.trim(),
              }, {
                onSuccess: () => setBudgetReason(''),
                onError: (failure) => setError(problemCode(failure, 'Could not save budget')),
              })
            }}
          >
            Save budget
          </Button>
        </Group>
        {host.kind === 'runner' && (
          <Group align="flex-end" gap="xs">
            <NumberInput
              label={`Capacity for ${host.hostId}`}
              aria-label={`Capacity for ${host.hostId}`}
              value={capacityValue}
              onChange={setCapacityValue}
              min={1}
              w={125}
            />
            <TextInput
              label={`Reason for ${host.hostId} capacity`}
              aria-label={`Reason for ${host.hostId} capacity`}
              value={capacityReason}
              onChange={(event) => setCapacityReason(event.currentTarget.value)}
              w={210}
            />
            <Button
              size="sm"
              aria-label={`Save capacity for ${host.hostId}`}
              loading={capacity.isPending}
              disabled={!capacityReason.trim() || capacityValue === ''}
              onClick={() => {
                setError(null)
                capacity.mutate({
                  runnerId: host.hostId,
                  capacity: Number(capacityValue),
                  reason: capacityReason.trim(),
                }, {
                  onSuccess: () => setCapacityReason(''),
                  onError: (failure) => setError(problemCode(failure, 'Could not change runner capacity')),
                })
              }}
            >
              Save capacity
            </Button>
          </Group>
        )}
        {error && <Text size="sm" c="red">{error}</Text>}
      </Stack>
    </Paper>
  )
}

export function HostsPanel() {
  const hosts = useHosts()
  return (
    <Paper withBorder p="md" data-testid="hosts-panel">
      <Stack gap="sm">
        <Title order={5}>Host budgets</Title>
        {hosts.isLoading ? <Loader size="sm" /> : hosts.error ? (
          <Text size="sm" c="red">Could not load host budgets.</Text>
        ) : (hosts.data ?? []).map((host) => <HostRow key={host.hostId} host={host} />)}
      </Stack>
    </Paper>
  )
}
