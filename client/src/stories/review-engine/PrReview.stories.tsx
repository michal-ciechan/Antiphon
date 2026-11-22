import type { Meta, StoryObj } from '@storybook/react'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import {
  Alert,
  Avatar,
  Badge,
  Box,
  Button,
  Chip,
  Code,
  Collapse,
  Divider,
  Group,
  Kbd,
  Loader,
  Menu,
  Modal,
  Paper,
  Progress,
  Radio,
  ScrollArea,
  SegmentedControl,
  Stack,
  Text,
  Table,
  Textarea,
  Tooltip,
  UnstyledButton,
} from '@mantine/core'
import {
  TbAlertTriangle,
  TbChevronDown,
  TbChevronRight,
  TbCircleCheckFilled,
  TbCircleXFilled,
  TbClockHour4,
  TbCloudUpload,
  TbFileCode,
  TbGitBranch,
  TbGitPullRequest,
  TbLock,
  TbInfoCircle,
  TbKeyboard,
  TbLink,
  TbRefresh,
  TbRobot,
  TbSend,
  TbShieldOff,
  TbSparkles,
} from 'react-icons/tb'
import {
  ACTION_META,
  BOT_HANDLE,
  BYPASS_CATEGORIES,
  CODE_OWNERS,
  FINDINGS,
  PEOPLE,
  PR612,
  PR612_ROUNDS,
  REASON_META,
  ROUND_POLICY,
  SCOPE_META,
  SEVERITIES,
  SEVERITY_META,
  blastTrips,
  personName,
  thresholdFor,
  trustOf,
  type DiffLine,
  type DismissReason,
  type DismissScope,
  type Finding,
  type Person,
  type ResponseAction,
} from './mockData'
import { BlastPanel, FlagBadges, OwnersLine, PersonChip, SectionLabel, SeverityBadge, StageChip, TrustBadge } from './shared'

/**
 * PROPOSAL MOCK — not production code. The Antiphon review UI for answering bot findings on one
 * PR (`docs/features/012-review-engine/design.md` §11.2, §6 comment workflow, §5 rounds,
 * §4.9–4.11 posting/approval/statuses). Three layouts over the same data (payments-api#612,
 * round 3, human-review gate open after 3 bot-only rounds):
 *   A — split view: findings list + detail + context rail
 *   B — GitHub-like: thread cards inline over diff hunks, "finish your review" box
 *   C — triage deck: one finding at a time, keyboard-first, the gate is the last card
 * A "viewing as" toggle switches between the author (Alice, trust 82), the assigned gate reviewer
 * (Hugo, trust 91) and another team member (Kenji, trust 41). Blockers: the bot never approves with
 * one open; "Not a blocker" clears it alone at trust ≥ 60, below that it needs a second trusted
 * reviewer. Authors can't decide their own gate (solo-owner exception shown separately). Dismissing at
 * path, repo or team scope opens a confirmation with the exact rule, an optional agent steer, and the
 * learn PR it opens (two-way linked with the finding and its thread).
 */

// ---------------------------------------------------------------- shared model

type Role = 'author' | 'reviewer' | 'member'
const VIEWERS: Record<Role, Person> = { author: PEOPLE.asmith, reviewer: PEOPLE.hmartin, member: PEOPLE.ksato }

const REPO = 'payments-api'
const OWNERS_REFUNDS = CODE_OWNERS['payments-api:/src/Refunds/**']
const OWNERS_DB = CODE_OWNERS['payments-api:/db/**']
const OWNERS_SOLO = CODE_OWNERS['payments-api:/tools/refund-replay/**']
const CLEAR_AT = thresholdFor('clear-blocker', 'payments').value
const WIDE_AT = thresholdFor('dismiss-wide', 'payments').value
const GATE_AT = thresholdFor('decide-gate', 'payments').value
const BYPASS_ON = BYPASS_CATEGORIES.filter((c) => c.on).map((c) => c.key)
const BYPASS_OFF = BYPASS_CATEGORIES.filter((c) => !c.on).map((c) => c.key)
const bypasses = (f: Finding) => f.flags.some((x) => BYPASS_ON.includes(x))
const LEARN_PR = 4821
const SECOND_Q = 'Q-5523'

/** The learn PR (or queued proposal) a wide-scope dismissal produced; linked both ways with the finding and its thread. */
interface Learn {
  pr?: number
  queued?: 'team' | 'proposal'
  steer?: string
  rule: string[]
}

interface Draft {
  action: ResponseAction
  reason?: DismissReason
  scope?: DismissScope
  text: string
  learn?: Learn
}
type Drafts = Record<string, Draft | undefined>

const SEED: Record<Role, Drafts> = {
  author: {
    F1: { action: 'will-fix', text: 'Scoping by merchant id, and bumping the cache prefix to v2 so old keys cannot collide.' },
    F3: { action: 'dispute', text: 'RefundService runs inside the payment\'s serializable transaction (UnitOfWorkMiddleware), so the second refund blocks on the row lock until the first commits.' },
    F5: { action: 'dismiss', reason: 'fix-later', scope: 'instance', text: 'The OpenAPI bump goes in the release PR with the changelog (#614).' },
    F6: { action: 'will-fix', text: '' },
  },
  reviewer: {
    F8: { action: 'dismiss', reason: 'style-preference', scope: 'repo', text: 'payments-api has not adopted TimeProvider yet; do not flag this here until it does.' },
    F3: { action: 'question', text: 'Alice says the UoW transaction is serializable. Can you confirm from the pack whether RefundReconciler also goes through UnitOfWorkMiddleware?' },
  },
  member: {
    F1: { action: 'not-blocker', text: 'IDistributedCache here is the per-merchant partitioned cache from RefundCacheFactory, so keys from two merchants never meet.' },
  },
}

function trustOfViewer(who: Person): number {
  return trustOf(who.login, REPO) ?? 0
}

/** Open blockers, and what the viewer's drafted "Not a blocker" replies would do to them. */
function blockerState(drafts: Drafts, who: Person) {
  const open = OPEN.filter((f) => f.severity === 'blocker' && f.status === 'open')
  const trust = trustOfViewer(who)
  const clearing = open.filter((f) => drafts[f.id]?.action === 'not-blocker' && draftValid(drafts[f.id]!))
  return { open, trust, cleared: trust >= CLEAR_AT ? clearing : [], second: trust >= CLEAR_AT ? [] : clearing }
}

type GateDecision = 'approve' | 'another-round' | 'close'

const CURRENT_BAR = ROUND_POLICY[2]
const OPEN = FINDINGS.filter((f) => !f.belowBar && f.status !== 'dismissed')

function learnLine(l: Learn): string {
  if (l.pr) return `Learn PR: lumen/${REPO}#${l.pr}`
  return l.queued === 'team' ? 'Learn proposal queued for the payments-core layer (team scope: waits for 2 repos).' : 'Learn proposal sent to the payments-api maintainers.'
}

function replyPreview(d: Draft, who: Person): string {
  const m = ACTION_META[d.action]
  let head = m.verb
  if (d.action === 'dismiss') head = `Dismissed: ${d.reason ? REASON_META[d.reason].label.toLowerCase() : '(pick a reason)'} · scope: ${d.scope ? SCOPE_META[d.scope].label.toLowerCase() : '(pick a scope)'}.`
  let tail = ''
  if (d.action === 'not-blocker') {
    const t = trustOfViewer(who)
    tail = t >= CLEAR_AT ? `\n(Trust ${t} · ${REPO} ≥ ${CLEAR_AT}: blocker cleared.)` : `\n(Trust ${t} · ${REPO} < ${CLEAR_AT}: needs a second trusted reviewer, ${SECOND_Q}.)`
  }
  if (d.learn) tail += `\n${learnLine(d.learn)}`
  return `${head}${d.text ? ` ${d.text}` : ''}${tail}\n\n— ${who.name} via Antiphon`
}

function draftValid(d: Draft): boolean {
  if (d.action === 'dismiss') return !!d.reason && !!d.scope && (d.scope === 'instance' || !!d.learn)
  if (d.action === 'dispute' || d.action === 'question' || d.action === 'not-blocker') return d.text.trim().length > 0
  return true
}

/** Actions offered for a finding: "Not a blocker" only on blockers. */
function actionsFor(f: Finding): ResponseAction[] {
  return (Object.keys(ACTION_META) as ResponseAction[]).filter((a) => a !== 'not-blocker' || f.severity === 'blocker')
}

function useDrafts(role: Role) {
  const [drafts, setDrafts] = useState<Drafts>(() => ({ ...SEED[role] }))
  const set = useCallback((id: string, d: Draft | undefined) => setDrafts((prev) => ({ ...prev, [id]: d })), [])
  return [drafts, set] as const
}

// ---------------------------------------------------------------- shared pieces

function ViewerToggle({ role, onChange }: { role: Role; onChange: (r: Role) => void }) {
  return (
    <Group gap={6}>
      <Text size="xs" c="dimmed">
        Viewing as
      </Text>
      <SegmentedControl
        size="xs"
        value={role}
        onChange={(v) => onChange(v as Role)}
        data={[
          { value: 'author', label: 'Alice Smith · author' },
          { value: 'reviewer', label: 'Hugo Martin · gate reviewer' },
          { value: 'member', label: 'Kenji Sato · member' },
        ]}
      />
      <TrustBadge login={VIEWERS[role].login} repo={REPO} />
    </Group>
  )
}

function StatusLine() {
  return (
    <Group gap="md" wrap="nowrap">
      <Tooltip label="Commit status on 3b81f0d, links to this page">
        <Group gap={4} wrap="nowrap">
          <TbCircleXFilled size={13} color="var(--mantine-color-danger-5)" />
          <Text size="xs" ff="monospace">
            antiphon/review
          </Text>
          <Text size="xs" c="dimmed" truncate>
            6 open (1 blocker) · waiting on human review (payments-core)
          </Text>
        </Group>
      </Tooltip>
      <Tooltip label="Required by branch protection. Success only after bot or human approval; records the SHA it covers.">
        <Group gap={4} wrap="nowrap">
          <TbClockHour4 size={13} color="var(--mantine-color-warning-5)" />
          <Text size="xs" ff="monospace">
            antiphon/approval
          </Text>
          <Text size="xs" c="dimmed">
            pending · required
          </Text>
        </Group>
      </Tooltip>
    </Group>
  )
}

function PrTitle({ compact = false }: { compact?: boolean }) {
  return (
    <Box style={{ minWidth: 0 }}>
      <Group gap={8} wrap="nowrap">
        <Text size="xs" ff="monospace" c="dimmed">
          lumen/payments-api#612
        </Text>
        <StageChip stage="human-gate" size="xs" detail={compact ? undefined : 'human-review · bot-round limit 3/3 · Hugo Martin'} />
      </Group>
      <Text fw={700} size={compact ? 'md' : 'lg'} truncate>
        {PR612.title}
      </Text>
      {!compact && (
        <Group gap={10} mt={2} wrap="nowrap">
          <PersonChip login="asmith" size="xs" trustRepo={REPO} />
          <Group gap={4} wrap="nowrap">
            <TbGitBranch size={12} />
            <Text size="xs" ff="monospace" c="dimmed">
              {PR612.branch} → main @ {PR612.headSha}
            </Text>
          </Group>
          <Text size="xs" ff="monospace" c="dimmed">
            +{PR612.changed.add} −{PR612.changed.del} · {PR612.changed.files} files
          </Text>
        </Group>
      )}
    </Box>
  )
}

