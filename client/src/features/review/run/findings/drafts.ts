import type {
  DismissScope,
  FindingAction,
  ReasonCategory,
  ReviewFindingDto,
  ReviewSubjectDto,
  RunFindingResponse,
} from '../../../../api/reviewEngine'
import { ACTION_META, COMPOSER_ACTIONS, REASON_META, SCOPE_META } from '../../reviewMeta'

/** One drafted reply. Drafts are posted together as one review (one code-host notification). */
export interface Draft {
  action: FindingAction
  reasonCategory?: ReasonCategory
  scope?: DismissScope
  text: string
  /** A path/repo/team dismissal must be confirmed in the wide-scope step before it can post. */
  wideConfirmed?: boolean
  /** Optional steer for the agent that drafts the learn rule (UI only in this slice). */
  steer?: string
}

export type Drafts = Record<string, Draft | undefined>

/** Reasons that clear a blocker (design §11.6): a clear closes it as one of these. */
export const BLOCKER_CLEAR_REASONS: ReasonCategory[] = ['FalsePositive', 'ByDesign', 'AcceptedRisk']

/** A blocker offers "Not a blocker" instead of Dismiss; everything else offers Dismiss. */
export function actionsFor(f: ReviewFindingDto): FindingAction[] {
  return COMPOSER_ACTIONS.filter((a) => (f.severity === 'Blocker' ? a !== 'Dismiss' : a !== 'NotABlocker'))
}

export function isWideScope(scope: DismissScope | undefined): boolean {
  return !!scope && scope !== 'Instance'
}

/** Why a draft cannot be posted yet, or null when it can. */
export function draftProblem(d: Draft): string | null {
  if (d.action === 'Dismiss') {
    if (!d.reasonCategory) return 'pick a reason'
    if (!d.scope) return 'pick a scope'
    if (isWideScope(d.scope) && !d.wideConfirmed) return 'confirm the wider scope'
  }
  if (d.action === 'NotABlocker' && !d.reasonCategory) return 'pick a reason'
  if (ACTION_META[d.action].needsText && !d.text.trim()) return 'add text'
  return null
}

export function replyPreview(d: Draft, displayName: string): string {
  let head = ACTION_META[d.action].verb
  if (d.action === 'Dismiss') {
    const reason = d.reasonCategory ? REASON_META[d.reasonCategory].label.toLowerCase() : '(pick a reason)'
    const scope = d.scope ? SCOPE_META[d.scope].label.toLowerCase() : '(pick a scope)'
    head = `Dismissed: ${reason} · scope: ${scope}.`
  }
  if (d.action === 'NotABlocker' && d.reasonCategory) head = `Not a blocker (${REASON_META[d.reasonCategory].label.toLowerCase()}):`
  const body = d.text.trim()
  return `${head}${head && body ? ' ' : ''}${body}\n\n— ${displayName} via Antiphon`
}

export function toResponses(drafts: Drafts): RunFindingResponse[] {
  return Object.entries(drafts).flatMap(([findingId, d]) =>
    d
      ? [
          {
            findingId,
            action: d.action,
            ...(d.reasonCategory && (d.action === 'Dismiss' || d.action === 'NotABlocker') ? { reasonCategory: d.reasonCategory } : {}),
            ...(d.scope && d.action === 'Dismiss' ? { scope: d.scope } : {}),
            ...(d.text.trim() ? { text: d.text.trim() } : {}),
          },
        ]
      : [],
  )
}

const SUPPRESSING: ReasonCategory[] = ['FalsePositive', 'ByDesign', 'OutOfScope', 'Duplicate']

/**
 * The rule a wide-scope dismissal would create (design §11.7), previewed before confirming. The
 * learn API is not in this slice, so this is computed in the client from the finding alone.
 */
export function ruleFor(f: ReviewFindingDto, subject: ReviewSubjectDto, reason: ReasonCategory, scope: DismissScope, text: string, actor: string, steer?: string) {
  const suppress = SUPPRESSING.includes(reason)
  const dir = `${f.filePath.split('/').slice(0, -1).join('/') || '.'}/**`
  const repo = subject.repo.split('/').pop() ?? subject.repo
  // a team layer belongs to a team: the first team among the code owners (users have no layer)
  const team = subject.owners.find((o) => o.kind === 'Team')?.name ?? 'owning-team'
  const check = f.checkId ?? `unprompted:${f.key}`
  const target = scope === 'Team' ? `${team} team layer` : `${repo} repo layer · .antiphon/review/ in ${subject.repo}`
  const file =
    scope === 'Team'
      ? suppress
        ? `${team}/suppressions.yaml`
        : `${team}/checks/${check}.notes.md`
      : suppress
        ? '.antiphon/review/suppressions.yaml'
        : '.antiphon/review/context/notes.md'
  const note = text.trim() || f.title
  const source = `${repo}#${subject.prNumber ?? '?'} · ${f.key} · thread (${actor})`
  const lines = suppress
    ? [`- check: ${check}`, ...(scope === 'Path' ? [`  paths: ["${dir}"]`] : []), `  reason: ${reason}`, `  note: "${note}"`, ...(steer?.trim() ? [`  when: "${steer.trim()}"`] : []), `  source: ${source}`]
    : [`## ${check} · ${f.title}`, ...(scope === 'Path' ? [`Applies to ${dir} only.`] : []), note, ...(steer?.trim() ? [`Scope, from the steer: ${steer.trim()}`] : []), `<!-- source: ${source} -->`]
  return { kind: suppress ? 'suppression' : 'context note', target, file, dir, lines, team }
}
