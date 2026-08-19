import {
  Alert,
  Button,
  Code,
  Divider,
  Group,
  Input,
  Modal,
  MultiSelect,
  NumberInput,
  SegmentedControl,
  Select,
  Stack,
  Switch,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useEffect, useMemo, useState } from 'react'
import { TbAlertTriangle, TbTrash } from 'react-icons/tb'
import type { AgentAssignmentPolicy, AgentReplyStyle, AgentSummaryDto } from '../../api/agents'
import {
  AGENT_REPLY_STYLE_OPTIONS,
  fetchPreamblePreset,
  useAgent,
  useDeleteAgent,
  useInstructionBundles,
  useUpdateAgent,
} from '../../api/agents'
import { useBoards } from '../../api/boards'
import { getApiErrorMessage } from '../../api/client'
import { AgentTuiSelection } from './AgentTuiSelection'

const ASSIGNMENT_POLICIES: Array<{ value: AgentAssignmentPolicy; label: string }> = [
  { value: 'AutoPick', label: 'Auto pick' },
  { value: 'ManualConfirm', label: 'Manual confirm' },
  { value: 'Paused', label: 'Paused' },
]

interface AgentSettingsModalProps {
  agent: AgentSummaryDto | null
  opened: boolean
  onClose: () => void
  onDeleted: (agentId: string) => void
}

