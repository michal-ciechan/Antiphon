import { useEffect, useState } from 'react'
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

export type IgnoreScope = 'name' | 'path'

/**
 * The .gitignore line for a target, for each scope.
 *
 * - `name` — matches anywhere in the repo. Bare, unanchored: gitignore matches a pattern with no
 *   slash against the basename at every level, which is what "all the bin-check folders" means.
 * - `path` — matches only this one. Leading slash anchors it to the .gitignore's own directory,
 *   so `/server/bin` cannot also catch `client/server/bin`.
 *
 * Folders get a trailing slash so the pattern can never match a FILE that happens to share the
 * name. Exported for tests: getting these two lines right is the whole feature.
 */
export function ignorePatternFor(path: string, isFolder: boolean, scope: IgnoreScope): string {
  const clean = path.replace(/^\/+|\/+$/g, '')
  if (scope === 'name') {
    const name = clean.split('/').pop() ?? clean
    return isFolder ? `${name}/` : name
  }
  return isFolder ? `/${clean}/` : `/${clean}`
}

export interface IgnorePathModalProps {
  agentId: string
  /** Workspace-relative path of the right-clicked node; null closes the dialog. */
  target: { path: string; isFolder: boolean } | null
  onClose: () => void
}

export function IgnorePathModal({ agentId, target, onClose }: IgnorePathModalProps) {
  const [scope, setScope] = useState<IgnoreScope>('name')
  const [pattern, setPattern] = useState('')
  const [edited, setEdited] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const addIgnore = useAddIgnore(agentId)

  // Re-derive when the target or scope changes, unless the user has typed their own line — their
  // edit must survive a scope toggle being clicked by accident.
  useEffect(() => {
    if (!target) return
    setPattern(ignorePatternFor(target.path, target.isFolder, scope))
    setError(null)
  }, [target?.path, target?.isFolder, scope]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    setEdited(false)
    setScope('name')
  }, [target?.path])

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
