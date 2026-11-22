/**
 * PROPOSAL MOCK — not production code. Artifact registry for the review-engine Storybook
 * prototypes (`docs/features/012-review-engine/design.md` §3.6): what each pr-review node
 * produces and consumes, how each artifact is validated, where its definition is edited, and the
 * inputs every node decides from. Example instances come from payments-api#618, iteration 3.
 * Pure data and helpers; nothing outside `src/stories/review-engine/` imports this file.
 */

export type ArtifactKind = 'decision' | 'abf-document' | 'json-record' | 'file' | 'code-host-object' | 'event'

export const ARTIFACT_KINDS: ArtifactKind[] = ['decision', 'abf-document', 'json-record', 'file', 'code-host-object', 'event']

export const ARTIFACT_KIND_META: Record<ArtifactKind, { label: string; color: string; blurb: string }> = {
  decision: { label: 'Decision', color: 'cyan', blurb: 'Typed answers from IDecisionModel: choice, score or yes/no, each with probabilities, confidence and the model that served it.' },
  'abf-document': { label: 'ABF document', color: 'grape', blurb: 'Model-written text in Antiphon Block Format, parsed and stored as canonical JSON.' },
  'json-record': { label: 'JSON record', color: 'gray', blurb: 'A structured record written by a code step, stored as canonical JSON in node_execution.outputs.' },
  file: { label: 'File', color: 'indigo', blurb: "A file in the run's artifact store, content-addressed and staged to runners." },
  'code-host-object': { label: 'Code-host object', color: 'green', blurb: 'Something that lives on the code host: a PR review, comments, commit statuses.' },
  event: { label: 'Event', color: 'orange', blurb: 'Something that happened: a push, a recheck, a human gate decision, a timer.' },
}

export type Validation = 'valid' | 'warning' | 'failed' | 'pending'

export const VALIDATION_META: Record<Validation, { label: string; color: string }> = {
  valid: { label: 'valid', color: 'success' },
  warning: { label: 'warning', color: 'warning' },
  failed: { label: 'failed', color: 'danger' },
  pending: { label: 'pending', color: 'gray' },
}

export interface ArtifactField {
  name: string
  type: string
  description: string
}

export interface ArtifactRule {
  id: string
  text: string
}

export interface EditLocation {
  what: string
  path: string
  layers: string
  note?: string
}

export interface ArtifactDef {
  id: string
  title: string
  kind: ArtifactKind
  format: string
  purpose: string
  fields: ArtifactField[]
  producedBy: string[]
  consumedBy: string[]
  rules: ArtifactRule[]
  storage: string
  retention: string
  editIn: EditLocation[]
  perGroup?: boolean
  link?: string
  example: string
}

const LAYERED = 'common@v14 → payments-core → repo'

