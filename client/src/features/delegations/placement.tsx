import { Badge, Tooltip } from '@mantine/core'

export type RequiredPlatformName = 'Any' | 'Windows' | 'Linux'

export function displayRunner(runnerId: string | null | undefined): string {
  if (!runnerId || runnerId === 'local' || runnerId === 'desktop') return 'Desktop'
  return runnerId
}

/** Concise frozen placement. Any stays Any. A missing platform is omitted, never guessed. */
export function placementSummary(
  platform: string | null | undefined,
  runnerId: string | null | undefined,
): string | null {
  if (!platform) return null
  return `${platform} · ${displayRunner(runnerId)}`
}

export function PlacementBadge({
  platform,
  runnerId,
  source,
  observed,
  status,
}: {
  platform?: string | null
  runnerId?: string | null
  source?: string | null
  observed?: string | null
  status?: string | null
}) {
  const line = placementSummary(platform, runnerId)
  if (!line) return null
  const host = displayRunner(runnerId)
  const observedText = observed ? observed : 'platform unknown'
  const queued = status === 'Queued' ? 'Queued; this task has not executed.' : null
  const tip = [
    `Requirement: ${platform}`,
    source ? `Source: ${source}` : null,
    `Host: ${host}`,
    `Observed: ${observedText}`,
    queued,
  ]
    .filter(Boolean)
    .join(' · ')
  return (
    <Tooltip label={tip} withArrow multiline w={280}>
      <Badge size="xs" variant="light" data-testid="placement">
        {line}
      </Badge>
    </Tooltip>
  )
}
