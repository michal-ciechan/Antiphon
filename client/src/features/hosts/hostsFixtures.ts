import type { HostStats } from '../../api/hosts'

export const observedAt = '2026-09-26T12:00:00Z'

export function host(overrides: Partial<HostStats> = {}): HostStats {
  return {
    hostId: 'desktop', displayName: 'Desktop', platform: 'windows', state: 'live', reason: null,
    observedAt, intervalSeconds: 5, cores: 8,
    current: {
      cpuPercent: 42, load1: null, load5: null, load15: null,
      memoryUsedBytes: 8_000_000_000, memoryAvailableBytes: 8_000_000_000,
      memoryTotalBytes: 16_000_000_000, swapUsedBytes: 2_000_000_000,
      swapTotalBytes: 20_000_000_000, disks: [{ path: 'C:\\work', freeBytes: 50_000_000_000, totalBytes: 100_000_000_000 }],
      processCount: 100, processes: [],
    },
    rollups: { '1m': { cpuPercent: { avg: 42, max: 55 }, load1: null, memoryUsedBytes: null } },
    antiphon: { tasksInFlight: 2, byStage: { Code: 2 }, byKind: { Codex: 2 }, queued: 1, held: 0,
      landsPending: 0, sessionsLive: 2, seatsDeclared: 7, buildSlots: { occupied: 1, budget: 2, waiters: 0, availableMb: 9000 } },
    ...overrides,
  }
}