export const ARTIFACTS: ArtifactDef[] = [
  {
    id: 'pr-snapshot',
    title: 'PR snapshot',
    kind: 'json-record',
    format: 'pr-snapshot/1',
    purpose: 'Pins exactly what is being reviewed: base and head SHAs, the PR text, the changed files, and the review config read at the base SHA, plus code-only tags.',
    fields: [
      { name: 'baseSha / headSha', type: 'sha', description: 'Pinned at intake; every later step reads code at these SHAs.' },
      { name: 'title / body', type: 'string', description: 'PR text. Untrusted data: never instructions.' },
      { name: 'files[]', type: '{ path, status, add, del, generated }', description: 'Changed files with line stats.' },
      { name: 'tags', type: '{ languages, areas, classes, size }', description: 'Code-only tags: no model involved.' },
      { name: 'owners[]', type: 'team slug[]', description: 'CODEOWNERS owners of the changed paths, primary first.' },
      { name: 'configRevision', type: 'sha256', description: 'Hash of the resolved .antiphon/review/ config at the base SHA.' },
    ],
    producedBy: ['intake'],
    consumedBy: ['delta', 'categorise', 'packs', 'needs-human-approval', 'publish'],
    rules: [
      { id: 'pinned', text: 'Base and head SHAs exist in the server mirror.' },
      { id: 'base-config', text: 'Policy, checks and workflow are read at the base SHA, never the PR head (D-8).' },
      { id: 'tags', text: 'Every changed file has a language or is marked generated/binary.' },
    ],
    storage: 'node_execution.outputs (canonical JSON) · review_iteration.head_sha',
    retention: 'Life of the run, then 400 days',
    editIn: [
      { what: 'Area tags', path: 'antiphon.areas.json', layers: 'repo' },
      { what: 'Path-glob classes and size buckets', path: '.antiphon/review/policy.yaml → intake.tags', layers: LAYERED },
      { what: 'Owners', path: 'CODEOWNERS', layers: 'repo' },
    ],
    example: `{
  "subject": "lumen/payments-api#618",
  "baseSha": "1f20c9e", "headSha": "e41b07c",
  "title": "Ledger: reconcile partial refunds nightly",
  "files": [
    { "path": "src/Ledger/RefundReconciler.cs", "status": "modified", "add": 212, "del": 0 },
    { "path": "db/migrations/20261120_ledger_recon.sql", "status": "added", "add": 33, "del": 0 }
  ],
  "tags": { "languages": ["csharp", "sql"], "areas": ["ledger", "refunds"], "size": "L" },
  "owners": ["payments-core"],
  "configRevision": "sha256:4be1c09…"
}`,
  },
  {
    id: 'change-delta',
    title: 'Change delta',
    kind: 'json-record',
    format: 'change-delta/1',
    purpose: 'On a recheck, what changed since the last reviewed head, where each open finding moved to, and whether it is still present.',
    fields: [
      { name: 'mode', type: "'full' | 'delta' | 'interdiff' | 'base-only'", description: 'Full on the first iteration; interdiff after a force-push.' },
      { name: 'fromSha / toSha', type: 'sha', description: 'Last reviewed head → new head.' },
      { name: 'changedFiles[]', type: 'path[]', description: 'Only these go through categorise/select/review again.' },
      { name: 'reanchored[]', type: '{ finding, from, to, outdated }', description: 'Each open finding moved through the diff by its quoted anchor.' },
      { name: 'stillPresent[]', type: '{ finding, p, rebuttalStrength? }', description: 'Jev yes/no per open finding.' },
    ],
    producedBy: ['delta'],
    consumedBy: ['categorise', 'select', 'packs', 'collate'],
    rules: [
      { id: 'anchors', text: 'Every open finding is re-anchored or marked outdated.' },
      { id: 'still-present', text: 'Every open, non-outdated finding has a still_present answer.' },
    ],
    storage: 'node_execution.outputs · finding_event (re-anchors)',
    retention: 'Life of the run, then 400 days',
    editIn: [
      { what: 'still_present / rebuttal_strength questions', path: '.antiphon/review/questions/recheck.yaml', layers: LAYERED },
      { what: 'Interdiff and slicing rules', path: 'Delta (C# step, ships with Antiphon)', layers: 'Antiphon release' },
    ],
    example: `{
  "mode": "delta", "fromSha": "4a77b10", "toSha": "e41b07c",
  "changedFiles": ["src/Ledger/RefundReconciler.cs", "src/Ledger/LedgerEntry.cs"],
  "reanchored": [{ "finding": "F3", "from": "RefundReconciler.cs:88", "to": "RefundReconciler.cs:91" }],
  "stillPresent": [
    { "finding": "F3", "p": 0.82, "rebuttalStrength": 0.41 },
    { "finding": "F5", "p": 0.77 }
  ]
}`,
  },
  {
    id: 'categorisation',
    title: 'Categorisation',
    kind: 'decision',
    format: 'decision-response/1.0 (canonical JSON)',
    purpose: 'What kind of change this is, which risk areas it touches, and its blast radius. One batched Jev call; unsure answers escalate up the chain one question at a time.',
    fields: [
      { name: 'answers.change_kind', type: 'choice', description: 'feature | bugfix | refactor | test | docs | infra | dependency' },
      { name: 'answers.<risk area>', type: 'noul (p yes)', description: 'concurrency, persistence, auth/security, session-runtime, infra, perf hot path, plus layer additions.' },
      { name: 'answers.reach … exposure', type: 'score 1–5', description: 'The blast-radius dimensions (§4.3).' },
      { name: 'answers.*.servedBy', type: '{ provider, transport, resolvedModel }', description: 'Who answered, after any escalation.' },
      { name: 'usage / costUsd', type: 'number', description: 'Input tokens and cost from x-litellm-response-cost.' },
    ],
    producedBy: ['categorise'],
    consumedBy: ['select', 'group', 'packs', 'needs-human-approval', 'publish', 'human-approve', 'human-review'],
    rules: [
      { id: 'answered', text: 'Every question is answered or explicitly refused.' },
      { id: 'served-by', text: 'Every answer records the resolved model that served it.' },
      { id: 'confidence', text: 'Answers below their provider threshold were escalated (only that question).' },
      { id: 'limits', text: 'Each Jev call has 1–128 questions and ≤ 64k tokens; larger PRs are split by area.' },
    ],
    storage: 'node_execution.outputs · llm_call · decision_cache (model, state hash, question hash)',
    retention: 'Life of the run, then 400 days; cache 30 days',
    editIn: [
      { what: 'Questions and instructions', path: '.antiphon/review/questions/categorise.yaml', layers: LAYERED },
      { what: 'Decision chain and thresholds', path: 'Antiphon → Settings → Providers → decisions-default', layers: 'tenant' },
    ],
    example: `{
  "model": "jev-latest", "resolvedModel": "typesafe/jev-1.14.0",
  "answers": {
    "change_kind":         { "value": "feature", "confidence": 0.91 },
    "persistent_data":     { "value": 0.93 },
    "external_contract":   { "value": 0.18 },
    "concurrency":         { "value": 0.71, "servedBy": "luna seat (escalated: jev 0.48 < 0.60)" },
    "reach":               { "value": 3, "probabilities": [0.02, 0.21, 0.58, 0.16, 0.03] },
    "rollback_at_release": { "value": 4 },
    "reversal_after_months": { "value": 3 },
    "exposure":            { "value": 2 }
  },
  "usage": { "input_tokens": 19900 }, "costUsd": 0.0008
}`,
  },
  {
    id: 'check-selection',
    title: 'Check selection',
    kind: 'decision',
    format: 'check-selection/1 (canonical JSON)',
    purpose: 'Which checks from the catalogue apply to this change this round, and which were rejected, each with its probability, so misses can be traced later.',
    fields: [
      { name: 'candidates', type: 'number', description: 'Left after the code prefilter (triggers + embedding similarity).' },
      { name: 'selected[]', type: '{ check, p, status }', description: 'Jev applies = yes, after the posting-bar filter.' },
      { name: 'rejected[]', type: '{ check, p, reason }', description: 'Kept for learning: "why wasn\'t CHK-x run?"' },
      { name: 'calls', type: 'number', description: 'Jev calls (≤ 128 questions each).' },
    ],
    producedBy: ['select'],
    consumedBy: ['group', 'needs-human-approval'],
    rules: [
      { id: 'recorded', text: 'Every candidate is recorded as selected or rejected, with its probability.' },
      { id: 'split', text: 'No Jev call carries more than 128 questions.' },
      { id: 'bar', text: "Checks whose severity can't clear the round's bar are dropped unless in a bypass category." },
    ],
    storage: 'node_execution.outputs · check_stat (selected counts)',
    retention: 'Life of the run, then 400 days',
    editIn: [
      { what: 'Each check’s Selector text and triggers', path: '.antiphon/review/checks/<category>/<id>.md → # @model: Selector', layers: LAYERED },
      { what: 'Posting bar per round', path: '.antiphon/review/policy.yaml → rounds.post', layers: LAYERED },
    ],
    example: `{
  "candidates": 163, "calls": 2,
  "selected": [
    { "check": "CHK-0142", "p": 0.94, "status": "active" },
    { "check": "CHK-0745", "p": 0.71, "status": "candidate", "via": "steer" }
  ],
  "rejected": [
    { "check": "CHK-0618", "p": 0.03, "reason": "no-match" },
    { "check": "CHK-0640", "p": 0.88, "reason": "same prompt hash as CHK-0033" }
  ]
}`,
  },
  {
    id: 'reviewer-groups',
    title: 'Reviewer groups',
    kind: 'json-record',
    format: 'reviewer-groups/1',
    purpose: 'How the selected checks are split between reviewers, and at which model tier each reviewer runs.',
    fields: [
      { name: 'groups[]', type: '{ name, checks[], tier, model, suffixTokens }', description: 'One reviewer per group.' },
      { name: 'bypass', type: 'group name[]', description: 'Groups holding security / data-loss checks.' },
    ],
    producedBy: ['group'],
    consumedBy: ['packs', 'review'],
    rules: [
      { id: 'once', text: 'Every selected check is in exactly one group.' },
      { id: 'bypass', text: 'Bypass categories get their own group.' },
      { id: 'suffix', text: 'Each group suffix fits its token budget (8k).' },
    ],
    storage: 'node_execution.outputs',
    retention: 'Life of the run, then 400 days',
    editIn: [{ what: 'Group definitions, affinity and tiers', path: '.antiphon/review/groups/*.yaml', layers: LAYERED }],
    example: `{
  "groups": [
    { "name": "security",    "checks": ["CHK-0311", "CHK-0402"], "tier": "high", "model": "opus" },
    { "name": "concurrency", "checks": ["CHK-0142", "CHK-0741"], "tier": "high", "model": "opus" },
    { "name": "data",        "checks": ["CHK-0087", "CHK-0510", "CHK-0745"], "tier": "standard", "model": "sonnet" },
    { "name": "general",     "checks": ["CHK-0033", "CHK-0219", "CHK-0122"], "tier": "standard", "model": "sonnet" }
  ],
  "bypass": ["security", "data"]
}`,
  },
  {
    id: 'context-pack',
    title: 'Context pack',
    kind: 'file',
    format: 'context-pack/1 (markdown file + ledger rows)',
    perGroup: true,
    link: 'Open pack viewer',
    purpose: "Everything one reviewer reads, in one file: a shared prefix (PR, diff, related files with reasons) that is byte-identical across the run's reviewers so the prompt cache applies, then that group's check prompts and the output contract.",
    fields: [
      { name: 'prefix', type: 'markdown', description: 'PR metadata, categorisation, blast radius, round + steer, open findings, numbered diff, related files.' },
      { name: 'prefixHash', type: 'sha256', description: 'Must match across every pack in the iteration.' },
      { name: 'suffix', type: 'markdown', description: "The group's checks' @model sections and the review-findings/1 contract." },
      { name: 'ledger[]', type: 'context_item', description: 'One row per candidate file: source, reason, score, tokens, included.' },
      { name: 'stagedTo', type: 'runner id + path', description: 'Where the runner reads it.' },
    ],
    producedBy: ['packs'],
    consumedBy: ['reviewer', 'verify'],
    rules: [
      { id: 'prefix-identical', text: "The shared prefix is byte-identical across the iteration's packs (same sha256)." },
      { id: 'budget', text: 'Prefix ≤ 60k tokens; prefix + suffix ≤ 68k.' },
      { id: 'checks-present', text: "Every assigned check's @model sections (Prompt, Examples) are in the suffix." },
      { id: 'ledger', text: 'Every included file has a ledger row with source and reason.' },
      { id: 'no-head-config', text: "Nothing from the PR head's .antiphon/ is included (config comes from base)." },
      { id: 'contract', text: 'The output contract names review-findings/1.0.' },
    ],
    storage: 'artifacts/<run>/<iteration>/pack-<group>.md · pack (hash, staged path) · context_item',
    retention: '30 days after the run closes (rebuildable from hashes for replay)',
    editIn: [
      { what: 'Related-file rules', path: '.antiphon/review/related.yaml', layers: LAYERED },
      { what: 'Per-check related files', path: '.antiphon/review/checks/<category>/<id>.md → relatedFiles', layers: LAYERED },
      { what: 'Token budget, whole-file threshold', path: '.antiphon/review/policy.yaml → packs', layers: LAYERED },
      { what: 'Output contract', path: 'docs/specs/block-format/v1.md §7.1', layers: 'Antiphon release' },
    ],
    example: `# Context pack · payments-api#618 · iteration 3 · group: concurrency
## PR
Ledger: reconcile partial refunds nightly (cwei) · base 1f20c9e → head e41b07c
Round 3. Human steer from Hugo Martin: "check the refund ledger reconciliation
path against the double-entry invariant in src/Ledger/README.md"
## Outstanding findings (do not re-report)
F3 major  src/Ledger/RefundReconciler.cs:91  over-refund race …
## Diff (numbered)  ·  ## Related files (ledger reasons)  …
---- shared prefix ends · sha256:9c41e2a7… ----
## Checks: CHK-0142, CHK-0741   ## Output: @abf 1 review-findings/1.0`,
  },
  {
    id: 'review-findings',
    title: 'Review findings',
    kind: 'abf-document',
    format: 'review-findings/1.0 (ABF)',
    perGroup: true,
    purpose: "One reviewer's output: its findings with quoted anchors, an account of every assigned check, and the files it opened beyond the pack.",
    fields: [
      { name: 'finding <id>', type: 'record 0..n', description: 'check, severity, flags, confidence, file, lines, anchor, title, body, suggestion.' },
      { name: 'checks', type: 'record 1', description: '<check> pass | finding | n/a | unverified. Every assigned check must appear.' },
      { name: 'context', type: 'record 0..1', description: 'opened / missing files: the main signal for improving packs.' },
      { name: 'summary', type: 'record 0..1', description: 'Reviewer note; not posted unless policy says so.' },
    ],
    producedBy: ['reviewer'],
    consumedBy: ['collate', 'needs-human-approval'],
    rules: [
      { id: 'parse', text: 'Parses as review-findings/1.x; one cheap repair call on failure, then the reviewer is retried.' },
      { id: 'checks-accounted', text: 'Every assigned check appears in the checks record (missing = error).' },
      { id: 'anchors', text: 'Every anchor resolves at the reviewed SHA; unresolved findings are dropped and recorded.' },
    ],
    storage: 'artifacts/<run>/<iteration>/findings-<group>.abf · canonical JSON in node_execution.outputs',
    retention: 'Forever (findings are evidence for incident investigations)',
    editIn: [
      { what: 'Output contract', path: 'docs/specs/block-format/v1.md §7.1', layers: 'Antiphon release' },
      { what: 'Reviewer role prompt', path: '.antiphon/review/prompts/reviewer.md', layers: LAYERED },
    ],
    example: `@abf 1 review-findings/1.0
=== finding F1
check: CHK-0087
severity: major
flags: data-loss
confidence: 0.84
file: db/migrations/20261120_ledger_recon.sql
lines: 14
anchor: DELETE FROM ledger_entries WHERE kind = 'refund_pending';
--- body
Pending refund entries are deleted before the reconciled rows are written…
=== checks
CHK-0087 finding F1
CHK-0510 pass
CHK-0745 unverified needs PSP sandbox data
=== context
opened src/Ledger/Exports/SettlementCsvWriter.cs followed SettlementCsv to its writer
=== end`,
  },
  {
    id: 'collated-findings',
    title: 'Collated findings',
    kind: 'json-record',
    format: 'collated-findings/1',
    purpose: "All reviewers' findings after duplicates are collapsed across groups, against open findings and against human comments, with anchors re-resolved at the reviewed SHA.",
    fields: [
      { name: 'findings[]', type: 'finding + sources[]', description: 'Each with the reviewer groups that raised it.' },
      { name: 'collapsed[]', type: '{ kept, dropped[], similarity }', description: 'Duplicates merged into one.' },
      { name: 'dropped[]', type: '{ finding, reason }', description: 'e.g. anchor not found.' },
    ],
    producedBy: ['collate'],
    consumedBy: ['verify'],
    rules: [
      { id: 'join', text: 'Every reviewer in the fan-out reported (policy: all).' },
      { id: 'dedupe', text: 'No two kept findings share an anchor and check with similarity ≥ 0.86.' },
    ],
    storage: 'node_execution.outputs · finding',
    retention: 'Forever',
    editIn: [{ what: 'Dedupe similarity threshold', path: '.antiphon/review/policy.yaml → collate.dedupeSimilarity', layers: LAYERED }],
    example: `{
  "findings": [{ "id": "F7", "check": "CHK-0087", "severity": "major", "sources": ["data"] }],
  "collapsed": [{ "kept": "F7", "dropped": ["general:F2"], "similarity": 0.91 }],
  "dropped": []
}`,
  },
  {
    id: 'verified-findings',
    title: 'Verified findings',
    kind: 'abf-document',
    format: 'verify-result/1.0 (ABF via completions-cheap)',
    purpose: 'A cheaper model tries to disprove each finding. Survivors go on to posting; disproved findings are dropped and recorded so the checks that produced them can be tuned.',
    fields: [
      { name: 'kept[]', type: 'finding id[]', description: 'Not disproved.' },
      { name: 'disproved[]', type: '{ finding, reason }', description: 'Dropped, with the verifier\'s reasoning.' },
    ],
    producedBy: ['verify'],
    consumedBy: ['publish', 'route'],
    rules: [
      { id: 'bypass', text: 'Security / data-loss findings cannot be dropped by the verifier alone.' },
      { id: 'all', text: 'Every collated finding is either kept or disproved.' },
    ],
    storage: 'node_execution.outputs · finding_event (disproved)',
    retention: 'Forever',
    editIn: [
      { what: 'Verifier prompt', path: '.antiphon/review/prompts/verify.md', layers: LAYERED },
      { what: 'Chain', path: 'Antiphon → Settings → Providers → completions-cheap', layers: 'tenant' },
    ],
    example: `{ "kept": ["F7"], "disproved": [] }`,
  },
  {
    id: 'posted-review',
    title: 'Posted review',
    kind: 'code-host-object',
    format: 'GitHub PR review + statuses',
    purpose: "What actually reached the PR: one review with inline comments for findings above the round's bar, the summary comment (edited in place) and the commit statuses.",
    fields: [
      { name: 'reviewId', type: 'GitHub id', description: 'One review per iteration = one notification.' },
      { name: 'comments[]', type: '{ finding, commentId, threadId }', description: 'Links findings to threads (finding_thread_link).' },
      { name: 'summaryCommentId', type: 'GitHub id', description: 'Edited in place each iteration.' },
      { name: 'statuses', type: 'antiphon/review, antiphon/approval', description: 'Commit statuses on the head SHA.' },
      { name: 'belowBar[]', type: 'finding id[]', description: 'Stored, not posted.' },
    ],
    producedBy: ['publish'],
    consumedBy: ['route', 'human-review'],
    rules: [
      { id: 'bar', text: "Only findings clearing the round's bar (or in a bypass category) are inline." },
      { id: 'links', text: 'Every posted finding has a thread link.' },
    ],
    storage: 'finding_thread_link · review_event · the PR on GitHub',
    retention: 'Forever',
    editIn: [
      { what: 'Posting bar and nits', path: '.antiphon/review/policy.yaml → rounds.post', layers: LAYERED },
      { what: 'Bot handle', path: '.antiphon/review/policy.yaml → handles', layers: LAYERED },
      { what: 'Summary template', path: '.antiphon/review/prompts/summary.md', layers: LAYERED },
    ],
    example: `{ "reviewId": 2194417, "comments": [{ "finding": "F7", "threadId": "PRRT_kwDO…" }],
  "statuses": { "antiphon/review": "failure: 1 finding (1 major, data-loss)" } }`,
  },
  {
    id: 'round-route',
    title: 'Round route',
    kind: 'json-record',
    format: 'round-route/1',
    purpose: 'Where the run goes after a round: findings → wait for changes, clean → needs-human-approval, bot-round limit → human review. Records why.',
    fields: [
      { name: 'output', type: "'findings' | 'clean' | 'bot-round-limit'", description: 'Selects the outgoing edge.' },
      { name: 'botOnlyRounds', type: 'number', description: 'Counter after this round.' },
      { name: 'reasons[]', type: 'string[]', description: 'What decided it.' },
    ],
    producedBy: ['route'],
    consumedBy: ['await-fixes', 'needs-human-approval', 'human-review'],
    rules: [{ id: 'one', text: 'Exactly one output, and it names an existing edge.' }],
    storage: 'node_execution.outputs',
    retention: 'Life of the run, then 400 days',
    editIn: [{ what: 'Bot-only round limit', path: '.antiphon/review/policy.yaml → rounds.humanGateAfterBotOnlyRounds', layers: LAYERED }],
    example: `{ "output": "findings", "botOnlyRounds": 1, "reasons": ["1 major above the round-3 bar"] }`,
  },
  {
    id: 'human-approval-verdict',
    title: 'Human-approval verdict',
    kind: 'json-record',
    format: 'human-approval-verdict/1',
    purpose: 'For a clean round: must a human approve? Answers yes or no, lists every rule that fired, and is sticky for the rest of the PR.',
    fields: [
      { name: 'verdict', type: "'yes' | 'no'", description: 'yes → human-approve, no → bot-approve.' },
      { name: 'rulesFired[]', type: '{ rule, evidence }', description: 'Shown in the UI and the summary comment.' },
      { name: 'sticky', type: 'boolean', description: 'Once yes, always yes for this PR.' },
    ],
    producedBy: ['needs-human-approval'],
    consumedBy: ['bot-approve', 'human-approve'],
    rules: [{ id: 'evidence', text: 'Every fired rule cites its evidence (a score, a flag, a count).' }],
    storage: 'node_execution.outputs',
    retention: 'Life of the run, then 400 days',
    editIn: [{ what: 'humanRequiredWhen and autoApproveRequires', path: '.antiphon/review/policy.yaml → approval', layers: LAYERED }],
    example: `{ "verdict": "yes", "sticky": true,
  "rulesFired": [
    { "rule": "category: migration", "evidence": "db/migrations/20261120_ledger_recon.sql" },
    { "rule": "blastRadius.persistent_data", "evidence": "p=0.93" }
  ] }`,
  },
  {
    id: 'gate-decision',
    title: 'Gate decision',
    kind: 'event',
    format: 'gate-decision/1',
    purpose: 'A human decision at a gate: approve, another round (with an optional steer prompt that goes into every pack), or close.',
    fields: [
      { name: 'decision', type: "'approve' | 'another-round' | 'close'", description: 'Selects the outgoing edge. The optional disputed-findings gate (§4.8) decides re-review | drop | keep.' },
      { name: 'auto', type: 'boolean', description: 'True when a gate took its automatic decision (under autoRetries) without a human.' },
      { name: 'decidedBy', type: 'user', description: 'Must be authorised for the gate queue.' },
      { name: 'steer', type: 'string?', description: '"Human steer from <name>: …" in the next packs.' },
      { name: 'sha', type: 'sha', description: 'The head it applies to.' },
    ],
    producedBy: ['human-approve', 'human-review'],
    consumedBy: ['intake', 'base-moved'],
    rules: [
      { id: 'authorised', text: 'The decider is a member of the gate queue and not the author.' },
      { id: 'allowed', text: "The decision is one of the gate's allowed decisions." },
    ],
    storage: 'gate · review_event',
    retention: 'Forever',
    editIn: [{ what: 'Allowed decisions, queue, timeout', path: '.antiphon/review/workflow.yaml → human-approve / human-review', layers: LAYERED }],
    example: `{ "gate": "human-approve", "decision": "another-round", "decidedBy": "hmartin", "sha": "4a77b10",
  "steer": "check the refund ledger reconciliation path against the double-entry invariant in src/Ledger/README.md" }`,
  },
  {
    id: 'wait-event',
    title: 'Wait event',
    kind: 'event',
    format: 'wait-event/1',
    purpose: 'Why a parked run woke up: a push, a /recheck, the base branch moving, a close or merge, or the timeout. await-fixes and await-merge both write it.',
    fields: [
      { name: 'on', type: "'push' | 'recheck' | 'base-moved' | 'close' | 'merge' | 'timeout'", description: 'Selects the outgoing edge.' },
      { name: 'baseRange', type: 'sha..sha?', description: 'Old and new base for base-moved.' },
      { name: 'sha', type: 'sha?', description: 'New head for push.' },
      { name: 'parkedWh', type: 'number', description: 'Working hours parked (no seat held).' },
    ],
    producedBy: ['await-fixes', 'await-merge'],
    consumedBy: ['intake', 'base-drift'],
    rules: [{ id: 'iterations', text: 'The back edge is still under maxIterations (20).' }],
    storage: 'review_event',
    retention: 'Forever',
    editIn: [{ what: 'Events and timeout', path: '.antiphon/review/workflow.yaml → await-fixes / await-merge', layers: LAYERED }],
    example: `{ "wait": "await-fixes", "on": "push", "sha": "4a77b10", "parkedWh": 19.5, "iteration": 1 }`,
  },
  {
    id: 'base-drift-assessment',
    title: 'Base-drift assessment',
    kind: 'decision',
    format: 'decision-response/1 (base_drift)',
    purpose:
      'Whether commits that landed on the base branch could affect this PR, while it is parked on a wait or since its approval (base-moved). A code-only prefilter runs first; only when it finds overlap does one cheap Jev call answer. Affected sends the PR back through a delta re-review with an automatic rebase steer; unaffected and approved goes to update-branch with the approval carried forward.',
    fields: [
      { name: 'baseRange', type: 'sha..sha', description: 'Reviewed base → new base.' },
      { name: 'commits[]', type: '{ sha, subject, files }', description: 'Base commits since the reviewed base.' },
      { name: 'overlap[]', type: '{ file, why }', description: 'PR files, related files (impact index, callers) or contract globs the base touched.' },
      { name: 'prefilter', type: "'no-overlap' | 'overlap'", description: 'No overlap → unaffected, no model call.' },
      { name: 'probability', type: 'number?', description: 'Jev yes-probability that the PR is affected; absent when the prefilter decided.' },
      { name: 'verdict', type: "'affected' | 'unaffected-approved' | 'unaffected-open'", description: 'Selects the outgoing edge.' },
      { name: 'steer', type: 'string?', description: 'Only when affected: "Base moved <old>..<new>: review as if rebased onto the new base; check interactions with …"' },
      { name: 'servedBy', type: '{ provider, resolvedModel }?', description: 'Present only when a model answered.' },
    ],
    producedBy: ['base-drift'],
    consumedBy: ['intake', 'update-branch', 'await-fixes'],
    rules: [
      { id: 'prefilter-first', text: 'The code-only prefilter runs before any model call; no overlap means no call.' },
      { id: 'evidence', text: 'An affected verdict lists the overlapping commits and files it is based on.' },
      { id: 'threshold', text: 'A probability below the escalation threshold escalates up the decision chain (Jev → luna → haiku → grok).' },
      { id: 'iterations', text: 'The affected back edge is still under maxIterations (10).' },
    ],
    storage: 'node_execution.outputs · llm_call (when a model answered)',
    retention: 'Life of the run, then 400 days',
    editIn: [
      { what: 'Prefilter and contract globs, threshold', path: '.antiphon/review/workflow.yaml → base-drift', layers: LAYERED },
      { what: 'The base_drift question', path: '.antiphon/review/questions/base-drift.yaml', layers: LAYERED },
    ],
    example: `{ "baseRange": "1f20c9e..7b3e9d0", "prefilter": "no-overlap", "overlap": [],
  "commits": [{ "sha": "5d1e0a2", "subject": "docs: refresh on-call rota" }, { "sha": "7b3e9d0", "subject": "web: bump eslint" }],
  "verdict": "unaffected-open", "iteration": 1 }`,
  },
  {
    id: 'approval-record',
    title: 'Approval record',
    kind: 'code-host-object',
    format: 'antiphon/approval status',
    purpose: 'The approval itself: antiphon/approval = success on the exact SHA it covers, by bot or human. Branch protection requires it.',
    fields: [
      { name: 'sha', type: 'sha', description: 'Revoked by any push except a base-only update.' },
      { name: 'baseSha', type: 'sha', description: 'The base the approved iteration was reviewed against (pinned at intake). base-moved compares it with the base tip.' },
      { name: 'by', type: "'bot' | user", description: 'Who approved.' },
      { name: 'rules', type: 'string[]', description: 'Rules that applied.' },
    ],
    producedBy: ['bot-approve', 'human-approve', 'human-review'],
    consumedBy: ['base-moved', 'update-branch', 'await-merge'],
    rules: [{ id: 'sha', text: 'Head = reviewed SHA at the time of approval.' }],
    storage: 'review_event · commit status on GitHub',
    retention: 'Forever',
    editIn: [{ what: 'Auto-approve conditions', path: '.antiphon/review/policy.yaml → approval.autoApproveRequires', layers: LAYERED }],
    example: `{ "sha": "c5d1a09", "baseSha": "7b3e9d0", "by": "bot", "rules": ["no-open-findings", "ci-green"] }`,
  },
  {
    id: 'base-tip-check',
    title: 'Base-tip check',
    kind: 'json-record',
    format: 'base-tip-check/1',
    purpose:
      "Right after an approval, code only, no model call. keepUpToDate on: compares the PR branch's merge-base with the base tip, so a branch that was already behind at review time is caught too. keepUpToDate off: compares the reviewed base with the base tip, for drift since the review only. Unchanged → await-merge with no update; moved → base-drift.",
    fields: [
      { name: 'mergeBase', type: 'sha', description: 'git merge-base of the approved head and the base tip: where the PR branch actually forks from the base.' },
      { name: 'reviewedBase', type: 'sha', description: 'The base the approved iteration was reviewed against (pinned at intake, from the approval record).' },
      { name: 'baseTip', type: 'sha', description: 'The base tip after fetching the base ref.' },
      { name: 'comparison', type: "'merge-base' | 'reviewed-base'", description: 'Which SHA was compared with baseTip: merge-base when keepUpToDate is on, reviewed-base when it is off.' },
      { name: 'keepUpToDate', type: 'boolean', description: 'policy.yaml afterApproval.keepUpToDate, resolved through the layers; it selects the comparison.' },
      { name: 'output', type: "'unchanged' | 'moved'", description: 'Selects the outgoing edge.' },
      { name: 'reason', type: "'drift' | 'behind' | null", description: 'drift: the base moved since the review. behind: the branch was already behind the tip at review time (merge-base comparison only).' },
      { name: 'commits', type: 'number', description: 'Base commits in compared..baseTip; 0 when unchanged.' },
      { name: 'approvedBy', type: "'bot' | user", description: 'Which approval it checked.' },
    ],
    producedBy: ['base-moved'],
    consumedBy: ['base-drift', 'await-merge'],
    rules: [
      { id: 'fetched', text: 'The base tip comes from a fetch made after the approval, not from the last webhook.' },
      { id: 'compare', text: 'keepUpToDate on → compare mergeBase with baseTip; off → compare reviewedBase with baseTip. The record names the comparison it used.' },
      { id: 'one', text: 'Exactly one output: unchanged when the compared SHA = baseTip, otherwise moved.' },
    ],
    storage: 'node_execution.outputs',
    retention: 'Life of the run, then 400 days',
    editIn: [
      { what: 'Fetch before compare', path: '.antiphon/review/workflow.yaml → base-moved', layers: LAYERED },
      { what: 'keepUpToDate (selects the comparison)', path: '.antiphon/review/policy.yaml → afterApproval.keepUpToDate', layers: LAYERED },
    ],
    example: `{ "mergeBase": "1f20c9e", "reviewedBase": "7b3e9d0", "baseTip": "7b3e9d0", "keepUpToDate": true, "comparison": "merge-base", "output": "moved", "reason": "behind", "commits": 3, "approvedBy": "hmartin" }`,
  },
  {
    id: 'branch-update',
    title: 'Branch update',
    kind: 'event',
    format: 'branch-update/1',
    purpose: 'When base-drift says the base moved without affecting an approved PR: how the branch was brought up to date, or why it was not (keepUpToDate off and no conflicts), and whether an agent resolved conflicts (only conflict hunks are rechecked).',
    fields: [
      { name: 'action', type: "'updated' | 'not-needed' | 'conflicts-resolved'", description: 'not-needed: keepUpToDate is off and the head still merges cleanly into the new base; nothing is pushed.' },
      { name: 'method', type: "'merge' | 'rebase'", description: 'Per repo.' },
      { name: 'conflicts[]', type: '{ file, hunks, resolvedBy }', description: 'AI-resolved hunks are rechecked.' },
      { name: 'approvalCarried', type: 'boolean', description: 'True when only the base changed (patch ids unchanged), or nothing was pushed.' },
    ],
    producedBy: ['update-branch'],
    consumedBy: ['await-merge'],
    rules: [{ id: 'patch-ids', text: 'Approval carries forward only if base-drift said unaffected and the PR’s patch ids are unchanged.' }],
    storage: 'review_event',
    retention: 'Forever',
    editIn: [{ what: 'afterApproval settings', path: '.antiphon/review/policy.yaml → afterApproval', layers: LAYERED }],
    example: `{ "action": "updated", "method": "rebase", "conflicts": [], "approvalCarried": true }`,
  },
]

