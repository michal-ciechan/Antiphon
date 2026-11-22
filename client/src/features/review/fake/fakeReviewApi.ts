import { ApiError } from '../../../api/client'
import type {
  CreateReviewRunRequest,
  FindingAction,
  FindingEventDto,
  FindingResponseRequest,
  FindingStatus,
  GateDecisionRequest,
  ListResult,
  ModelUsageDto,
  NodeExecutionDto,
  QueueItemDto,
  QueueScope,
  ReviewApi,
  ReviewFindingDto,
  ReviewGateDto,
  ReviewKpisDto,
  ReviewRunDetailDto,
  ReviewRunFilter,
  ReviewRunSummaryDto,
  ReviewStage,
  RunFindingResponse,
  SlaState,
  WorkflowGraphDto,
  WorkflowValidationDto,
} from '../../../api/reviewEngine'
import {
  BOT_LOGIN,
  FAKE_MODELS,
  FAKE_PEOPLE,
  FAKE_TEAM_SLA_MINUTES,
  ID_KIND,
  createSeed,
  makeExec,
  personName,
  seedGuid,
  type ExecFactory,
  type ModelKey,
  type RunMeta,
} from './seed'
import { DEFAULT_WORKFLOW } from './workflowSeed'
import { validateWorkflowGraph } from './validateWorkflow'

/**
 * The in-memory review API. It implements the contract's `ReviewApi` against seeded data and keeps
 * state: responses change finding status and add events that go Pending → Synced; gate decisions
 * move the run (AnotherRound → round + 1 with the steer stored, Approve → awaiting merge, Close →
 * closed); assign and pass change the queue; KPIs are recomputed on every read. Errors are
 * `ApiError`s shaped like the server's Problem Details (404 / 409 / 422).
 *
 * `subscribe` is the fake's stand-in for SignalR `ReviewRunChanged { runId }`: it fires on every
 * change, including the delayed sync flips, so the UI invalidates exactly as it will in HTTP mode.
 */

export interface FakeReviewApiOptions {
  now?: () => number
  /** Artificial latency per call. 0 resolves on the next microtask. */
  latencyMs?: number
  /** Delay before a Pending event becomes Synced. */
  syncDelayMs?: number
  /** Delay between steps when a new round runs. */
  progressStepMs?: number
  /** The acting user (identity comes from auth in the real API). */
  actor?: () => string
  random?: () => number
}

export interface ReviewRunChangedEvent {
  runId: string
}

export interface FakeReviewApi extends ReviewApi {
  subscribe(listener: (event: ReviewRunChangedEvent) => void): () => void
  /** Re-seeds from scratch and cancels pending timers. */
  reset(): void
  /** Cancels pending timers (tests). */
  dispose(): void
}

const OPEN_STATUSES: FindingStatus[] = ['Open', 'WillFix', 'Disputed']
const ACCEPTED_STATUSES: FindingStatus[] = ['Fixed', 'WillFix', 'Accepted']

const STATUS_AFTER: Partial<Record<FindingAction, FindingStatus>> = {
  WillFix: 'WillFix',
  Fixed: 'Fixed',
  Dismiss: 'Dismissed',
  Dispute: 'Disputed',
  Defer: 'Deferred',
  NotABlocker: 'Dismissed',
}

const TEXT_REQUIRED: FindingAction[] = ['Dispute', 'Question', 'NotABlocker', 'Reply']

const PROGRESS: { node: string; stage: ReviewStage }[] = [
  { node: 'intake', stage: 'Intake' },
  { node: 'delta', stage: 'Intake' },
  { node: 'categorise', stage: 'Categorise' },
  { node: 'select', stage: 'Select' },
  { node: 'group', stage: 'Select' },
  { node: 'packs', stage: 'Review' },
]

function problem(status: 404 | 409 | 422, detail: string, errors?: Record<string, string[]>): ApiError {
  const title = status === 404 ? 'Not Found' : status === 409 ? 'Conflict' : 'Unprocessable Entity'
  return new ApiError(status, title, { type: `https://httpstatuses.io/${status}`, title, status, detail, ...(errors ? { errors } : {}) })
}