/** R1 → R2 → R3 (current) with posted / below-bar counts, and the bot-only counter. */
function RoundStepper() {
  return (
    <Group gap={4} wrap="nowrap">
      {PR612_ROUNDS.map((r, i) => {
        const current = i === PR612_ROUNDS.length - 1
        return (
          <Group key={r.round} gap={4} wrap="nowrap">
            <Tooltip label={`Round ${r.round} at ${r.sha} · started ${r.started} · ${r.posted} posted, ${r.belowBar} stored below the bar`}>
              <Paper
                px={6}
                py={2}
                radius="sm"
                withBorder
                style={{ borderColor: current ? 'var(--mantine-color-active-5)' : undefined, background: current ? 'var(--mantine-color-active-9)' : undefined }}
              >
                <Text size="xs" fw={700}>
                  R{r.round}{' '}
                  <Text span size="xs" c="dimmed" ff="monospace" fw={400}>
                    {r.sha} · {r.posted}↑ {r.belowBar}↓
                  </Text>
                </Text>
              </Paper>
            </Tooltip>
            {i < PR612_ROUNDS.length - 1 && <TbChevronRight size={12} style={{ opacity: 0.5 }} />}
          </Group>
        )
      })}
      <Tooltip label="The bot-only counter resets on any human involvement: a review, a thread comment, resolving or dismissing a finding, or a gate decision. Author pushes don't reset it.">
        <Badge color="pink" variant="light" size="sm" ml={6}>
          bot-only 3/3 → human gate
        </Badge>
      </Tooltip>
    </Group>
  )
}

/** The current posting bar: severity scale with the cut marked, plus bypass categories. */
function PostingBar({ dense = false }: { dense?: boolean }) {
  const cut = SEVERITY_META[CURRENT_BAR.minSeverity].rank
  return (
    <Tooltip
      multiline
      w={340}
      label={
        <Stack gap={2}>
          {ROUND_POLICY.map((p) => (
            <Text key={p.round} size="xs">
              Round {p.round === 3 ? '3+' : p.round}: {p.minSeverity}+ · confidence ≥ {p.minConfidence.toFixed(2)} {p.note && `(${p.note})`}
            </Text>
          ))}
          <Text size="xs">
            Bypass ({BYPASS_ON.join(', ')}) posts in any round; the adjudicator can't concede it alone.{BYPASS_OFF.length > 0 && ` Off: ${BYPASS_OFF.join(', ')}.`}
          </Text>
        </Stack>
      }
    >
      <Group gap={6} wrap="nowrap">
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
          {dense ? 'Bar' : 'Round 3 posting bar'}
        </Text>
        <Group gap={1} wrap="nowrap">
          {SEVERITIES.map((s) => (
            <Box
              key={s}
              px={5}
              py={1}
              style={{
                borderRadius: 2,
                background: SEVERITY_META[s].rank >= cut ? `var(--mantine-color-${SEVERITY_META[s].color}-8)` : 'var(--mantine-color-dark-5)',
                opacity: SEVERITY_META[s].rank >= cut ? 1 : 0.55,
                borderLeft: SEVERITY_META[s].rank === cut ? '2px solid white' : undefined,
              }}
            >
              <Text size="10px" fw={600} c={SEVERITY_META[s].rank >= cut ? 'white' : 'dimmed'}>
                {s}
              </Text>
            </Box>
          ))}
        </Group>
        <Text size="xs" ff="monospace" style={{ whiteSpace: 'nowrap' }}>
          p ≥ {CURRENT_BAR.minConfidence.toFixed(2)}
        </Text>
        {!dense && (
          <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
            · bypass: <Text span c="red.4" size="xs">security</Text>, <Text span c="grape.4" size="xs">data-loss</Text> · nits: R1 summary only
          </Text>
        )}
      </Group>
    </Tooltip>
  )
}

function HunkView({ hunk, anchor, maxH }: { hunk: DiffLine[]; anchor?: string; maxH?: number }) {
  return (
    <ScrollArea.Autosize mah={maxH ?? 400} type="auto">
      <Box ff="monospace" fz={12} style={{ borderRadius: 4, border: '1px solid var(--mantine-color-dark-4)', overflow: 'hidden' }}>
        {hunk.map((l, i) => {
          const isAnchor = anchor !== undefined && l.code.trim() === anchor.trim()
          const bg = l.t === '+' ? 'rgba(46,160,67,0.15)' : l.t === '-' ? 'rgba(248,81,73,0.15)' : undefined
          return (
            <Group
              key={i}
              gap={0}
              wrap="nowrap"
              style={{
                background: bg,
                boxShadow: isAnchor ? 'inset 3px 0 0 var(--mantine-color-active-4)' : undefined,
                outline: isAnchor ? '1px solid var(--mantine-color-active-7)' : undefined,
              }}
            >
              <Box w={44} ta="right" pr={8} c="dimmed" style={{ flexShrink: 0, userSelect: 'none' }}>
                {l.t === '-' ? '' : l.n}
              </Box>
              <Box w={14} c={l.t === '+' ? 'success.4' : l.t === '-' ? 'danger.4' : 'dimmed'} style={{ flexShrink: 0 }}>
                {l.t === ' ' ? '' : l.t}
              </Box>
              <Box style={{ whiteSpace: 'pre', flex: 1 }}>{l.code || ' '}</Box>
              {isAnchor && (
                <Badge size="xs" variant="filled" color="active" mr={6} style={{ flexShrink: 0 }}>
                  anchor
                </Badge>
              )}
            </Group>
          )
        })}
      </Box>
    </ScrollArea.Autosize>
  )
}

function SuggestionView({ f }: { f: Finding }) {
  if (!f.suggestion) return null
  return (
    <Box>
      <Text size="xs" c="dimmed" mb={2}>
        Suggested change
      </Text>
      <Box ff="monospace" fz={12} style={{ borderRadius: 4, border: '1px solid var(--mantine-color-dark-4)' }}>
        <Box px={8} style={{ background: 'rgba(248,81,73,0.15)', whiteSpace: 'pre' }}>
          − {f.anchor.trim()}
        </Box>
        {f.suggestion.split('\n').map((l, i) => (
          <Box key={i} px={8} style={{ background: 'rgba(46,160,67,0.15)', whiteSpace: 'pre' }}>
            + {l}
          </Box>
        ))}
      </Box>
    </Box>
  )
}