export function artifactById(id: string): ArtifactDef | undefined {
  return ARTIFACTS.find((a) => a.id === id)
}

// ---------------------------------------------------------------- instances: payments-api#618

export interface RuleResult {
  rule: string
  status: 'pass' | 'warn' | 'fail' | 'pending'
  detail: string
}

export interface ArtifactInstance {
  key: string
  artifact: string
  label: string
  iteration: number
  status: Validation
  producedBy: string
  consumedBy: string[]
  ref: string
  size?: string
  results: RuleResult[]
  summary: string
}

const PREFIX_HASH = '9c41e2a7'
const PENDING = (why: string): RuleResult[] => [{ rule: 'produced', status: 'pending', detail: why }]

export const PACK_INSTANCES: { group: string; model: string; checks: string[]; prefixTokens: number; suffixTokens: number; status: Validation; results: RuleResult[] }[] = [
  {
    group: 'security', model: 'opus', checks: ['CHK-0311', 'CHK-0402'], prefixTokens: 51_700, suffixTokens: 4_100, status: 'valid',
    results: [
      { rule: 'prefix-identical', status: 'pass', detail: `sha256:${PREFIX_HASH}… matches 4/4 packs` },
      { rule: 'budget', status: 'pass', detail: '51.7k prefix + 4.1k suffix = 55.8k of 68k' },
      { rule: 'checks-present', status: 'pass', detail: '2/2 checks: Prompt + Examples present' },
      { rule: 'ledger', status: 'pass', detail: '14/14 included files have ledger rows' },
      { rule: 'no-head-config', status: 'pass', detail: 'no .antiphon/ paths from e41b07c' },
      { rule: 'contract', status: 'pass', detail: 'review-findings/1.0' },
    ],
  },
  {
    group: 'concurrency', model: 'opus', checks: ['CHK-0142', 'CHK-0741'], prefixTokens: 51_700, suffixTokens: 5_300, status: 'valid',
    results: [
      { rule: 'prefix-identical', status: 'pass', detail: `sha256:${PREFIX_HASH}… matches 4/4 packs` },
      { rule: 'budget', status: 'pass', detail: '51.7k prefix + 5.3k suffix = 57.0k of 68k' },
      { rule: 'checks-present', status: 'pass', detail: '2/2 checks: Prompt + Examples present' },
      { rule: 'ledger', status: 'pass', detail: '14/14 included files have ledger rows' },
      { rule: 'no-head-config', status: 'pass', detail: 'no .antiphon/ paths from e41b07c' },
      { rule: 'contract', status: 'pass', detail: 'review-findings/1.0' },
    ],
  },
  {
    group: 'data', model: 'sonnet', checks: ['CHK-0087', 'CHK-0510', 'CHK-0745'], prefixTokens: 51_700, suffixTokens: 6_800, status: 'warning',
    results: [
      { rule: 'prefix-identical', status: 'pass', detail: `sha256:${PREFIX_HASH}… matches 4/4 packs` },
      { rule: 'budget', status: 'pass', detail: '51.7k prefix + 6.8k suffix = 58.5k of 68k' },
      { rule: 'checks-present', status: 'warn', detail: 'CHK-0745 is a candidate (from recurring steers): no # @model: Examples section; runs as shadow, findings not posted' },
      { rule: 'ledger', status: 'pass', detail: '14/14 included files have ledger rows' },
      { rule: 'no-head-config', status: 'pass', detail: 'no .antiphon/ paths from e41b07c' },
      { rule: 'contract', status: 'pass', detail: 'review-findings/1.0' },
    ],
  },
  {
    group: 'general', model: 'sonnet', checks: ['CHK-0033', 'CHK-0219', 'CHK-0122'], prefixTokens: 51_700, suffixTokens: 3_900, status: 'warning',
    results: [
      { rule: 'prefix-identical', status: 'pass', detail: `sha256:${PREFIX_HASH}… matches 4/4 packs` },
      { rule: 'budget', status: 'pass', detail: '51.7k prefix + 3.9k suffix = 55.6k of 68k' },
      { rule: 'checks-present', status: 'warn', detail: 'CHK-0122 is retired in common@v14 but pinned at v2 by the payments-api layer' },
      { rule: 'ledger', status: 'pass', detail: '14/14 included files have ledger rows' },
      { rule: 'no-head-config', status: 'pass', detail: 'no .antiphon/ paths from e41b07c' },
      { rule: 'contract', status: 'pass', detail: 'review-findings/1.0' },
    ],
  },
]

