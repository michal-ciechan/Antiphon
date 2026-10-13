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
