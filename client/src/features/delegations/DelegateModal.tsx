import {
  Button,
  Chip,
  Group,
  Modal,
  SegmentedControl,
  Select,
  Switch,
  Stack,
  Text,
  TextInput,
  Textarea,
  Tooltip,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useState } from 'react'
import type { AgentModelLevel } from '../../api/agents'
import { AGENT_MODEL_LEVEL_OPTIONS } from '../../api/agents'
import {
  AGENT_TASK_ROLES,
  useCreateAgentTask,
  type AgentTaskKind,
  type AgentTaskRole,
  type WorkspaceMode,
} from '../../api/agentTasks'
import { getApiErrorMessage } from '../../api/client'
import { useComplexityChainEffective } from '../../api/complexityChains'
import { useRolePolicy } from '../../api/rolePolicy'
import { useRunnerDefaults } from '../../api/runnerDefaults'
import { useSessionRunners } from '../../api/sessionRunners'
import { nextCreateLabel, nextCreateSkipping, previewNextCreate, type NextCreatePreview } from '../settings/nextCreatePreview'
import { TierBadge } from './TaskChip'
import type { RequiredPlatformName } from './placement'

type TierSource = 'matrix' | 'pin' | 'rolePolicy' | 'agent' | 'exhausted' | 'refused' | 'unknown' | 'fallback'

/**
 * The line under the tier badge. The preview carries the create's context (kind, the role's
 * default-tier row, availability and stage pin). Unknown and refused outcomes are displayed
 * explicitly (C1250 V18-V20). A withheld policy target uses a labelled AGENT_TASK_ROLES suggestion
 * (C097 V18, V30; C1234 unsupported-policy-kind-falls-back-to-constant).
 */
function tierSourceLabel(source: TierSource, preview: NextCreatePreview | null): string {
  const skipping = preview ? nextCreateSkipping(preview) : ''
  switch (source) {
    case 'matrix':
      return `from the ${preview?.complexity} row${skipping}`
    case 'pin':
      return `from the stage pin${skipping}${preview?.note ? `; ${preview.note}` : ''}`
    case 'rolePolicy':
      return `from the role policy${preview?.note ? `; ${preview.note}` : ''}`
    case 'agent':
      return preview?.note ?? 'the stage pin binds a standing agent'
    case 'exhausted':
      return preview?.skipReasons.every((reason) => reason === 'held')
        ? `every ${preview?.complexity ? `${preview.complexity}-row` : 'pin'} candidate is held; the create would be Blocked`
        : 'no eligible candidate; the create would be Blocked'
    case 'refused':
    case 'unknown':
      return nextCreateLabel(preview)
    case 'fallback':
      return 'built-in suggestion — routing depends on the server'
  }
}

export interface DelegatePrefill {
  /** The modal was opened from a card; retain unknown card-pin routing after goal edits. */
  cardContext?: boolean
  /** Seeds the goal — the files view passes the path it is looking at. */
  goal?: string
  /** Where the delegate runs. Defaults to the server's caller directory when omitted. */
  workingDirectory?: string
  /** Declares the files this task owns; intersecting scopes are serialised by the dispatcher. */
  scope?: string
  /** Shown as the inherited platform. Omitted means the server's card or Any. Not sent by itself. */
  cardDefaultPlatform?: RequiredPlatformName
}

/**
 * Hand a piece of work to an agent. Deliberately plain: the two decisions that matter are the SHAPE
 * (worker or sub-orchestrator) and the ROLE — the role sets the tier, and the tier is the cost
 * decision. Nothing here infers, splits, or decomposes anything: you say what the piece of work is.
 */
export function DelegateModal({
  opened,
  onClose,
  prefill,
  title = 'Delegate a task',
}: {
  opened: boolean
  onClose: () => void
  prefill?: DelegatePrefill
  title?: string
}) {
  return (
    <Modal opened={opened} onClose={onClose} title={title} size="lg">
      {/* The form is mounted only while the modal is open, so every open starts from the prefill
          with no effect resetting state behind it. */}
      {opened && <DelegateForm onClose={onClose} prefill={prefill} />}
    </Modal>
  )
}