const clone = <T>(value: T): T => structuredClone(value)

export function createFakeReviewApi(options: FakeReviewApiOptions = {}): FakeReviewApi {
  const now = options.now ?? (() => Date.now())
  const latencyMs = options.latencyMs ?? 250
  const syncDelayMs = options.syncDelayMs ?? 1500
  const progressStepMs = options.progressStepMs ?? 1500
  const actor = options.actor ?? (() => 'asmith')
  const random = options.random ?? Math.random

  let runs = new Map<string, ReviewRunDetailDto>()
  let findings: ReviewFindingDto[] = []
  let meta: Record<string, RunMeta> = {}
  let counter = 1
  const timers = new Set<ReturnType<typeof setTimeout>>()
  const listeners = new Set<(event: ReviewRunChangedEvent) => void>()
  /** "Not me" reasons. The contract's ReviewGateDto only carries `passedBy`. */
  const passReasons = new Map<string, { login: string; reason: string }[]>()

  const nextId = (kind: number) => seedGuid(kind, counter++)
  const factory = (): ExecFactory => ({ nextId: () => nextId(ID_KIND.node), now: now() })
  const isoNow = () => new Date(now()).toISOString()

  function seed() {
    const s = createSeed(now())
    runs = new Map(s.runs.map((r) => [r.id, r]))
    findings = s.findings
    meta = s.meta
    counter = s.nextN
    passReasons.clear()
    for (const r of runs.values()) recompute(r)
  }

  function schedule(fn: () => void, ms: number) {
    const handle = setTimeout(() => {
      timers.delete(handle)
      fn()
    }, ms)
    timers.add(handle)
  }

  function clearTimers() {
    for (const t of timers) clearTimeout(t)
    timers.clear()
  }

  function emit(runId: string) {
    for (const listener of [...listeners]) {
      try {
        listener({ runId })
      } catch {
        // a listener's failure must not break the fake
      }
    }
  }

  async function respond<T>(fn: () => T): Promise<T> {
    if (latencyMs > 0) await new Promise((resolve) => setTimeout(resolve, latencyMs))
    return clone(fn())
  }

  // ---------------------------------------------------------------- derivation

  function findingsOf(runId: string): ReviewFindingDto[] {
    return findings.filter((f) => f.runId === runId)
  }

  function recompute(run: ReviewRunDetailDto) {
    const fs = findingsOf(run.id)
    run.openFindings = fs.filter((f) => OPEN_STATUSES.includes(f.status)).length
    run.openBlockers = fs.filter((f) => f.severity === 'Blocker' && OPEN_STATUSES.includes(f.status)).length
    const models: string[] = []
    for (const n of run.nodes) if (n.model && !models.includes(n.model)) models.push(n.model)
    run.models = models
    run.costUsd = Math.round(run.nodes.reduce((sum, n) => sum + n.costUsd, 0) * 10_000) / 10_000
  }

  function touch(run: ReviewRunDetailDto) {
    run.updatedAt = isoNow()
    recompute(run)
  }

  function summaryOf(run: ReviewRunDetailDto): ReviewRunSummaryDto {
    return {
      id: run.id,
      subject: run.subject,
      status: run.status,
      stage: run.stage,
      round: run.round,
      botOnlyRounds: run.botOnlyRounds,
      needsHumanApproval: run.needsHumanApproval,
      openFindings: run.openFindings,
      openBlockers: run.openBlockers,
      models: run.models,
      costUsd: run.costUsd,
      startedAt: run.startedAt,
      updatedAt: run.updatedAt,
      waitingOn: run.waitingOn,
    }
  }

  function requireRun(runId: string): ReviewRunDetailDto {
    const run = runs.get(runId)
    if (!run) throw problem(404, `No review run ${runId}.`)
    return run
  }

  function requireFinding(findingId: string): ReviewFindingDto {
    const f = findings.find((x) => x.id === findingId)
    if (!f) throw problem(404, `No finding ${findingId}.`)
    return f
  }

  function findGate(gateId: string): { run: ReviewRunDetailDto; gate: ReviewGateDto } {
    for (const run of runs.values()) {
      const gate = run.gates.find((g) => g.id === gateId)
      if (gate) return { run, gate }
    }
    throw problem(404, `No gate ${gateId}.`)
  }

  function currentIteration(run: ReviewRunDetailDto): number {
    return run.nodes.reduce((max, n) => Math.max(max, n.iteration), 0)
  }

  // ---------------------------------------------------------------- findings

  function responseError(f: ReviewFindingDto, r: FindingResponseRequest): string | null {
    if (f.status === 'Outdated') return `${f.key} is outdated: the code it anchored to has changed.`
    if (r.action === 'Dismiss' && (!r.reasonCategory || !r.scope)) return 'A dismissal needs a reason and a scope.'
    if (r.action === 'NotABlocker' && f.severity !== 'Blocker') return '"Not a blocker" applies to blockers only.'
    if (TEXT_REQUIRED.includes(r.action) && !r.text?.trim()) return `${r.action === 'NotABlocker' ? 'Not a blocker' : r.action} needs text.`
    return null
  }

  function applyResponse(f: ReviewFindingDto, r: FindingResponseRequest) {
    const login = actor()
    const next = STATUS_AFTER[r.action]
    if (next) f.status = next
    const event: FindingEventDto = {
      id: nextId(ID_KIND.event),
      action: r.action,
      actorLogin: login,
      actorDisplayName: personName(login),
      reasonCategory: r.reasonCategory ?? (r.action === 'NotABlocker' ? 'FalsePositive' : null),
      scope: r.scope ?? null,
      text: r.text?.trim() || null,
      syncState: f.posted ? 'Pending' : 'NotApplicable',
      createdAt: isoNow(),
    }
    f.events.push(event)
    if (!f.posted) return
    f.syncState = 'Pending'
    schedule(() => {
      event.syncState = 'Synced'
      settleSync(f)
      if (r.action === 'Question') {
        f.events.push({
          id: nextId(ID_KIND.event),
          action: 'Reply',
          actorLogin: BOT_LOGIN,
          actorDisplayName: 'Antiphon',
          reasonCategory: null,
          scope: null,
          text: 'Answered from the pack context: the reviewer saw the same code at the reviewed SHA. (Fake API reply.)',
          syncState: 'Synced',
          createdAt: isoNow(),
        })
      }
      const run = runs.get(f.runId)
      if (run) touch(run)
      emit(f.runId)
    }, syncDelayMs)
  }

  function settleSync(f: ReviewFindingDto) {
    if (!f.posted) return
    f.syncState = f.events.some((e) => e.syncState === 'Failed')
      ? 'Failed'
      : f.events.some((e) => e.syncState === 'Pending')
        ? 'Pending'
        : 'Synced'
  }

  // ---------------------------------------------------------------- rounds

  function exec(nodeId: string, iteration: number, status: 'Running' | 'Waiting' | 'Succeeded' | 'Pending', extra: Parameters<typeof makeExec>[6] = {}): NodeExecutionDto {
    return makeExec(factory(), nodeId, iteration, status, status === 'Pending' ? null : 0, status === 'Succeeded' ? 0 : null, extra)
  }

  function complete(n: NodeExecutionDto, summary: string) {
    n.status = 'Succeeded'
    n.completedAt = isoNow()
    n.summary = summary
  }

  const STEP_SUMMARY: Record<string, string> = {
    intake: 'head pinned · mirror fetched · config read at base',
    delta: 'delta since the last reviewed head',
    categorise: 'unchanged categories',
    select: 'checks re-selected for the new round',
    group: 'groups rebuilt',
    packs: 'packs rebuilt with the steer in the shared prefix',
  }

  const STEP_MODEL: Record<string, { model: ModelKey; inputTokens: number; outputTokens: number }> = {
    categorise: { model: 'jev', inputTokens: 19_400, outputTokens: 300 },
    select: { model: 'jev', inputTokens: 38_000, outputTokens: 1_200 },
  }

  /** Walks a new iteration through intake → packs → the reviewer fan-out, one step per tick. */
  function progress(runId: string, iteration: number, index: number) {
    schedule(() => {
      const run = runs.get(runId)
      if (!run || run.status === 'Closed' || currentIteration(run) !== iteration) return
      const step = PROGRESS[index]
      const live = run.nodes.find((n) => n.iteration === iteration && n.nodeId === step.node)
      if (live) complete(live, STEP_SUMMARY[step.node] ?? '')
      if (run.status === 'Queued') run.status = 'Running'
      const next = PROGRESS[index + 1]
      if (next) {
        run.stage = next.stage
        run.nodes.push(exec(next.node, iteration, 'Running', { ...STEP_MODEL[next.node], summary: 'running' }))
        touch(run)
        emit(runId)
        progress(runId, iteration, index + 1)
        return
      }
      // fan out: every reviewer starts; all but the last finish on the following ticks
      const plan = meta[runId]?.reviewers ?? []
      run.stage = 'Review'
      run.nodes.push(exec('review', iteration, 'Running', { summary: `0/${plan.length} reviewers done · policy all` }))
      const reviewers = plan.map((r) =>
        exec('reviewer', iteration, 'Running', { model: r.model, inputTokens: 57_000, outputTokens: 1_100, summary: `${r.group}: running` }),
      )
      run.nodes.push(...reviewers)
      touch(run)
      emit(runId)
      finishReviewers(runId, iteration, reviewers.map((r) => r.id))
    }, progressStepMs)
  }

  function finishReviewers(runId: string, iteration: number, ids: string[]) {
    if (ids.length <= 1) return
    schedule(() => {
      const run = runs.get(runId)
      if (!run || currentIteration(run) !== iteration) return
      const n = run.nodes.find((x) => x.id === ids[0])
      if (n && n.status === 'Running') {
        const group = n.summary?.split(':')[0] ?? 'group'
        n.outputTokens = 2_600
        n.costUsd = Math.round(((n.inputTokens * FAKE_MODELS[modelKeyOf(n.model)].inPerM + n.outputTokens * FAKE_MODELS[modelKeyOf(n.model)].outPerM) / 1e6) * 10_000) / 10_000
        complete(n, `${group}: 0 findings · every assigned check accounted for`)
        const fan = run.nodes.find((x) => x.iteration === iteration && x.nodeId === 'review')
        const all = run.nodes.filter((x) => x.iteration === iteration && x.nodeId === 'reviewer')
        if (fan) fan.summary = `${all.filter((x) => x.status === 'Succeeded').length}/${all.length} reviewers done · policy all`
      }
      touch(run)
      emit(runId)
      finishReviewers(runId, iteration, ids.slice(1))
    }, progressStepMs)
  }

  function modelKeyOf(model: string | null): ModelKey {
    const hit = (Object.keys(FAKE_MODELS) as ModelKey[]).find((k) => FAKE_MODELS[k].model === model)
    return hit ?? 'sonnet'
  }

  function startRound(run: ReviewRunDetailDto) {
    const iteration = currentIteration(run) + 1
    run.nodes.push(exec('intake', iteration, 'Running', { summary: 'running' }))
    progress(run.id, iteration, 0)
  }

  // ---------------------------------------------------------------- gates

  function gateNodeOf(run: ReviewRunDetailDto): NodeExecutionDto | undefined {
    const it = currentIteration(run)
    return run.nodes.find((n) => n.iteration === it && n.kind === 'Gate' && n.status === 'Waiting')
  }

  function teamMembers(teamKey: string | null): string[] {
    return FAKE_PEOPLE.filter((p) => !p.service && teamKey !== null && p.teams.includes(teamKey)).map((p) => p.login)
  }

  function queueItem(run: ReviewRunDetailDto, gate: ReviewGateDto): QueueItemDto {
    const age = Math.max(0, Math.round((now() - Date.parse(gate.openedAt)) / 60_000))
    const sla = FAKE_TEAM_SLA_MINUTES[gate.teamKey ?? ''] ?? 480
    const ratio = age / sla
    const slaState: SlaState = ratio >= 1 ? 'Breached' : ratio >= 0.75 ? 'Warn' : 'Ok'
    return { gate, run: summaryOf(run), ageWorkingMinutes: age, slaState }
  }

  // ---------------------------------------------------------------- the API

  seed()

  const api: FakeReviewApi = {
    subscribe(listener) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
    reset() {
      clearTimers()
      seed()
      for (const id of runs.keys()) emit(id)
    },
    dispose() {
      clearTimers()
    },

    listRuns: (filter: ReviewRunFilter = {}) =>
      respond((): ListResult<ReviewRunSummaryDto> => {
        const q = filter.q?.trim().toLowerCase()
        const items = [...runs.values()]
          .filter((r) => !filter.status || r.status === filter.status)
          .filter((r) => !filter.stage || r.stage === filter.stage)
          .filter((r) => !filter.repo || r.subject.repo === filter.repo || r.subject.repo.endsWith(`/${filter.repo}`))
          .filter(
            (r) =>
              !q ||
              `${r.subject.repo}#${r.subject.prNumber} ${r.subject.title} ${r.subject.authorLogin} ${r.subject.authorDisplayName ?? ''} ${r.subject.headRef}`
                .toLowerCase()
                .includes(q),
          )
          .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
          .map(summaryOf)
        return { items, total: items.length }
      }),

    getKpis: () =>
      respond((): ReviewKpisDto => {
        const all = [...runs.values()]
        const done = all.filter((r) => r.status === 'Approved' || r.status === 'AwaitingMerge' || r.status === 'Merged')
        const basis = done.length > 0 ? done : all
        const dayAgo = now() - 24 * 60 * 60_000
        const nodes = all.flatMap((r) => r.nodes)
        const costToday = nodes.filter((n) => n.startedAt && Date.parse(n.startedAt) >= dayAgo).reduce((s, n) => s + n.costUsd, 0)
        const totalCost = all.reduce((s, r) => s + r.costUsd, 0)
        const accepted = findings.filter((f) => ACCEPTED_STATUSES.includes(f.status)).length
        const usage = new Map<string, ModelUsageDto>()
        for (const n of nodes) {
          if (!n.model) continue
          const u = usage.get(n.model) ?? { model: n.model, provider: n.provider ?? 'unknown', transport: n.transport ?? 'Http', calls: 0, costUsd: 0 }
          u.calls++
          u.costUsd = Math.round((u.costUsd + n.costUsd) * 10_000) / 10_000
          usage.set(n.model, u)
        }
        return {
          inReview: all.filter((r) => r.status === 'Queued' || r.status === 'Running').length,
          waitingOnHumans: all.filter((r) => r.waitingOn === 'Humans').length,
          averageRounds: Math.round((basis.reduce((s, r) => s + r.round, 0) / Math.max(1, basis.length)) * 10) / 10,
          costTodayUsd: Math.round(costToday * 10_000) / 10_000,
          costPerAcceptedFindingUsd: accepted > 0 ? Math.round((totalCost / accepted) * 10_000) / 10_000 : null,
          modelUsage: [...usage.values()].sort((a, b) => b.costUsd - a.costUsd),
        }
      }),

    createRun: (request: CreateReviewRunRequest) =>
      respond(() => {
        if (!request.repo?.trim()) throw problem(422, 'A repo is required.', { Repo: ['A repo is required.'] })
        const n = counter++
        const id = seedGuid(ID_KIND.run, 0x10000 + n)
        const login = actor()
        const run: ReviewRunDetailDto = {
          id,
          subject: {
            id: seedGuid(ID_KIND.subject, 0x10000 + n),
            host: request.host,
            repo: request.repo.trim(),
            prNumber: request.prNumber ?? null,
            title: request.title?.trim() || (request.prNumber ? `${request.repo}#${request.prNumber}` : `${request.repo} (manual)`),
            url: request.host === 'GitHub' && request.prNumber ? `https://github.com/${request.repo}/pull/${request.prNumber}` : null,
            authorLogin: login,
            authorDisplayName: personName(login),
            authorResolvedBy: null,
            baseRef: 'main',
            headRef: 'manual',
            baseSha: request.baseSha ?? '0000000',
            headSha: request.headSha ?? '0000000',
            owners: [],
          },
          status: 'Queued',
          stage: 'Intake',
          round: 1,
          botOnlyRounds: 0,
          needsHumanApproval: null,
          openFindings: 0,
          openBlockers: 0,
          models: [],
          costUsd: 0,
          startedAt: isoNow(),
          updatedAt: isoNow(),
          waitingOn: 'Bot',
          blastRadius: null,
          categories: [],
          workflowRevisionId: DEFAULT_WORKFLOW.revisionId,
          nodes: [],
          gates: [],
        }
        meta[id] = { teamKey: '', reviewers: [{ group: 'general', model: 'sonnet' }], verifyModel: 'haiku', headSha: run.subject.headSha }
        runs.set(id, run)
        startRound(run)
        emit(id)
        return run
      }),

    getRun: (runId) => respond(() => requireRun(runId)),

    listFindings: (runId, status) =>
      respond((): ListResult<ReviewFindingDto> => {
        requireRun(runId)
        const items = findingsOf(runId)
          .filter((f) => !status || f.status === status)
          .sort((a, b) => Number(a.key.slice(1)) - Number(b.key.slice(1)))
        return { items, total: items.length }
      }),

    refreshRun: (runId) =>
      respond(() => {
        const run = requireRun(runId)
        for (const f of findingsOf(runId)) {
          const failed = f.events.filter((e) => e.syncState === 'Failed')
          if (failed.length === 0) continue
          for (const e of failed) e.syncState = 'Pending'
          settleSync(f)
          schedule(() => {
            for (const e of failed) e.syncState = 'Synced'
            settleSync(f)
            touch(run)
            emit(runId)
          }, syncDelayMs)
        }
        touch(run)
        emit(runId)
        return undefined
      }),

    respondToFinding: (findingId, request) =>
      respond(() => {
        const f = requireFinding(findingId)
        const error = responseError(f, request)
        if (error) throw problem(error.includes('outdated') ? 409 : 422, error, { [f.key]: [error] })
        applyResponse(f, request)
        const run = requireRun(f.runId)
        touch(run)
        emit(run.id)
        return f
      }),

    respondToRun: (runId, responses: RunFindingResponse[]) =>
      respond(() => {
        const run = requireRun(runId)
        if (responses.length === 0) throw problem(422, 'A review needs at least one response.')
        const errors: Record<string, string[]> = {}
        let conflict = false
        const targets = responses.map((r) => {
          const f = findings.find((x) => x.id === r.findingId && x.runId === runId)
          if (!f) {
            errors[r.findingId] = [`Finding ${r.findingId} is not on this run.`]
            return null
          }
          const error = responseError(f, r)
          if (error) {
            errors[f.key] = [error]
            if (error.includes('outdated')) conflict = true
          }
          return f
        })
        const keys = Object.keys(errors)
        if (keys.length > 0) throw problem(conflict ? 409 : 422, `${keys.length} response${keys.length === 1 ? '' : 's'} could not be posted: ${Object.values(errors).flat().join(' ')}`, errors)
        responses.forEach((r, i) => applyResponse(targets[i]!, r))
        touch(run)
        emit(runId)
        return { items: targets.filter((f): f is ReviewFindingDto => !!f) }
      }),

    getQueue: (scope: QueueScope, login?: string) =>
      respond((): ListResult<QueueItemDto> => {
        const who = login ?? actor()
        const teams = FAKE_PEOPLE.find((p) => p.login === who)?.teams ?? []
        const items: QueueItemDto[] = []
        for (const run of runs.values()) {
          for (const gate of run.gates) {
            if (gate.status === 'Done') continue
            const inTeam = gate.teamKey !== null && teams.includes(gate.teamKey)
            const include =
              scope === 'All' ||
              (scope === 'Teams' && inTeam) ||
              (scope === 'Mine' && (gate.assigneeLogin === who || (gate.assigneeLogin === null && inTeam && !gate.passedBy.includes(who))))
            if (include) items.push(queueItem(run, gate))
          }
        }
        const sla = (i: QueueItemDto) => i.ageWorkingMinutes / (FAKE_TEAM_SLA_MINUTES[i.gate.teamKey ?? ''] ?? 480)
        items.sort((a, b) => sla(b) - sla(a))
        return { items, total: items.length }
      }),

    assignGate: (gateId, login) =>
      respond(() => {
        const { run, gate } = findGate(gateId)
        if (gate.status === 'Done') throw problem(409, 'This gate is already decided.')
        const author = run.subject.authorLogin
        let pick = login
        if (pick) {
          if (pick === author) throw problem(409, "Authors can't be assigned their own gate.")
        } else {
          const pool = teamMembers(gate.teamKey).filter((m) => m !== author && !gate.passedBy.includes(m) && m !== gate.assigneeLogin)
          if (pool.length === 0) throw problem(409, `Nobody in ${gate.teamKey ?? 'the team'} is eligible: the author and everyone who passed are excluded.`)
          pick = pool[Math.min(pool.length - 1, Math.floor(random() * pool.length))]
        }
        gate.assigneeLogin = pick
        gate.status = 'Assigned'
        touch(run)
        emit(run.id)
        return gate
      }),

    passGate: (gateId, reason) =>
      respond(() => {
        const { run, gate } = findGate(gateId)
        if (!reason?.trim()) throw problem(422, '"Not me" needs a reason.', { Reason: ['"Not me" needs a reason.'] })
        if (gate.status === 'Done') throw problem(409, 'This gate is already decided.')
        const who = actor()
        if (gate.assigneeLogin && gate.assigneeLogin !== who) throw problem(409, `This gate is assigned to ${gate.assigneeLogin}.`)
        if (!gate.passedBy.includes(who)) gate.passedBy.push(who)
        passReasons.set(gate.id, [...(passReasons.get(gate.id) ?? []), { login: who, reason: reason.trim() }])
        gate.assigneeLogin = null
        gate.status = 'Unassigned'
        touch(run)
        emit(run.id)
        return gate
      }),

    decideGate: (gateId, request: GateDecisionRequest) =>
      respond(() => {
        const { run, gate } = findGate(gateId)
        if (gate.status === 'Done') throw problem(409, 'This gate is already decided.')
        const who = actor()
        if (who === run.subject.authorLogin) throw problem(409, "You're the author: authors can't decide their own gate.")
        if (gate.kind !== 'DisputedFindings' && run.status !== 'AtGate') throw problem(409, "The run hasn't reached this gate yet; the decision opens when it does.")
        const name = personName(who) ?? who
        gate.status = 'Done'
        gate.decision = request.decision
        gate.decidedAt = isoNow()
        gate.assigneeLogin = gate.assigneeLogin ?? who
        gate.steerPrompt = request.decision === 'AnotherRound' ? request.steerPrompt?.trim() || null : null

        if (gate.kind === 'DisputedFindings') {
          for (const f of findingsOf(run.id).filter((x) => x.status === 'Disputed')) {
            f.status = request.decision === 'Approve' ? 'Dismissed' : request.decision === 'AnotherRound' ? 'Open' : f.status
          }
          touch(run)
          emit(run.id)
          return run
        }

        const gateNode = gateNodeOf(run)
        const verb = request.decision === 'AnotherRound' ? 'another-round' : request.decision.toLowerCase()
        if (gateNode) complete(gateNode, `${name} → ${verb}${gate.steerPrompt ? ' with a steer' : ''}`)
        const it = currentIteration(run)
        if (request.decision === 'Approve') {
          run.nodes.push(exec('base-moved', it, 'Succeeded', { summary: 'unchanged' }))
          run.nodes.push(exec('await-merge', it, 'Waiting', { summary: 'holding the approval until merge or close' }))
          run.status = 'AwaitingMerge'
          run.stage = 'BranchMaintenance'
          run.waitingOn = 'Author'
        } else if (request.decision === 'AnotherRound') {
          run.round += 1
          run.botOnlyRounds = 0
          run.status = 'Running'
          run.stage = 'Intake'
          run.waitingOn = 'Bot'
          startRound(run)
        } else {
          run.status = 'Closed'
          run.stage = 'Done'
          run.waitingOn = null
        }
        touch(run)
        emit(run.id)
        return run
      }),

    getDefaultWorkflow: () => respond((): WorkflowGraphDto => DEFAULT_WORKFLOW),

    validateWorkflow: (graph) => respond((): WorkflowValidationDto => validateWorkflowGraph(graph)),
  }

  return api
}