export const RUN618_INSTANCES: ArtifactInstance[] = [
  {
    key: 'gate-decision@it2', artifact: 'gate-decision', label: 'gate decision (it2)', iteration: 2, status: 'valid', producedBy: 'human-approve', consumedBy: ['intake', 'packs'],
    ref: 'gate G-2231', summary: 'Hugo Martin: another-round, with a steer',
    results: [
      { rule: 'authorised', status: 'pass', detail: 'hmartin is in payments-core; author is cwei' },
      { rule: 'allowed', status: 'pass', detail: 'another-round is allowed at human-approve' },
    ],
  },
  {
    key: 'pr-snapshot', artifact: 'pr-snapshot', label: 'PR snapshot', iteration: 3, status: 'valid', producedBy: 'intake', consumedBy: ['delta', 'categorise', 'packs', 'needs-human-approval', 'publish'],
    ref: 'node_execution 88121', size: '2.1 KB', summary: '1f20c9e → e41b07c · 17 files · size L',
    results: [
      { rule: 'pinned', status: 'pass', detail: 'both SHAs in mirror' },
      { rule: 'base-config', status: 'pass', detail: 'config sha256:4be1c09… read at 1f20c9e' },
      { rule: 'tags', status: 'pass', detail: '17/17 files tagged (csharp 14, sql 2, md 1)' },
    ],
  },
  {
    key: 'change-delta', artifact: 'change-delta', label: 'change delta', iteration: 3, status: 'valid', producedBy: 'delta', consumedBy: ['categorise', 'select', 'packs', 'collate'],
    ref: 'node_execution 88122', size: '0.8 KB', summary: '2 files since 4a77b10 · 2 open findings still present',
    results: [
      { rule: 'anchors', status: 'pass', detail: '2/2 open findings re-anchored' },
      { rule: 'still-present', status: 'pass', detail: 'F3 p=0.82, F5 p=0.77 (Jev)' },
    ],
  },
  {
    key: 'categorisation', artifact: 'categorisation', label: 'categorisation', iteration: 3, status: 'valid', producedBy: 'categorise', consumedBy: ['select', 'group', 'packs', 'needs-human-approval', 'publish'],
    ref: 'llm_call 4410921–3', size: '27 answers', summary: 'feature · data p=0.93 · rollback 4 · 2 escalated',
    results: [
      { rule: 'answered', status: 'pass', detail: '27/27 answered, 0 refused' },
      { rule: 'served-by', status: 'pass', detail: '25 jev-1.14.0, 2 gpt-5.6-luna' },
      { rule: 'confidence', status: 'pass', detail: 'concurrency and session_runtime escalated Jev → luna (< 0.60)' },
      { rule: 'limits', status: 'pass', detail: '1 call, 27 questions, 19.9k tokens' },
    ],
  },
  {
    key: 'check-selection', artifact: 'check-selection', label: 'check selection', iteration: 3, status: 'valid', producedBy: 'select', consumedBy: ['group', 'needs-human-approval'],
    ref: 'node_execution 88124', size: '163 rows', summary: '163 candidates → 11 applies → 10 after dedupe',
    results: [
      { rule: 'recorded', status: 'pass', detail: '163/163 candidates recorded' },
      { rule: 'split', status: 'pass', detail: '2 calls: 128 + 35 questions' },
      { rule: 'bar', status: 'pass', detail: 'round 3 bar: major+, bypass security/data-loss' },
    ],
  },
  {
    key: 'reviewer-groups', artifact: 'reviewer-groups', label: 'reviewer groups', iteration: 3, status: 'valid', producedBy: 'group', consumedBy: ['packs', 'review'],
    ref: 'node_execution 88125', summary: '4 groups: security, concurrency, data, general',
    results: [
      { rule: 'once', status: 'pass', detail: '10/10 checks in exactly one group (CHK-0640 deduped by prompt hash)' },
      { rule: 'bypass', status: 'pass', detail: 'security and data are separate groups' },
      { rule: 'suffix', status: 'pass', detail: 'largest suffix 6.8k of 8k (data)' },
    ],
  },
  ...PACK_INSTANCES.map(
    (p): ArtifactInstance => ({
      key: `context-pack:${p.group}`,
      artifact: 'context-pack',
      label: `pack · ${p.group}`,
      iteration: 3,
      status: p.status,
      producedBy: 'packs',
      consumedBy: [`reviewer:${p.group}`, 'verify'],
      ref: `artifacts/RR-3118/it3/pack-${p.group}.md`,
      size: `${((p.prefixTokens + p.suffixTokens) / 1000).toFixed(1)}k tokens`,
      summary: `${p.checks.length} checks · ${p.model}`,
      results: p.results,
    }),
  ),
  {
    key: 'review-findings:security', artifact: 'review-findings', label: 'findings · security', iteration: 3, status: 'valid', producedBy: 'reviewer:security', consumedBy: ['collate'],
    ref: 'artifacts/RR-3118/it3/findings-security.abf', summary: '0 findings · 2/2 checks pass',
    results: [
      { rule: 'parse', status: 'pass', detail: 'review-findings/1.0, no diagnostics' },
      { rule: 'checks-accounted', status: 'pass', detail: '2/2 assigned checks present' },
      { rule: 'anchors', status: 'pass', detail: 'no findings' },
    ],
  },
  {
    key: 'review-findings:concurrency', artifact: 'review-findings', label: 'findings · concurrency', iteration: 3, status: 'pending', producedBy: 'reviewer:concurrency', consumedBy: ['collate'],
    ref: '—', summary: 'reviewer running (4m 12s, opus)', results: PENDING('Reviewer still running; validated when its document arrives.'),
  },
  {
    key: 'review-findings:data', artifact: 'review-findings', label: 'findings · data', iteration: 3, status: 'warning', producedBy: 'reviewer:data', consumedBy: ['collate'],
    ref: 'artifacts/RR-3118/it3/findings-data.abf', summary: '1 finding (major, data-loss) · 3/3 checks',
    results: [
      { rule: 'parse', status: 'warn', detail: 'missing-end: needed 1 repair call (haiku) to close the document' },
      { rule: 'checks-accounted', status: 'pass', detail: '3/3 assigned checks present (CHK-0745 unverified)' },
      { rule: 'anchors', status: 'pass', detail: '1/1 anchors resolve at e41b07c' },
    ],
  },
  {
    key: 'review-findings:general', artifact: 'review-findings', label: 'findings · general', iteration: 3, status: 'valid', producedBy: 'reviewer:general', consumedBy: ['collate'],
    ref: 'artifacts/RR-3118/it3/findings-general.abf', summary: '1 finding (minor, below bar) · 3/3 checks',
    results: [
      { rule: 'parse', status: 'pass', detail: 'review-findings/1.0' },
      { rule: 'checks-accounted', status: 'pass', detail: '3/3 assigned checks present' },
      { rule: 'anchors', status: 'pass', detail: '1/1 anchors resolve' },
    ],
  },
  { key: 'collated-findings', artifact: 'collated-findings', label: 'collated findings', iteration: 3, status: 'pending', producedBy: 'collate', consumedBy: ['verify'], ref: '—', summary: 'waiting on the concurrency reviewer', results: PENDING('Join waits for all 4 reviewers (policy: all); 3/4 reported.') },
  { key: 'verified-findings', artifact: 'verified-findings', label: 'verified findings', iteration: 3, status: 'pending', producedBy: 'verify', consumedBy: ['publish', 'route'], ref: '—', summary: 'not produced yet', results: PENDING('Runs after collate.') },
  { key: 'posted-review', artifact: 'posted-review', label: 'posted review', iteration: 3, status: 'pending', producedBy: 'publish', consumedBy: ['route'], ref: '—', summary: 'not produced yet', results: PENDING('Runs after verify.') },
  { key: 'round-route', artifact: 'round-route', label: 'round route', iteration: 3, status: 'pending', producedBy: 'route', consumedBy: ['await-fixes', 'needs-human-approval', 'human-review'], ref: '—', summary: 'not produced yet', results: PENDING('Runs after publish.') },
]

