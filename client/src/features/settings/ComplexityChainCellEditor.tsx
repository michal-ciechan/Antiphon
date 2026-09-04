import {
  ActionIcon,
  Alert,
  Button,
  Group,
  Modal,
  Select,
  Stack,
  Text,
  TextInput,
  Textarea,
} from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { useRef, useState } from 'react'
import { TbChevronDown, TbChevronUp, TbPlus, TbTrash } from 'react-icons/tb'
import type { AgentModelLevel } from '../../api/agents'
import type { AgentTaskRole } from '../../api/agentTasks'
import type { AgentKind } from '../../api/boards'
import { getApiErrorMessage, getApiFieldErrors } from '../../api/client'
import {
  useClearComplexityChain,
  usePutComplexityChain,
  type ComplexityChainDto,
  type ComplexityResolvedFrom,
  type TaskComplexity,
} from '../../api/complexityChains'
import { ModelLevelSelect } from '../agents/ModelLevelSelect'
import {
  CHAIN_CLEAR_SUCCESS,
  CHAIN_KIND_OPTIONS,
  CHAIN_MUTATION_EFFECT,
  CHAIN_SAVE_SUCCESS,
  INHERITED_REPLACE_WARNING,
  MAX_CHAIN_CANDIDATES,
  UNSET_CELL_EDITOR_NOTE,
  canClearOverride,
  candidateListError,
  cellEditorTitle,
  clearOverrideCopy,
  effectiveResolvedFrom,
  isReplacingInheritedList,
  lookupFieldError,
  nextUnusedCandidate,
} from './routingSettingsModel'

export interface ComplexityChainEditorTarget {
  role: AgentTaskRole | null
  complexity: TaskComplexity
  chain: ComplexityChainDto | undefined
  isAnyRoleRow: boolean
  fallbackResolvedFrom: ComplexityResolvedFrom
}

export interface ComplexityChainCellEditorProps extends ComplexityChainEditorTarget {
  opened: boolean
  onClose: () => void
}

interface DraftCandidate {
  id: string
  agentKind: AgentKind
  modelLevel: AgentModelLevel
}

function toDraft(chain: ComplexityChainDto | undefined): DraftCandidate[] {
  const rows = chain?.candidates ?? []
  if (rows.length === 0) {
    return [{ id: 'c1', agentKind: 'ClaudeCode', modelLevel: 'Frontier' }]
  }
  return rows.map((row, index) => ({
    id: `c${index + 1}`,
    agentKind: row.agentKind,
    modelLevel: row.modelLevel,
  }))
}

