import type { FindingStatus, ReviewFindingDto } from '../../../../api/reviewEngine'
import { SEVERITY_META } from '../../reviewMeta'

const OPEN: FindingStatus[] = ['Open', 'Disputed']
const TRACKING: FindingStatus[] = ['WillFix', 'Deferred']

export interface FindingGroup {
  title: string
  items: ReviewFindingDto[]
}

/**
 * Open (posted) · Tracking (will fix, deferred, or a reply still syncing) · Not posted inline · Closed;
 * worst severity first. `hostless` (a review with no pull request, e.g. a local commit range) posts
 * nothing anywhere, so "not posted inline" means nothing there and every finding is grouped as shown.
 */
export function groupFindings(findings: ReviewFindingDto[], hostless = false): FindingGroup[] {
  const bySeverity = (a: ReviewFindingDto, b: ReviewFindingDto) =>
    SEVERITY_META[b.severity].rank - SEVERITY_META[a.severity].rank || Number(a.key.slice(1)) - Number(b.key.slice(1))
  // a reply still on its way to the code host (or failed) needs watching whatever the status
  const syncing = (f: ReviewFindingDto) => f.syncState === 'Pending' || f.syncState === 'Failed'
  const shown = (f: ReviewFindingDto) => f.posted || hostless
  const open = findings.filter((f) => shown(f) && OPEN.includes(f.status) && !syncing(f)).sort(bySeverity)
  const tracking = findings.filter((f) => shown(f) && (TRACKING.includes(f.status) || syncing(f))).sort(bySeverity)
  const unposted = findings.filter((f) => !shown(f) && [...OPEN, ...TRACKING].includes(f.status)).sort(bySeverity)
  const closed = findings.filter((f) => !OPEN.includes(f.status) && !TRACKING.includes(f.status) && !syncing(f)).sort(bySeverity)
  return [
    { title: 'Open', items: open },
    { title: 'Tracking', items: tracking },
    { title: 'Not posted inline', items: unposted },
    { title: 'Closed', items: closed },
  ].filter((g) => g.items.length > 0)
}