export function instanceByKey(key: string): ArtifactInstance | undefined {
  return RUN618_INSTANCES.find((i) => i.key === key)
}

export function instancesOf(artifact: string): ArtifactInstance[] {
  return RUN618_INSTANCES.filter((i) => i.artifact === artifact)
}

/** Rule id → definition text for an artifact. */
export function ruleText(artifact: string, rule: string): string {
  return artifactById(artifact)?.rules.find((r) => r.id === rule)?.text ?? rule
}

// ---------------------------------------------------------------- node inputs and outputs

export type InputKind = 'artifact' | 'config' | 'context' | 'human'

export const INPUT_KIND_META: Record<InputKind, { label: string; color: string }> = {
  artifact: { label: 'from an earlier step', color: 'cyan' },
  config: { label: 'configuration', color: 'indigo' },
  context: { label: 'context', color: 'gray' },
  human: { label: 'human', color: 'pink' },
}

export interface NodeInput {
  name: string
  kind: InputKind
  /** artifact id, when kind = artifact */
  artifact?: string
  /** producing node id, when kind = artifact */
  from?: string
  /** file path, table or system the input comes from */
  source: string
  layer?: string
  decides: string
  example: string
}

export interface NodeIO {
  decides: string
  inputs: NodeInput[]
  outputs: string[]
}

