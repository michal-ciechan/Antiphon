import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { hostKeys, type HostMetric, type HostSeries, type HostStats, type HostWindow } from '../../api/hosts'

const HUB_URL = '/hubs/antiphon'
const WINDOW_MS: Record<HostWindow, number> = { '1m': 60_000, '5m': 300_000, '15m': 900_000, '30m': 1_800_000 }

function metricValue(host: HostStats, metric: HostMetric): number | null {
  if (!host.current) return null
  if (metric === 'cpu') return host.current.cpuPercent
  if (metric === 'load') return host.current.load1
  return host.current.memoryUsedBytes
}

export function useHostStatsLive() {
  const queryClient = useQueryClient()

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()
    let disposed = false

    const onStats = (hosts: HostStats[]) => {
      if (disposed) return
      queryClient.setQueryData(hostKeys.stats, hosts)
      for (const host of hosts) {
        if (!host.observedAt || host.state !== 'live') continue
        for (const [key] of queryClient.getQueriesData<HostSeries>({ queryKey: ['hosts', host.hostId, 'series'] })) {
          const metric = key[3] as HostMetric
          const window = key[4] as HostWindow
          const value = metricValue(host, metric)
          if (value === null || value === undefined || !Number.isFinite(value)) continue
          queryClient.setQueryData<HostSeries>(key, old => {
            if (!old || old.points.some(point => point.t === host.observedAt)) return old
            const cutoff = Date.parse(host.observedAt!) - WINDOW_MS[window]
            return { ...old, points: [...old.points.filter(point => Date.parse(point.t) >= cutoff), { t: host.observedAt!, v: value }] }
          })
        }
      }
    }

    connection.on('HostStatsUpdated', onStats)
    connection.onreconnected(() => {
      void connection.invoke('JoinGroup', 'hosts').then(() => {
        if (!disposed) {
          void queryClient.invalidateQueries({ queryKey: hostKeys.stats })
          void queryClient.invalidateQueries({ queryKey: ['hosts'], predicate: query => query.queryKey[2] === 'series' })
        }
      }).catch(() => { /* next reconnect will retry */ })
    })
    void connection.start().then(() => connection.invoke('JoinGroup', 'hosts')).catch(() => { /* REST remains available */ })

    return () => {
      disposed = true
      connection.off('HostStatsUpdated', onStats)
      if (connection.state === HubConnectionState.Connected) {
        void connection.invoke('LeaveGroup', 'hosts').finally(() => void connection.stop())
      } else {
        void connection.stop()
      }
    }
  }, [queryClient])
}