function DelegateForm({ onClose, prefill }: { onClose: () => void; prefill?: DelegatePrefill }) {
  const create = useCreateAgentTask()
  const [kind, setKind] = useState<AgentTaskKind>('Worker')
  const [role, setRole] = useState<AgentTaskRole>('Code')
  const [goal, setGoal] = useState(prefill?.goal ?? '')
  const [workspace, setWorkspace] = useState<WorkspaceMode>('Worktree')
  const [directory, setDirectory] = useState(prefill?.workingDirectory ?? '')
  const [scope, setScope] = useState(prefill?.scope ?? '')
  const [level, setLevel] = useState<AgentModelLevel | null>(null)
  const [denyEdits, setDenyEdits] = useState(true)
  const [platformChoice, setPlatformChoice] = useState<'inherit' | RequiredPlatformName>('inherit')
  const [runnerChoice, setRunnerChoice] = useState('automatic')
  const runners = useSessionRunners()
  const defaults = useRunnerDefaults()
  const rolePolicy = useRolePolicy()
  const cells = useComplexityChainEffective(role)

  const policyRow = Array.isArray(rolePolicy.data?.roles) ? rolePolicy.data.roles.find((r) => r.role === role) : undefined
  const preview = previewNextCreate({ kind, explicitLevel: level, row: policyRow, cells: cells.data, goal: goal.trim(), cardContext: prefill?.cardContext })
  const serverLevel = AGENT_MODEL_LEVEL_OPTIONS.find((option) => option.value === preview?.modelLevel)?.value ?? null
  const pending = rolePolicy.isPending || (kind === 'Worker' && cells.isPending)
  const tierSource: TierSource | null = pending
    ? null
    : preview && preview.source !== 'explicit' && (serverLevel || preview.source === 'agent' || preview.source === 'unknown' || preview.source === 'refused')
      ? preview.source
      : 'fallback'
  const roleLevel = serverLevel ?? AGENT_TASK_ROLES.find((r) => r.value === role)?.level ?? 'High'
  const effectiveLevel: AgentModelLevel = level ?? roleLevel
  const unknownTarget = preview?.source === 'unknown' || preview?.source === 'agent'

  const catalogue = runners.data ?? []
  const selectedRunner = catalogue.find((row) => row.runnerId === runnerChoice)
  const effectivePlatform = platformChoice === 'inherit' ? prefill?.cardDefaultPlatform : platformChoice
  const runnerPlatform = selectedRunner?.platform ?? null
  const platformConflict =
    runnerChoice !== 'automatic'
    && (effectivePlatform === 'Windows' || effectivePlatform === 'Linux')
    && runnerPlatform !== null
    && runnerPlatform !== effectivePlatform.toLowerCase()
  const inheritedHint = defaults.data
    ? `Automatic uses ${defaults.data.globalRunnerId ?? 'the built-in fallback'} unless a kind override or a platform requirement says otherwise.`
    : 'Automatic leaves the runner to the server.'

  const submit = () => {
    if (!goal.trim() || platformConflict) return
    create.mutate(
      {
        goal: goal.trim(),
        kind,
        role,
        modelLevel: level,
        workspace,
        workingDirectory: directory.trim() || null,
        scope: scope.trim() || null,
        denyDirectEdits: kind === 'Orchestrator' ? denyEdits : null,
        ...(platformChoice === 'inherit' ? {} : { requiredPlatform: platformChoice }),
        ...(runnerChoice === 'automatic' ? {} : { runnerId: runnerChoice }),
      },
      {
        onSuccess: (task) => {
          notifications.show({
            color: 'green',
            message: `Task ${task.shortId} queued at ${task.modelLevel.toLowerCase()} tier`,
          })
          // A warning means "legal but risky" — the one moment the caller can still reconsider.
          if (task.warning) {
            notifications.show({ color: 'yellow', message: task.warning, autoClose: 10_000 })
          }
          onClose()
        },
        onError: (error) =>
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Could not create the task') }),
      },
    )
  }

  return (
    <Stack>
      <Stack gap={4}>
        <Text size="sm" fw={500}>
          Shape
        </Text>
        <SegmentedControl
          value={kind}
          onChange={(value) => {
            const next = value as AgentTaskKind
            setKind(next)
            // A sub-orchestrator's job is to decompose, so Plan is its default role. Workspace is
            // the caller's own choice — Worktree to start — and a kind change does not reset it.
            setRole(next === 'Orchestrator' ? 'Plan' : 'Code')
          }}
          data={[
            { label: 'Worker', value: 'Worker' },
            { label: 'Sub-orchestrator', value: 'Orchestrator' },
          ]}
        />
        <Text size="xs" c="dimmed">
          {kind === 'Worker'
            ? 'One piece of work you can state in a sentence, done by one agent.'
            : 'Owns a chunk, decomposes it, and runs its own delegates. Its report is a rollup of the whole subtree.'}
        </Text>
      </Stack>

      <Stack gap={4}>
        <Group gap="xs">
          <Text size="sm" fw={500}>
            Role
          </Text>
          <Text size="xs" c="dimmed">
            sets the tier — that is the cost decision
          </Text>
        </Group>
        <Chip.Group multiple={false} value={role} onChange={(value) => setRole(value as AgentTaskRole)}>
          <Group gap={6}>
            {AGENT_TASK_ROLES.map((option) => (
              <Tooltip key={option.value} label={option.use} withArrow>
                <Chip value={option.value} size="xs" variant="outline">
                  {option.label}
                </Chip>
              </Tooltip>
            ))}
          </Group>
        </Chip.Group>
      </Stack>

      <Group gap="xs">
        <Text size="sm">Create preview</Text>
        {!unknownTarget && <TierBadge level={effectiveLevel} size="sm" />}
        {tierSource && (!level || preview?.source === 'unknown' || preview?.source === 'refused') && (
          <Text size="xs" c="dimmed" data-testid={`delegate-tier-source-${tierSource}`}>
            {tierSourceLabel(tierSource, preview)}
          </Text>
        )}
        {level ? (
          <Button variant="subtle" size="compact-xs" onClick={() => setLevel(null)}>
            use the role’s tier
          </Button>
        ) : (
          <Group gap={2}>
            <Text size="xs" c="dimmed">
              override →
            </Text>
            {AGENT_MODEL_LEVEL_OPTIONS.filter((o) => unknownTarget || o.value !== effectiveLevel).map((option) => (
              <Button
                key={option.value}
                variant="subtle"
                size="compact-xs"
                color="gray"
                aria-label={`override → ${option.value.toLowerCase()}`}
                onClick={() => setLevel(option.value)}
              >
                {option.value.toLowerCase()}
              </Button>
            ))}
          </Group>
        )}
      </Group>

      <Text size="xs" c="dimmed" data-testid="delegate-admission-note">
        Routing and admission depend on the server: card pins, standing-agent details, quota, model holds, provider sign-in, concurrency, workspace and runner conditions can change this preview.
      </Text>

      <Textarea
        label="Goal"
        description="An outcome, not a procedure — the delegate decides how."
        placeholder="e.g. make the install section in README.md say pwsh 7 instead of cmd"
        autosize
        minRows={3}
        value={goal}
        onChange={(event) => setGoal(event.currentTarget.value)}
        data-autofocus
      />

      <Stack gap={4}>
        <Text size="sm" fw={500}>
          Workspace
        </Text>
        <SegmentedControl
          value={workspace}
          onChange={(value) => setWorkspace(value as WorkspaceMode)}
          data={[
            { label: 'Shared', value: 'Shared' },
            { label: 'Worktree', value: 'Worktree' },
            { label: 'Read-only', value: 'ReadOnly' },
          ]}
        />
        <Text size="xs" c={kind === 'Orchestrator' && workspace === 'Shared' ? 'warning.5' : 'dimmed'}>
          {kind === 'Orchestrator' && workspace === 'Shared'
            ? 'An orchestrator fans out writers — sharing its caller’s directory means its delegates and its caller can overwrite each other. The server will warn.'
            : workspace === 'Shared'
              ? 'Runs in the caller’s directory. Explicit: commit-on-settle, when it applies, commits only this task’s footprint.'
              : workspace === 'Worktree'
                ? 'Own branch and checkout. This is the default. Finishing does not merge the branch back; landing is a separate step.'
                : 'Shared directory, but the brief says don’t write. For reviews and coverage audits.'}
        </Text>
      </Stack>

      {kind === 'Orchestrator' && (
        <Switch
          label="Block direct edits"
          description="Arms a PreToolUse hook in its worktree: Edit/Write are refused with “delegate this instead”. Turn off if the orchestrator must write a plan file itself."
          checked={denyEdits}
          disabled={workspace !== 'Worktree'}
          onChange={(event) => setDenyEdits(event.currentTarget.checked)}
        />
      )}

      <TextInput
        label="Directory"
        description="Another repo or checkout. Leave blank to run where the caller is."
        placeholder="C:\src\am-service"
        value={directory}
        onChange={(event) => setDirectory(event.currentTarget.value)}
      />

      <TextInput
        label="Scope"
        description="Files this task owns. Two shared tasks with intersecting scopes are run one after the other."
        placeholder="docs/**"
        value={scope}
        onChange={(event) => setScope(event.currentTarget.value)}
      />

      <Select
        label="Platform"
        description={
          prefill?.cardDefaultPlatform
            ? `Inherit card default (${prefill.cardDefaultPlatform}). The server still decides the host.`
            : 'Inherit leaves the card or follow-up requirement. Explicit Any resets it.'
        }
        value={platformChoice}
        onChange={(value) => setPlatformChoice((value as 'inherit' | RequiredPlatformName) ?? 'inherit')}
        data={[
          { value: 'inherit', label: prefill?.cardDefaultPlatform ? `Inherit card default (${prefill.cardDefaultPlatform})` : 'Inherit card default' },
          { value: 'Any', label: 'Any' },
          { value: 'Windows', label: 'Windows' },
          { value: 'Linux', label: 'Linux' },
        ]}
        data-testid="delegate-platform"
      />

      <Select
        label="Runner"
        description={inheritedHint}
        value={runnerChoice}
        onChange={(value) => setRunnerChoice(value ?? 'automatic')}
        data={[
          { value: 'automatic', label: 'Automatic' },
          ...catalogue.map((row) => ({
            value: row.runnerId,
            label: `${row.displayName} · ${row.platform ?? 'platform unknown'} · ${
              row.occupied === null || row.capacity === null ? 'occupancy unknown' : `${row.occupied}/${row.capacity}`
            }${row.available ? '' : ' · offline'}`,
          })),
        ]}
        data-testid="delegate-runner"
      />
      {platformConflict && (
        <Text size="sm" c="danger" data-testid="delegate-platform-conflict">
          {runnerChoice} is {runnerPlatform} and cannot run a {effectivePlatform} task. Choose a matching runner, or Automatic.
        </Text>
      )}

      <Group justify="flex-end">
        <Button variant="subtle" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={submit} loading={create.isPending} disabled={!goal.trim() || platformConflict}>
          Delegate
        </Button>
      </Group>
    </Stack>
  )
}
