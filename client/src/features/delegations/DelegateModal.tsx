import {
  Button,
  Chip,
  Group,
  Modal,
  SegmentedControl,
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
import { TierBadge } from './TaskChip'

export interface DelegatePrefill {
  /** Seeds the goal — the files view passes the path it is looking at. */
  goal?: string
  /** Where the delegate runs. Defaults to the server's caller directory when omitted. */
  workingDirectory?: string
  /** Declares the files this task owns; intersecting scopes are serialised by the dispatcher. */
  scopeGlob?: string
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
  const [workspace, setWorkspace] = useState<WorkspaceMode>('Shared')
  const [directory, setDirectory] = useState(prefill?.workingDirectory ?? '')
  const [scope, setScope] = useState(prefill?.scopeGlob ?? '')
  const [level, setLevel] = useState<AgentModelLevel | null>(null)

  // A sub-orchestrator decomposes, which is the expensive kind of thinking — the server floors it
  // at High, so showing anything cheaper here would be a lie.
  const roleLevel = AGENT_TASK_ROLES.find((r) => r.value === role)?.level ?? 'High'
  const effectiveLevel: AgentModelLevel =
    level ?? (kind === 'Orchestrator' && (roleLevel === 'Medium' || roleLevel === 'Low') ? 'High' : roleLevel)

  const submit = () => {
    if (!goal.trim()) return
    create.mutate(
      {
        goal: goal.trim(),
        kind,
        role,
        modelLevel: level,
        workspace,
        workingDirectory: directory.trim() || null,
        scopeGlob: scope.trim() || null,
      },
      {
        onSuccess: (task) => {
          notifications.show({
            color: 'green',
            message: `Task ${task.shortId} queued at ${task.modelLevel.toLowerCase()} tier`,
          })
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
            // A sub-orchestrator's job is to decompose, so Plan is its default role — and a worker
            // that was going to write code still is. Both are defaults the user can override.
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
        <Text size="sm">Runs at</Text>
        <TierBadge level={effectiveLevel} size="sm" />
        {level ? (
          <Button variant="subtle" size="compact-xs" onClick={() => setLevel(null)}>
            use the role’s tier
          </Button>
        ) : (
          <Group gap={2}>
            <Text size="xs" c="dimmed">
              override →
            </Text>
            {AGENT_MODEL_LEVEL_OPTIONS.filter((o) => o.value !== effectiveLevel).map((option) => (
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
        <Text size="xs" c="dimmed">
          {workspace === 'Shared'
            ? 'Runs directly in the directory, like you would yourself. The right default for almost everything.'
            : workspace === 'Worktree'
              ? 'Isolated on its own branch and merged back when it finishes. Use it when several delegates would write the same files at once.'
              : 'Shared directory, but the brief says don’t write. For reviews and coverage audits.'}
        </Text>
      </Stack>

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

      <Group justify="flex-end">
        <Button variant="subtle" onClick={onClose}>
          Cancel
        </Button>
        <Button onClick={submit} loading={create.isPending} disabled={!goal.trim()}>
          Delegate
        </Button>
      </Group>
    </Stack>
  )
}
