import { Box, Button, Chip, Group, Paper, Stack, Text, Textarea } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useRef, useState, type ReactNode } from 'react'
import { TbUserShare } from 'react-icons/tb'
import { useCreateAgentTask, type AgentTaskRole } from '../../api/agentTasks'
import { getApiErrorMessage } from '../../api/client'
import { DelegateModal } from '../delegations/DelegateModal'
import { buildSelectionGoal } from './selectionGoal'

const QUICK_ROLES: AgentTaskRole[] = ['Docs', 'Code', 'Plan']

interface PendingSelection {
  text: string
  /** Position of the affordance, relative to the wrapper. */
  x: number
  y: number
}

/**
 * Highlight → prompt (feature 008): wraps the rendered file view; selecting text floats a
 * "Send to agents" affordance at the end of the selection. The composer itself is the caller's to
 * place (via onCompose) — rendered content can scroll horizontally, and a composer inside that
 * scroll box would carry its buttons off-screen.
 */
export function SelectionDelegate({
  onCompose,
  children,
}: {
  /** Called with the selected text when the affordance is clicked. */
  onCompose: (text: string) => void
  children: ReactNode
}) {
  const wrapperRef = useRef<HTMLDivElement | null>(null)
  const [pending, setPending] = useState<PendingSelection | null>(null)

  const capture = () => {
    // Let the browser finalise the selection first — reading it inside mouseup sees the old one
    // when the user clicks to collapse.
    requestAnimationFrame(() => {
      const selection = window.getSelection()
      const wrapper = wrapperRef.current
      if (!selection || selection.isCollapsed || !wrapper) {
        setPending(null)
        return
      }
      const text = selection.toString()
      if (!text.trim()) {
        setPending(null)
        return
      }
      const range = selection.getRangeAt(0)
      if (!wrapper.contains(range.commonAncestorContainer)) return
      const rect = range.getBoundingClientRect()
      const outer = wrapper.getBoundingClientRect()
      setPending({
        text,
        x: Math.max(0, Math.min(rect.right - outer.left, outer.width - 140)),
        y: rect.bottom - outer.top + 6,
      })
    })
  }

  return (
    <Box ref={wrapperRef} pos="relative" onMouseUp={capture} data-testid="selection-delegate">
      {children}
      {pending && (
        <Button
          size="compact-xs"
          color="violet"
          leftSection={<TbUserShare size={13} />}
          style={{ position: 'absolute', left: pending.x, top: pending.y, zIndex: 5 }}
          // Keep the selection alive: a mousedown on the button would collapse it before click.
          onMouseDown={(event) => event.preventDefault()}
          onClick={() => {
            onCompose(pending.text)
            setPending(null)
          }}
          data-testid="selection-send"
        >
          Send to agents
        </Button>
      )}
    </Box>
  )
}

/** The inline composer — exported for tests. */
export function SelectionComposer({
  filePath,
  workingDirectory,
  selection,
  defaultRole,
  goalContext,
  scopeGlob,
  onClose,
}: {
  filePath: string
  workingDirectory: string
  selection: string
  defaultRole: AgentTaskRole
  /** Optional task/card context retained above the standard, byte-stable selection goal. */
  goalContext?: string
  /** Reports have no file lease; file review keeps the selected path by default. */
  scopeGlob?: string | null
  onClose: () => void
}) {
  const create = useCreateAgentTask()
  const [instruction, setInstruction] = useState('')
  const [role, setRole] = useState<AgentTaskRole>(defaultRole)
  const [moreOpen, setMoreOpen] = useState(false)

  const goal = buildSelectionGoal(filePath, selection, instruction, goalContext)

  const submit = () => {
    if (!instruction.trim()) return
    create.mutate(
      {
        goal,
        role,
        kind: 'Worker',
        // null = the server decides (workers run Shared) — exactly the pool's pickup path.
        workspace: null,
        workingDirectory,
        scopeGlob: scopeGlob === undefined ? filePath : scopeGlob,
      },
      {
        onSuccess: (task) => {
          notifications.show({
            color: 'green',
            message: `Task ${task.shortId} queued at ${task.modelLevel.toLowerCase()} tier`,
          })
          if (task.warning) {
            notifications.show({ color: 'yellow', message: task.warning, autoClose: 10_000 })
          }
          onClose()
        },
        onError: (error) =>
          notifications.show({
            color: 'red',
            message: getApiErrorMessage(error, 'Could not queue the task'),
          }),
      },
    )
  }

  return (
    <Paper withBorder shadow="md" p="sm" data-testid="selection-composer">
      <Stack gap="xs">
        <Text size="xs" c="dimmed">
          Queue work on {filePath} about:
        </Text>
        <Text
          size="xs"
          c="dimmed"
          lineClamp={3}
          style={{
            borderLeft: '2px solid var(--mantine-color-violet-6)',
            paddingLeft: 8,
            whiteSpace: 'pre-wrap',
          }}
        >
          {selection.trim()}
        </Text>
        <Textarea
          autosize
          minRows={2}
          placeholder="What should be done about this passage?"
          value={instruction}
          onChange={(event) => setInstruction(event.currentTarget.value)}
          data-autofocus
          autoFocus
        />
        <Group justify="space-between" wrap="nowrap">
          <Chip.Group multiple={false} value={role} onChange={(value) => setRole(value as AgentTaskRole)}>
            <Group gap={4}>
              {QUICK_ROLES.map((r) => (
                <Chip key={r} value={r} size="xs" variant="outline">
                  {r}
                </Chip>
              ))}
            </Group>
          </Chip.Group>
          <Group gap="xs" wrap="nowrap">
            <Button size="compact-sm" variant="subtle" onClick={onClose}>
              Cancel
            </Button>
            <Button size="compact-sm" variant="subtle" onClick={() => setMoreOpen(true)}>
              More options…
            </Button>
            <Button
              size="compact-sm"
              color="violet"
              leftSection={<TbUserShare size={14} />}
              loading={create.isPending}
              disabled={!instruction.trim()}
              onClick={submit}
              data-testid="selection-queue"
            >
              Queue for agents
            </Button>
          </Group>
        </Group>
      </Stack>
      <DelegateModal
        opened={moreOpen}
        onClose={() => {
          setMoreOpen(false)
          onClose()
        }}
        title={`Delegate — ${filePath}`}
        prefill={{ goal, workingDirectory, scopeGlob: filePath }}
      />
    </Paper>
  )
}