const art = (artifact: string, from: string, name: string, decides: string, example: string): NodeInput => ({
  name,
  kind: 'artifact',
  artifact,
  from,
  source: `${from} → ${artifact}`,
  decides,
  example,
})

const POLICY = '.antiphon/review/policy.yaml'
const WORKFLOW = '.antiphon/review/workflow.yaml'

export const NODE_IO: Record<string, NodeIO> = {
  intake: {
    decides: 'Which SHAs are reviewed, which config applies (read at base), and the code-only tags.',
    inputs: [
      { name: 'Trigger', kind: 'human', source: 'human-approve → gate-decision (it2)', artifact: 'gate-decision', from: 'human-approve', decides: 'Starts iteration 3 and carries the steer prompt forward.', example: 'another-round by Hugo Martin, with steer' },
      { name: 'Repo mirror and PR refs', kind: 'context', source: 'server bare mirror · ICodeHost', decides: 'Pins base and head.', example: 'base 1f20c9e · head e41b07c' },
      { name: 'Review config at base SHA', kind: 'config', source: '.antiphon/review/ @ 1f20c9e', layer: LAYERED, decides: 'Which policy, checks and workflow apply; never from the PR head (D-8).', example: 'config sha256:4be1c09…' },
      { name: 'Areas and path classes', kind: 'config', source: 'antiphon.areas.json · policy.yaml → intake.tags', layer: 'repo', decides: 'Code-only tags.', example: 'areas: ledger, refunds' },
      { name: 'CODEOWNERS', kind: 'config', source: 'CODEOWNERS @ base', layer: 'repo', decides: 'Owning teams of the changed paths.', example: '/src/Ledger/** @lumen/payments-core' },
    ],
    outputs: ['pr-snapshot'],
  },
  delta: {
    decides: 'What changed since the last reviewed head, and which open findings are still present.',
    inputs: [
      art('pr-snapshot', 'intake', 'PR snapshot', 'The new head to compare.', 'head e41b07c'),
      { name: 'Last reviewed head', kind: 'context', source: 'review_iteration (it2)', decides: 'Diff start; interdiff if unreachable.', example: '4a77b10 (reachable)' },
      { name: 'Open findings and replies', kind: 'context', source: 'finding · code-host threads', decides: 'What to re-anchor and ask still_present about.', example: 'F3 (1 author reply), F5' },
      { name: 'Recheck questions', kind: 'config', source: '.antiphon/review/questions/recheck.yaml', layer: LAYERED, decides: 'still_present and rebuttal_strength wording.', example: '2 questions' },
    ],
    outputs: ['change-delta'],
  },
  categorise: {
    decides: 'Change kind, risk areas and blast radius.',
    inputs: [
      art('pr-snapshot', 'intake', 'PR title, description, changed files with stats, code-only tags', 'The decision state Jev reads.', '17 files · +642 −118 · ledger, refunds'),
      art('change-delta', 'delta', 'Diff summary', 'Narrows the state to what changed this iteration.', '2 files since 4a77b10'),
      { name: 'Categorise questions', kind: 'config', source: '.antiphon/review/questions/categorise.yaml', layer: LAYERED, decides: 'Which questions are asked and their instructions.', example: '27 questions: change_kind, 6 blast-radius, 20 risk areas' },
      { name: 'Decision chain', kind: 'config', source: 'Antiphon → Settings → Providers → decisions-default', layer: 'tenant', decides: 'Who answers and when an answer escalates.', example: 'Jev → luna → haiku → grok-low · escalate < 0.60' },
    ],
    outputs: ['categorisation'],
  },
  select: {
    decides: 'Which checks apply this round.',
    inputs: [
      art('categorisation', 'categorise', 'Categorisation', 'Risk areas and tags that trigger checks.', 'persistence p=0.93, concurrency p=0.71'),
      art('change-delta', 'delta', 'Change delta', 'Only changed files are matched again.', '2 files'),
      { name: 'Check triggers and embeddings', kind: 'config', source: '.antiphon/review/checks/** (front matter) · check_index', layer: LAYERED, decides: 'The code prefilter: ~100–200 candidates.', example: '1,184 indexed → 163 candidates' },
      { name: 'Each check’s # @model: Selector', kind: 'config', source: '.antiphon/review/checks/<category>/<id>.md', layer: LAYERED, decides: 'The Jev "applies?" question per candidate.', example: 'CHK-0142: "Does this change release a lock before an async save…"' },
      { name: 'Posting bar', kind: 'config', source: `${POLICY} → rounds.post`, layer: LAYERED, decides: "Drops checks that can't clear this round's bar.", example: 'round 3+: major, p ≥ 0.80' },
      { name: 'Steer prompt', kind: 'human', source: 'gate-decision (it2)', artifact: 'gate-decision', from: 'human-approve', decides: 'Can add candidate checks.', example: 'adds CHK-0745 (candidate)' },
    ],
    outputs: ['check-selection'],
  },
  group: {
    decides: 'Which reviewer gets which checks, and at what tier.',
    inputs: [
      art('check-selection', 'select', 'Selected checks', 'What to split.', '11 checks'),
      art('categorisation', 'categorise', 'Blast radius', 'Raises the tier of a group.', 'rollback 4 → concurrency at high tier'),
      { name: 'Group definitions', kind: 'config', source: '.antiphon/review/groups/*.yaml', layer: LAYERED, decides: 'Affinity, budgets, default tiers.', example: '6 group templates' },
      { name: 'Bypass categories', kind: 'config', source: `${POLICY} → bypassCategories`, layer: LAYERED, decides: 'Groups that must stand alone.', example: '[security, data-loss]' },
    ],
    outputs: ['reviewer-groups'],
  },
  packs: {
    decides: 'What each reviewer reads, and in what order.',
    inputs: [
      art('pr-snapshot', 'intake', 'PR snapshot', 'PR metadata and the diff.', '17 files'),
      art('change-delta', 'delta', 'Change delta', 'Outstanding findings to list.', 'F3, F5'),
      art('categorisation', 'categorise', 'Categorisation', 'Intent and blast radius in the prefix.', 'feature · data p=0.93'),
      art('reviewer-groups', 'group', 'Reviewer groups', 'One pack per group; the suffix per group.', '4 groups'),
      { name: 'Related-file sources', kind: 'config', source: '.antiphon/review/related.yaml · checks relatedFiles · impact index · callers · co-change', layer: LAYERED, decides: 'Candidate related files with ledger reasons.', example: '17 candidates, 14 included' },
      { name: 'Token budget', kind: 'config', source: `${POLICY} → packs.prefixTokens`, layer: LAYERED, decides: 'Where the ledger is cut.', example: '60,000' },
      { name: 'Steer prompt', kind: 'human', source: 'gate-decision (it2)', artifact: 'gate-decision', from: 'human-approve', decides: 'Goes into every pack’s shared prefix.', example: '"Human steer from Hugo Martin: check the refund ledger…"' },
    ],
    outputs: ['context-pack'],
  },
  review: {
    decides: 'How many reviewers run, and when the join is complete.',
    inputs: [
      art('reviewer-groups', 'group', 'Reviewer groups', 'One child per group.', '4 children'),
      art('context-pack', 'packs', 'Context packs', 'Staged to each child’s runner.', '4 packs'),
      { name: 'Failure policy', kind: 'config', source: `${WORKFLOW} → review.policy`, layer: LAYERED, decides: 'all | quorum:n | best-effort.', example: 'all' },
      { name: 'Runner seats', kind: 'context', source: 'runner_capability (label: review)', decides: 'Where children run.', example: '3 of 4 runners match' },
    ],
    outputs: ['review-findings'],
  },
  reviewer: {
    decides: "What is wrong within this group's checks.",
    inputs: [
      art('context-pack', 'packs', 'Its context pack', 'Nearly everything it reads.', 'pack-concurrency.md · 57.0k tokens'),
      { name: 'Checkout at head', kind: 'context', source: 'repo mirror, read-only', decides: 'Files it opens beyond the pack (logged).', example: 'e41b07c' },
      { name: 'Reviewer role prompt', kind: 'config', source: '.antiphon/review/prompts/reviewer.md', layer: LAYERED, decides: 'How to review and what to output.', example: 'reviewer.md v6' },
      { name: 'Model tier', kind: 'config', source: '.antiphon/review/groups/<group>.yaml → tier', layer: LAYERED, decides: 'Which model runs it.', example: 'high → opus' },
    ],
    outputs: ['review-findings'],
  },
  collate: {
    decides: 'Which findings are duplicates, and where each one anchors.',
    inputs: [
      art('review-findings', 'reviewer', 'Every reviewer’s findings', 'What to join.', '3/4 reported'),
      art('change-delta', 'delta', 'Open findings', 'Do not re-raise what is already open.', 'F3, F5'),
      { name: 'Human comments', kind: 'context', source: 'code-host threads', decides: 'Collapse findings a human already raised.', example: '1 human-raised (F7 on #612 style)' },
      { name: 'Dedupe threshold', kind: 'config', source: `${POLICY} → collate.dedupeSimilarity`, layer: LAYERED, decides: 'When two findings are the same.', example: '0.86' },
    ],
    outputs: ['collated-findings'],
  },
  verify: {
    decides: 'Which findings survive an attempt to disprove them.',
    inputs: [
      art('collated-findings', 'collate', 'Collated findings', 'What to try to disprove.', '—'),
      art('context-pack', 'packs', 'The same packs', 'Evidence for or against.', '4 packs'),
      { name: 'Verifier prompt', kind: 'config', source: '.antiphon/review/prompts/verify.md', layer: LAYERED, decides: 'How hard to push back.', example: 'verify.md v3' },
      { name: 'Chain', kind: 'config', source: 'Antiphon → Settings → Providers → completions-cheap', layer: 'tenant', decides: 'Who verifies.', example: 'luna → haiku → grok-low' },
    ],
    outputs: ['verified-findings'],
  },
  publish: {
    decides: 'What is posted to the PR, and how the summary reads.',
    inputs: [
      art('verified-findings', 'verify', 'Verified findings', 'Candidates to post.', '—'),
      art('pr-snapshot', 'intake', 'PR snapshot', 'Where to post (head SHA).', 'e41b07c'),
      { name: 'Posting bar and nits', kind: 'config', source: `${POLICY} → rounds.post`, layer: LAYERED, decides: 'Inline vs stored below the bar.', example: 'round 3+: major, p ≥ 0.80' },
      { name: 'Bot handle', kind: 'config', source: `${POLICY} → handles`, layer: LAYERED, decides: 'Who it posts as.', example: '@MikeysBot' },
      { name: 'Summary template', kind: 'config', source: '.antiphon/review/prompts/summary.md', layer: LAYERED, decides: 'The summary comment.', example: 'summary.md v2' },
    ],
    outputs: ['posted-review'],
  },
  route: {
    decides: 'bot-round-limit | findings | clean.',
    inputs: [
      art('verified-findings', 'verify', 'Collated, verified findings', 'Anything open above the bar?', '—'),
      art('posted-review', 'publish', 'Posted review', 'What the author was told.', '—'),
      { name: 'Posting bar', kind: 'config', source: `${POLICY} → rounds.post`, layer: LAYERED, decides: 'What counts as a finding this round.', example: 'major+' },
      { name: 'Bot-only counter', kind: 'context', source: 'review_iteration', decides: 'Rounds since the last human involvement.', example: '0 (reset by Hugo’s gate decision)' },
      { name: 'humanGateAfterBotOnlyRounds', kind: 'config', source: `${POLICY} → rounds.humanGateAfterBotOnlyRounds`, layer: LAYERED, decides: 'When the human-review gate opens.', example: '3' },
    ],
    outputs: ['round-route'],
  },
  'await-fixes': {
    decides: 'When the next iteration starts (push or /recheck), whether the base moved, or that the run ends (close, timeout).',
    inputs: [
      art('round-route', 'route', 'Round route', 'Parks the run on findings.', 'findings'),
      art('base-drift-assessment', 'base-drift', 'Base-drift verdict', 'Returns here on unaffected-open.', 'unaffected-open (it1)'),
      { name: 'Code-host events', kind: 'context', source: 'webhook or poll via ICodeHost', decides: 'Push, /recheck, base moved, close.', example: 'push 4a77b10 after 19.5 wh (it1)' },
      { name: 'Events and timeout', kind: 'config', source: `${WORKFLOW} → await-fixes`, layer: LAYERED, decides: 'Which events wake it; when it gives up.', example: 'push, recheck, close, base-moved · 14d' },
    ],
    outputs: ['wait-event'],
  },
  'base-drift': {
    decides: 'affected | unaffected-approved | unaffected-open: did the base commits since the reviewed base change anything this PR relies on?',
    inputs: [
      art('wait-event', 'await-fixes', 'Base-moved event', 'The base range to assess, when a wait woke on base-moved.', '1f20c9e..7b3e9d0 (it1)'),
      art('base-tip-check', 'base-moved', 'Base-tip check', 'The base range to assess after an approval: merge-base..tip (keepUpToDate on) or reviewedBase..tip (off).', '—'),
      { name: 'Base commits since the reviewed base', kind: 'context', source: 'server mirror · git log base..newBase', decides: 'What landed: subjects and files.', example: '2 commits: docs rota, eslint bump' },
      art('pr-snapshot', 'intake', 'PR changed files', 'Direct overlap with the base commits.', '17 files'),
      { name: 'Related-file index', kind: 'context', source: 'impact index · callers of changed symbols', decides: 'Indirect overlap (a base change to something the PR calls).', example: 'RefundLock, LedgerDbContext, PspRefundResponse' },
      { name: 'Contract and schema globs', kind: 'config', source: `${WORKFLOW} → base-drift.contractGlobs`, layer: LAYERED, decides: 'Base changes to contracts always count as overlap.', example: '**/Contracts/**, db/migrations/**, openapi.yaml' },
      { name: 'base_drift question', kind: 'config', source: '.antiphon/review/questions/base-drift.yaml', layer: LAYERED, decides: 'The Jev yes/no question, asked only on overlap.', example: 'Could these base changes affect this PR?' },
      { name: 'Approval state', kind: 'context', source: 'approval-record', decides: 'unaffected-approved vs unaffected-open.', example: 'not approved' },
    ],
    outputs: ['base-drift-assessment'],
  },
  'needs-human-approval': {
    decides: 'yes | no: must a human approve this clean round? Records which rules fired.',
    inputs: [
      art('categorisation', 'categorise', 'Blast-radius scores', 'Against humanRequiredWhen.blastRadius.', 'data p=0.93 · rollback 4'),
      art('check-selection', 'select', 'requiresHuman flags', 'Any selected check that requires a human.', 'CHK-0087 requiresHuman'),
      art('pr-snapshot', 'intake', 'Size', 'size: large.', 'L'),
      art('review-findings', 'reviewer', 'Unverified high-severity checks', 'unverifiedHighSeverity.', '0'),
      { name: 'Author tenure', kind: 'context', source: 'identity + git history', decides: 'author: first-time.', example: 'cwei: 212 commits, not first-time' },
      { name: 'Conceded disputes', kind: 'context', source: 'finding_event', decides: 'concededDisputes ≥ 2.', example: '0' },
      { name: 'humanRequiredWhen', kind: 'config', source: `${POLICY} → approval.humanRequiredWhen`, layer: LAYERED, decides: 'The rules themselves.', example: 'categories [migration, …] · data ≥ 0.6 · reversal ≥ 4' },
      { name: 'Earlier verdict (sticky)', kind: 'context', source: 'human-approval-verdict (it2)', decides: 'Once yes, always yes.', example: 'yes (it2)' },
    ],
    outputs: ['human-approval-verdict'],
  },
  'bot-approve': {
    decides: 'Whether every auto-approve condition holds.',
    inputs: [
      art('human-approval-verdict', 'needs-human-approval', 'Human-approval verdict', 'Only on no.', 'no'),
      { name: 'Open findings', kind: 'context', source: 'finding', decides: 'no-open-findings.', example: '0' },
      { name: 'Head = reviewed SHA', kind: 'context', source: 'ICodeHost', decides: 'head-is-reviewed.', example: 'yes' },
      { name: 'CI status', kind: 'context', source: 'GitHub checks', decides: 'ci-green.', example: 'green' },
      { name: 'Open human threads', kind: 'context', source: 'code-host threads', decides: 'no-open-human-threads.', example: '0' },
      { name: 'autoApproveRequires', kind: 'config', source: `${POLICY} → approval.autoApproveRequires`, layer: LAYERED, decides: 'The conditions.', example: '5 conditions' },
    ],
    outputs: ['approval-record'],
  },
  'human-approve': {
    decides: 'approve | another-round | close.',
    inputs: [
      { name: 'Queue item', kind: 'context', source: 'queue_item (payments-core)', decides: 'Who decides and by when.', example: 'assigned Hugo Martin · 4 wh SLA' },
      art('human-approval-verdict', 'needs-human-approval', 'Rules fired', 'Why a human is needed.', 'yes: migration · data p=0.93'),
      art('categorisation', 'categorise', 'Blast radius', 'How careful to be.', 'rollback 4'),
      { name: 'Open findings', kind: 'context', source: 'finding', decides: 'What is still unresolved.', example: '0 (clean round)' },
      { name: 'Steer history', kind: 'context', source: 'review_iteration.steer', decides: 'Earlier steers on this PR.', example: 'none before it2' },
      { name: 'Gate settings', kind: 'config', source: `${WORKFLOW} → human-approve`, layer: LAYERED, decides: 'Allowed decisions, queue, timeout.', example: 'approve, another-round, close · 3d' },
    ],
    outputs: ['gate-decision', 'approval-record'],
  },
  'human-review': {
    decides: 'approve | another-round | close, after the bot-round limit.',
    inputs: [
      art('round-route', 'route', 'Round route', 'Opened on bot-round-limit.', '—'),
      art('posted-review', 'publish', 'Posted review', 'What the bot has said so far.', '—'),
      { name: 'Queue item', kind: 'context', source: 'queue_item (payments-core)', decides: 'Who decides.', example: '—' },
      art('categorisation', 'categorise', 'Blast radius', 'How careful to be.', 'rollback 4'),
      { name: 'Open findings and steer history', kind: 'context', source: 'finding · review_iteration', decides: 'What is unresolved; what was asked before.', example: '—' },
      { name: 'Gate settings', kind: 'config', source: `${WORKFLOW} → human-review`, layer: LAYERED, decides: 'Allowed decisions, queue, timeout.', example: 'approve, another-round, close · 3d' },
    ],
    outputs: ['gate-decision', 'approval-record'],
  },
  'base-moved': {
    decides: 'unchanged | moved: is the approved branch behind the base tip? keepUpToDate on: merge-base vs tip (also catches a branch already behind at review); off: reviewed base vs tip (drift only).',
    inputs: [
      art('approval-record', 'bot-approve', 'Approval record', 'The approved head and the base it was reviewed against.', '—'),
      art('gate-decision', 'human-approve', 'Gate decision', 'A human approve (human-approve or human-review) leads here.', '—'),
      { name: 'Base tip', kind: 'context', source: 'ICodeHost · mirror (base ref fetched)', decides: 'Where the base is now.', example: '—' },
      { name: 'Merge-base', kind: 'context', source: 'mirror · git merge-base <approved head> <base tip>', decides: 'Where the branch forks from the base; compared when keepUpToDate is on.', example: '—' },
      { name: 'Fetch before compare', kind: 'config', source: `${WORKFLOW} → base-moved.fetchBase`, layer: LAYERED, decides: 'Fetch the base ref first, so a late webhook cannot hide a move.', example: 'true' },
      { name: 'keepUpToDate', kind: 'config', source: `${POLICY} → afterApproval.keepUpToDate`, layer: LAYERED, decides: 'Which comparison: merge-base (on) or reviewed base (off).', example: 'true → merge-base' },
    ],
    outputs: ['base-tip-check'],
  },
  'update-branch': {
    decides: 'Whether to bring the branch up to date (keepUpToDate, conflicts), how, and whether conflicts need an agent.',
    inputs: [
      art('base-drift-assessment', 'base-drift', 'Base-drift verdict', 'unaffected-approved: update without a new round.', '—'),
      art('approval-record', 'human-approve', 'Approval record', 'Carried forward when patch ids are unchanged.', '—'),
      { name: 'Merge check against the new base', kind: 'context', source: 'mirror · git merge-tree', decides: 'Whether the head still merges cleanly; conflicts force an update.', example: '—' },
      { name: 'afterApproval', kind: 'config', source: `${POLICY} → afterApproval`, layer: LAYERED, decides: 'keepUpToDate (off: only act on conflicts), method, AI conflict resolution.', example: 'keepUpToDate: true · rebase · resolveConflicts: ai' },
    ],
    outputs: ['branch-update'],
  },
  // optional pattern from the editor's "Re-review disputed findings" proposal (design.md §4.8)
  'disputed-findings': {
    decides: 're-review | drop | keep for a finding the verifier disputes. Its first dispute re-reviews automatically (autoRetries 1).',
    inputs: [
      art('verified-findings', 'verify', 'Disputed findings and the verifier’s objection', 'What is disputed and why; the objection goes into the re-review pack.', '—'),
      { name: 'Disputes so far, per finding', kind: 'context', source: 'finding_event', decides: 'Automatic pass while under autoRetries; otherwise open the gate.', example: '—' },
      art('context-pack', 'packs', 'Context packs', 'Evidence for the human deciding.', '—'),
      { name: 'Gate settings', kind: 'config', source: `${WORKFLOW} → disputed-findings`, layer: LAYERED, decides: 'Decisions, autoRetries and its decision, queue, timeout.', example: 're-review, drop, keep · auto ×1 re-review · 2wh → drop' },
    ],
    outputs: ['gate-decision'],
  },
  'await-merge': {
    decides: 'Ends the run on merge or close; a push revokes the approval and re-reviews; a base move goes to base-drift.',
    inputs: [
      art('base-tip-check', 'base-moved', 'Base-tip check', 'unchanged: hold the approval as it is.', '—'),
      art('branch-update', 'update-branch', 'Branch update', 'The approved head it is holding after a base move.', '—'),
      art('approval-record', 'human-approve', 'Approval record', 'Kept while only the base moves.', '—'),
      { name: 'Code-host events', kind: 'context', source: 'webhook or poll via ICodeHost', decides: 'Merge, close, push, base moved.', example: '—' },
      { name: 'Events', kind: 'config', source: `${WORKFLOW} → await-merge`, layer: LAYERED, decides: 'Which events wake it.', example: 'merge, close, push, base-moved' },
    ],
    outputs: ['wait-event'],
  },
}

export function nodeIO(nodeId: string): NodeIO | undefined {
  return NODE_IO[nodeId.startsWith('reviewer:') ? 'reviewer' : nodeId]
}

/** Artifact instance keys this node produced or consumed in payments-api#618 iteration 3. */
export function nodeInstances(nodeId: string): { inputs: ArtifactInstance[]; outputs: ArtifactInstance[] } {
  return {
    inputs: RUN618_INSTANCES.filter((i) => i.consumedBy.includes(nodeId) || (nodeId.startsWith('reviewer:') && i.consumedBy.includes(nodeId))),
    outputs: RUN618_INSTANCES.filter((i) => i.producedBy === nodeId),
  }
}