function toDatetimeLocalValue(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

function fromDatetimeLocalValue(value: string): string | null {
  const trimmed = value.trim()
  if (!trimmed) return null
  const date = new Date(trimmed)
  if (Number.isNaN(date.getTime())) return null
  return date.toISOString()
}

/**
 * Settings-owned editor for one complexity-chain cell. Save/clear write only that cell
 * (Human PUT or matching DELETE) and do not touch pins, availability, usage monitoring,
 * or dispatch.
 */
export function ComplexityChainCellEditor({
  opened,
  onClose,
  role,
  complexity,
  chain,
  isAnyRoleRow,
  fallbackResolvedFrom,
}: ComplexityChainCellEditorProps) {
  const put = usePutComplexityChain()
  const clear = useClearComplexityChain()
  const saveRef = useRef<HTMLButtonElement>(null)
  const clearRef = useRef<HTMLButtonElement>(null)
  const idRef = useRef((chain?.candidates.length ?? 0) + 1)

  const [candidates, setCandidates] = useState<DraftCandidate[]>(() => toDraft(chain))
  const [reason, setReason] = useState(chain?.reason ?? '')
  const [notAfter, setNotAfter] = useState(toDatetimeLocalValue(chain?.notAfter))
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [confirmingClear, setConfirmingClear] = useState(false)

  const resolved = chain ? effectiveResolvedFrom(chain) : 'none'
  const canClear = canClearOverride(resolved, isAnyRoleRow)
  const title = cellEditorTitle(role, complexity)
  const copy = clearOverrideCopy({ isAnyRoleRow, role, complexity, fallbackResolvedFrom })
  const candidatesError = lookupFieldError(fieldErrors, 'Candidates')
  const notAfterError = lookupFieldError(fieldErrors, 'NotAfter')
  const reasonError = lookupFieldError(fieldErrors, 'Reason')
  const listError = candidateListError(candidates)
  const busy = put.isPending || clear.isPending

  const focusOperation = (operation: 'save' | 'clear') => {
    window.setTimeout(() => {
      if (operation === 'clear') clearRef.current?.focus()
      else saveRef.current?.focus()
    }, 0)
  }

  const showMutationError = (error: unknown, operation: 'save' | 'clear', fallback: string) => {
    const fields = getApiFieldErrors(error)
    setFieldErrors(fields)
    setFormError(Object.keys(fields).length > 0 ? null : getApiErrorMessage(error, fallback))
    focusOperation(operation)
  }

  const addCandidate = () => {
    if (candidates.length >= MAX_CHAIN_CANDIDATES) return
    const next = nextUnusedCandidate(candidates)
    if (!next) return
    idRef.current += 1
    setCandidates((current) => [
      ...current,
      { id: `c${idRef.current}`, agentKind: next.agentKind, modelLevel: next.modelLevel },
    ])
    setFieldErrors({})
  }

  const removeCandidate = (index: number) => {
    setCandidates((current) => current.filter((_, itemIndex) => itemIndex !== index))
    setFieldErrors({})
  }

  const moveCandidate = (index: number, delta: number) => {
    setCandidates((current) => {
      const target = index + delta
      if (target < 0 || target >= current.length) return current
      const next = [...current]
      const [item] = next.splice(index, 1)
      next.splice(target, 0, item)
      return next
    })
  }

  const updateCandidate = (index: number, patch: Partial<DraftCandidate>) => {
    setCandidates((current) =>
      current.map((row, itemIndex) => (itemIndex === index ? { ...row, ...patch } : row)),
    )
    setFieldErrors({})
  }

  const handleSave = () => {
    setFormError(null)
    if (listError) {
      setFieldErrors({ Candidates: listError })
      focusOperation('save')
      return
    }

    const notAfterIso = fromDatetimeLocalValue(notAfter)
    if (notAfter.trim() && !notAfterIso) {
      setFieldErrors({ NotAfter: 'Enter a valid expiry date and time.' })
      focusOperation('save')
      return
    }

    setFieldErrors({})
    put.mutate(
      {
        role,
        complexity,
        candidates: candidates.map((row) => ({
          agentKind: row.agentKind,
          modelLevel: row.modelLevel,
        })),
        reason: reason.trim() ? reason.trim() : null,
        notAfter: notAfterIso,
      },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: CHAIN_SAVE_SUCCESS })
          onClose()
        },
        onError: (error) => showMutationError(error, 'save', 'Could not save this cell'),
      },
    )
  }

  const handleClear = () => {
    setFormError(null)
    setFieldErrors({})
    clear.mutate(
      { role, complexity },
      {
        onSuccess: () => {
          notifications.show({ color: 'green', message: CHAIN_CLEAR_SUCCESS })
          onClose()
        },
        onError: (error) => showMutationError(error, 'clear', 'Could not clear this cell'),
      },
    )
  }

  return (
    <Modal opened={opened} onClose={onClose} title={title} size="lg">
      <Stack gap="sm" data-testid="routing-cell-editor">
        <Text size="sm" c="dimmed">
          {CHAIN_MUTATION_EFFECT}
        </Text>
        {isReplacingInheritedList(resolved, isAnyRoleRow) ? (
          <Alert color="yellow" variant="light">
            {INHERITED_REPLACE_WARNING}
          </Alert>
        ) : null}
        {resolved === 'none' ? (
          <Alert color="red" variant="light">
            {UNSET_CELL_EDITOR_NOTE}
          </Alert>
        ) : null}

        <Stack gap="xs" data-testid="routing-cell-editor-candidates">
          {candidates.map((row, index) => (
            <Group
              key={row.id}
              gap="xs"
              wrap="nowrap"
              align="flex-end"
              data-testid={`routing-cell-editor-candidate-${index}`}
              data-agent-kind={row.agentKind}
              data-model-level={row.modelLevel}
            >
              <Select
                label="Kind"
                aria-label={`Candidate ${index + 1} kind`}
                data={[...CHAIN_KIND_OPTIONS]}
                value={row.agentKind}
                onChange={(value) =>
                  updateCandidate(index, { agentKind: (value as AgentKind | null) ?? row.agentKind })
                }
                allowDeselect={false}
                size="xs"
                w={140}
              />
              <ModelLevelSelect
                label="Model level"
                aria-label={`Candidate ${index + 1} model level`}
                value={row.modelLevel}
                onChange={(value) => updateCandidate(index, { modelLevel: value })}
                withDescription={false}
                size="xs"
                w={180}
              />
              <ActionIcon
                variant="subtle"
                aria-label={`Move candidate ${index + 1} up`}
                onClick={() => moveCandidate(index, -1)}
                disabled={index === 0 || busy}
              >
                <TbChevronUp size={16} />
              </ActionIcon>
              <ActionIcon
                variant="subtle"
                aria-label={`Move candidate ${index + 1} down`}
                onClick={() => moveCandidate(index, 1)}
                disabled={index === candidates.length - 1 || busy}
              >
                <TbChevronDown size={16} />
              </ActionIcon>
              <ActionIcon
                variant="subtle"
                color="red"
                aria-label={`Remove candidate ${index + 1}`}
                onClick={() => removeCandidate(index)}
                disabled={busy}
              >
                <TbTrash size={16} />
              </ActionIcon>
            </Group>
          ))}
          {candidatesError ? (
            <Text size="sm" c="red">
              {candidatesError}
            </Text>
          ) : listError && candidates.length === 0 ? (
            <Text size="sm" c="red">
              {listError}
            </Text>
          ) : null}
          <Button
            variant="light"
            size="compact-sm"
            leftSection={<TbPlus size={14} />}
            onClick={addCandidate}
            disabled={busy || candidates.length >= MAX_CHAIN_CANDIDATES || !nextUnusedCandidate(candidates)}
          >
            Add candidate
          </Button>
        </Stack>

        <Textarea
          label="Reason (optional)"
          value={reason}
          error={reasonError}
          autosize
          minRows={2}
          onChange={(event) => setReason(event.currentTarget.value)}
        />
        <TextInput
          label="Expires (optional)"
          type="datetime-local"
          value={notAfter}
          error={notAfterError}
          onChange={(event) => setNotAfter(event.currentTarget.value)}
        />

        {formError ? (
          <Alert color="red" variant="light" data-testid="routing-cell-editor-error">
            {formError}
          </Alert>
        ) : null}

        {confirmingClear ? (
          <>
            <Alert color="red" variant="light" title={copy.title} data-testid="routing-cell-editor-clear-confirm">
              {copy.body}
            </Alert>
            <Group justify="flex-end">
              <Button variant="subtle" onClick={() => setConfirmingClear(false)} disabled={busy}>
                Cancel
              </Button>
              <Button
                ref={clearRef}
                color="red"
                onClick={handleClear}
                loading={clear.isPending}
              >
                {copy.confirm}
              </Button>
            </Group>
          </>
        ) : (
          <Group justify="flex-end">
            {canClear ? (
              <Button
                color="red"
                variant="light"
                onClick={() => {
                  setFormError(null)
                  setConfirmingClear(true)
                }}
                disabled={busy}
              >
                Clear override
              </Button>
            ) : null}
            <Button variant="subtle" onClick={onClose} disabled={busy}>
              Cancel
            </Button>
            <Button
              ref={saveRef}
              onClick={handleSave}
              loading={put.isPending}
              disabled={candidates.length === 0}
            >
              Save
            </Button>
          </Group>
        )}
      </Stack>
    </Modal>
  )
}
