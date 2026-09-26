import { Badge, Group, Paper, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { type HostMetric, type HostStats, useHostSeries } from '../../api/hosts'
import { RollupTable } from './RollupTable'
import { Sparkline } from './Sparkline'

const bytes = (value: number | null | undefined) => value == null ? '—' : `${(value / 1_000_000_000).toFixed(1)} GB`
const value = (n: number | null | undefined, suffix = '') => n == null ? '—' : `${n.toFixed(0)}${suffix}`
const STATE_COLORS: Record<HostStats['state'], string> = { live: 'success', stale: 'warning', offline: 'danger', unsupported: 'gray' }

function Metric({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><Text size="xs" c="dimmed">{label}</Text><Text fw={600}>{children}</Text></div>
}

function HostGraph({ host, metric, label }: { host: HostStats; metric: HostMetric; label: string }) {
  const { data, isError } = useHostSeries(host.hostId, metric, '30m', host.state === 'live' || host.state === 'stale')
  return <div>
    <Text size="xs" fw={600} mb={4}>{label} · 30m</Text>
    {isError ? <Text size="xs" c="dimmed">History unavailable</Text> : <Sparkline points={data?.points ?? []} title={`${host.displayName} ${label} history`} />}
  </div>
}

export function HostCard({ host }: { host: HostStats }) {
  const current = host.current
  const hasData = current !== null && host.state !== 'unsupported'
  return (
    <Paper component="section" aria-label={host.displayName} withBorder radius="md" p="md" style={{ minWidth: 0 }}>
      <Stack gap="md">
        <Group justify="space-between" align="flex-start" wrap="wrap">
          <div><Title order={3}>{host.displayName}</Title><Text size="xs" c="dimmed">{host.hostId} · {host.platform ?? 'Platform unknown'}</Text></div>
          <Badge color={STATE_COLORS[host.state]} variant="light">{host.state[0].toUpperCase() + host.state.slice(1)}</Badge>
        </Group>
        {host.observedAt && <Text size="xs" c="dimmed">Last sample: {new Date(host.observedAt).toLocaleString()}</Text>}
        {host.reason && <Text size="xs" c="dimmed" style={{ overflowWrap: 'anywhere' }}>{host.reason}</Text>}
        {!hasData ? <Text c="dimmed">No data</Text> : <>
          <SimpleGrid cols={{ base: 2, sm: 3 }} spacing="sm">
            <Metric label="CPU">{value(current.cpuPercent, ' %')}</Metric>
            <Metric label="Memory used / total">{bytes(current.memoryUsedBytes)} / {bytes(current.memoryTotalBytes)}</Metric>
            <Metric label="Load (1m)">{current.load1 == null ? '—' : current.load1.toFixed(2)}</Metric>
            <Metric label="Tasks in flight">{host.antiphon.tasksInFlight}</Metric>
            <Metric label="Sessions / seats">{value(host.antiphon.sessionsLive)} / {value(host.antiphon.seatsDeclared)}</Metric>
            <Metric label="Build slots">{host.antiphon.buildSlots ? `${host.antiphon.buildSlots.occupied} / ${host.antiphon.buildSlots.budget}` : '—'}</Metric>
            <Metric label={host.platform === 'windows' ? 'Commit charge / limit' : 'Swap used / total'}>{bytes(current.swapUsedBytes)} / {bytes(current.swapTotalBytes)}</Metric>
            <Metric label="Processes">{value(current.processCount)}</Metric>
            <Metric label="Queued / held / lands">{host.antiphon.queued} / {host.antiphon.held} / {host.antiphon.landsPending}</Metric>
          </SimpleGrid>
          {current.disks.length > 0 && <Text size="xs" c="dimmed">Volumes: {current.disks.map(disk => `${disk.path} ${bytes(disk.freeBytes)} free of ${bytes(disk.totalBytes)}`).join(' · ')}</Text>}
          <RollupTable rollups={host.rollups} />
          <SimpleGrid cols={{ base: 1, sm: 3 }} spacing="md">
            <HostGraph host={host} metric="cpu" label="CPU" />
            <HostGraph host={host} metric="load" label="Load" />
            <HostGraph host={host} metric="memory" label="Memory" />
          </SimpleGrid>
        </>}
      </Stack>
    </Paper>
  )
}
