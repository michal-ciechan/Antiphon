import { useState } from 'react'
import {
  Alert,
  Button,
  Code,
  Group,
  Loader,
  Modal,
  ScrollArea,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
} from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { TbAlertTriangle } from 'react-icons/tb'
import { getApiErrorMessage } from '../../api/client'
import { useAddIgnore, useIgnorePreview } from '../../api/review'

import { ignorePatternFor, type IgnoreScope } from './ignorePattern'

export interface IgnorePathModalProps {
  agentId: string
  /** Workspace-relative path of the right-clicked node; null closes the dialog. */
  target: { path: string; isFolder: boolean } | null
  onClose: () => void
}

export function IgnorePathModal({ agentId, target, onClose }: IgnorePathModalProps) {
  const [scope, setScope] = useState<IgnoreScope>('name')
  const [pattern, setPattern] = useState(() =>
    target ? ignorePatternFor(target.path, target.isFolder, 'name') : '',
  )
  const [edited, setEdited] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const addIgnore = useAddIgnore(agentId)

  // A new target starts over: name scope, derived line, no stale error. Adjusted during render
  // (not in an effect) so the previous target's line never flashes. Scope toggles re-derive in
  // the SegmentedControl handler below.
  const targetKey = target ? `${target.isFolder ? 'd' : 'f'}:${target.path}` : null
  const [prevTargetKey, setPrevTargetKey] = useState(targetKey)
  if (targetKey !== prevTargetKey) {
    setPrevTargetKey(targetKey)
    if (target) {
      setScope('name')
      setEdited(false)
      setPattern(ignorePatternFor(target.path, target.isFolder, 'name'))
      setError(null)
    }
  }

  // Debounced so typing a pattern doesn't fire a git call per keystroke.
  const [debounced] = useDebouncedValue(pattern.trim(), 250)
  const preview = useIgnorePreview(target ? agentId : null, debounced)

  const matches = preview.data?.matches ?? []
  const tracked = preview.data?.trackedMatches ?? []
  const isCurrent = preview.data?.pattern === debounced && !preview.isFetching

  const handleAdd = async () => {
    setError(null)
    try {
      await addIgnore.mutateAsync(pattern.trim())
      onClose()
    } catch (e) {
      setError(getApiErrorMessage(e, 'Could not update .gitignore.'))
    }
  }

  return (
    <Modal opened={!!target} onClose={onClose} title="Ignore in git" size="lg">
      <Stack>
        <Text size="sm">
          Add a line to <Code>.gitignore</Code> for{' '}
          <Code>{target?.path}</Code>
          {target?.isFolder ? ' (folder)' : ''}.
        </Text>

        <SegmentedControl
          value={scope}
          onChange={(v) => {
            setScope(v as IgnoreScope)
            setEdited(false)
            if (target) {
              setPattern(ignorePatternFor(target.path, target.isFolder, v as IgnoreScope))
              setError(null)
            }
          }}
          data={[
            { value: 'name', label: `Anywhere named "${target?.path.split('/').pop() ?? ''}"` },
            { value: 'path', label: 'Only this one' },
          ]}
        />

        <TextInput
          label="Ignore line"
          description="Edit freely — the list below always reflects what this exact line would do."
          value={pattern}
          onChange={(event) => {
            setPattern(event.currentTarget.value)
            setEdited(true)
          }}
          styles={{ input: { fontFamily: 'var(--mantine-font-family-monospace)' } }}
          data-testid="ignore-pattern-input"
        />
        {edited && (
          <Text size="xs" c="dimmed">
            Custom line — switching the option above will replace it.
          </Text>
        )}

        <Stack gap={4}>
          <Group gap="xs">
            <Text size="sm" fw={500}>
              Would be hidden
            </Text>
            {preview.isFetching && <Loader size="xs" />}
            {isCurrent && (
              <Text size="sm" c="dimmed" data-testid="ignore-match-count">
                {matches.length}
                {preview.data?.truncated ? '+' : ''} item{matches.length === 1 ? '' : 's'}
              </Text>
            )}
          </Group>

          {isCurrent && matches.length === 0 && (
            <Text size="sm" c="dimmed" data-testid="ignore-no-matches">
              Nothing in the workspace matches this line.
            </Text>
          )}

          {matches.length > 0 && (
            <ScrollArea.Autosize mah={220}>
              <Stack gap={0} data-testid="ignore-match-list">
                {matches.map((m) => (
                  <Text key={m} size="xs" ff="monospace" c="dimmed">
                    {m}
                  </Text>
                ))}
                {preview.data?.truncated && (
                  <Text size="xs" c="dimmed" fs="italic">
                    …and more
                  </Text>
                )}
              </Stack>
            </ScrollArea.Autosize>
          )}
        </Stack>

        {tracked.length > 0 && (
          <Alert color="orange" icon={<TbAlertTriangle />} data-testid="ignore-tracked-warning">
            <Text size="sm">
              {tracked.length} tracked file{tracked.length === 1 ? '' : 's'} match this line and
              will stay visible — git ignores only untracked files. Remove them from the index
              (<Code>git rm --cached</Code>) if you want them gone.
            </Text>
          </Alert>
        )}

        {preview.isError && (
          <Alert color="red" icon={<TbAlertTriangle />}>
            Could not preview this line. The workspace may not be a git repository.
          </Alert>
        )}

        {error && (
          <Alert color="red" icon={<TbAlertTriangle />}>
            {error}
          </Alert>
        )}

        <Group justify="flex-end">
          <Button variant="subtle" onClick={onClose}>
            Cancel
          </Button>
          <Button
            onClick={handleAdd}
            loading={addIgnore.isPending}
            disabled={pattern.trim().length === 0}
          >
            Add to .gitignore
          </Button>
        </Group>
      </Stack>
    </Modal>
  )
}