export function AgentSettingsModal({ agent, opened, onClose, onDeleted }: AgentSettingsModalProps) {
  const boards = useBoards()
  // Hooks need a stable agent id; fall back to an empty string when closed (modal is gated on agent).
  const updateAgent = useUpdateAgent(agent?.id ?? '')
  const deleteAgent = useDeleteAgent()

  const [name, setName] = useState('')
  const [workingDirectory, setWorkingDirectory] = useState('')
  const [details, setDetails] = useState('')
  const [assignmentPolicy, setAssignmentPolicy] = useState<AgentAssignmentPolicy>('AutoPick')
  const [tuiProfileId, setTuiProfileId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)
  const [boardId, setBoardId] = useState<string | null>(null)
  const [alwaysOn, setAlwaysOn] = useState(false)
  const [remoteControlEnabled, setRemoteControlEnabled] = useState(false)
  const [autoCompactEnabled, setAutoCompactEnabled] = useState<boolean | null>(null)
  const [autoCompactIdleMinutes, setAutoCompactIdleMinutes] = useState<number | null>(null)
  const [autoCompactContextPercent, setAutoCompactContextPercent] = useState<number | null>(null)
  const [systemPromptAppend, setSystemPromptAppend] = useState('')
  const [replyStyle, setReplyStyle] = useState<AgentReplyStyle>('Normal')
  const [bundleKeys, setBundleKeys] = useState<string[]>([])
  const [seededBundlesFor, setSeededBundlesFor] = useState<string | null>(null)
  const [loadingPreset, setLoadingPreset] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // The composed bundle list lives on the DETAIL dto (it is derived, not stored), and this modal is
  // opened from the summary in the agents list. Only fetched while the modal is open.
  const detail = useAgent(opened && agent ? agent.id : null)
  const composedBundles = detail.data?.composedBundles ?? []
  const bundles = useInstructionBundles(opened)
  // The option LABEL is the bare key, because it is also what the selected pill shows and a pill
  // carrying a whole sentence is unreadable. The summary rides renderOption instead, where there is
  // room for it and where it is actually needed — choosing.
  const bundleOptions = useMemo(
    () => (bundles.data ?? []).map((b) => ({ value: b.key, label: b.key })),
    [bundles.data],
  )
  const bundleSummaries = useMemo(
    () => new Map((bundles.data ?? []).map((b) => [b.key, b])),
    [bundles.data],
  )
  // The badge, not a button: the agent picks the new instructions up at its NEXT launch and nothing
  // here forces one. Typing bundles into a live session is the thing this design deliberately does
  // not do, so the notice says what will happen rather than offering to make it happen now.
  const outOfDate = detail.data?.bundlesOutOfDate ?? false

  // Reload the form whenever a different agent is opened. Attachments come from the DETAIL fetch,
  // so they are seeded in their own effect below rather than here.
  useEffect(() => {
    if (!opened || !agent) return
    setName(agent.name)
    setWorkingDirectory(agent.workingDirectory)
    setDetails(agent.details)
    setAssignmentPolicy(agent.assignmentPolicy)
    setTuiProfileId(agent.tuiProfileId ?? null)
    setModelId(agent.modelId ?? null)
    setBoardId(agent.boardId)
    setAlwaysOn(agent.alwaysOn)
    setRemoteControlEnabled(agent.remoteControlEnabled)
    setAutoCompactEnabled(agent.autoCompactEnabled ?? null)
    setAutoCompactIdleMinutes(agent.autoCompactIdleMinutes ?? null)
    setAutoCompactContextPercent(agent.autoCompactContextPercent ?? null)
    setSystemPromptAppend(agent.systemPromptAppend ?? '')
    // An older server response omits the field entirely; Normal is what that means.
    setReplyStyle(agent.replyStyle ?? 'Normal')
    setConfirmingDelete(false)
    setSeededBundlesFor(null)
  }, [agent, opened])

  // Seeded from the DETAIL response, which is the only place attachments are reported — the list
  // this modal opens from carries the summary. Seeded ONCE per agent: the detail query polls every
  // 5s, and re-seeding on each response would wipe a selection the operator was still editing.
  useEffect(() => {
    if (!opened || !detail.data || detail.data.id === seededBundlesFor) return
    setBundleKeys(detail.data.attachedBundleKeys ?? [])
    setSeededBundlesFor(detail.data.id)
  }, [opened, detail.data, seededBundlesFor])

  const handleUsePreset = async () => {
    setLoadingPreset(true)
    try {
      const preset = await fetchPreamblePreset('telegram')
      setSystemPromptAppend(preset.template)
    } catch (error) {
      notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Failed to load preset') })
    } finally {
      setLoadingPreset(false)
    }
  }

  const boardOptions = useMemo(
    () => (boards.data ?? []).map((board) => ({ value: board.id, label: `${board.projectName} / ${board.name}` })),
    [boards.data],
  )

  const handleSave = () => {
    if (!agent || !name.trim() || !workingDirectory.trim()) return

    updateAgent.mutate(
      {
        name: name.trim(),
        workingDirectory: workingDirectory.trim(),
        details: details.trim() || null,
        defaultWorkflowTemplateId: agent.defaultWorkflowTemplateId,
        assignmentPolicy,
        boardId,
        alwaysOn,
        remoteControlEnabled,
        // Empty / null = use the installation ContextCompactionSettings default.
        autoCompactEnabled,
        autoCompactIdleMinutes,
        autoCompactContextPercent,
        // Empty string clears the preamble server-side; null would mean "leave unchanged".
        systemPromptAppend: systemPromptAppend.trim(),
        tuiProfileId,
        modelId,
        replyStyle,
        // Always sent, so an emptied picker detaches: null on the request means "leave unchanged".
        bundleKeys,
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: 'Agent updated' })
          onClose()
        },
        onError: (error) => {
          notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Agent update failed') })
        },
      },
    )
  }

  const handleDelete = () => {
    if (!agent) return

    deleteAgent.mutate(agent.id, {
      onSuccess: () => {
        notifications.show({ color: 'green', message: 'Agent deleted' })
        onDeleted(agent.id)
        onClose()
      },
      onError: (error) => {
        notifications.show({ color: 'red', message: getApiErrorMessage(error, 'Agent deletion failed') })
      },
    })
  }

  return (
    <Modal opened={opened} onClose={onClose} title="Agent Settings" size="lg">
      <Stack>
        <TextInput
          label="Name"
          value={name}
          onChange={(event) => setName(event.currentTarget.value)}
        />
        <TextInput
          label="Working directory"
          value={workingDirectory}
          onChange={(event) => setWorkingDirectory(event.currentTarget.value)}
        />
        <Textarea
          label="Details"
          autosize
          minRows={3}
          value={details}
          onChange={(event) => setDetails(event.currentTarget.value)}
        />
        <Select
          label="Default board"
          description="Where Add Work cards land. Every agent has one — it can be moved, not cleared."
          placeholder="Pick a board"
          data={boardOptions}
          value={boardId}
          onChange={setBoardId}
          disabled={boards.isLoading}
          searchable
        />
        <AgentTuiSelection
          tuiProfileId={tuiProfileId}
          modelId={modelId}
          onProfileChange={setTuiProfileId}
          onModelChange={setModelId}
          liveSessionSelection={agent?.liveSessionSelection}
        />
        <Select
          label="Assignment policy"
          data={ASSIGNMENT_POLICIES}
          value={assignmentPolicy}
          onChange={(value) => setAssignmentPolicy((value as AgentAssignmentPolicy | null) ?? 'AutoPick')}
          allowDeselect={false}
        />

        <Switch
          label="Always on"
          description="Auto-start at boot and auto-restart on crash (backing off, never giving up). Stop suspends until the next manual start."
          checked={alwaysOn}
          onChange={(event) => setAlwaysOn(event.currentTarget.checked)}
        />
        <Switch
          label="Remote control"
          description="Every start arms /remote-control so the session can be driven from claude.ai."
          checked={remoteControlEnabled}
          onChange={(event) => setRemoteControlEnabled(event.currentTarget.checked)}
        />

        <Select
          label="Auto-compact"
          description="Idle auto-compaction for this agent's Claude sessions. Empty uses the installation default (on)."
          placeholder="Use default"
          clearable
          data={[
            { value: 'on', label: 'On' },
            { value: 'off', label: 'Off' },
          ]}
          value={autoCompactEnabled === null ? null : autoCompactEnabled ? 'on' : 'off'}
          onChange={(value) => {
            if (value === 'on') setAutoCompactEnabled(true)
            else if (value === 'off') setAutoCompactEnabled(false)
            else setAutoCompactEnabled(null)
          }}
        />
        <NumberInput
          label="Auto-compact idle minutes"
          description="Minutes idle before a compact is considered. Empty uses the installation default (480 = 8 hours)."
          placeholder="Use default"
          min={1}
          allowDecimal={false}
          allowNegative={false}
          value={autoCompactIdleMinutes ?? ''}
          onChange={(value) => setAutoCompactIdleMinutes(typeof value === 'number' ? value : null)}
        />
        <NumberInput
          label="Auto-compact context percent"
          description="Compact when context is at least this full (1–100). Empty uses the installation default (50)."
          placeholder="Use default"
          min={1}
          max={100}
          allowDecimal={false}
          allowNegative={false}
          value={autoCompactContextPercent ?? ''}
          onChange={(value) => setAutoCompactContextPercent(typeof value === 'number' ? value : null)}
        />

        <Textarea
          label="System prompt (appended)"
          description="Channel preamble appended to the system prompt on every launch (--append-system-prompt). {agentName} and {channels} render at launch time. Empty = none; also disables bootstrap/restart/compaction notes."
          autosize
          minRows={3}
          maxRows={12}
          value={systemPromptAppend}
          onChange={(event) => setSystemPromptAppend(event.currentTarget.value)}
        />
        <Group justify="flex-start">
          <Button variant="light" size="xs" onClick={handleUsePreset} loading={loadingPreset}>
            Use Telegram preset
          </Button>
        </Group>

        <Input.Wrapper
          label="Reply style"
          description={
            AGENT_REPLY_STYLE_OPTIONS.find((option) => option.value === replyStyle)?.description ?? ''
          }
        >
          <SegmentedControl
            fullWidth
            mt={4}
            data={AGENT_REPLY_STYLE_OPTIONS.map(({ value, label }) => ({ value, label }))}
            value={replyStyle}
            onChange={(value) => setReplyStyle(value as AgentReplyStyle)}
          />
        </Input.Wrapper>

        <MultiSelect
          label="Attached bundles"
          description="Standing instruction blocks this agent carries on top of anything its role implies. The bundles themselves live in the repo (server/Bundles/) — this only chooses which ones this agent gets. Reply style is picked above, not here."
          placeholder={bundleKeys.length === 0 ? 'None attached' : undefined}
          data={bundleOptions}
          value={bundleKeys}
          onChange={setBundleKeys}
          disabled={bundles.isLoading}
          renderOption={({ option }) => (
            <Stack gap={0}>
              <Text size="sm">{option.value}</Text>
              <Text size="xs" c="dimmed">
                {bundleSummaries.get(option.value)?.summary ?? ''}
              </Text>
            </Stack>
          )}
          searchable
          clearable
        />

        <Input.Wrapper
          label="Carries bundles"
          description="What the agent's NEXT launch composes into --append-system-prompt, in order. Versions are content hashes — editing a bundle in the repo changes them."
        >
          <Group gap="xs" mt={4}>
            {composedBundles.length === 0 ? (
              <Text size="sm" c="dimmed">
                None — this agent launches with its own system prompt alone.
              </Text>
            ) : (
              composedBundles.map((bundle) => (
                <Code key={bundle}>{bundle}</Code>
              ))
            )}
          </Group>
        </Input.Wrapper>

        {outOfDate && (
          <Alert color="yellow" icon={<TbAlertTriangle size={16} />} title="Restarts with updated instructions">
            The running session was launched with different bundles than the list above. It keeps the ones
            it started with until its next launch — nothing is typed into a live session.
          </Alert>
        )}

        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={handleSave}
            loading={updateAgent.isPending}
            disabled={!name.trim() || !workingDirectory.trim()}
          >
            Save
          </Button>
        </Group>

        <Divider label="Danger zone" labelPosition="center" />

        {confirmingDelete ? (
          <Group justify="space-between">
            <Text size="sm" c="red">
              Delete this agent? Its cards will be unassigned.
            </Text>
            <Group gap="xs">
              <Button variant="subtle" onClick={() => setConfirmingDelete(false)}>
                Cancel
              </Button>
              <Button color="red" onClick={handleDelete} loading={deleteAgent.isPending}>
                Delete agent
              </Button>
            </Group>
          </Group>
        ) : (
          <Group justify="flex-end">
            <Button
              variant="light"
              color="red"
              leftSection={<TbTrash size={16} />}
              onClick={() => setConfirmingDelete(true)}
            >
              Delete agent
            </Button>
          </Group>
        )}
      </Stack>
    </Modal>
  )
}
