import { Alert, Container, Loader, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useHostStats } from '../../api/hosts'
import { HostCard } from './HostCard'
import { useHostStatsLive } from './useHostStatsLive'
import './hosts.css'

export function HostsPage({ live = true }: { live?: boolean }) {
  const { data, isLoading, isError, refetch } = useHostStats()
  if (live) return <LiveHostsPage data={data} isLoading={isLoading} isError={isError} refetch={refetch} />
  return <HostsContent data={data} isLoading={isLoading} isError={isError} refetch={refetch} />
}

function LiveHostsPage(props: React.ComponentProps<typeof HostsContent>) {
  useHostStatsLive()
  return <HostsContent {...props} />
}

function HostsContent({ data, isLoading, isError, refetch }: {
  data: ReturnType<typeof useHostStats>['data']
  isLoading: boolean
  isError: boolean
  refetch: ReturnType<typeof useHostStats>['refetch']
}) {
  return <Container size="xl" py="xl">
    <Stack gap="lg">
      <div><Title order={2}>Hosts</Title><Text size="sm" c="dimmed">Current health and the last 30 minutes of runner samples</Text></div>
      {isLoading && <Loader aria-label="Loading hosts" />}
      {isError && <Alert color="danger" title="Hosts unavailable"><button type="button" onClick={() => void refetch()}>Retry</button></Alert>}
      {!isLoading && !isError && data?.length === 0 && <Text c="dimmed">No hosts configured</Text>}
      {data && <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="md">{data.map(host => <HostCard key={host.hostId} host={host} />)}</SimpleGrid>}
    </Stack>
  </Container>
}
