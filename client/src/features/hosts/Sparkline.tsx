import { Text } from '@mantine/core'

interface Point { t: string; v: number }

export function Sparkline({ points, title }: { points: Point[]; title: string }) {
  const valid = points.filter(point => Number.isFinite(point.v))
  if (valid.length === 0) return <Text size="xs" c="dimmed">No history</Text>

  const values = valid.map(point => point.v)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const range = max - min || 1
  const first = Date.parse(valid[0].t)
  const last = Date.parse(valid[valid.length - 1].t)
  const span = last - first
  const path = valid.map((point, index) => {
    const x = span > 0 ? ((Date.parse(point.t) - first) / span) * 100 : valid.length === 1 ? 100 : (index / (valid.length - 1)) * 100
    const y = 36 - ((point.v - min) / range) * 32
    return `${index === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`
  }).join(' ')

  return (
    <svg viewBox="0 0 100 40" preserveAspectRatio="none" width="100%" height="64" role="img" aria-label={title}>
      <title>{title}</title>
      <path d={path} fill="none" stroke="var(--mantine-color-active-6)" strokeWidth="1.5" vectorEffect="non-scaling-stroke" />
    </svg>
  )
}