function WhyCheck({ f, defaultOpen = true }: { f: Finding; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  const text =
    f.humansWhy ??
    (f.check === 'none'
      ? 'Unprompted: no check produced this; the reviewer raised it on its own. If it is accepted, the learn stage may seed a candidate check from it.'
      : f.check === 'human'
        ? `Raised by ${personName(f.humanRaisedBy)}. Tracked by rechecks like a bot finding; it counts as a "missed" learn signal for the bot.`
        : '')
  return (
    <Paper p={8} radius="sm" bg="dark.6" style={{ borderLeft: '3px solid var(--mantine-color-indigo-5)' }}>
      <UnstyledButton onClick={() => setOpen((o) => !o)} w="100%">
        <Group gap={6} justify="space-between" wrap="nowrap" align="flex-start">
          <Group gap={6} wrap="wrap" style={{ minWidth: 0 }}>
            <Group gap={6} wrap="nowrap">
              <TbInfoCircle size={14} color="var(--mantine-color-indigo-4)" />
              <Text size="xs" fw={600} style={{ whiteSpace: 'nowrap' }}>
                Why this check exists
              </Text>
            </Group>
            <Text size="xs" ff="monospace" c="dimmed">
              {f.check !== 'none' && f.check !== 'human' ? `${f.check} · ${f.checkTitle}` : f.checkTitle}
            </Text>
          </Group>
          <Group gap={6} wrap="nowrap" style={{ flexShrink: 0 }}>
            {f.checkPrecision !== undefined && (
              <Tooltip label="Share of this check's posted findings that were accepted, payments-api, last 30 days">
                <Text size="xs" style={{ whiteSpace: 'nowrap' }} c={f.checkPrecision >= 0.7 ? 'success.4' : f.checkPrecision >= 0.5 ? 'yellow.4' : 'danger.4'}>
                  precision {Math.round(f.checkPrecision * 100)}%
                </Text>
              </Tooltip>
            )}
            {open ? <TbChevronDown size={12} /> : <TbChevronRight size={12} />}
          </Group>
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Text size="xs" mt={4} c="gray.4">
          {text}
        </Text>
      </Collapse>
    </Paper>
  )
}

function ThreadView({ f, max }: { f: Finding; max?: number }) {
  const entries = max ? f.thread.slice(-max) : f.thread
  if (entries.length === 0)
    return (
      <Text size="xs" c="dimmed" fs="italic">
        {f.inline ? 'No replies yet.' : 'Not posted inline: listed in the summary comment only (round-1 nit).'}
      </Text>
    )
  return (
    <Stack gap={6}>
      {max && f.thread.length > max && (
        <Text size="xs" c="dimmed">
          {f.thread.length - max} earlier
        </Text>
      )}
      {entries.map((t, i) => (
        <Group key={i} gap={8} align="flex-start" wrap="nowrap">
          <Avatar size={20} radius="xl" color={t.kind === 'bot' ? 'cyan' : 'active'}>
            {t.kind === 'bot' ? <TbRobot size={12} /> : <Text size="9px">{personName(t.who).split(' ').map((s) => s[0]).join('')}</Text>}
          </Avatar>
          <Box style={{ flex: 1, minWidth: 0 }}>
            <Group gap={6}>
              <Text size="xs" fw={600}>
                {t.kind === 'bot' ? t.who : personName(t.who)}
              </Text>
              {t.kind === 'human' && <TrustBadge login={t.who} repo={REPO} showScope={false} />}
              {t.via && (
                <Badge size="xs" variant="outline" color="gray">
                  via Antiphon
                </Badge>
              )}
              <Text size="xs" c="dimmed">
                {t.at}
              </Text>
            </Group>
            <Text size="xs">{t.body}</Text>
          </Box>
        </Group>
      ))}
    </Stack>
  )
}

function PendingSync({ f }: { f: Finding }) {
  if (!f.sync) return null
  return (
    <Alert color="warning" variant="light" p={8} icon={<TbCloudUpload size={16} />} title={<Text size="xs" fw={700}>Pending sync: saved in Antiphon, not yet on GitHub</Text>}>
      <Text size="xs">
        Reply "{f.sync.pendingReply}" failed: {f.sync.error}. Retry {f.sync.attempt}/{f.sync.max} {f.sync.next}. The finding stays open here until GitHub confirms the
        reply and the thread resolves. It is never closed in Antiphon alone.
      </Text>
      <Group gap={6} mt={6}>
        <Button size="compact-xs" variant="light" color="warning" leftSection={<TbRefresh size={12} />}>
          Retry now
        </Button>
        <Button size="compact-xs" variant="subtle" color="gray">
          Edit reply
        </Button>
      </Group>
    </Alert>
  )
}

function BypassBadge({ f }: { f: Finding }) {
  if (!bypasses(f)) return null
  return (
    <Tooltip multiline w={300} label={`${f.flags.filter((x) => BYPASS_ON.includes(x)).join(', ')} is a bypass category (Trust settings → bypass categories): it posts in every round regardless of the round's severity and confidence bar, and the adjudicator can't concede it alone.`}>
      <Badge size="xs" color="red" variant="light" leftSection={<TbShieldOff size={10} />} tt="none">
        bypasses round bar
      </Badge>
    </Tooltip>
  )
}

function LearnBadge({ learn }: { learn?: Learn }) {
  if (!learn) return null
  return (
    <Tooltip label={learn.pr ? `Learn PR lumen/${REPO}#${learn.pr} · from ${REPO}#612 · F-thread` : learnLine(learn)}>
      <Badge size="xs" color="cyan" variant="light" leftSection={<TbGitPullRequest size={10} />} tt="none">
        {learn.pr ? `Learn PR #${learn.pr}` : learn.queued === 'team' ? 'learn proposal queued (1 of 2 repos)' : 'learn proposal to maintainers'}
      </Badge>
    </Tooltip>
  )
}

function FindingMeta({ f, draft }: { f: Finding; draft?: Draft }) {
  return (
    <Group gap={6} wrap="wrap">
      <SeverityBadge severity={f.severity} />
      <FlagBadges flags={f.flags} />
      <BypassBadge f={f} />
      <LearnBadge learn={draft?.learn} />
      <Tooltip label="Reviewer confidence (the posting bar compares it with the round's minimum)">
        <Text size="xs" ff="monospace" c={f.confidence >= CURRENT_BAR.minConfidence ? 'gray.3' : 'dimmed'}>
          p {f.confidence.toFixed(2)}
        </Text>
      </Tooltip>
      <Text size="xs" c="dimmed">
        · R{f.round} · {f.reviewer}
      </Text>
      {f.status === 'will-fix' && (
        <Badge size="xs" color="active" variant="dot">
          will fix (tracked)
        </Badge>
      )}
      {f.status === 'pending-sync' && (
        <Badge size="xs" color="warning" variant="dot">
          pending sync
        </Badge>
      )}
      {!f.inline && !f.belowBar && (
        <Badge size="xs" color="gray" variant="outline">
          summary only
        </Badge>
      )}
      {f.belowBar && (
        <Badge size="xs" color="gray" variant="outline">
          below bar · not posted
        </Badge>
      )}
    </Group>
  )
}

/** The acting user's trust for "Not a blocker", and what happens when they post it. */
function NotBlockerPanel({ who }: { who: Person }) {
  const t = trustOfViewer(who)
  const ok = t >= CLEAR_AT
  return (
    <Paper p={6} radius="sm" bg="dark.6" style={{ borderLeft: `3px solid var(--mantine-color-${ok ? 'success' : 'orange'}-6)` }}>
      <Group gap={6} mb={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          Acting as
        </Text>
        <PersonChip login={who.login} size="xs" />
        <TrustBadge login={who.login} repo={REPO} />
        <Text size="xs" c="dimmed">
          · clear-blocker threshold {CLEAR_AT}
        </Text>
      </Group>
      {ok ? (
        <Text size="xs" c="success.4">
          At or above the threshold: the blocker is cleared when you post. The bot can approve once everything else is clean (no other open findings, all checks
          accounted for, CI green).
        </Text>
      ) : (
        <>
          <Text size="xs" c="orange.4" fw={700}>
            Needs a second trusted reviewer
          </Text>
          <Text size="xs" c="orange.3">
            Trust {t} is below {CLEAR_AT}. Posting creates queue item {SECOND_Q} for code owners with trust ≥ {CLEAR_AT} on {REPO}. The finding stays a blocker, and bot
            approval stays blocked, until one of them agrees.
          </Text>
        </>
      )}
      <Text size="xs" c="dimmed" mt={4}>
        The reason is required and posts to the thread. Why-missed investigations search cleared blockers; one later linked to a bug counts against the person who cleared it.
      </Text>
    </Paper>
  )
}

/** "Bot approval blocked: 1 open blocker", plus what the viewer's drafted "Not a blocker" replies would change. */
function BotApprovalLine({ drafts, who }: { drafts: Drafts; who: Person }) {
  const b = blockerState(drafts, who)
  if (b.open.length === 0)
    return (
      <Text size="xs" c="success.4">
        Bot approval: no open blockers.
      </Text>
    )
  return (
    <Alert color="danger" variant="light" p={6} mb={6} icon={<TbLock size={14} />} title={<Text size="xs" fw={700}>Bot approval blocked: {b.open.length} open blocker{b.open.length === 1 ? '' : 's'}</Text>}>
      <Text size="xs">
        {b.open.map((f) => f.id).join(', ')} · the bot never approves while a blocker is open. A human approve here records it as accepted risk.
      </Text>
      {b.cleared.length > 0 && (
        <Text size="xs" c="success.4" mt={2}>
          Your "Not a blocker" on {b.cleared.map((f) => f.id).join(', ')} clears it when you post (Trust {b.trust} ≥ {CLEAR_AT}); the bot can approve once everything else is clean.
        </Text>
      )}
      {b.second.length > 0 && (
        <Text size="xs" c="orange.4" mt={2}>
          Your "Not a blocker" on {b.second.map((f) => f.id).join(', ')} needs a second trusted reviewer (Trust {b.trust} &lt; {CLEAR_AT}): {SECOND_Q} opens when you post.
        </Text>
      )}
    </Alert>
  )
}

type WideStep = 'preview' | 'drafting' | 'drafted' | 'done'

function ruleFor(f: Finding, reason: DismissReason, scope: DismissScope, text: string, who: Person) {
  const suppress = ['false-positive', 'by-design', 'out-of-scope', 'duplicate'].includes(reason)
  const dir = `${f.file.split('/').slice(0, -1).join('/')}/**`
  const team = scope === 'team'
  const target = team ? 'payments-core team layer · lumen/review-prompts-payments-core' : `${REPO} repo layer · .antiphon/review/ in lumen/${REPO}`
  const file = team ? (suppress ? 'suppressions.yaml' : `checks/${f.check}.notes.md`) : suppress ? '.antiphon/review/suppressions.yaml' : '.antiphon/review/context/notes.md'
  const note = text.trim() || f.title
  const src = `${REPO}#612 · ${f.id} · thread (${who.name})`
  const lines = suppress
    ? [`- check: ${f.check}`, ...(scope === 'path' ? [`  paths: ["${dir}"]`] : []), `  reason: ${reason}`, `  note: "${note}"`, `  source: ${src}`]
    : [`## ${f.check} · ${f.checkTitle}`, ...(scope === 'path' ? [`Applies to ${dir} only.`] : []), note, `<!-- source: ${src} -->`]
  return { kind: suppress ? 'suppression' : 'context note', target, file, lines, dir }
}

/** What the mock agent drafts from the steer: the same rule, narrowed and with a recheck. */
function draftedRule(lines: string[], steer: string, suppress: boolean): string[] {
  const body = lines.slice(0, -1)
  const src = lines[lines.length - 1]
  return suppress
    ? [...body, `  when: "${steer}"`, '  recheck: monthly (the rule drops when it stops matching the steer)', src]
    : [...body, `Scope, from the steer: ${steer}`, 'Recheck monthly; remove this note when it no longer holds.', src]
}

function RuleBlock({ lines }: { lines: string[] }) {
  return (
    <Code block fz={11} style={{ whiteSpace: 'pre-wrap' }}>
      {lines.join('\n')}
    </Code>
  )
}

function RuleDiff({ a, b }: { a: string[]; b: string[] }) {
  const rows: { t: ' ' | '+' | '-'; l: string }[] = [...a.filter((l) => !b.includes(l)).map((l) => ({ t: '-' as const, l })), ...b.map((l) => ({ t: a.includes(l) ? (' ' as const) : ('+' as const), l }))]
  return (
    <Box ff="monospace" fz={11} style={{ border: '1px solid var(--mantine-color-dark-4)', borderRadius: 4 }}>
      {rows.map((r, i) => (
        <Box key={i} px={6} style={{ background: r.t === '+' ? 'rgba(46,160,67,0.15)' : r.t === '-' ? 'rgba(248,81,73,0.15)' : undefined, whiteSpace: 'pre-wrap' }}>
          <Text span ff="monospace" fz={11} c={r.t === '+' ? 'success.4' : r.t === '-' ? 'danger.4' : 'dimmed'}>
            {r.t === ' ' ? '  ' : `${r.t} `}
          </Text>
          {r.l}
        </Box>
      ))}
    </Box>
  )
}

function KV({ k, v }: { k: string; v: ReactNode }) {
  return (
    <Table.Tr>
      <Table.Td w={130} c="dimmed" style={{ verticalAlign: 'top' }}>
        {k}
      </Table.Td>
      <Table.Td>{v}</Table.Td>
    </Table.Tr>
  )
}

/**
 * Choosing scope path, repo or team opens this confirmation: the target layer, the rule text, the file it changes and
 * who can open it. An optional steer has an agent redraft the rule first (drafting → diff → learn PR opened).
 */
function WideScopeModal({
  opened,
  f,
  reason,
  scope,
  text,
  who,
  initialSteer = '',
  onClose,
  onDone,
}: {
  opened: boolean
  f: Finding
  reason: DismissReason
  scope: DismissScope
  text: string
  who: Person
  initialSteer?: string
  onClose: () => void
  onDone: (l: Learn) => void
}) {
  const [step, setStep] = useState<WideStep>('preview')
  const [steer, setSteer] = useState(initialSteer)
  const r = ruleFor(f, reason, scope, text, who)
  const suppress = r.kind === 'suppression'
  const st = steer.trim()
  const drafted = st ? draftedRule(r.lines, st, suppress) : r.lines
  const finalRule = step === 'preview' ? r.lines : drafted
  const trust = trustOfViewer(who)
  const direct = trust >= WIDE_AT
  const team = scope === 'team'
  const learn: Learn = team ? { queued: 'team', steer: st || undefined, rule: finalRule } : direct ? { pr: LEARN_PR, steer: st || undefined, rule: finalRule } : { queued: 'proposal', steer: st || undefined, rule: finalRule }
  const outcome = team ? 'queue the team-layer proposal' : direct ? `open learn PR #${LEARN_PR}` : 'send it to a maintainer as a proposal'
  const draftIt = () => {
    setStep('drafting')
    window.setTimeout(() => setStep('drafted'), 1600)
  }
  return (
    <Modal opened={opened} onClose={onClose} size={760} title={<Text fw={700}>Dismiss {f.id} at {SCOPE_META[scope].label.toLowerCase()} scope: confirm what changes</Text>}>
      <Stack gap="sm">
        <Table fz="xs" verticalSpacing={3} withRowBorders={false}>
          <Table.Tbody>
            <KV k="Finding" v={`${f.id} · ${f.check} ${f.checkTitle}`} />
            <KV k="Target" v={r.target} />
            <KV k="Rule" v={`${r.kind} · reason: ${REASON_META[reason].label.toLowerCase()}`} />
            <KV k="File it changes" v={<Code fz={11}>{r.file}</Code>} />
            {scope === 'path' && <KV k="Paths" v={<Code fz={11}>{r.dir}</Code>} />}
            <KV
              k="Who"
              v={
                <Group gap={6} wrap="wrap">
                  <TrustBadge login={who.login} repo={REPO} />
                  <Text size="xs" c={direct ? 'success.4' : 'orange.4'}>
                    {direct ? `≥ ${WIDE_AT} (wide dismissal, payments-core override): the learn PR opens directly` : `< ${WIDE_AT}: becomes a proposal for a payments-api maintainer`}
                  </Text>
                </Group>
              }
            />
            {team && (
              <KV
                k="Team scope"
                v={
                  <Group gap={6} wrap="wrap">
                    <Badge color="grape" variant="light" tt="none">
                      team scope: queued until 2 repos
                    </Badge>
                    <Text size="xs" c="dimmed">
                      This is repo 1 of 2 ({REPO}). The PR to the payments-core layer opens when a second repo dismisses {f.check} the same way.
                    </Text>
                  </Group>
                }
              />
            )}
          </Table.Tbody>
        </Table>
        {step === 'preview' && (
          <>
            <Box>
              <Text size="xs" c="dimmed" mb={2}>
                Rule text ({r.kind})
              </Text>
              <RuleBlock lines={r.lines} />
            </Box>
            <Textarea
              size="xs"
              autosize
              minRows={2}
              label="Steer the agent that drafts the rule (optional)"
              placeholder="e.g. only until the TimeProvider migration lands; keep flagging new files"
              value={steer}
              onChange={(e) => setSteer(e.currentTarget.value)}
            />
            <Group justify="space-between" wrap="nowrap" gap="sm">
              <Text size="xs" c="dimmed">
                {st ? 'With a steer, an agent (sonnet seat) redrafts the rule first; you see the diff before anything opens.' : `Without a steer, confirming will ${outcome} with the rule above.`}
              </Text>
              <Group gap={6} wrap="nowrap">
                <Button size="xs" variant="subtle" color="gray" onClick={onClose}>
                  Cancel
                </Button>
                {st ? (
                  <Button size="xs" color="grape" leftSection={<TbSparkles size={14} />} onClick={draftIt}>
                    Draft with agent
                  </Button>
                ) : (
                  <Button size="xs" color="cyan" onClick={() => setStep('done')}>
                    Confirm: {outcome}
                  </Button>
                )}
              </Group>
            </Group>
          </>
        )}
        {step === 'drafting' && (
          <Group gap="sm" py="lg" justify="center">
            <Loader size="sm" color="grape" />
            <Text size="sm">Agent drafting rule…</Text>
            <Text size="xs" c="dimmed">
              sonnet seat · steer: "{st}"
            </Text>
          </Group>
        )}
        {step === 'drafted' && (
          <>
            <Text size="xs" c="dimmed">
              Drafted rule · diff against the previewed rule
            </Text>
            <RuleDiff a={r.lines} b={drafted} />
            <Group justify="flex-end" gap={6}>
              <Button size="xs" variant="subtle" color="gray" onClick={() => setStep('preview')}>
                Back: edit the steer
              </Button>
              <Button size="xs" color="cyan" onClick={() => setStep('done')}>
                Confirm: {outcome}
              </Button>
            </Group>
          </>
        )}
        {step === 'done' && (
          <>
            <Alert
              color="success"
              variant="light"
              p={8}
              icon={<TbCircleCheckFilled size={16} />}
              title={<Text size="sm" fw={700}>{team ? 'Learn proposal queued (team scope, 1 of 2 repos)' : direct ? `Learn PR #${LEARN_PR} opened` : 'Proposal sent to the payments-api maintainers'}</Text>}
            >
              <Text size="xs">
                {learn.pr
                  ? `lumen/${REPO}#${LEARN_PR} changes ${r.file}. The finding now shows "Learn PR #${LEARN_PR}", and your reply on the ${f.id} thread links it.`
                  : `Queued with the rule below. The finding shows the proposal, and your reply on the ${f.id} thread links it.`}
              </Text>
            </Alert>
            {learn.pr && (
              <Paper withBorder radius="md" p="xs">
                <Group gap={6} wrap="nowrap">
                  <TbGitPullRequest size={14} color="var(--mantine-color-success-5)" />
                  <Text size="sm" fw={700}>
                    learn: {f.check} {r.kind} for {REPO}
                  </Text>
                  <Text size="xs" c="dimmed" ff="monospace">
                    lumen/{REPO}#{LEARN_PR}
                  </Text>
                </Group>
                <Group gap={6} mt={4} wrap="wrap">
                  <Badge size="xs" variant="outline" color="cyan" tt="none" leftSection={<TbLink size={10} />}>
                    From {REPO}#612 · {f.id} · thread
                  </Badge>
                  <Text size="xs" c="dimmed">
                    opened for {who.name} · reviewers: payments-api maintainers{learn.steer ? ' · drafted by an agent from your steer' : ''}
                  </Text>
                </Group>
                <Text size="xs" ff="monospace" mt={6} c="dimmed">
                  {r.file}
                </Text>
                <RuleBlock lines={finalRule} />
              </Paper>
            )}
            <Paper withBorder radius="md" p="xs">
              <Text size="xs" c="dimmed" mb={2}>
                Reply on the {f.id} thread (posts with your review)
              </Text>
              <Text size="xs" style={{ whiteSpace: 'pre-wrap' }}>
                {replyPreview({ action: 'dismiss', reason, scope, text, learn }, who)}
              </Text>
            </Paper>
            <Group justify="flex-end">
              <Button size="xs" onClick={() => onDone(learn)}>
                Done
              </Button>
            </Group>
          </>
        )}
      </Stack>
    </Modal>
  )
}

function Composer({
  f,
  draft,
  onChange,
  who,
  dense = false,
  autoConfirm = false,
  initialSteer,
}: {
  f: Finding
  draft?: Draft
  onChange: (d: Draft | undefined) => void
  who: Person
  dense?: boolean
  autoConfirm?: boolean
  initialSteer?: string
}) {
  const d = draft
  const disabled = f.status === 'pending-sync'
  const [confirm, setConfirm] = useState(autoConfirm)
  const set = (patch: Partial<Draft>) => onChange({ action: d?.action ?? 'will-fix', text: d?.text ?? '', ...d, ...patch })
  const isBypass = bypasses(f)
  const wide = d?.action === 'dismiss' && !!d.scope && d.scope !== 'instance'
  return (
    <Stack gap={6}>
      <Group gap={4}>
        {actionsFor(f).map((a) => (
          <Button
            key={a}
            size="compact-xs"
            disabled={disabled}
            variant={d?.action === a ? 'filled' : 'default'}
            color={ACTION_META[a].color}
            onClick={() => (d?.action === a ? onChange(undefined) : set({ action: a, learn: undefined }))}
          >
            {ACTION_META[a].label}
          </Button>
        ))}
      </Group>
      {d?.action === 'not-blocker' && <NotBlockerPanel who={who} />}
      {d?.action === 'dismiss' && (
        <Paper p={6} radius="sm" bg="dark.6">
          <Text size="xs" c="dimmed" mb={4}>
            Reason
          </Text>
          <Chip.Group value={d.reason ?? ''} onChange={(v) => set({ reason: v as DismissReason, learn: undefined })}>
            <Group gap={4}>
              {(Object.keys(REASON_META) as DismissReason[]).map((r) => (
                <Chip key={r} value={r} size="xs" variant="outline">
                  {REASON_META[r].label}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
          <Text size="xs" c="dimmed" mt={6} mb={4}>
            Scope
          </Text>
          <Radio.Group
            value={d.scope ?? ''}
            onChange={(v) => {
              const sc = v as DismissScope
              set({ scope: sc, learn: undefined })
              if (sc !== 'instance') setConfirm(true)
            }}
          >
            <Group gap="md">
              {(Object.keys(SCOPE_META) as DismissScope[]).map((sc) => (
                <Tooltip key={sc} label={SCOPE_META[sc].hint}>
                  <Radio value={sc} size="xs" label={SCOPE_META[sc].label} />
                </Tooltip>
              ))}
            </Group>
          </Radio.Group>
          {f.severity === 'blocker' && (
            <Text size="xs" c="warning.4" mt={6}>
              Dismissing a blocker: allowed for the author (policy dismiss.blocker: author). Incident investigations search dismissed findings; a dismissal later
              linked to a bug counts against you.
            </Text>
          )}
          {wide &&
            (d.learn ? (
              <Group gap={6} mt={6} wrap="wrap">
                <LearnBadge learn={d.learn} />
                <Text size="xs" c="cyan.4">
                  {learnLine(d.learn)}
                </Text>
                <Button size="compact-xs" variant="subtle" color="cyan" onClick={() => setConfirm(true)}>
                  Review
                </Button>
              </Group>
            ) : (
              <Group gap={6} mt={6} wrap="wrap">
                <Text size="xs" c="warning.4">
                  {d.reason ? 'Wider than this instance: confirm the rule it creates before posting.' : 'Pick a reason: the confirmation previews the rule it creates.'}
                </Text>
                <Button size="compact-xs" variant="light" color="cyan" disabled={!d.reason} onClick={() => setConfirm(true)}>
                  Review and confirm
                </Button>
              </Group>
            ))}
        </Paper>
      )}
      {d?.action === 'dispute' && isBypass && (
        <Text size="xs" c="red.4">
          {f.flags.join(', ')} is a bypass category: the adjudicator can hold but can't concede alone, so a dispute escalates to the payments-core gate.
        </Text>
      )}
      {d && (
        <Textarea
          size="xs"
          autosize
          minRows={dense ? 1 : 2}
          placeholder={
            d.action === 'dispute'
              ? 'Why is this wrong? (required)'
              : d.action === 'question'
                ? 'Ask the bot (answered from the pack context)'
                : d.action === 'not-blocker'
                  ? 'Why is this not a blocker? (required)'
                  : 'Optional note'
          }
          value={d.text}
          onChange={(e) => set({ text: e.currentTarget.value })}
        />
      )}
      {d && !dense && (
        <Box>
          <Text size="xs" c="dimmed">
            Posts to the thread as
          </Text>
          <Paper p={6} radius="sm" withBorder>
            <Text size="xs" style={{ whiteSpace: 'pre-wrap' }} c={draftValid(d) ? undefined : 'dimmed'}>
              {replyPreview(d, who)}
            </Text>
          </Paper>
        </Box>
      )}
      {wide && d.reason && (
        <WideScopeModal
          opened={confirm}
          f={f}
          reason={d.reason}
          scope={d.scope!}
          text={d.text}
          who={who}
          initialSteer={initialSteer}
          onClose={() => setConfirm(false)}
          onDone={(learn) => {
            set({ learn })
            setConfirm(false)
          }}
        />
      )}
    </Stack>
  )
}

function SubmitBar({ drafts, who, sticky = true, onSubmit }: { drafts: Drafts; who: Person; sticky?: boolean; onSubmit?: () => void }) {
  const list = Object.entries(drafts).filter(([, d]) => d) as [string, Draft][]
  const invalid = list.filter(([, d]) => !draftValid(d))
  const n = list.length
  return (
    <Paper
      withBorder
      p="xs"
      radius="md"
      style={sticky ? { position: 'sticky', bottom: 0, zIndex: 5, boxShadow: '0 -6px 16px rgba(0,0,0,0.5)' } : undefined}
    >
      <Group justify="space-between" wrap="nowrap">
        <Group gap={8} wrap="wrap" style={{ minWidth: 0 }}>
          {list.map(([id, d]) => (
            <Badge key={id} size="sm" variant={draftValid(d) ? 'light' : 'outline'} color={draftValid(d) ? ACTION_META[d.action].color : 'gray'}>
              {id} · {ACTION_META[d.action].label}
              {d.reason ? ` (${REASON_META[d.reason].label.toLowerCase()})` : ''}
            </Badge>
          ))}
          {n === 0 && (
            <Text size="xs" c="dimmed">
              No replies drafted.
            </Text>
          )}
          {invalid.length > 0 && (
            <Text size="xs" c="warning.4">
              {invalid.map(([id]) => id).join(', ')} incomplete
            </Text>
          )}
        </Group>
        <Group gap={8} wrap="nowrap">
          <Tooltip
            multiline
            w={320}
            label="One GitHub review (one notification). Each reply is saved in Antiphon first, then posted to its thread, then the thread is resolved. A failed post stays pending-sync and is retried. Signed with your name, not an @mention, so nobody is pinged."
          >
            <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
              signed "— {who.name} via Antiphon"
            </Text>
          </Tooltip>
          <Button size="xs" leftSection={<TbSend size={14} />} disabled={n === 0 || invalid.length > 0} onClick={onSubmit}>
            Post {n} {n === 1 ? 'reply' : 'replies'} to GitHub as {who.name} via Antiphon
          </Button>
        </Group>
      </Group>
    </Paper>
  )
}

/** Why this viewer can't decide the gate, or null when they can. */
function gateBlockReason(role: Role, who: Person, solo: boolean): string | null {
  if (role === 'author' && !solo) return `You're the author of ${REPO}#612: authors can't decide their own gate. Your replies still count as human involvement.`
  if (role === 'member') {
    const t = trustOfViewer(who)
    return t < GATE_AT
      ? `Trust ${t} is below the decide-gate threshold (${GATE_AT}), and the gate is assigned to Hugo Martin. You can still reply on threads.`
      : 'The gate is assigned to Hugo Martin.'
  }
  return null
}

function DecideBlocked({ reason }: { reason: string }) {
  return (
    <Box mt={8}>
      <Tooltip multiline w={300} label={reason}>
        <Box>
          <Button size="xs" fullWidth disabled leftSection={<TbLock size={12} />}>
            Decide gate
          </Button>
        </Box>
      </Tooltip>
      <Text size="xs" c="dimmed" mt={4} fs="italic">
        {reason}
      </Text>
    </Box>
  )
}

function GatePanel({ role, compact = false, drafts, solo = false }: { role: Role; compact?: boolean; drafts: Drafts; solo?: boolean }) {
  const who = VIEWERS[role]
  const [decision, setDecision] = useState<GateDecision | null>(role === 'reviewer' ? 'another-round' : null)
  const [steer, setSteer] = useState('Recheck the over-refund race (F3) against the actual transaction isolation in UnitOfWorkMiddleware, and ignore style nits this round.')
  const [override, setOverride] = useState<'off' | 'on'>('off')
  const blocked = gateBlockReason(role, who, solo)
  const canDecide = !blocked
  const b = blockerState(drafts, who)
  const stillOpen = b.open.length - b.cleared.length
  const assignee = solo ? 'asmith' : 'hmartin'
  return (
    <Paper withBorder p="xs" radius="md" style={{ borderTop: '2px solid var(--mantine-color-pink-6)' }}>
      <SectionLabel
        right={
          <Badge size="xs" color="pink" variant="light">
            gate: human-review
          </Badge>
        }
      >
        Gate decision
      </SectionLabel>
      <Group gap={4} mb={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          Opened after 3 bot-only rounds · assigned to
        </Text>
        <PersonChip login={assignee} size="xs" trustRepo={REPO} />
        <Text size="xs" c="dimmed">
          {solo ? '(only member of refund-tooling)' : '(random pick, payments-core)'} · 3.6/4 wh
        </Text>
      </Group>
      <Stack gap={2} mb={6}>
        {solo ? (
          <OwnersLine owners={OWNERS_SOLO} label="Owners · tools/refund-replay/**" />
        ) : (
          <>
            <OwnersLine owners={OWNERS_REFUNDS} label="Owners · src/Refunds/**" />
            <OwnersLine owners={OWNERS_DB} label="Owners · db/**" />
          </>
        )}
      </Stack>
      {solo && (
        <Paper p={6} radius="sm" bg="dark.6" mb={6} style={{ borderLeft: '3px solid var(--mantine-color-yellow-6)' }}>
          <Group gap={6} justify="space-between" wrap="nowrap">
            <Badge size="sm" color="yellow" variant="light" tt="none">
              solo owner · repo override {override}
            </Badge>
            <SegmentedControl size="xs" value={override} onChange={(v) => setOverride(v as 'off' | 'on')} data={['off', 'on']} />
          </Group>
          <Text size="xs" c="dimmed" mt={4}>
            {override === 'off'
              ? 'Author may decide: the owning team (refund-tooling) has one member and these paths have no other code owners (instance rule). Recorded on the gate as a solo-owner decision.'
              : `Author may decide: allowed by the ${REPO} repo override (gate.soloOwnerSelfDecide: on, set by Hugo Martin on 2026-10-02) as well as the solo-owner rule. Recorded on the gate as a solo-owner decision.`}
          </Text>
        </Paper>
      )}
      {!compact && (
        <Stack gap={2} mb={6}>
          {(PR612.humanRequired ?? []).map((r) => (
            <Text key={r} size="xs" c="pink.3">
              • {r}
            </Text>
          ))}
          <Text size="xs" c="dimmed">
            Human approval is sticky for the rest of this PR.
          </Text>
        </Stack>
      )}
      <BotApprovalLine drafts={drafts} who={who} />
      <Radio.Group value={decision ?? ''} onChange={(v) => setDecision(v as GateDecision)}>
        <Stack gap={6}>
          <Radio
            size="xs"
            value="approve"
            disabled={!canDecide}
            label="Approve"
            description={`antiphon/approval → success @ ${PR612.headSha}. Merge left to the author.${stillOpen ? ` ${stillOpen} open blocker recorded as accepted risk by you.` : ''}`}
          />
          <Radio size="xs" value="another-round" disabled={!canDecide} label="Another round" description="Resets the bot-only counter. Optional steer goes into every reviewer pack." />
          <Radio size="xs" value="close" disabled={!canDecide} label="Close run" description="Stops reviewing this PR. Open findings stay on the PR." />
        </Stack>
      </Radio.Group>
      {decision === 'another-round' && canDecide && (
        <Textarea
          mt={6}
          size="xs"
          autosize
          minRows={2}
          label={<Text size="xs">Steer prompt (shared prefix: "Human steer from {who.name}: …")</Text>}
          value={steer}
          onChange={(e) => setSteer(e.currentTarget.value)}
        />
      )}
      {canDecide ? (
        <Button mt={8} size="xs" fullWidth color={decision === 'approve' ? 'success' : decision === 'close' ? 'gray' : 'pink'} disabled={!decision}>
          {decision === 'approve' ? 'Approve 3b81f0d' : decision === 'close' ? 'Close run' : decision ? 'Start round 4 with steer' : 'Pick a decision'}
        </Button>
      ) : (
        <DecideBlocked reason={blocked!} />
      )}
    </Paper>
  )
}

function BlastCard() {
  return (
    <Paper withBorder p="xs" radius="md">
      <SectionLabel>Blast radius</SectionLabel>
      <BlastPanel blast={PR612.blast!} trips={blastTrips(PR612.blast!)} compact />
      <Text size="xs" c="dimmed" mt={6}>
        Consumers notified: web-portal (checkout), partner-sdk-js, partner-sdk-java (contract register).
      </Text>
    </Paper>
  )
}

function FindingDetail({
  f,
  draft,
  onDraft,
  who,
  autoConfirm,
  initialSteer,
}: {
  f: Finding
  draft?: Draft
  onDraft: (d: Draft | undefined) => void
  who: Person
  autoConfirm?: boolean
  initialSteer?: string
}) {
  return (
    <Stack gap="sm">
      <Box>
        <FindingMeta f={f} draft={draft} />
        <Text fw={700} size="md" mt={4}>
          {f.id} · {f.title}
        </Text>
        <Group gap={4} mt={2}>
          <TbFileCode size={12} />
          <Text size="xs" ff="monospace" c="dimmed">
            {f.file}:{f.lines}
          </Text>
        </Group>
      </Box>
      <PendingSync f={f} />
      <HunkView hunk={f.hunk} anchor={f.anchor} />
      <Text size="sm">{f.body}</Text>
      <SuggestionView f={f} />
      <WhyCheck f={f} />
      <Divider label={<Text size="xs">Thread</Text>} labelPosition="left" />
      <ThreadView f={f} />
      <Divider label={<Text size="xs">Your response</Text>} labelPosition="left" />
      <Composer key={f.id} f={f} draft={draft} onChange={onDraft} who={who} autoConfirm={autoConfirm} initialSteer={initialSteer} />
    </Stack>
  )
}

// ---------------------------------------------------------------- (A) split view

interface SplitOpts {
  initialSel?: string
  /** variant: the PR only touches paths owned by a one-member team with no other code owners */
  solo?: boolean
  /** open the wide-scope confirmation for the selected finding's drafted dismissal, with this steer */
  wideSteer?: string
}

function SplitView({ initialRole, ...opts }: { initialRole: Role } & SplitOpts) {
  const [role, setRole] = useState<Role>(initialRole)
  return <SplitViewInner key={role} role={role} onRole={setRole} {...opts} />
}

function SplitViewInner({ role, onRole, initialSel, solo = false, wideSteer }: { role: Role; onRole: (r: Role) => void } & SplitOpts) {
  const [drafts, setDraft] = useDrafts(role)
  const [sel, setSel] = useState(initialSel ?? 'F1')
  const who = VIEWERS[role]
  const f = FINDINGS.find((x) => x.id === sel)!
  const groups: { title: string; items: Finding[] }[] = [
    { title: 'Open', items: FINDINGS.filter((x) => x.status === 'open' && x.inline && !x.belowBar) },
    { title: 'Tracking', items: FINDINGS.filter((x) => x.status === 'will-fix' || x.status === 'pending-sync') },
    { title: 'Not posted inline', items: FINDINGS.filter((x) => !x.inline) },
  ]
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <PrTitle />
        <ViewerToggle role={role} onChange={onRole} />
      </Group>
      {solo && (
        <Alert color="yellow" variant="light" p={8} icon={<TbInfoCircle size={16} />}>
          <Text size="xs">
            Variant: this PR only touches tools/refund-replay/**, owned by <b>refund-tooling</b> (one member: Alice Smith) with no other code owners. That is the
            one case where the author may decide their own gate.
          </Text>
        </Alert>
      )}
      <Paper withBorder p={6} radius="md">
        <Group justify="space-between" wrap="nowrap" gap="md">
          <RoundStepper />
          <PostingBar />
        </Group>
        <Divider my={6} />
        <StatusLine />
      </Paper>
      <Box style={{ display: 'grid', gridTemplateColumns: '340px minmax(0, 1fr) 300px', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p={6}>
          {groups.map((g) => (
            <Box key={g.title} mb={6}>
              <SectionLabel right={<Text size="xs" c="dimmed">{g.items.length}</Text>}>{g.title}</SectionLabel>
              {g.items.map((x) => {
                const d = drafts[x.id]
                return (
                  <UnstyledButton
                    key={x.id}
                    w="100%"
                    onClick={() => setSel(x.id)}
                    p={6}
                    style={{
                      borderRadius: 4,
                      background: sel === x.id ? 'var(--mantine-color-dark-5)' : undefined,
                      borderLeft: `3px solid var(--mantine-color-${SEVERITY_META[x.severity].color}-6)`,
                      marginBottom: 2,
                    }}
                  >
                    <Group gap={6} wrap="nowrap" align="flex-start">
                      <Text size="xs" ff="monospace" c="dimmed" w={18}>
                        {x.id}
                      </Text>
                      <Box style={{ flex: 1, minWidth: 0 }}>
                        <Text size="xs" fw={500} lineClamp={2}>
                          {x.title}
                        </Text>
                        <Group gap={6} mt={2} wrap="nowrap">
                          <Text size="10px" c={`${SEVERITY_META[x.severity].color}.4`} fw={700} tt="uppercase">
                            {x.severity}
                          </Text>
                          {x.flags.length > 0 && (
                            <Tooltip label={bypasses(x) ? 'bypass category: bypasses the round bar' : 'flag'}>
                              <Text size="10px" c="red.4">
                                {x.flags.join(' ')}
                                {bypasses(x) ? ' · bypass' : ''}
                              </Text>
                            </Tooltip>
                          )}
                          <Text size="10px" c="dimmed" ff="monospace" truncate>
                            {x.file.split('/').pop()}:{x.lines}
                          </Text>
                        </Group>
                      </Box>
                      {d ? (
                        <Badge size="xs" variant="light" color={ACTION_META[d.action].color}>
                          {ACTION_META[d.action].label}
                        </Badge>
                      ) : x.status === 'pending-sync' ? (
                        <Tooltip label="Reply saved; GitHub post retrying">
                          <Badge size="xs" variant="dot" color="warning">
                            sync
                          </Badge>
                        </Tooltip>
                      ) : x.status === 'will-fix' ? (
                        <Badge size="xs" variant="dot" color="active">
                          will fix
                        </Badge>
                      ) : null}
                    </Group>
                  </UnstyledButton>
                )
              })}
            </Box>
          ))}
          <Text size="xs" c="dimmed" px={4}>
            1 more below the round-3 bar (F9, minor p 0.58): stored, not posted.
          </Text>
        </Paper>
        <Paper withBorder radius="md" p="sm">
          <FindingDetail f={f} draft={drafts[f.id]} onDraft={(d) => setDraft(f.id, d)} who={who} autoConfirm={!!wideSteer && f.id === initialSel} initialSteer={wideSteer} />
        </Paper>
        <Stack gap="sm">
          <GatePanel role={role} drafts={drafts} solo={solo} />
          <BlastCard />
        </Stack>
      </Box>
      <SubmitBar drafts={drafts} who={who} />
    </Stack>
  )
}

// ---------------------------------------------------------------- (B) GitHub-like inline threads

function DismissMenu({ onPick }: { onPick: (r: DismissReason) => void }) {
  return (
    <Menu shadow="md" width={200} position="bottom-start">
      <Menu.Target>
        <Button size="compact-xs" variant="default" rightSection={<TbChevronDown size={10} />}>
          Dismiss
        </Button>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Reason</Menu.Label>
        {(Object.keys(REASON_META) as DismissReason[]).map((r) => (
          <Menu.Item key={r} onClick={() => onPick(r)}>
            <Text size="xs">{REASON_META[r].label}</Text>
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  )
}

function InlineThreadCard({ f, draft, onDraft, who }: { f: Finding; draft?: Draft; onDraft: (d: Draft | undefined) => void; who: Person }) {
  const [replying, setReplying] = useState(!!draft)
  const pick = (a: ResponseAction) => {
    setReplying(true)
    onDraft({ action: a, text: draft?.text ?? '' })
  }
  return (
    <Paper withBorder radius="md" m={8} ml={58} style={{ fontFamily: 'var(--mantine-font-family)', borderColor: draft ? 'var(--mantine-color-warning-8)' : undefined }}>
      <Group gap={8} px={10} py={6} bg="dark.6" style={{ borderRadius: '8px 8px 0 0' }} wrap="nowrap">
        <Avatar size={20} radius="xl" color={f.check === 'human' ? 'active' : 'cyan'}>
          {f.check === 'human' ? <Text size="9px">HM</Text> : <TbRobot size={12} />}
        </Avatar>
        <Text size="xs" fw={700}>
          {f.check === 'human' ? personName(f.humanRaisedBy) : BOT_HANDLE}
        </Text>
        <SeverityBadge severity={f.severity} size="xs" />
        <FlagBadges flags={f.flags} />
        <BypassBadge f={f} />
        <LearnBadge learn={draft?.learn} />
        <Text size="xs" c="dimmed" truncate style={{ flex: 1 }}>
          {f.check !== 'human' && `${f.check} · `}R{f.round} · p {f.confidence.toFixed(2)}
        </Text>
        {draft && (
          <Badge size="xs" color="warning" variant="outline">
            Pending
          </Badge>
        )}
        {f.status === 'pending-sync' && (
          <Badge size="xs" color="warning" variant="dot">
            pending sync
          </Badge>
        )}
      </Group>
      <Stack gap={8} p={10}>
        <Text size="sm" fw={600}>
          {f.title}
        </Text>
        <Text size="sm">{f.body}</Text>
        <SuggestionView f={f} />
        <WhyCheck f={f} defaultOpen={false} />
        {f.thread.length > 1 && <ThreadView f={{ ...f, thread: f.thread.slice(1) }} />}
        <PendingSync f={f} />
        {f.status !== 'pending-sync' && (
          <>
            <Divider />
            {!replying ? (
              <Group gap={4}>
                <Button size="compact-xs" variant="default" onClick={() => pick('will-fix')}>
                  Will fix
                </Button>
                <Button size="compact-xs" variant="default" onClick={() => pick('fixed')}>
                  Fixed
                </Button>
                <DismissMenu onPick={(r) => { setReplying(true); onDraft({ action: 'dismiss', reason: r, scope: 'instance', text: '' }) }} />
                <Button size="compact-xs" variant="default" onClick={() => pick('dispute')}>
                  Dispute
                </Button>
                <Button size="compact-xs" variant="default" onClick={() => pick('question')}>
                  Question
                </Button>
                <Button size="compact-xs" variant="default" onClick={() => pick('defer')}>
                  Defer
                </Button>
                {f.severity === 'blocker' && (
                  <Button size="compact-xs" variant="default" color="orange" onClick={() => pick('not-blocker')}>
                    Not a blocker
                  </Button>
                )}
                <Text size="xs" c="dimmed" ml="auto">
                  or reply on GitHub; the comment workflow picks it up
                </Text>
              </Group>
            ) : (
              <Box>
                <Composer f={f} draft={draft} onChange={(d) => { onDraft(d); if (!d) setReplying(false) }} who={who} dense />
                <Group justify="space-between" mt={6}>
                  <Text size="xs" c="dimmed">
                    Added to your pending review
                  </Text>
                  <Button size="compact-xs" variant="subtle" color="gray" onClick={() => { onDraft(undefined); setReplying(false) }}>
                    Discard
                  </Button>
                </Group>
              </Box>
            )}
          </>
        )}
      </Stack>
    </Paper>
  )
}

function DiffFile({ path, findings, drafts, setDraft, who }: { path: string; findings: Finding[]; drafts: Drafts; setDraft: (id: string, d: Draft | undefined) => void; who: Person }) {
  const [open, setOpen] = useState(true)
  const lines = findings.flatMap((f) => f.hunk.map((l) => ({ ...l, key: `${l.t}${l.n}` })))
  const uniq: (DiffLine & { key: string })[] = []
  for (const l of lines.sort((a, b) => a.n - b.n || (a.t === '-' ? -1 : 1))) if (!uniq.some((u) => u.key === l.key)) uniq.push(l)
  return (
    <Paper withBorder radius="md" mb="sm">
      <UnstyledButton onClick={() => setOpen((o) => !o)} w="100%">
        <Group px={10} py={6} bg="dark.6" gap={8} style={{ borderRadius: '8px 8px 0 0' }}>
          {open ? <TbChevronDown size={14} /> : <TbChevronRight size={14} />}
          <Text size="xs" ff="monospace" fw={600}>
            {path}
          </Text>
          <Badge size="xs" variant="light" color="gray">
            {findings.length} {findings.length === 1 ? 'thread' : 'threads'}
          </Badge>
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Box ff="monospace" fz={12}>
          {uniq.map((l, i) => {
            const gap = i > 0 && l.t !== '-' && uniq[i - 1].n < l.n - 1
            const anchored = findings.filter((f) => f.anchor.trim() === l.code.trim() && l.t !== '-')
            const bg = l.t === '+' ? 'rgba(46,160,67,0.15)' : l.t === '-' ? 'rgba(248,81,73,0.15)' : undefined
            return (
              <Box key={l.key}>
                {gap && (
                  <Box px={58} py={2} bg="dark.6" c="dimmed" fz={11}>
                    ⋯ lines {uniq[i - 1].n + 1}–{l.n - 1} unchanged
                  </Box>
                )}
                <Group gap={0} wrap="nowrap" style={{ background: bg }}>
                  <Box w={44} ta="right" pr={8} c="dimmed" style={{ flexShrink: 0 }}>
                    {l.t === '-' ? '' : l.n}
                  </Box>
                  <Box w={14} c={l.t === '+' ? 'success.4' : l.t === '-' ? 'danger.4' : 'dimmed'} style={{ flexShrink: 0 }}>
                    {l.t === ' ' ? '' : l.t}
                  </Box>
                  <Box style={{ whiteSpace: 'pre' }}>{l.code || ' '}</Box>
                </Group>
                {anchored.map((f) => (
                  <InlineThreadCard key={f.id} f={f} draft={drafts[f.id]} onDraft={(d) => setDraft(f.id, d)} who={who} />
                ))}
              </Box>
            )
          })}
        </Box>
      </Collapse>
    </Paper>
  )
}

function SummaryComment() {
  const [open, setOpen] = useState(false)
  const nits = FINDINGS.filter((f) => f.severity === 'nit')
  return (
    <Paper withBorder radius="md" mb="sm">
      <UnstyledButton onClick={() => setOpen((o) => !o)} w="100%">
        <Group gap={8} px={10} py={6} bg="dark.6" style={{ borderRadius: '8px 8px 0 0' }} wrap="nowrap">
          <Avatar size={20} radius="xl" color="cyan">
            <TbRobot size={12} />
          </Avatar>
          <Text size="xs" fw={700}>
            {BOT_HANDLE}
          </Text>
          <Text size="xs" c="dimmed">
            summary comment · edited in place · round 3 · Fri 15:52
          </Text>
          <Text size="xs" ml="auto">
            6 open · 1 blocker · 3 accepted · 4 below bar
          </Text>
          {open ? <TbChevronDown size={12} /> : <TbChevronRight size={12} />}
        </Group>
      </UnstyledButton>
      <Collapse in={open}>
        <Stack gap={4} p={10}>
          <Text size="xs">
            <b>Antiphon review, round 3</b> at 3b81f0d · 11 checks across 4 reviewers (security · concurrency · data · api-compat) · all checks accounted for.
          </Text>
          <Text size="xs">
            Blast radius: reach 3 · <b>contract yes (0.91)</b> · <b>data yes (0.84)</b> · rollback 4 · reversal 4 · exposure 4. A human must approve: external_contract,
            reversal_after_months, public-api.
          </Text>
          <Text size="xs">Round-1 nits (not posted inline):</Text>
          {nits.map((n) => (
            <Text key={n.id} size="xs" pl={10}>
              • {n.file}:{n.lines} {n.title}
            </Text>
          ))}
          <Text size="xs" c="dimmed">
            Bot-only rounds 3/3 → human-review gate opened (payments-core).
          </Text>
        </Stack>
      </Collapse>
    </Paper>
  )
}

function FinishReview({ role, drafts, who }: { role: Role; drafts: Drafts; who: Person }) {
  const [gate, setGate] = useState<GateDecision | 'comment'>('comment')
  const [steer, setSteer] = useState('')
  const n = Object.values(drafts).filter(Boolean).length
  const blocked = gateBlockReason(role, who, false)
  return (
    <Paper withBorder radius="md" p="xs" style={{ position: 'sticky', top: 8 }}>
      <Group justify="space-between">
        <Text size="sm" fw={700}>
          Finish your review
        </Text>
        <Badge size="sm" color="warning" variant="light">
          {n} pending
        </Badge>
      </Group>
      <Group gap={4} mt={4} wrap="wrap">
        <Text size="xs" c="dimmed">
          as
        </Text>
        <PersonChip login={who.login} size="xs" trustRepo={REPO} />
      </Group>
      <Box mt={4}>
        <OwnersLine owners={OWNERS_REFUNDS} />
      </Box>
      <Textarea size="xs" mt={6} autosize minRows={2} placeholder="Leave a review comment (optional)" />
      <Box mt={8}>
        <BotApprovalLine drafts={drafts} who={who} />
      </Box>
      <Radio.Group value={gate} onChange={(v) => setGate(v as GateDecision | 'comment')} mt={4}>
        <Stack gap={6}>
          <Radio size="xs" value="comment" label="Replies only" description="Post the thread replies. The gate stays open." />
          <Radio size="xs" value="approve" disabled={!!blocked} label="Approve" description="antiphon/approval → success @ 3b81f0d" />
          <Radio size="xs" value="another-round" disabled={!!blocked} label="Another round" description="With an optional steer prompt" />
          <Radio size="xs" value="close" disabled={!!blocked} label="Close run" />
        </Stack>
      </Radio.Group>
      {gate === 'another-round' && (
        <Textarea
          size="xs"
          mt={6}
          autosize
          minRows={2}
          placeholder="Steer: what should round 4 focus on?"
          value={steer}
          onChange={(e) => setSteer(e.currentTarget.value)}
        />
      )}
      {blocked && (
        <Group gap={4} mt={6} wrap="nowrap" align="flex-start">
          <TbLock size={12} style={{ flexShrink: 0, marginTop: 3 }} />
          <Text size="xs" c="dimmed">
            Gate decisions disabled: {blocked}
          </Text>
        </Group>
      )}
      <Button size="xs" fullWidth mt={8} leftSection={<TbSend size={12} />} disabled={n === 0 && gate === 'comment'}>
        {gate === 'comment' ? `Post ${n} replies as ${who.name}` : gate === 'approve' ? `Post ${n} replies + approve` : gate === 'another-round' ? `Post ${n} replies + round 4` : 'Close run'}
      </Button>
      <Text size="xs" c="dimmed" mt={4}>
        One GitHub review · signed "— {who.name} via Antiphon" · no @mention.
      </Text>
      <Divider my={8} />
      <BlastPanel blast={PR612.blast!} compact />
    </Paper>
  )
}

function GithubLike({ initialRole }: { initialRole: Role }) {
  const [role, setRole] = useState<Role>(initialRole)
  return <GithubLikeInner key={role} role={role} onRole={setRole} />
}

function GithubLikeInner({ role, onRole }: { role: Role; onRole: (r: Role) => void }) {
  const [drafts, setDraft] = useDrafts(role)
  const who = VIEWERS[role]
  const files = useMemo(() => {
    const m = new Map<string, Finding[]>()
    for (const f of FINDINGS.filter((x) => x.inline)) m.set(f.file, [...(m.get(f.file) ?? []), f])
    return [...m.entries()]
  }, [])
  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        <PrTitle />
        <ViewerToggle role={role} onChange={onRole} />
      </Group>
      <Group justify="space-between" wrap="nowrap">
        <RoundStepper />
        <PostingBar dense />
      </Group>
      <StatusLine />
      <Box style={{ display: 'grid', gridTemplateColumns: '220px minmax(0, 1fr) 280px', gap: 12, alignItems: 'start' }}>
        <Paper withBorder radius="md" p={6} style={{ position: 'sticky', top: 8 }}>
          <SectionLabel>Files with threads</SectionLabel>
          {files.map(([path, fs]) => (
            <Group key={path} gap={4} wrap="nowrap" py={3}>
              <Text size="xs" ff="monospace" truncate style={{ flex: 1 }}>
                {path.split('/').pop()}
              </Text>
              {fs.map((f) => (
                <Box key={f.id} w={8} h={8} style={{ borderRadius: 4 }} bg={`${SEVERITY_META[f.severity].color}.6`} />
              ))}
            </Group>
          ))}
          <Divider my={6} />
          <Text size="xs" c="dimmed">
            15 more files without threads
          </Text>
        </Paper>
        <Box>
          <SummaryComment />
          {files.map(([path, fs]) => (
            <DiffFile key={path} path={path} findings={fs} drafts={drafts} setDraft={setDraft} who={who} />
          ))}
        </Box>
        <FinishReview role={role} drafts={drafts} who={who} />
      </Box>
    </Stack>
  )
}

// ---------------------------------------------------------------- (C) triage deck

type DeckStep = 'action' | 'reason' | 'scope'

function TriageDeck({ initialRole, startAtGate = false }: { initialRole: Role; startAtGate?: boolean }) {
  const [role, setRole] = useState<Role>(initialRole)
  return <TriageDeckInner key={role} role={role} onRole={setRole} startAtGate={startAtGate} />
}

function TriageDeckInner({ role, onRole, startAtGate }: { role: Role; onRole: (r: Role) => void; startAtGate: boolean }) {
  const deck = useMemo(() => FINDINGS.filter((f) => !f.belowBar), [])
  const [drafts, setDraft] = useDrafts(role)
  const [idx, setIdx] = useState(startAtGate ? deck.length : 0)
  const [step, setStep] = useState<DeckStep>('action')
  const [gate, setGate] = useState<GateDecision | null>(null)
  const [wide, setWide] = useState<string | null>(null)
  const textRef = useRef<HTMLTextAreaElement>(null)
  const who = VIEWERS[role]
  const atGate = idx === deck.length
  const f = deck[Math.min(idx, deck.length - 1)]
  const d = drafts[f.id]

  const next = useCallback(() => {
    setStep('action')
    setIdx((i) => Math.min(deck.length, i + 1))
  }, [deck.length])
  const prev = useCallback(() => {
    setStep('action')
    setIdx((i) => Math.max(0, i - 1))
  }, [])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const typing = e.target instanceof HTMLTextAreaElement || e.target instanceof HTMLInputElement
      if (typing) {
        if (e.key === 'Escape') (e.target as HTMLElement).blur()
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
          ;(e.target as HTMLElement).blur()
          next()
        }
        return
      }
      const k = e.key.toLowerCase()
      if (k === 'j' || k === 'arrowright') return next()
      if (k === 'k' || k === 'arrowleft') return prev()
      if (atGate) {
        if (role === 'reviewer') {
          if (k === 'a') setGate('approve')
          if (k === 'r') setGate('another-round')
          if (k === 'c') setGate('close')
        }
        return
      }
      if (f.status === 'pending-sync') return
      if (k === 'enter') return next()
      if (k === 'u') return setDraft(f.id, undefined)
      if (k === '/') {
        e.preventDefault()
        textRef.current?.focus()
        return
      }
      if (step === 'reason') {
        const r = (Object.keys(REASON_META) as DismissReason[]).find((x) => REASON_META[x].key === k)
        if (r) {
          setDraft(f.id, { ...(d ?? { text: '' }), action: 'dismiss', reason: r })
          setStep('scope')
        }
        return
      }
      if (step === 'scope') {
        const s = (Object.keys(SCOPE_META) as DismissScope[]).find((x) => SCOPE_META[x].key === k)
        if (s) {
          setDraft(f.id, { ...(d ?? { text: '' }), action: 'dismiss', scope: s, learn: undefined })
          setStep('action')
          if (s !== 'instance') setWide(f.id)
        }
        return
      }
      const a = actionsFor(f).find((x) => ACTION_META[x].key === k)
      if (a) {
        setDraft(f.id, { action: a, text: d?.text ?? '' })
        if (a === 'dismiss') setStep('reason')
        if (a === 'dispute' || a === 'question') setTimeout(() => textRef.current?.focus(), 0)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [atGate, d, f, next, prev, role, setDraft, step])

  const handled = deck.filter((x) => drafts[x.id] || x.status === 'will-fix' || x.status === 'pending-sync').length

  return (
    <Stack gap="sm" maw={1280} mx="auto">
      <Group justify="space-between" wrap="nowrap">
        <PrTitle compact />
        <Group gap="md" wrap="nowrap">
          <PostingBar dense />
          <ViewerToggle role={role} onChange={onRole} />
        </Group>
      </Group>
      <Group gap={8} wrap="nowrap">
        <Progress value={(handled / deck.length) * 100} size="sm" style={{ flex: 1 }} color="success" />
        <Text size="xs" c="dimmed" style={{ whiteSpace: 'nowrap' }}>
          {handled}/{deck.length} handled · card {Math.min(idx + 1, deck.length + 1)} of {deck.length + 1}
        </Text>
      </Group>
      <Box style={{ display: 'grid', gridTemplateColumns: '56px minmax(0, 1fr) 280px', gap: 12, alignItems: 'start' }}>
        <Stack gap={4}>
          {deck.map((x, i) => {
            const dx = drafts[x.id]
            return (
              <Tooltip key={x.id} label={`${x.id} · ${x.title}`} position="right">
                <UnstyledButton onClick={() => { setIdx(i); setStep('action') }}>
                  <Paper
                    p={4}
                    radius="sm"
                    ta="center"
                    style={{
                      border: i === idx ? '2px solid var(--mantine-color-active-4)' : '1px solid var(--mantine-color-dark-4)',
                      background: `var(--mantine-color-${SEVERITY_META[x.severity].color}-9)`,
                      opacity: dx || x.status !== 'open' ? 0.65 : 1,
                    }}
                  >
                    <Text size="xs" fw={700}>
                      {x.id}
                    </Text>
                    <Text size="9px" c="dimmed" tt="uppercase">
                      {dx ? ACTION_META[dx.action].label : x.status === 'open' ? x.severity : x.status === 'pending-sync' ? 'sync' : 'will fix'}
                    </Text>
                  </Paper>
                </UnstyledButton>
              </Tooltip>
            )
          })}
          <UnstyledButton onClick={() => setIdx(deck.length)}>
            <Paper p={4} radius="sm" ta="center" style={{ border: atGate ? '2px solid var(--mantine-color-pink-4)' : '1px dashed var(--mantine-color-pink-7)' }}>
              <Text size="xs" fw={700} c="pink.4">
                Gate
              </Text>
            </Paper>
          </UnstyledButton>
        </Stack>

        {atGate ? (
          <Paper withBorder radius="lg" p="lg" style={{ borderColor: 'var(--mantine-color-pink-7)' }}>
            <Text size="xl" fw={700}>
              {role === 'reviewer' ? 'Decide the gate' : 'Send your replies'}
            </Text>
            <Text size="sm" c="dimmed" mb="md">
              {handled}/{deck.length} findings handled. {role === 'reviewer' ? 'Your replies and the decision post as one GitHub review.' : 'Hugo Martin decides the gate; your replies count as human involvement and reset the bot-only counter.'}
            </Text>
            <BotApprovalLine drafts={drafts} who={who} />
            {role !== 'reviewer' && (
              <Group gap={4} mb="sm" wrap="nowrap" align="flex-start">
                <TbLock size={12} style={{ flexShrink: 0, marginTop: 3 }} />
                <Text size="xs" c="dimmed">
                  {gateBlockReason(role, who, false)}
                </Text>
              </Group>
            )}
            {role === 'reviewer' ? (
              <Stack gap="sm">
                <Group gap="sm" grow>
                  {(
                    [
                      ['approve', 'a', 'Approve', 'success', 'antiphon/approval → success @ 3b81f0d'],
                      ['another-round', 'r', 'Another round', 'pink', 'Round 4, resets the bot-only counter'],
                      ['close', 'c', 'Close run', 'gray', 'Stop reviewing this PR'],
                    ] as const
                  ).map(([v, key, label, color, desc]) => (
                    <UnstyledButton key={v} onClick={() => setGate(v)}>
                      <Paper withBorder p="sm" radius="md" style={{ borderColor: gate === v ? `var(--mantine-color-${color}-5)` : undefined, background: gate === v ? `var(--mantine-color-${color}-9)` : undefined }}>
                        <Group justify="space-between">
                          <Text fw={700}>{label}</Text>
                          <Kbd size="xs">{key.toUpperCase()}</Kbd>
                        </Group>
                        <Text size="xs" c="dimmed">
                          {desc}
                        </Text>
                      </Paper>
                    </UnstyledButton>
                  ))}
                </Group>
                {gate === 'another-round' && (
                  <Textarea size="sm" autosize minRows={3} label="Steer prompt" placeholder="What should round 4 look at? Goes into every reviewer pack as 'Human steer from Hugo Martin: …'" />
                )}
                <Group gap={4}>
                  {(PR612.humanRequired ?? []).map((r) => (
                    <Badge key={r} size="xs" color="pink" variant="outline">
                      {r}
                    </Badge>
                  ))}
                </Group>
              </Stack>
            ) : (
              <SubmitBar drafts={drafts} who={who} sticky={false} />
            )}
            {role === 'reviewer' && (
              <Box mt="md">
                <SubmitBar drafts={drafts} who={who} sticky={false} />
              </Box>
            )}
          </Paper>
        ) : (
          <Paper withBorder radius="lg" p="md" style={{ borderTop: `4px solid var(--mantine-color-${SEVERITY_META[f.severity].color}-6)` }}>
            <FindingMeta f={f} draft={d} />
            <Text size="xl" fw={700} mt={4} lh={1.25}>
              {f.title}
            </Text>
            <Text size="xs" ff="monospace" c="dimmed" mb="sm">
              {f.file}:{f.lines} · {f.check === 'human' ? `raised by ${personName(f.humanRaisedBy)}` : f.check}
            </Text>
            <Stack gap="sm">
              <PendingSync f={f} />
              <HunkView hunk={f.hunk} anchor={f.anchor} maxH={220} />
              <Text size="sm">{f.body}</Text>
              <SuggestionView f={f} />
              <ThreadView f={f} max={2} />
              <Divider />
              {f.status === 'pending-sync' ? (
                <Text size="sm" c="dimmed">
                  Waiting on GitHub. <Kbd size="xs">J</Kbd> to skip.
                </Text>
              ) : (
                <Stack gap={6}>
                  <Group gap={6}>
                    {actionsFor(f).map((a) => (
                      <Button
                        key={a}
                        size="xs"
                        variant={d?.action === a ? 'filled' : 'default'}
                        color={ACTION_META[a].color}
                        onClick={() => {
                          setDraft(f.id, { action: a, text: d?.text ?? '' })
                          setStep(a === 'dismiss' ? 'reason' : 'action')
                        }}
                        rightSection={<Kbd size="xs">{ACTION_META[a].key.toUpperCase()}</Kbd>}
                      >
                        {ACTION_META[a].label}
                      </Button>
                    ))}
                  </Group>
                  {(step === 'reason' || (d?.action === 'dismiss' && step !== 'scope')) && (
                    <Group gap={4}>
                      <Text size="xs" c="dimmed" w={50}>
                        Reason
                      </Text>
                      {(Object.keys(REASON_META) as DismissReason[]).map((r) => (
                        <Button
                          key={r}
                          size="compact-xs"
                          variant={d?.reason === r ? 'filled' : step === 'reason' ? 'light' : 'default'}
                          color="gray"
                          onClick={() => { setDraft(f.id, { ...(d ?? { text: '' }), action: 'dismiss', reason: r }); setStep('scope') }}
                          leftSection={<Kbd size="xs">{REASON_META[r].key}</Kbd>}
                        >
                          {REASON_META[r].label}
                        </Button>
                      ))}
                    </Group>
                  )}
                  {(step === 'scope' || (d?.action === 'dismiss' && d.reason)) && (
                    <Group gap={4}>
                      <Text size="xs" c="dimmed" w={50}>
                        Scope
                      </Text>
                      {(Object.keys(SCOPE_META) as DismissScope[]).map((s) => (
                        <Tooltip key={s} label={SCOPE_META[s].hint}>
                          <Button
                            size="compact-xs"
                            variant={d?.scope === s ? 'filled' : step === 'scope' ? 'light' : 'default'}
                            color="gray"
                            onClick={() => {
                              setDraft(f.id, { ...(d ?? { text: '' }), action: 'dismiss', scope: s, learn: undefined })
                              setStep('action')
                              if (s !== 'instance') setWide(f.id)
                            }}
                            leftSection={<Kbd size="xs">{SCOPE_META[s].key.toUpperCase()}</Kbd>}
                          >
                            {SCOPE_META[s].label}
                          </Button>
                        </Tooltip>
                      ))}
                    </Group>
                  )}
                  <Textarea
                    ref={textRef}
                    size="xs"
                    autosize
                    minRows={1}
                    disabled={!d}
                    placeholder={d ? 'Note (Ctrl+Enter saves and moves on)' : 'Pick an action first'}
                    value={d?.text ?? ''}
                    onChange={(e) => d && setDraft(f.id, { ...d, text: e.currentTarget.value })}
                  />
                  {d?.action === 'not-blocker' && <NotBlockerPanel who={who} />}
                  {d?.action === 'dismiss' && d.scope && d.scope !== 'instance' && (
                    <Group gap={6}>
                      <LearnBadge learn={d.learn} />
                      {!d.learn && (
                        <Text size="xs" c="warning.4">
                          Wide scope: confirm the rule first.
                        </Text>
                      )}
                      <Button size="compact-xs" variant="light" color="cyan" disabled={!d.reason} onClick={() => setWide(f.id)}>
                        {d.learn ? 'Review' : 'Review and confirm'}
                      </Button>
                    </Group>
                  )}
                  {d && (
                    <Text size="xs" c="dimmed" style={{ whiteSpace: 'pre-wrap' }}>
                      → {replyPreview(d, who).replace('\n\n', ' ')}
                    </Text>
                  )}
                  {d?.action === 'dismiss' && d.reason && d.scope && d.scope !== 'instance' && (
                    <WideScopeModal
                      key={`${f.id}:${d.scope}`}
                      opened={wide === f.id}
                      f={f}
                      reason={d.reason}
                      scope={d.scope}
                      text={d.text}
                      who={who}
                      onClose={() => setWide(null)}
                      onDone={(learn) => {
                        setDraft(f.id, { ...d, learn })
                        setWide(null)
                      }}
                    />
                  )}
                </Stack>
              )}
            </Stack>
          </Paper>
        )}

        <Stack gap="sm">
          <Paper withBorder p="xs" radius="md">
            <Group gap={6} mb={6}>
              <TbKeyboard size={14} />
              <Text size="xs" fw={700}>
                Keys
              </Text>
            </Group>
            <Stack gap={3}>
              {[
                ['J / K', 'next / previous'],
                ['W F D X Q L', 'will fix, fixed, dismiss, dispute, question, defer'],
                ['B', 'not a blocker (blockers only; trust ≥ 60 clears alone)'],
                ['1–7', 'dismiss reason'],
                ['I P R T', 'scope: instance, path, repo, team (P R T open the confirmation)'],
                ['/', 'write a note'],
                ['Ctrl+Enter', 'save note and move on'],
                ['U', 'undo this card'],
                ['A R C', 'on the gate card: approve, another round, close'],
              ].map(([k, v]) => (
                <Group key={k} gap={6} wrap="nowrap" align="flex-start">
                  <Kbd size="xs" style={{ whiteSpace: 'nowrap' }}>
                    {k}
                  </Kbd>
                  <Text size="xs" c="dimmed">
                    {v}
                  </Text>
                </Group>
              ))}
            </Stack>
          </Paper>
          {!atGate && <WhyCheck f={f} />}
          <Paper withBorder p="xs" radius="md">
            <SectionLabel>Blast radius</SectionLabel>
            <BlastPanel blast={PR612.blast!} compact />
          </Paper>
          <Paper withBorder p="xs" radius="md">
            <Group gap={4} wrap="nowrap" align="flex-start">
              <TbAlertTriangle size={12} color="var(--mantine-color-pink-4)" style={{ flexShrink: 0, marginTop: 3 }} />
              <Text size="xs">Gate: human-review · Hugo Martin · 3.6/4 wh</Text>
            </Group>
            <Group gap={4} mt={4} wrap="nowrap" align="flex-start">
              <TbCircleCheckFilled size={12} color="var(--mantine-color-success-5)" style={{ flexShrink: 0, marginTop: 3 }} />
              <Text size="xs" c="dimmed">
                3 accepted earlier (fixed and confirmed by recheck)
              </Text>
            </Group>
          </Paper>
        </Stack>
      </Box>
    </Stack>
  )
}

const meta: Meta = {
  title: 'Proposals/Review Engine/PR Review',
  parameters: { layout: 'fullscreen' },
}
export default meta

type Story = StoryObj

/** (A) Findings list + detail + context rail (gate, blast radius). The author's view, with 4 replies drafted. */
export const SplitAuthor: Story = { name: 'A · Split view (author)', render: () => <SplitView initialRole="author" /> }

/** (A) The same screen as the gate reviewer: gate decisions enabled, with a steer prompt for another round. */
export const SplitReviewer: Story = { name: 'A · Split view (gate reviewer)', render: () => <SplitView initialRole="reviewer" /> }

/** (B) GitHub-like: thread cards under their anchor lines in the diff, plus a "finish your review" box carrying the gate. */
export const GithubInline: Story = { name: 'B · Inline threads over diff', render: () => <GithubLike initialRole="reviewer" /> }

/** (A) Kenji Sato (Trust 41 · payments-api) marks the blocker F1 "Not a blocker": below the clear-blocker threshold (60) it needs a second trusted reviewer and opens queue item Q-5523; bot approval stays blocked. He also can't decide the gate (decide-gate 50). */
export const SplitLowTrustBlocker: Story = { name: 'A · Split view (low-trust member: "Not a blocker")', render: () => <SplitView initialRole="member" /> }

/** (A) The solo-owner exception: the PR's paths are owned only by refund-tooling (one member, the author) with no other code owners, so the author may decide the gate, with the note "solo owner · repo override off/on". */
export const SplitSoloOwner: Story = { name: 'A · Split view (author, solo-owner exception)', render: () => <SplitView initialRole="author" solo /> }

/** (A) Hugo's repo-scope dismissal of F8 opens the confirmation: target layer, rule text, file, trust check, and a steer. Press "Draft with agent": drafting → diff → "Learn PR #4821 opened", linked both ways with F8 and its thread. Clear the steer to open the PR directly. */
export const SplitWideScopeDismissal: Story = {
  name: 'A · Wide-scope dismissal (confirm, steer, learn PR)',
  render: () => <SplitView initialRole="reviewer" initialSel="F8" wideSteer="Only until the TimeProvider migration (CARD-2210) lands; keep flagging DateTime.UtcNow in files a PR adds." />,
}

/** (C) One finding per card. Keyboard first: W/F/D/X/Q/L, 1–7 reasons, I/P/R/T scopes, J/K to move. The last card is the gate. */
export const TriageDeckAuthor: Story = { name: 'C · Triage deck (keyboard)', render: () => <TriageDeck initialRole="author" /> }

/** (C) The deck as the gate reviewer: the final card decides the gate (A/R/C). */
export const TriageDeckReviewer: Story = { name: 'C · Triage deck → gate card', render: () => <TriageDeck initialRole="reviewer" startAtGate /> }
