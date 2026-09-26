import type { Meta, StoryObj } from '@storybook/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { hostKeys, type HostStats } from '../../api/hosts'
import { HostsPage } from './HostsPage'

const sample: HostStats = {
  hostId: 'desktop', displayName: 'Desktop', platform: 'windows', state: 'stale', reason: null,
  observedAt: '2026-09-26T12:00:00Z', intervalSeconds: 5, cores: 8,
  current: { cpuPercent: 42, load1: null, load5: null, load15: null,
    memoryUsedBytes: 8_000_000_000, memoryAvailableBytes: 8_000_000_000, memoryTotalBytes: 16_000_000_000,
    swapUsedBytes: 2_000_000_000, swapTotalBytes: 20_000_000_000,
    disks: [{ path: 'C:\\work', freeBytes: 50_000_000_000, totalBytes: 100_000_000_000 }],
    processCount: 100, processes: [] },
  rollups: { '1m': { cpuPercent: { avg: 40, max: 60 }, load1: null, memoryUsedBytes: { avg: 8_000_000_000, max: 9_000_000_000 } },
    '30m': { cpuPercent: { avg: 35, max: 80 }, load1: null, memoryUsedBytes: { avg: 7_000_000_000, max: 9_000_000_000 } } },
  antiphon: { tasksInFlight: 2, byStage: { Code: 2 }, byKind: { Codex: 2 }, queued: 1, held: 0,
    landsPending: 0, sessionsLive: 2, seatsDeclared: 7,
    buildSlots: { occupied: 1, budget: 2, waiters: 0, availableMb: 9000 } },
}
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity, gcTime: Infinity } } })
queryClient.setQueryData(hostKeys.stats, [sample, {
  ...sample, hostId: 'server2', displayName: 'Server 2', platform: 'linux', state: 'offline',
  observedAt: null, current: null, rollups: null,
}])
const meta: Meta<typeof HostsPage> = {
  title: 'Pages/Hosts', component: HostsPage, args: { live: false },
  decorators: [(Story) => <QueryClientProvider client={queryClient}><Story /></QueryClientProvider>],
}
export default meta
type Story = StoryObj<typeof HostsPage>
export const Desktop: Story = {}
export const Mobile: Story = { globals: { viewport: { value: 'iphone12' } } }
